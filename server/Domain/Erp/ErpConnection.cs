using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Erp;

/// <summary>
/// The ERP systems a Nexus Docs tenant knows how to talk to. Only SAP Business One is
/// implemented in Phase 1, but this is an enum (rather than a hard-coded assumption) so
/// D365 Business Central, QuickBooks and others can be added later without reshaping the
/// link key or the outbox.
/// </summary>
public enum ErpSystemType
{
    SapBusinessOne = 0,
}

/// <summary>
/// A configured connection to one ERP company database. A tenant may hold several of these
/// (e.g. one SAP B1 CompanyDb per legal entity). Credentials are never stored here — only a
/// reference key into whatever secret store the deployment uses (Azure Key Vault, the Nexus
/// B1 Gateway's local vault, etc.). See ARCHITECTURE.md section 4.3 (Nexus B1 Gateway) and
/// section 4.1 (the link key that ties archived documents back to objects in this connection).
/// </summary>
public class ErpConnection : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public ErpSystemType SystemType { get; set; } = ErpSystemType.SapBusinessOne;

    /// <summary>Human-readable name shown in admin UI, e.g. "SAP B1 - Kuwait Live".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The B1 CompanyDb this connection targets.</summary>
    public string CompanyDb { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the Nexus B1 Gateway instance that proxies Service Layer / read-only SQL
    /// calls for this connection, when the customer site is behind a firewall. Null when
    /// <see cref="BaseUrl"/> is used instead (direct, unproxied Service Layer access).
    /// </summary>
    public string? GatewayId { get; set; }

    /// <summary>
    /// Direct Service Layer base URL, used only when no gateway is required (e.g. the B1
    /// Service Layer is already reachable from the Nexus API, such as a cloud-hosted B1).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Key into the secret store holding the actual Service Layer credentials. Never the raw
    /// username/password/token itself.
    /// </summary>
    public string CredentialsRef { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
