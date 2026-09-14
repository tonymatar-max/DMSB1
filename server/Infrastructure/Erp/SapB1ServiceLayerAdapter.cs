namespace NexusDocs.Api.Infrastructure.Erp;

/// <summary>
/// SAP Business One Service Layer implementation of <see cref="IErpAdapter"/>
/// (ARCHITECTURE.md §4.2-4.3). This phase only stands up the DI-registerable shape so the outbox
/// worker and lookup callers have a concrete target to depend on; the actual Service Layer calls
/// are implemented in a later phase.
/// </summary>
public class SapB1ServiceLayerAdapter : IErpAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public string SystemType => "SapBusinessOne";

    public SapB1ServiceLayerAdapter(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // TODO (later phase): Service Layer session-cookie login flow.
    //
    // POST {ServiceLayerBaseUrl}/b1s/v1/Login with a JSON body of
    //   { "CompanyDB": ..., "UserName": ..., "Password": ... }
    // (per-tenant B1 connection settings, credentials pulled from the tenant's ERP connection
    // config rather than app settings). The response sets a `B1SESSION` cookie plus a
    // `ROUTEID` cookie when B1 sits behind a load balancer; both must be captured and replayed on
    // every subsequent Service Layer call for this session. Sessions expire (default ~30 minutes
    // of inactivity, configurable server-side) and must be re-established (re-POST /Login) on a
    // 401, not merely on a fixed timer. On-prem estates route this call — and every other Service
    // Layer call — through the outbound Nexus B1 Gateway tunnel described in ARCHITECTURE.md §4.3
    // rather than dialling the customer's Service Layer endpoint directly.

    public Task<ErpLookupResult?> LookupObjectAsync(Guid tenantId, int objectType, string externalKey)
    {
        throw new NotImplementedException(
            "SAP B1 Service Layer object lookup is implemented in a later phase.");
    }

    public Task PushOutboxItemAsync(Guid tenantId, Guid outboxItemId)
    {
        throw new NotImplementedException(
            "SAP B1 Service Layer outbox push is implemented in a later phase.");
    }
}
