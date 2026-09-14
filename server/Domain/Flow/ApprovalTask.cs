using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>Lifecycle status of a single approver's task within a stage instance.</summary>
public enum ApprovalTaskStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Reassigned = 3,
}

/// <summary>
/// A single approver's assignment within a <see cref="StageInstance"/>. When delegation applies,
/// <see cref="AssigneeId"/> is the delegate who must actually act while
/// <see cref="OriginalAssigneeId"/> keeps the principal on record, per ARCHITECTURE.md section 5
/// "Accountability".
/// </summary>
public class ApprovalTask : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid StageInstanceId { get; set; }

    /// <summary>The resolved approver (User) who must act on this task — the delegate if delegation applied.</summary>
    public Guid AssigneeId { get; set; }

    /// <summary>
    /// Set when this task exists because of a delegation: the principal user who was originally
    /// the approver, before the delegate took over.
    /// </summary>
    public Guid? OriginalAssigneeId { get; set; }

    public ApprovalTaskStatus Status { get; set; } = ApprovalTaskStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<Decision> Decisions { get; set; } = new List<Decision>();
}
