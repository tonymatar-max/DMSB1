using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

/// <summary>
/// Append-only, hash-chained audit trail (see ARCHITECTURE.md 2.5). <see cref="Hash"/> =
/// SHA256(<see cref="PrevHash"/> || canonical(<see cref="PayloadJson"/>)), so any retroactive edit
/// of history is detectable. Rows are never updated or deleted once written.
/// </summary>
/// <remarks>
/// Does not derive from <see cref="BaseEntity"/>: <see cref="Id"/> here is a strictly increasing
/// database-identity <see cref="long"/>, not a <see cref="Guid"/> — the chain relies on a total
/// order, which an identity column gives for free and a Guid does not.
/// </remarks>
public class AuditEvent : ITenantScoped
{
    /// <summary>Identity column; also the row's insertion order.</summary>
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Per-tenant sequence number of this event in the hash chain (1, 2, 3, ...).</summary>
    public long Seq { get; set; }

    /// <summary>User id (as a string) or "system" for background-worker-initiated events.</summary>
    public string Actor { get; set; } = "";

    /// <summary>e.g. "Document.Created", "TenantLicense.Updated".</summary>
    public string Action { get; set; } = "";

    /// <summary>e.g. "Document/{id}" — the entity the event describes.</summary>
    public string Subject { get; set; } = "";

    /// <summary>Canonical JSON of the change payload; this is what gets hashed.</summary>
    public string PayloadJson { get; set; } = "";

    /// <summary>Hash of the previous event in this tenant's chain ("" for Seq == 1).</summary>
    public string PrevHash { get; set; } = "";

    /// <summary>SHA256(PrevHash || canonical(PayloadJson)).</summary>
    public string Hash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
