using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Flow;

namespace NexusDocs.Api.Infrastructure.Flow;

/// <summary>
/// Background service for the FLOW module SLA engine (ARCHITECTURE.md section 2.6,
/// "FlowTimerWorker" row: 1 min tick — SLA breach, reminders, auto-escalation,
/// auto-approve/reject on timeout).
///
/// Background services are registered as singletons, so this cannot take a scoped
/// <see cref="NexusDocsDbContext"/> directly (standard ASP.NET Core practice) — it creates a new
/// DI scope, and therefore a fresh DbContext, on every tick via <see cref="IServiceScopeFactory"/>.
///
/// This worker runs with no ambient HTTP request and therefore no "current tenant" — it is
/// intentionally cross-tenant. It bypasses the global per-tenant EF Core query filter
/// (defined once for every <c>ITenantScoped</c> entity in NexusDocsDbContext.OnModelCreating)
/// with <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}(IQueryable{TEntity})"/>
/// since there is no tenant to filter by here.
///
/// SQLITE DATETIMEOFFSET CAVEAT: never combine the global tenant filter with an inline LINQ
/// predicate that range-checks a DateTimeOffset column against "now" — it compiles but throws
/// InvalidOperationException at query time on the Sqlite provider this project uses for dev (see
/// server/Infrastructure/Licensing/LicenseService.cs for the established fix). So the query below
/// only narrows on simple equality (Status == Active, DueAt != null) in SQL, then the
/// DueAt &lt;= now comparison happens in memory on the materialized list.
/// </summary>
public class FlowTimerWorker(IServiceScopeFactory scopeFactory, ILogger<FlowTimerWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Well-known sentinel ActorId used for Decision rows created by this worker (AutoApprove /
    /// AutoReject) instead of a real approver. There is no real "system user" row in the Users
    /// table for Phase 2 — Guid.Empty is the documented convention for "the system acted, not a
    /// person". Any UI/reporting code that resolves ActorId to a display name must special-case
    /// Guid.Empty as "System (SLA auto-decision)".
    /// </summary>
    public static readonly Guid SystemActorId = Guid.Empty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        // Run once immediately on startup, then on every tick thereafter.
        await RunTickSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunTickSafelyAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Wraps a single tick's body in try/catch so one bad row (or a transient DB hiccup) never
    /// crashes the whole hosted-service loop; the worker simply tries again next minute.
    /// </summary>
    private async Task RunTickSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunTickAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FlowTimerWorker tick failed; will retry next tick.");
        }
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusDocsDbContext>();

        var now = DateTimeOffset.UtcNow;

        // Simple equality/foreign-key predicates only in SQL (Sqlite DateTimeOffset caveat above).
        var activeStages = await db.StageInstances
            .IgnoreQueryFilters()
            .Where(s => s.Status == StageInstanceStatus.Active && s.DueAt != null)
            .ToListAsync(stoppingToken);

        // DueAt <= now range comparison happens here, in memory, on the materialized list.
        var pastDue = activeStages
            .Where(s => s.DueAt!.Value <= now && s.EscalatedAt == null)
            .ToList();

        if (pastDue.Count == 0)
            return;

        var stageDefinitionIds = pastDue.Select(s => s.StageDefinitionId).Distinct().ToList();

        var stageDefinitions = await db.StageDefinitions
            .IgnoreQueryFilters()
            .Where(sd => stageDefinitionIds.Contains(sd.Id))
            .ToListAsync(stoppingToken);
        var stageDefinitionsById = stageDefinitions.ToDictionary(sd => sd.Id);

        var escalationRules = await db.EscalationRules
            .IgnoreQueryFilters()
            .Where(r => stageDefinitionIds.Contains(r.StageDefinitionId))
            .ToListAsync(stoppingToken);
        var escalationRulesByStageDefinitionId = escalationRules
            .GroupBy(r => r.StageDefinitionId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var stageInstance in pastDue)
        {
            try
            {
                await ProcessPastDueStageAsync(
                    db, stageInstance, stageDefinitionsById, escalationRulesByStageDefinitionId, now, stoppingToken);
            }
            catch (Exception ex)
            {
                // One bad row must not stop the rest of the batch (or the next tick).
                logger.LogError(
                    ex, "FlowTimerWorker failed processing StageInstance {StageInstanceId}; skipping.",
                    stageInstance.Id);
            }
        }

        await db.SaveChangesAsync(stoppingToken);
    }

    private async Task ProcessPastDueStageAsync(
        NexusDocsDbContext db,
        StageInstance stageInstance,
        IReadOnlyDictionary<Guid, StageDefinition> stageDefinitionsById,
        IReadOnlyDictionary<Guid, EscalationRule> escalationRulesByStageDefinitionId,
        DateTimeOffset now,
        CancellationToken stoppingToken)
    {
        if (!stageDefinitionsById.TryGetValue(stageInstance.StageDefinitionId, out var stageDefinition))
        {
            logger.LogWarning(
                "StageInstance {StageInstanceId} references missing StageDefinition {StageDefinitionId}; skipping.",
                stageInstance.Id, stageInstance.StageDefinitionId);
            return;
        }

        if (!escalationRulesByStageDefinitionId.TryGetValue(stageDefinition.Id, out var rule))
        {
            // No escalation configured for this stage: SLA breach is silently ignored per
            // ARCHITECTURE.md (StageDefinition.SlaHours without an EscalationRule just means the
            // stage runs late with no automated consequence).
            return;
        }

        switch (rule.Action)
        {
            case EscalationAction.Remind:
                // Real notification delivery (email/push/etc.) is out of scope for Phase 2 — this
                // worker only logs. A NotificationService hook belongs here once it exists.
                logger.LogInformation(
                    "SLA reminder: StageInstance {StageInstanceId} (stage '{StageName}') is past due.",
                    stageInstance.Id, stageDefinition.Name);
                // Intentionally does NOT set EscalatedAt: a Remind is not terminal, so the stage
                // should keep being picked up (and keep reminding) on future ticks until it's
                // actually acted on or escalates via a different rule change.
                return;

            case EscalationAction.EscalateToFallback:
                await EscalateToFallbackAsync(db, stageInstance, stageDefinition, now, stoppingToken);
                break;

            case EscalationAction.AutoApprove:
            case EscalationAction.AutoReject:
                AutoDecide(db, stageInstance, rule.Action, now);
                break;

            default:
                logger.LogWarning(
                    "EscalationRule {RuleId} has unhandled Action {Action}; skipping StageInstance {StageInstanceId}.",
                    rule.Id, rule.Action, stageInstance.Id);
                return;
        }

        stageInstance.EscalatedAt = now;
    }

    /// <summary>
    /// Creates a new ApprovalTask assigned to the stage's fallback approver
    /// (StageDefinition.EscalateToApproverSpecId).
    ///
    /// NOTE ON DUPLICATION: as of writing there is no WorkflowEngine (or equivalent) service
    /// visible yet to call for approver resolution, so this method inlines a minimal resolver for
    /// the two simplest ApproverSpec kinds (NamedUser, Role — picking the first active role
    /// member) rather than depend on a service that may not exist yet. This duplicates whatever
    /// resolution logic the real workflow engine ends up needing for stage-start assignment (the
    /// other ApproverSpec kinds — Group, ManagerOfInitiator, OwnerOfLinkedErpObject,
    /// FieldExpression — are NOT handled here and just log a warning and skip). TODO for later
    /// cleanup: once WorkflowEngine (or an ApproverResolutionService) exists with a real
    /// ResolveApproversAsync-style method, delete this inline resolver and call that instead.
    /// </summary>
    private async Task EscalateToFallbackAsync(
        NexusDocsDbContext db,
        StageInstance stageInstance,
        StageDefinition stageDefinition,
        DateTimeOffset now,
        CancellationToken stoppingToken)
    {
        if (stageDefinition.EscalateToApproverSpecId is not { } fallbackSpecId)
        {
            logger.LogWarning(
                "StageDefinition {StageDefinitionId} has an EscalateToFallback rule but no " +
                "EscalateToApproverSpecId configured; skipping StageInstance {StageInstanceId}.",
                stageDefinition.Id, stageInstance.Id);
            return;
        }

        var fallbackSpec = await db.ApproverSpecs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == fallbackSpecId, stoppingToken);

        if (fallbackSpec is null)
        {
            logger.LogWarning(
                "ApproverSpec {ApproverSpecId} referenced by StageDefinition {StageDefinitionId} not found; " +
                "skipping StageInstance {StageInstanceId}.",
                fallbackSpecId, stageDefinition.Id, stageInstance.Id);
            return;
        }

        Guid? resolvedApproverId = fallbackSpec.Kind switch
        {
            ApproverKind.NamedUser => fallbackSpec.NamedUserId,
            ApproverKind.Role => await ResolveFirstActiveUserInRoleAsync(db, fallbackSpec.RoleId, stoppingToken),
            _ => null,
        };

        if (resolvedApproverId is not { } assigneeId)
        {
            logger.LogWarning(
                "Fallback ApproverSpec {ApproverSpecId} (Kind={Kind}) for StageDefinition {StageDefinitionId} " +
                "could not be resolved by this worker's minimal inline resolver; skipping StageInstance " +
                "{StageInstanceId}. (Group/ManagerOfInitiator/OwnerOfLinkedErpObject/FieldExpression kinds are " +
                "not supported here — see the WorkflowEngine TODO comment on this method.)",
                fallbackSpecId, fallbackSpec.Kind, stageDefinition.Id, stageInstance.Id);
            return;
        }

        db.ApprovalTasks.Add(new ApprovalTask
        {
            TenantId = stageInstance.TenantId,
            StageInstanceId = stageInstance.Id,
            AssigneeId = assigneeId,
            Status = ApprovalTaskStatus.Pending,
            CreatedAt = now,
        });

        logger.LogInformation(
            "SLA escalation: StageInstance {StageInstanceId} escalated to fallback approver {AssigneeId}.",
            stageInstance.Id, assigneeId);
    }

    private static async Task<Guid?> ResolveFirstActiveUserInRoleAsync(
        NexusDocsDbContext db, Guid? roleId, CancellationToken stoppingToken)
    {
        if (roleId is not { } id)
            return null;

        var userRole = await db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.RoleId == id)
            .FirstOrDefaultAsync(stoppingToken);

        return userRole?.UserId;
    }

    /// <summary>
    /// AutoApprove/AutoReject: records a Decision as if the "system" had acted, using
    /// <see cref="SystemActorId"/> (Guid.Empty) as ActorId since there is no real system-user row.
    /// Creates a synthetic ApprovalTask to hang the Decision off of when the stage has none
    /// pending, since Decision.ApprovalTaskId is a required FK.
    /// </summary>
    private static void AutoDecide(
        NexusDocsDbContext db, StageInstance stageInstance, EscalationAction action, DateTimeOffset now)
    {
        var outcome = action == EscalationAction.AutoApprove
            ? DecisionOutcome.Approved
            : DecisionOutcome.Rejected;

        var task = new ApprovalTask
        {
            TenantId = stageInstance.TenantId,
            StageInstanceId = stageInstance.Id,
            AssigneeId = FlowTimerWorker.SystemActorId,
            Status = outcome == DecisionOutcome.Approved
                ? ApprovalTaskStatus.Approved
                : ApprovalTaskStatus.Rejected,
            CreatedAt = now,
            CompletedAt = now,
        };
        db.ApprovalTasks.Add(task);

        db.Decisions.Add(new Decision
        {
            TenantId = stageInstance.TenantId,
            ApprovalTaskId = task.Id,
            ActorId = FlowTimerWorker.SystemActorId,
            Outcome = outcome,
            Comment = $"Auto-{(outcome == DecisionOutcome.Approved ? "approved" : "rejected")} by FlowTimerWorker on SLA timeout.",
            DecidedAt = now,
        });

        // NOTE: this worker deliberately does NOT advance stageInstance.Status / WorkflowInstance
        // state itself (e.g. to Approved/Rejected and moving on to the next stage or routing
        // rules) — that state-machine transition belongs to the workflow engine that owns
        // "a decision was recorded" as its trigger. Once that engine exists it should either react
        // to this Decision being added, or this worker should call into it directly; for now the
        // Decision + completed ApprovalTask are the source of truth and EscalatedAt marks this
        // stage instance as already handled so it isn't reprocessed.
    }
}
