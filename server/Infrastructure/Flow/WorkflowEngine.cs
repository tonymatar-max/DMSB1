using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Erp;
using NexusDocs.Api.Domain.Flow;
using NexusDocs.Api.Domain.Platform;
using NexusDocs.Api.Infrastructure.Audit;

namespace NexusDocs.Api.Infrastructure.Flow;

/// <summary>
/// The FLOW approval engine (ARCHITECTURE.md section 5): starts workflow instances, resolves
/// approvers for a stage (including Role/Group membership, manager-chain walks and active
/// delegations), records decisions, advances stages via <see cref="RoutingRule"/> evaluation, and
/// — on final approval of a Requisition — enqueues the <see cref="IntegrationOutbox"/> row that
/// Phase 3+'s ERP dispatcher will pick up, in the same SaveChanges call as the decision that
/// completed the instance (ARCHITECTURE.md section 5's "same transaction as the decision"
/// guarantee, satisfied here by EF Core's own change-tracking rather than an explicit
/// transaction, since this class never calls SaveChangesAsync more than once per public method).
///
/// NOTE on the Sqlite/query-filter DateTimeOffset bug documented at the top of this task's brief:
/// every query in this file that needs a DateTimeOffset range comparison against "now" narrows to
/// simple equality/FK predicates in SQL first (`.Where(...).ToListAsync()`), then does the range
/// check in memory on the materialized list, following the pattern in
/// server/Infrastructure/Licensing/LicenseService.cs.
/// </summary>
public class WorkflowEngine(
    NexusDocsDbContext db,
    AuditService audit,
    ILogger<WorkflowEngine> logger)
{
    // ---------------------------------------------------------------------------------------
    // Start
    // ---------------------------------------------------------------------------------------

    public async Task<WorkflowInstance> StartInstanceAsync(
        Guid tenantId,
        Guid workflowDefinitionId,
        string subjectType,
        Guid subjectId,
        Guid initiatorId)
    {
        var instance = new WorkflowInstance
        {
            TenantId = tenantId,
            WorkflowDefinitionId = workflowDefinitionId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            InitiatorId = initiatorId,
            Status = WorkflowInstanceStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.WorkflowInstances.Add(instance);

        var firstStage = await ResolveNextStageAsync(tenantId, workflowDefinitionId, fromStageDefinitionId: null, subjectType, subjectId);
        if (firstStage is null)
        {
            // No stages configured at all / no routing rule and no stages to fall back to:
            // nothing to run, the instance completes immediately with nothing approved.
            logger.LogWarning(
                "WorkflowDefinition {WorkflowDefinitionId} has no starting stage; instance {InstanceId} completed with no stages.",
                workflowDefinitionId, instance.Id);
            instance.Status = WorkflowInstanceStatus.Approved;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            await CompleteInstanceSubjectSideEffectsAsync(instance);
            await db.SaveChangesAsync();
            return instance;
        }

        instance.CurrentStageDefinitionId = firstStage.Id;

        await CreateStageInstanceAsync(tenantId, instance, firstStage, subjectId, initiatorId);

        await db.SaveChangesAsync();
        return instance;
    }

    /// <summary>
    /// Creates and activates the StageInstance for <paramref name="stageDefinition"/> under
    /// <paramref name="instance"/>, plus one Pending ApprovalTask per resolved approver.
    /// </summary>
    private async Task<StageInstance> CreateStageInstanceAsync(
        Guid tenantId,
        WorkflowInstance instance,
        StageDefinition stageDefinition,
        Guid subjectId,
        Guid initiatorId)
    {
        var now = DateTimeOffset.UtcNow;
        var stageInstance = new StageInstance
        {
            TenantId = tenantId,
            WorkflowInstanceId = instance.Id,
            StageDefinitionId = stageDefinition.Id,
            Status = StageInstanceStatus.Active,
            StartedAt = now,
            DueAt = stageDefinition.SlaHours is { } slaHours ? now.AddHours(slaHours) : null,
        };
        db.StageInstances.Add(stageInstance);
        instance.Stages.Add(stageInstance);

        var approverIds = await ResolveApproversAsync(tenantId, stageDefinition.Id, subjectId, initiatorId);
        foreach (var (assigneeId, originalAssigneeId) in approverIds)
        {
            var task = new ApprovalTask
            {
                TenantId = tenantId,
                StageInstanceId = stageInstance.Id,
                AssigneeId = assigneeId,
                OriginalAssigneeId = originalAssigneeId,
                Status = ApprovalTaskStatus.Pending,
            };
            db.ApprovalTasks.Add(task);
            stageInstance.Tasks.Add(task);
        }

        if (approverIds.Count == 0)
        {
            logger.LogWarning(
                "StageDefinition {StageDefinitionId} resolved zero approvers for instance {InstanceId}; the stage has no ApprovalTask and can never be decided.",
                stageDefinition.Id, instance.Id);
        }

        return stageInstance;
    }

    // ---------------------------------------------------------------------------------------
    // Approver resolution
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the set of (AssigneeId, OriginalAssigneeId) pairs for every <see cref="ApproverSpec"/>
    /// on <paramref name="stageDefinitionId"/>, with active <see cref="Delegation"/> rules applied.
    /// OriginalAssigneeId is null unless a delegation substituted the delegate for the principal.
    /// </summary>
    public async Task<List<(Guid AssigneeId, Guid? OriginalAssigneeId)>> ResolveApproversAsync(
        Guid tenantId,
        Guid stageDefinitionId,
        Guid subjectId,
        Guid initiatorId)
    {
        var specs = await db.ApproverSpecs
            .Where(s => s.TenantId == tenantId && s.StageDefinitionId == stageDefinitionId)
            .ToListAsync();

        var principals = new List<Guid>();

        foreach (var spec in specs)
        {
            switch (spec.Kind)
            {
                case ApproverKind.NamedUser:
                    if (spec.NamedUserId is { } namedUserId)
                        principals.Add(namedUserId);
                    else
                        logger.LogWarning("ApproverSpec {SpecId} is Kind=NamedUser with no NamedUserId.", spec.Id);
                    break;

                case ApproverKind.Role:
                    if (spec.RoleId is { } roleId)
                        principals.AddRange(await ResolveRoleMembersAsync(tenantId, roleId));
                    else
                        logger.LogWarning("ApproverSpec {SpecId} is Kind=Role with no RoleId.", spec.Id);
                    break;

                case ApproverKind.Group:
                    // Phase 2 stub: a "Group" is not modelled as its own entity yet, so it is
                    // resolved the same way a Role is (via RoleId). Real group membership is out
                    // of scope until Phase 3+.
                    if (spec.RoleId is { } groupRoleId)
                        principals.AddRange(await ResolveRoleMembersAsync(tenantId, groupRoleId));
                    else
                        logger.LogWarning("ApproverSpec {SpecId} is Kind=Group with no RoleId (Group stub reuses RoleId).", spec.Id);
                    break;

                case ApproverKind.ManagerOfInitiator:
                    var levels = spec.ManagerHierarchyLevels ?? 1;
                    var manager = await WalkManagerChainAsync(tenantId, initiatorId, levels);
                    if (manager is { } managerId)
                        principals.Add(managerId);
                    break;

                case ApproverKind.OwnerOfLinkedErpObject:
                case ApproverKind.FieldExpression:
                    // Not resolvable without ERP/index-field context that doesn't exist in a
                    // generic form yet; Phase 3+ wires these through the ERP adapter and document
                    // index values.
                    logger.LogWarning(
                        "ApproverSpec {SpecId} has Kind={Kind}, which is not yet implemented; resolving to zero approvers.",
                        spec.Id, spec.Kind);
                    break;

                default:
                    logger.LogWarning("ApproverSpec {SpecId} has unhandled Kind={Kind}.", spec.Id, spec.Kind);
                    break;
            }
        }

        principals = principals.Distinct().ToList();
        if (principals.Count == 0)
            return new List<(Guid, Guid?)>();

        var activeDelegations = await db.Delegations
            .Where(d => d.TenantId == tenantId && d.IsActive && principals.Contains(d.PrincipalUserId))
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var result = new List<(Guid, Guid?)>();
        foreach (var principalId in principals)
        {
            var applicable = activeDelegations
                .Where(d => d.PrincipalUserId == principalId && d.StartsAt <= now && (d.EndsAt is null || now <= d.EndsAt))
                .ToList();

            if (applicable.Count > 0)
            {
                // If more than one active delegation somehow applies, take the most recently
                // started one as the current standing delegate.
                var delegation = applicable.OrderByDescending(d => d.StartsAt).First();
                result.Add((delegation.DelegateUserId, principalId));
            }
            else
            {
                result.Add((principalId, null));
            }
        }

        return result;
    }

    private async Task<List<Guid>> ResolveRoleMembersAsync(Guid tenantId, Guid roleId)
    {
        return await db.UserRoles
            .Where(ur => ur.TenantId == tenantId && ur.RoleId == roleId)
            .Join(db.Users.Where(u => u.TenantId == tenantId && u.IsActive),
                ur => ur.UserId, u => u.Id, (ur, u) => u.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Walks <see cref="User.ManagerId"/> up <paramref name="levels"/> steps starting from
    /// <paramref name="startUserId"/>. Returns null (and logs a warning) if the chain runs out
    /// before reaching the requested depth, rather than throwing.
    /// </summary>
    private async Task<Guid?> WalkManagerChainAsync(Guid tenantId, Guid startUserId, int levels)
    {
        if (levels <= 0)
        {
            logger.LogWarning("ManagerOfInitiator resolution requested with non-positive levels={Levels} for user {UserId}.", levels, startUserId);
            return null;
        }

        var currentUserId = startUserId;
        for (var i = 0; i < levels; i++)
        {
            var current = await db.Users
                .Where(u => u.TenantId == tenantId && u.Id == currentUserId)
                .Select(u => new { u.ManagerId })
                .FirstOrDefaultAsync();

            if (current?.ManagerId is not { } managerId)
            {
                logger.LogWarning(
                    "Manager chain from user {StartUserId} is broken/too short at depth {Depth} of requested {Levels}; resolving to no approver.",
                    startUserId, i, levels);
                return null;
            }

            currentUserId = managerId;
        }

        return currentUserId;
    }

    // ---------------------------------------------------------------------------------------
    // Decisions
    // ---------------------------------------------------------------------------------------

    public async Task<Decision> DecideAsync(
        Guid tenantId,
        Guid approvalTaskId,
        Guid actorId,
        DecisionOutcome outcome,
        string? comment)
    {
        if (outcome == DecisionOutcome.Rejected && string.IsNullOrWhiteSpace(comment))
            throw new ValidationException("A comment is required when rejecting an approval task.");

        var task = await db.ApprovalTasks
            .Include(t => t.Decisions)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == approvalTaskId)
            ?? throw new ValidationException($"ApprovalTask {approvalTaskId} was not found.");

        if (task.AssigneeId != actorId)
            throw new ValidationException("Only the task's current assignee may decide it.");

        if (task.Status != ApprovalTaskStatus.Pending)
            throw new ValidationException($"ApprovalTask {approvalTaskId} has already been decided (status {task.Status}).");

        var decision = new Decision
        {
            TenantId = tenantId,
            ApprovalTaskId = task.Id,
            ActorId = actorId,
            Outcome = outcome,
            Comment = comment,
        };
        db.Decisions.Add(decision);

        task.Status = outcome == DecisionOutcome.Approved ? ApprovalTaskStatus.Approved : ApprovalTaskStatus.Rejected;
        task.CompletedAt = DateTimeOffset.UtcNow;

        var stageInstance = await db.StageInstances
            .Include(s => s.Tasks)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == task.StageInstanceId)
            ?? throw new ValidationException($"StageInstance for ApprovalTask {approvalTaskId} was not found.");

        var stageDefinition = await db.StageDefinitions
            .FirstOrDefaultAsync(sd => sd.TenantId == tenantId && sd.Id == stageInstance.StageDefinitionId)
            ?? throw new ValidationException($"StageDefinition {stageInstance.StageDefinitionId} was not found.");

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == stageInstance.WorkflowInstanceId)
            ?? throw new ValidationException($"WorkflowInstance {stageInstance.WorkflowInstanceId} was not found.");

        await audit.RecordAsync(tenantId, actorId.ToString(), "flow.decision", $"ApprovalTask/{task.Id}", new
        {
            task.Id,
            task.StageInstanceId,
            ActorId = actorId,
            Outcome = outcome.ToString(),
            Comment = comment,
        });

        var stageOutcome = EvaluateStageOutcome(stageDefinition, stageInstance);
        if (stageOutcome is null)
        {
            // Stage is still pending further decisions; nothing else to do.
            await db.SaveChangesAsync();
            return decision;
        }

        stageInstance.CompletedAt = DateTimeOffset.UtcNow;

        if (stageOutcome == StageInstanceStatus.Rejected)
        {
            stageInstance.Status = StageInstanceStatus.Rejected;
            instance.Status = WorkflowInstanceStatus.Rejected;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            instance.CurrentStageDefinitionId = null;

            await audit.RecordAsync(tenantId, actorId.ToString(), "flow.instance_completed", $"WorkflowInstance/{instance.Id}", new
            {
                instance.Id,
                Status = instance.Status.ToString(),
            });

            await db.SaveChangesAsync();
            return decision;
        }

        // Stage approved: route to the next stage, or complete the instance.
        stageInstance.Status = StageInstanceStatus.Approved;

        var nextStage = await ResolveNextStageAsync(
            tenantId, instance.WorkflowDefinitionId, fromStageDefinitionId: stageDefinition.Id,
            instance.SubjectType, instance.SubjectId);

        if (nextStage is null)
        {
            instance.Status = WorkflowInstanceStatus.Approved;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            instance.CurrentStageDefinitionId = null;

            await CompleteInstanceSubjectSideEffectsAsync(instance);

            await audit.RecordAsync(tenantId, actorId.ToString(), "flow.instance_completed", $"WorkflowInstance/{instance.Id}", new
            {
                instance.Id,
                Status = instance.Status.ToString(),
            });
        }
        else
        {
            instance.CurrentStageDefinitionId = nextStage.Id;
            await CreateStageInstanceAsync(tenantId, instance, nextStage, instance.SubjectId, instance.InitiatorId);
        }

        await db.SaveChangesAsync();
        return decision;
    }

    /// <summary>
    /// Returns the resolved StageInstanceStatus (Approved or Rejected) once the stage's
    /// ApprovalMode condition is satisfied by the tasks decided so far, or null while the stage
    /// is still awaiting more decisions. A single Rejected task always rejects the stage
    /// immediately regardless of ApprovalMode.
    /// </summary>
    private static StageInstanceStatus? EvaluateStageOutcome(StageDefinition stageDefinition, StageInstance stageInstance)
    {
        var tasks = stageInstance.Tasks;
        if (tasks.Any(t => t.Status == ApprovalTaskStatus.Rejected))
            return StageInstanceStatus.Rejected;

        var approvedCount = tasks.Count(t => t.Status == ApprovalTaskStatus.Approved);

        var satisfied = stageDefinition.ApprovalMode switch
        {
            ApprovalMode.All => tasks.Count > 0 && tasks.All(t => t.Status == ApprovalTaskStatus.Approved),
            ApprovalMode.Any => approvedCount >= 1,
            ApprovalMode.Quorum => approvedCount >= (stageDefinition.QuorumCount ?? tasks.Count),
            _ => false,
        };

        return satisfied ? StageInstanceStatus.Approved : null;
    }

    /// <summary>
    /// Evaluates the RoutingRules whose FromStageDefinitionId matches (null for the workflow's
    /// starting rules), in ascending Priority order, returning the target StageDefinition of the
    /// first matching rule. Falls back to the lowest-SortOrder StageDefinition of the workflow
    /// when no rule matches (start only, per the task brief); for a from-stage evaluation with no
    /// matching rule, returns null (workflow completes) since that is the documented meaning of a
    /// rule with a null TargetStageDefinitionId and there is no other fallback to apply mid-flow.
    /// </summary>
    private async Task<StageDefinition?> ResolveNextStageAsync(
        Guid tenantId,
        Guid workflowDefinitionId,
        Guid? fromStageDefinitionId,
        string subjectType,
        Guid subjectId)
    {
        var rules = await db.RoutingRules
            .Where(r => r.TenantId == tenantId && r.FromStageDefinitionId == fromStageDefinitionId)
            .OrderBy(r => r.Priority)
            .ToListAsync();

        // Only rules that actually belong to this workflow definition's stages are relevant; a
        // starting rule (FromStageDefinitionId == null) has no stage to check against, so for
        // that case we filter by loading the workflow's stage ids and matching the rule's target
        // (routing rules are otherwise unscoped by workflow in the entity model as given).
        Requisition? requisition = null;
        if (subjectType == "Requisition")
        {
            requisition = await db.Requisitions.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == subjectId);
        }

        foreach (var rule in rules)
        {
            if (EvaluateRoutingCondition(rule, requisition))
            {
                if (rule.TargetStageDefinitionId is not { } targetId)
                    return null; // explicit "route to completion"

                var target = await db.StageDefinitions.FirstOrDefaultAsync(sd => sd.TenantId == tenantId && sd.Id == targetId);
                if (target is not null)
                    return target;

                logger.LogWarning("RoutingRule {RuleId} targets StageDefinition {TargetId}, which was not found.", rule.Id, targetId);
                return null;
            }
        }

        if (fromStageDefinitionId is not null)
        {
            // Mid-flow with no matching rule: nothing configured to route to next, so the
            // instance completes here.
            return null;
        }

        // Starting the workflow with no matching (or no) starting rule: fall back to the lowest
        // SortOrder stage of the definition, per the task brief.
        return await db.StageDefinitions
            .Where(sd => sd.TenantId == tenantId && sd.WorkflowDefinitionId == workflowDefinitionId)
            .OrderBy(sd => sd.SortOrder)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Evaluates one RoutingRule's condition. A null ConditionFieldCode always matches. Only a
    /// small set of well-known Requisition fields is understood here (Amount, Currency,
    /// CostCentre, Title) — generic document index-field conditions are Phase 3+ scope, same as
    /// OwnerOfLinkedErpObject/FieldExpression approver resolution. When the field can't be
    /// resolved (no subject loaded, or an unknown field code), the rule does not match.
    /// </summary>
    private bool EvaluateRoutingCondition(RoutingRule rule, Requisition? requisition)
    {
        if (rule.ConditionFieldCode is null)
            return true;

        if (requisition is null)
            return false;

        string? actual = rule.ConditionFieldCode switch
        {
            "Amount" => requisition.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Currency" => requisition.Currency,
            "CostCentre" => requisition.CostCentre,
            "Title" => requisition.Title,
            _ => null,
        };

        if (actual is null)
            return false;

        // Numeric comparison when both sides parse as decimal (covers Amount and >/< operators);
        // otherwise falls back to ordinal string comparison.
        if (decimal.TryParse(actual, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var actualNumber)
            && decimal.TryParse(rule.ConditionValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var expectedNumber))
        {
            return rule.ConditionOperator switch
            {
                RoutingConditionOperator.Equals => actualNumber == expectedNumber,
                RoutingConditionOperator.NotEquals => actualNumber != expectedNumber,
                RoutingConditionOperator.GreaterThan => actualNumber > expectedNumber,
                RoutingConditionOperator.GreaterThanOrEqual => actualNumber >= expectedNumber,
                RoutingConditionOperator.LessThan => actualNumber < expectedNumber,
                RoutingConditionOperator.LessThanOrEqual => actualNumber <= expectedNumber,
                RoutingConditionOperator.Contains => actual.Contains(rule.ConditionValue ?? "", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        return rule.ConditionOperator switch
        {
            RoutingConditionOperator.Equals => string.Equals(actual, rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            RoutingConditionOperator.NotEquals => !string.Equals(actual, rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            RoutingConditionOperator.Contains => actual.Contains(rule.ConditionValue ?? "", StringComparison.OrdinalIgnoreCase),
            _ => false, // GreaterThan/LessThan on non-numeric values: not meaningful, no match.
        };
    }

    /// <summary>
    /// Runs when a WorkflowInstance completes with Status=Approved: for a Requisition subject,
    /// marks it Approved and enqueues the IntegrationOutbox row for Phase 3+'s B1 push. Relies on
    /// the caller's single SaveChangesAsync for the same-transaction guarantee — this method only
    /// adds/modifies tracked entities, it never saves itself.
    /// </summary>
    private async Task CompleteInstanceSubjectSideEffectsAsync(WorkflowInstance instance)
    {
        if (instance.SubjectType != "Requisition")
            return;

        var requisition = await db.Requisitions
            .FirstOrDefaultAsync(r => r.TenantId == instance.TenantId && r.Id == instance.SubjectId);

        if (requisition is null)
        {
            logger.LogWarning(
                "WorkflowInstance {InstanceId} completed as Approved for SubjectType=Requisition but Requisition {SubjectId} was not found; no outbox entry enqueued.",
                instance.Id, instance.SubjectId);
            return;
        }

        requisition.Status = RequisitionStatus.Approved;

        var erpConnection = await db.ErpConnections
            .Where(c => c.TenantId == instance.TenantId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync();

        if (erpConnection is not { } erpConnectionId)
        {
            logger.LogWarning(
                "Tenant {TenantId} has no ErpConnection configured; Requisition {RequisitionId} was approved but no IntegrationOutbox entry could be enqueued.",
                instance.TenantId, requisition.Id);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            requisition.Id,
            requisition.RequestedById,
            requisition.Title,
            requisition.Description,
            requisition.Amount,
            requisition.Currency,
            requisition.CostCentre,
            WorkflowInstanceId = instance.Id,
        });

        db.IntegrationOutbox.Add(new IntegrationOutbox
        {
            TenantId = instance.TenantId,
            ErpConnectionId = erpConnectionId,
            OperationType = "CreatePurchaseRequisitionPosting",
            PayloadJson = payload,
            Status = IntegrationOutboxStatus.Pending,
        });
    }

    // ---------------------------------------------------------------------------------------
    // Delegation
    // ---------------------------------------------------------------------------------------

    public async Task<Delegation> DelegateAsync(
        Guid tenantId,
        Guid principalUserId,
        Guid delegateUserId,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        string? reason)
    {
        var delegation = new Delegation
        {
            TenantId = tenantId,
            PrincipalUserId = principalUserId,
            DelegateUserId = delegateUserId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Reason = reason,
            IsActive = true,
        };
        db.Delegations.Add(delegation);
        await db.SaveChangesAsync();
        return delegation;
    }

    public async Task RevokeDelegationAsync(Guid tenantId, Guid delegationId)
    {
        var delegation = await db.Delegations
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == delegationId)
            ?? throw new ValidationException($"Delegation {delegationId} was not found.");

        delegation.IsActive = false;
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------------------
    // Comments
    // ---------------------------------------------------------------------------------------

    public async Task<CommentThread> AddCommentAsync(Guid tenantId, Guid workflowInstanceId, Guid authorId, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ValidationException("Comment body must not be empty.");

        var comment = new CommentThread
        {
            TenantId = tenantId,
            WorkflowInstanceId = workflowInstanceId,
            AuthorId = authorId,
            Body = body,
        };
        db.CommentThreads.Add(comment);
        await db.SaveChangesAsync();
        return comment;
    }
}
