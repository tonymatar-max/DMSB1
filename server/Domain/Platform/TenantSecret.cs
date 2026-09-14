using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

/// <summary>
/// One tenant-scoped secret value (e.g. an SAP B1 Service Layer password), encrypted at rest via
/// <see cref="Infrastructure.Secrets.ISecretStore"/>. An <c>ErpConnection.CredentialsRef</c> is a
/// <see cref="Key"/> into this table — the connection row itself never holds the raw credential.
///
/// <see cref="EncryptedValue"/> is protected with ASP.NET Core Data Protection
/// (<c>IDataProtector</c>), the same mechanism the host already uses for its own key ring (see the
/// "DataProtection-Keys" log line on startup). This is a reasonable default for a single-host or
/// small on-prem deployment; a multi-instance cloud deployment should point Data Protection at a
/// shared key ring (Azure Blob/Key Vault, or a Redis-backed store) rather than the per-machine
/// default, or swap <see cref="Infrastructure.Secrets.ISecretStore"/> for a real KMS-backed
/// implementation — see ARCHITECTURE.md section 2.5's "signing keys never touch application disk"
/// guidance, which applies here too once this goes beyond local development.
/// </summary>
public class TenantSecret : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Opaque key the owning row (e.g. ErpConnection.CredentialsRef) stores.</summary>
    public string Key { get; set; } = string.Empty;

    public string EncryptedValue { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
