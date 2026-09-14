using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>
/// A standing rule that a principal user's approvals should be routed to a delegate for a period
/// of time (e.g. while on leave). Resolved by the workflow engine when assigning
/// <see cref="ApprovalTask"/>s, which records both principal and delegate for accountability.
/// </summary>
public class Delegation : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid PrincipalUserId { get; set; }

    public Guid DelegateUserId { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Null means indefinite, until revoked.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    public string? Reason { get; set; }

    public bool IsActive { get; set; } = true;
}
