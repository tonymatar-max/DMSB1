using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>The outcome an approver recorded for an <see cref="ApprovalTask"/>.</summary>
public enum DecisionOutcome
{
    Approved = 0,
    Rejected = 1,
}

/// <summary>
/// The recorded act of an approver deciding on an <see cref="ApprovalTask"/>. One task normally
/// has a single decision, but the entity stands on its own so the audit trail is explicit.
/// </summary>
public class Decision : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid ApprovalTaskId { get; set; }

    /// <summary>The User who actually made this decision.</summary>
    public Guid ActorId { get; set; }

    public DecisionOutcome Outcome { get; set; }

    /// <summary>
    /// Free-text comment. Mandatory only when <see cref="Outcome"/> is Rejected; not enforced
    /// here — the API layer validates that.
    /// </summary>
    public string? Comment { get; set; }

    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}
