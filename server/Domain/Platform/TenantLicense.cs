using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

public enum TenantLicenseStatus
{
    Active = 0,
    Expired = 1,
    Suspended = 2,
}

/// <summary>
/// One module's entitlement for a tenant (see ARCHITECTURE.md 2.4). The authoritative check
/// lives in <c>LicenseService</c> (IsModuleEnabled,
/// IsFeatureEnabled, AssertSeatAvailable, AssertEnvelopeQuota, GetEntitlements) and is enforced
/// server-side via <c>[RequiresModule]</c>. <see cref="Signature"/> is an RSA signature over the
/// licence payload so on-prem installs can validate a licence offline, without phoning home.
/// </summary>
public class TenantLicense : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    /// <summary>e.g. "CORE", "ARCHIVE" in Phase 1; "FLOW", "SIGN", "CAPTURE", "ERP" later.</summary>
    public string ModuleCode { get; set; } = "";

    /// <summary>e.g. "Standard", "Professional" — module-specific tiering.</summary>
    public string Edition { get; set; } = "";

    public int? SeatsLicensed { get; set; }
    public int? StorageQuotaGb { get; set; }

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }

    public TenantLicenseStatus Status { get; set; } = TenantLicenseStatus.Active;

    /// <summary>RSA signature over the canonical licence payload; enables offline validation.</summary>
    public string Signature { get; set; } = "";
}
