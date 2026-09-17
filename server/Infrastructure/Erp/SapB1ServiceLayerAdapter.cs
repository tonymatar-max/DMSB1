using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Erp;
using NexusDocs.Api.Infrastructure.Secrets;

namespace NexusDocs.Api.Infrastructure.Erp;

/// <summary>
/// SAP Business One Service Layer implementation of <see cref="IErpAdapter"/> (ARCHITECTURE.md
/// section 4.2-4.3): session-cookie login, generic OData lookup, and outbox dispatch for the
/// operation types the Flow engine enqueues.
///
/// IMPORTANT — this has been written to SAP's publicly documented Service Layer contract
/// (session-cookie auth via POST /b1s/v1/Login, standard OData v4 entity sets, DocumentLines
/// shape for marketing documents) and exercised against the rest of this codebase, but this
/// environment has no live SAP Business One instance to actually call. The HTTP request/response
/// handling below has NOT been verified against a real Service Layer endpoint. Before relying on
/// this in a real deployment: point an ErpConnection at a real (ideally non-production) B1
/// company database and run the outbox loop end to end once.
///
/// Not implemented yet: routing calls through the Nexus B1 Gateway tunnel for on-prem estates
/// (ARCHITECTURE.md section 4.3) — <see cref="ErpConnection.GatewayId"/> is read but calls always
/// go directly to <see cref="ErpConnection.BaseUrl"/> today; a connection with only a GatewayId
/// set will fail clearly rather than silently trying the wrong thing.
/// </summary>
public class SapB1ServiceLayerAdapter : IErpAdapter
{
    // B1 object code -> Service Layer entity set name, for the codes ARCHITECTURE.md section 4.1
    // lists. Extend as new document types are wired into CAPTURE/GEN in later phases.
    private static readonly IReadOnlyDictionary<int, string> ObjectTypeToEntitySet = new Dictionary<int, string>
    {
        [2] = "BusinessPartners",
        [4] = "Items",
        [13] = "Invoices", // A/R Invoice
        [15] = "DeliveryNotes",
        [16] = "Returns",
        [17] = "Orders", // Sales Order
        [18] = "PurchaseInvoices", // A/P Invoice
        [20] = "PurchaseDeliveryNotes", // Goods Receipt PO
        [22] = "PurchaseOrders",
        [23] = "QuotationsOfSales", // Sales Quotation
        [171] = "EmployeesInfo",
    };

    // One session per ErpConnection, shared across requests/tenants on this instance. Service
    // Layer sessions default to ~30 minutes of inactivity server-side; re-login proactively a
    // little early (25 min) rather than waiting for a 401, and always treat a 401 as "session
    // gone, log in again once" regardless of this cache.
    private static readonly ConcurrentDictionary<Guid, CachedSession> Sessions = new();
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(25);

    private readonly HttpClient _httpClient;
    private readonly NexusDocsDbContext _db;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<SapB1ServiceLayerAdapter> _logger;

    public string SystemType => "SapBusinessOne";

    public SapB1ServiceLayerAdapter(
        HttpClient httpClient,
        NexusDocsDbContext db,
        ISecretStore secretStore,
        ILogger<SapB1ServiceLayerAdapter> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _secretStore = secretStore;
        _logger = logger;
    }

    public async Task<ErpLookupResult?> LookupObjectAsync(Guid tenantId, int objectType, string externalKey)
    {
        if (!ObjectTypeToEntitySet.TryGetValue(objectType, out var entitySet))
        {
            _logger.LogWarning("No Service Layer entity set mapping for B1 object type {ObjectType}.", objectType);
            return null;
        }

        var connection = await ResolveConnectionAsync(tenantId);
        if (connection is null) return null;

        // Business partners key on CardCode (string); everything else here keys on the numeric
        // DocEntry. externalKey arrives as a string either way — this is a Phase 3 simplification
        // good enough for the object types actually wired up so far.
        var keyLiteral = objectType == 2 ? $"'{externalKey}'" : externalKey;
        var path = $"/b1s/v1/{entitySet}({keyLiteral})";

        JsonNode? node;
        try
        {
            node = await SendAsync(connection, HttpMethod.Get, path, body: null);
        }
        catch (SapB1NotFoundException)
        {
            return null;
        }

        if (node is null) return null;

        var label = DescribeLabel(objectType, node);
        var dataJson = node.ToJsonString();
        return new ErpLookupResult(externalKey, label, dataJson);
    }

    public async Task PushOutboxItemAsync(Guid tenantId, Guid outboxItemId)
    {
        var item = await _db.IntegrationOutbox
            .IgnoreQueryFilters() // called from the background worker: no ambient tenant to filter by.
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == outboxItemId);

        if (item is null)
        {
            _logger.LogWarning("IntegrationOutbox {OutboxId} not found for tenant {TenantId}; nothing to push.", outboxItemId, tenantId);
            return;
        }

        var connection = await _db.ErpConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == item.ErpConnectionId);

        if (connection is null)
        {
            item.Status = IntegrationOutboxStatus.Failed;
            item.LastError = $"ErpConnection {item.ErpConnectionId} no longer exists.";
            item.Attempts++;
            item.ProcessedAt = DateTimeOffset.UtcNow;
            return;
        }

        item.Attempts++;

        try
        {
            var (path, body) = BuildRequest(item);
            var response = await SendAsync(connection, HttpMethod.Post, path, body);

            var docEntry = response?["DocEntry"]?.GetValue<int>();
            var docNum = response?["DocNum"]?.ToString();

            item.Status = IntegrationOutboxStatus.Delivered;
            item.ErpKey = docEntry?.ToString() ?? docNum;
            item.ProcessedAt = DateTimeOffset.UtcNow;
            item.LastError = null;

            _logger.LogInformation(
                "Outbox {OutboxId} ({OperationType}) delivered to {SystemType} as DocEntry {DocEntry}.",
                item.Id, item.OperationType, SystemType, item.ErpKey);
        }
        catch (SapB1UnsupportedOperationException ex)
        {
            // Not a transient failure — retrying won't help. Fail immediately rather than burning
            // retry attempts on something that will never succeed without a code/mapping change.
            item.Status = IntegrationOutboxStatus.Failed;
            item.LastError = ex.Message;
            item.ProcessedAt = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "Outbox {OutboxId} has an unsupported OperationType {OperationType}.", item.Id, item.OperationType);
        }
        catch (Exception ex)
        {
            item.Status = IntegrationOutboxStatus.Failed;
            item.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            _logger.LogError(ex, "Outbox {OutboxId} push to {SystemType} failed (attempt {Attempts}).", item.Id, SystemType, item.Attempts);
            // Left Status=Failed rather than re-queued as Pending: IntegrationOutboxWorker decides
            // whether a Failed row is worth a bounded number of retries (see its own comment).
        }
    }

    // ---------------------------------------------------------------------------------------
    // Outbox operation dispatch
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Maps an IntegrationOutbox row to a Service Layer request. Each OperationType the Flow
    /// engine (or a future phase) enqueues needs an explicit case here — there is deliberately no
    /// generic fallback, so an unmapped OperationType fails loudly (via
    /// <see cref="SapB1UnsupportedOperationException"/>) instead of sending something wrong to B1.
    /// </summary>
    private static (string Path, JsonNode Body) BuildRequest(IntegrationOutbox item)
    {
        return item.OperationType switch
        {
            "CreatePurchaseRequisitionPosting" => (
                "/b1s/v1/PurchaseOrders",
                BuildPurchaseOrderFromRequisitionPayload(item.PayloadJson)),

            "CreatePurchaseInvoiceFromGrpo" => (
                "/b1s/v1/PurchaseInvoices",
                BuildPurchaseInvoiceFromGrpoPayload(item.PayloadJson)),

            _ => throw new SapB1UnsupportedOperationException(
                $"OperationType '{item.OperationType}' has no Service Layer mapping in SapB1ServiceLayerAdapter."),
        };
    }

    /// <summary>
    /// Default mapping for a Nexus Requisition -> B1 Purchase Order, per ARCHITECTURE.md section
    /// 4.4 Pattern A. A Requisition has no item lines (it's a free-text ask, not a line-item
    /// order), so this posts a single non-inventory/service-type line (ItemCode omitted,
    /// ItemDescription + AccountCode instead) — the standard B1 way to raise a PO for a cost
    /// rather than a stocked item. <c>AccountCode</c> is left as a placeholder
    /// ("_SYS00000000000") since the correct G/L account depends on the customer's chart of
    /// accounts and cost-centre mapping; a real implementation must resolve this per tenant
    /// (e.g. from the Requisition's CostCentre field via a configured mapping table) before this
    /// is more than a structurally-correct placeholder.
    /// </summary>
    private static JsonNode BuildPurchaseOrderFromRequisitionPayload(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        var title = root.GetProperty("Title").GetString() ?? "Requisition";
        var amount = root.GetProperty("Amount").GetDecimal();
        var costCentre = root.TryGetProperty("CostCentre", out var cc) ? cc.GetString() : null;

        return new JsonObject
        {
            ["DocDate"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            ["Comments"] = $"Created by Nexus Docs from an approved requisition. Cost centre: {costCentre ?? "(none)"}",
            ["DocumentLines"] = new JsonArray
            {
                new JsonObject
                {
                    ["ItemDescription"] = title,
                    ["Quantity"] = 1,
                    ["UnitPrice"] = amount,
                    ["AccountCode"] = "_SYS00000000000", // TODO: resolve from tenant cost-centre mapping.
                },
            },
        };
    }

    /// <summary>
    /// CAPTURE module A/P invoice automation (ARCHITECTURE.md section 7 step 6): a cleanly-matched
    /// captured invoice is posted as a B1 A/P Invoice created "from" (base-document-copied-to) the
    /// matched Goods Receipt PO, via Service Layer's standard base-document-reference mechanism —
    /// DocumentLines[].BaseType=20 (Goods Receipt PO object code), BaseEntry=the GRPO's DocEntry.
    ///
    /// PHASE 4 SIMPLIFICATION: BaseLine is hard-coded to 0 for every line — i.e. this always copies
    /// the GRPO's first line only. Real multi-line matching (copying each GRPO line the invoice
    /// actually covers, by its own BaseLine index) needs line-level data this phase's
    /// IThreeWayMatchService does not produce yet (it matches at the document/header level only —
    /// see MatchResult, which carries no line detail) and is a natural refinement once that exists.
    /// </summary>
    private static JsonNode BuildPurchaseInvoiceFromGrpoPayload(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("MatchedGrpoDocEntry", out var grpoDocEntryProp) || grpoDocEntryProp.ValueKind != JsonValueKind.Number)
        {
            throw new SapB1UnsupportedOperationException(
                "CreatePurchaseInvoiceFromGrpo outbox payload is missing a numeric MatchedGrpoDocEntry; cannot build the Service Layer request.");
        }
        var grpoDocEntry = grpoDocEntryProp.GetInt32();

        var cardCode = root.TryGetProperty("MatchedCardCode", out var cc) ? cc.GetString() : null;
        var invoiceNumber = root.TryGetProperty("InvoiceNumber", out var inv) ? inv.GetString() : null;

        var body = new JsonObject
        {
            ["DocDate"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            ["Comments"] = $"Created by Nexus Docs CAPTURE from ingested invoice {invoiceNumber ?? "(unknown number)"}, matched to GRPO DocEntry {grpoDocEntry}.",
            ["DocumentLines"] = new JsonArray
            {
                new JsonObject
                {
                    ["BaseType"] = 20, // Goods Receipt PO (B1 object code 20)
                    ["BaseEntry"] = grpoDocEntry,
                    ["BaseLine"] = 0, // Phase 4 simplification - see method doc comment.
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(cardCode))
        {
            body["CardCode"] = cardCode;
        }

        return body;
    }

    private static string DescribeLabel(int objectType, JsonNode node)
    {
        var docNum = node["DocNum"]?.ToString();
        var cardName = node["CardName"]?.ToString();
        var itemName = node["ItemName"]?.ToString();

        return (docNum, cardName, itemName) switch
        {
            (not null, not null, _) => $"Doc {docNum} - {cardName}",
            (not null, _, _) => $"Doc {docNum}",
            (_, not null, _) => cardName!,
            (_, _, not null) => itemName!,
            _ => $"{ObjectTypeToEntitySet.GetValueOrDefault(objectType, "Object")} record",
        };
    }

    // ---------------------------------------------------------------------------------------
    // Session / HTTP plumbing
    // ---------------------------------------------------------------------------------------

    private async Task<ErpConnection?> ResolveConnectionAsync(Guid tenantId)
    {
        var connection = await _db.ErpConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.IsActive && c.SystemType == ErpSystemType.SapBusinessOne);

        if (connection is null)
        {
            _logger.LogWarning("No active SAP Business One ErpConnection configured for tenant {TenantId}.", tenantId);
        }

        return connection;
    }

    private async Task<JsonNode?> SendAsync(ErpConnection connection, HttpMethod method, string path, JsonNode? body)
    {
        var session = await GetOrCreateSessionAsync(connection);
        var response = await SendWithSessionAsync(connection, session, method, path, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Session expired earlier than our cache assumed (idle timeout, server restart, ...).
            // Log in exactly once more and retry — never loop indefinitely on a 401.
            Sessions.TryRemove(connection.Id, out _);
            session = await GetOrCreateSessionAsync(connection);
            response = await SendWithSessionAsync(connection, session, method, path, body);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SapB1NotFoundException(path);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new SapB1RequestException(
                $"SAP B1 Service Layer returned {(int)response.StatusCode} for {method} {path}: {Truncate(errorBody, 1000)}");
        }

        if (response.Content.Headers.ContentLength is 0) return null;
        var responseBody = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(responseBody) ? null : JsonNode.Parse(responseBody);
    }

    private async Task<HttpResponseMessage> SendWithSessionAsync(
        ErpConnection connection, CachedSession session, HttpMethod method, string path, JsonNode? body)
    {
        var baseUrl = ResolveBaseUrl(connection);
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl), path));
        request.Headers.Add("Cookie", session.CookieHeader);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _httpClient.SendAsync(request);
    }

    private async Task<CachedSession> GetOrCreateSessionAsync(ErpConnection connection)
    {
        if (Sessions.TryGetValue(connection.Id, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached;
        }

        var password = await _secretStore.GetAsync(connection.TenantId, connection.CredentialsRef);
        if (password is null)
        {
            throw new SapB1RequestException(
                $"No credentials found for ErpConnection {connection.Id} (CredentialsRef '{connection.CredentialsRef}'). " +
                "Reconfigure the connection's password.");
        }

        var baseUrl = ResolveBaseUrl(connection);
        var loginBody = new JsonObject
        {
            ["CompanyDB"] = connection.CompanyDb,
            ["UserName"] = connection.UserName,
            ["Password"] = password,
        };

        var loginResponse = await _httpClient.PostAsJsonAsync(new Uri(new Uri(baseUrl), "/b1s/v1/Login"), loginBody);

        if (!loginResponse.IsSuccessStatusCode)
        {
            var errorBody = await loginResponse.Content.ReadAsStringAsync();
            throw new SapB1RequestException(
                $"SAP B1 Service Layer login failed for connection '{connection.Name}' ({(int)loginResponse.StatusCode}): {Truncate(errorBody, 1000)}");
        }

        // B1SESSION is required; ROUTEID is only present when B1 sits behind a load balancer with
        // sticky routing — both must be replayed together on every subsequent call when present.
        var cookies = loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.ToList()
            : [];

        var b1Session = ExtractCookieValue(cookies, "B1SESSION");
        if (b1Session is null)
        {
            throw new SapB1RequestException(
                $"SAP B1 Service Layer login for connection '{connection.Name}' succeeded but returned no B1SESSION cookie.");
        }

        var routeId = ExtractCookieValue(cookies, "ROUTEID");
        var cookieHeader = routeId is null ? $"B1SESSION={b1Session}" : $"B1SESSION={b1Session}; ROUTEID={routeId}";

        var session = new CachedSession(cookieHeader, DateTimeOffset.UtcNow.Add(SessionLifetime));
        Sessions[connection.Id] = session;
        return session;
    }

    private static string ResolveBaseUrl(ErpConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.BaseUrl)) return connection.BaseUrl.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(connection.GatewayId))
        {
            throw new SapB1RequestException(
                $"ErpConnection '{connection.Name}' routes through Nexus B1 Gateway '{connection.GatewayId}', " +
                "but gateway tunnelling is not implemented yet (ARCHITECTURE.md section 4.3). Set BaseUrl directly " +
                "for now (only viable when Service Layer is reachable without the gateway).");
        }

        throw new SapB1RequestException($"ErpConnection '{connection.Name}' has neither BaseUrl nor GatewayId set.");
    }

    private static string? ExtractCookieValue(IEnumerable<string> setCookieHeaders, string cookieName)
    {
        foreach (var header in setCookieHeaders)
        {
            var firstSegment = header.Split(';', 2)[0];
            var eq = firstSegment.IndexOf('=');
            if (eq <= 0) continue;

            var name = firstSegment[..eq].Trim();
            if (string.Equals(name, cookieName, StringComparison.OrdinalIgnoreCase))
            {
                return firstSegment[(eq + 1)..].Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    private sealed record CachedSession(string CookieHeader, DateTimeOffset ExpiresAt);
}

public class SapB1RequestException(string message) : Exception(message);

public class SapB1NotFoundException(string path) : Exception($"Not found: {path}");

public class SapB1UnsupportedOperationException(string message) : Exception(message);
