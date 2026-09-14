using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>Lifecycle status of a running workflow instance.</summary>
public enum WorkflowInstanceStatus
{
    Running = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

/// <summary>
/// One in-flight (or completed) approval workflow, tracking a single subject (e.g. a
/// <see cref="Requisition"/> or a document) through its approval stages (ARCHITECTURE.md
/// section 5).
/// </summary>
public class WorkflowInstance : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>FK to the WorkflowDefinition this instance was started from.</summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Discriminator for what is being approved, e.g. "Requisition" or "Document".
    /// </summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>The id of the Requisition/Document/etc. row this instance is approving.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>The User who initiated this workflow.</summary>
    public Guid InitiatorId { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;

    /// <summary>FK to the StageDefinition currently active, if any.</summary>
    public Guid? CurrentStageDefinitionId { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<StageInstance> Stages { get; set; } = new List<StageInstance>();

    public ICollection<CommentThread> Comments { get; set; } = new List<CommentThread>();
}
