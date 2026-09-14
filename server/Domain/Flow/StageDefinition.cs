using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

public enum ApprovalMode
{
    All,
    Any,
    Quorum
}

/// <summary>
/// One approval stage within a <see cref="WorkflowDefinition"/>, in <see cref="SortOrder"/> order.
/// </summary>
public class StageDefinition : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid WorkflowDefinitionId { get; set; }

    public int SortOrder { get; set; }

    public required string Name { get; set; }

    public ApprovalMode ApprovalMode { get; set; }

    /// <summary>Only meaningful when <see cref="ApprovalMode"/> is <see cref="Flow.ApprovalMode.Quorum"/>.</summary>
    public int? QuorumCount { get; set; }

    /// <summary>Per-stage SLA duration, in hours. Null means no SLA is enforced for this stage.</summary>
    public int? SlaHours { get; set; }

    /// <summary>
    /// FK to an <see cref="ApproverSpec"/> used as the SLA escalation fallback when this stage's
    /// SLA is breached. Null means no escalation is configured.
    /// </summary>
    public Guid? EscalateToApproverSpecId { get; set; }

    public ICollection<ApproverSpec> Approvers { get; set; } = new List<ApproverSpec>();

    public ICollection<RoutingRule> OutgoingRules { get; set; } = new List<RoutingRule>();
}
