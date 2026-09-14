using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

public enum RoutingConditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains
}

/// <summary>
/// A conditional transition out of a stage (or out of the workflow start, when
/// <see cref="FromStageDefinitionId"/> is null). Rules for a given source are evaluated in
/// ascending <see cref="Priority"/> order; the first whose condition matches wins.
/// </summary>
public class RoutingRule : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Null means this rule is evaluated when starting the workflow, before any stage has run.
    /// </summary>
    public Guid? FromStageDefinitionId { get; set; }

    /// <summary>An index field code or a well-known field name (e.g. "Amount"). Null means "always match".</summary>
    public string? ConditionFieldCode { get; set; }

    public RoutingConditionOperator ConditionOperator { get; set; }

    public string? ConditionValue { get; set; }

    /// <summary>Null means "route to completion / end the workflow".</summary>
    public Guid? TargetStageDefinitionId { get; set; }

    /// <summary>Rules are evaluated in ascending priority order; first match wins.</summary>
    public int Priority { get; set; }
}
