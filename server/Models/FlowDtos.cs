using NexusDocs.Api.Domain.Flow;

namespace NexusDocs.Api.Models;

// NOTE: this file may also be written to by another agent working on the FLOW module (e.g. a
// WorkflowDefinitions/designer controller). Only the DTOs needed by WorkflowInstancesController /
// RequisitionsController are defined here; a build-fix stage reconciles any duplicate-name clash.

/// <summary>One row of the current user's approval inbox.</summary>
public class InboxItemDto
{
    public Guid ApprovalTaskId { get; set; }
    public Guid StageInstanceId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string? StageName { get; set; }
    public Guid? OriginalAssigneeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public bool IsOverdue { get; set; }
    /// <summary>How long the task has been pending, in hours, computed server-side against UtcNow.</summary>
    public double PendingHours { get; set; }
}

public class DecideRequest
{
    public DecisionOutcome Outcome { get; set; }
    public string? Comment { get; set; }
}

public class AddCommentRequest
{
    public string Body { get; set; } = string.Empty;
}

public class CommentDto
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class DecisionDto
{
    public Guid Id { get; set; }
    public Guid ApprovalTaskId { get; set; }
    public Guid ActorId { get; set; }
    public DecisionOutcome Outcome { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

public class ApprovalTaskDto
{
    public Guid Id { get; set; }
    public Guid AssigneeId { get; set; }
    public Guid? OriginalAssigneeId { get; set; }
    public ApprovalTaskStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DecisionDto> Decisions { get; set; } = new();
}

public class StageInstanceDto
{
    public Guid Id { get; set; }
    public Guid StageDefinitionId { get; set; }
    public string? StageName { get; set; }
    public StageInstanceStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<ApprovalTaskDto> Tasks { get; set; } = new();
}

/// <summary>Full detail of a workflow instance: stages/tasks/decisions/comments, for a timeline view.</summary>
public class WorkflowInstanceDetailDto
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public Guid InitiatorId { get; set; }
    public WorkflowInstanceStatus Status { get; set; }
    public Guid? CurrentStageDefinitionId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<StageInstanceDto> Stages { get; set; } = new();
    public List<CommentDto> Comments { get; set; } = new();
}

public class CreateDelegationRequest
{
    public Guid DelegateUserId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Reason { get; set; }
}

public class DelegationDto
{
    public Guid Id { get; set; }
    public Guid PrincipalUserId { get; set; }
    public Guid DelegateUserId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; }
}

// ---- WorkflowDefinitionsController DTOs (design-time CRUD) ----

public record WorkflowDefinitionSummaryDto(
    Guid Id,
    Guid WorkflowFamilyId,
    string Name,
    Guid? DocumentTypeId,
    int Version,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record WorkflowDefinitionDetailDto(
    Guid Id,
    Guid WorkflowFamilyId,
    string Name,
    Guid? DocumentTypeId,
    int Version,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StageDefinitionDto> Stages);

public record StageDefinitionDto(
    Guid Id,
    string Name,
    int SortOrder,
    ApprovalMode ApprovalMode,
    int? QuorumCount,
    int? SlaHours,
    Guid? EscalateToApproverSpecId,
    IReadOnlyList<ApproverSpecDto> Approvers,
    IReadOnlyList<RoutingRuleDto> OutgoingRules);

public record ApproverSpecDto(
    Guid Id,
    ApproverKind Kind,
    Guid? NamedUserId,
    Guid? RoleId,
    int? ManagerHierarchyLevels,
    string? ErpOwnerField,
    string? FieldExpression);

public record RoutingRuleDto(
    Guid Id,
    string? ConditionFieldCode,
    RoutingConditionOperator ConditionOperator,
    string? ConditionValue,
    string? TargetStageName,
    int Priority);

/// <summary>
/// Request body for creating a new workflow definition (POST) or a new version of an existing
/// one (POST .../new-version). Stages are created first, then <see cref="RoutingRuleRequest.TargetStageName"/>
/// references are resolved to StageDefinitionIds within the same request.
/// </summary>
public class WorkflowDefinitionRequest
{
    public required string Name { get; set; }

    public Guid? DocumentTypeId { get; set; }

    public required List<StageDefinitionRequest> Stages { get; set; }
}

public class StageDefinitionRequest
{
    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public ApprovalMode ApprovalMode { get; set; }

    public int? QuorumCount { get; set; }

    public int? SlaHours { get; set; }

    public List<ApproverSpecRequest> Approvers { get; set; } = new();

    public List<RoutingRuleRequest> OutgoingRules { get; set; } = new();
}

public class ApproverSpecRequest
{
    public ApproverKind Kind { get; set; }

    public Guid? NamedUserId { get; set; }

    public Guid? RoleId { get; set; }

    public int? ManagerHierarchyLevels { get; set; }

    public string? ErpOwnerField { get; set; }

    public string? FieldExpression { get; set; }
}

public class RoutingRuleRequest
{
    public string? ConditionFieldCode { get; set; }

    public RoutingConditionOperator ConditionOperator { get; set; }

    public string? ConditionValue { get; set; }

    /// <summary>Name of the stage (within this same request) this rule routes to. Null/absent means "end workflow".</summary>
    public string? TargetStageName { get; set; }

    public int Priority { get; set; }
}
