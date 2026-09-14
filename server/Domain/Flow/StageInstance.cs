using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>Lifecycle status of a single stage within a workflow instance.</summary>
public enum StageInstanceStatus
{
    Pending = 0,
    Active = 1,
    Approved = 2,
    Rejected = 3,
    Skipped = 4,
}

/// <summary>
/// One stage of a <see cref="WorkflowInstance"/> as it actually ran (as opposed to the
/// StageDefinition template it was created from).
/// </summary>
public class StageInstance : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid WorkflowInstanceId { get; set; }

    /// <summary>FK to the StageDefinition this stage was created from.</summary>
    public Guid StageDefinitionId { get; set; }

    public StageInstanceStatus Status { get; set; } = StageInstanceStatus.Pending;

    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// StartedAt + the stage definition's SlaHours. Computed by the workflow engine when the
    /// stage starts, not here.
    /// </summary>
    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? EscalatedAt { get; set; }

    public ICollection<ApprovalTask> Tasks { get; set; } = new List<ApprovalTask>();
}
