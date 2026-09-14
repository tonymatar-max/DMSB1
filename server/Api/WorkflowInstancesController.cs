using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Flow;
using NexusDocs.Api.Infrastructure.Flow;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Infrastructure.Tenancy;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// The FLOW inbox/decisions/comments/delegations surface (ARCHITECTURE.md section 5): what the
/// current user needs to act on, deciding an approval task, discussing an instance, and CRUD over
/// the current user's own delegations.
/// </summary>
[ApiController]
[Authorize]
[RequiresModule("FLOW")]
public class WorkflowInstancesController(
    NexusDocsDbContext db,
    WorkflowEngine engine,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    // ---- Inbox -------------------------------------------------------------------------------

    [HttpGet("api/workflow-instances/inbox")]
    public async Task<ActionResult<List<InboxItemDto>>> Inbox(CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        // Narrow to simple equality predicates in SQL only (Status/AssigneeId); the DueAt
        // range/pending-time calculation happens in memory below, per the Sqlite query-filter +
        // DateTimeOffset range comparison bug documented in this task's brief.
        var tasks = await db.ApprovalTasks
            .Where(t => t.TenantId == tenantId
                        && t.Status == ApprovalTaskStatus.Pending
                        && t.AssigneeId == CurrentUserId)
            .ToListAsync(ct);

        if (tasks.Count == 0) return Ok(new List<InboxItemDto>());

        var stageInstanceIds = tasks.Select(t => t.StageInstanceId).Distinct().ToList();
        var stageInstances = await db.StageInstances
            .Where(s => s.TenantId == tenantId && stageInstanceIds.Contains(s.Id))
            .ToListAsync(ct);
        var stageInstanceById = stageInstances.ToDictionary(s => s.Id);

        var stageDefIds = stageInstances.Select(s => s.StageDefinitionId).Distinct().ToList();
        var stageDefs = await db.StageDefinitions
            .Where(sd => sd.TenantId == tenantId && stageDefIds.Contains(sd.Id))
            .ToListAsync(ct);
        var stageDefById = stageDefs.ToDictionary(sd => sd.Id);

        var instanceIds = stageInstances.Select(s => s.WorkflowInstanceId).Distinct().ToList();
        var instances = await db.WorkflowInstances
            .Where(i => i.TenantId == tenantId && instanceIds.Contains(i.Id))
            .ToListAsync(ct);
        var instanceById = instances.ToDictionary(i => i.Id);

        var now = DateTimeOffset.UtcNow;

        var items = tasks
            .Where(t => stageInstanceById.ContainsKey(t.StageInstanceId))
            .Select(t =>
            {
                var stageInstance = stageInstanceById[t.StageInstanceId];
                var stageDef = stageDefById.TryGetValue(stageInstance.StageDefinitionId, out var sd) ? sd : null;
                var instance = instanceById.TryGetValue(stageInstance.WorkflowInstanceId, out var i) ? i : null;

                return new InboxItemDto
                {
                    ApprovalTaskId = t.Id,
                    StageInstanceId = stageInstance.Id,
                    WorkflowInstanceId = stageInstance.WorkflowInstanceId,
                    SubjectType = instance?.SubjectType ?? string.Empty,
                    SubjectId = instance?.SubjectId ?? Guid.Empty,
                    StageName = stageDef?.Name,
                    OriginalAssigneeId = t.OriginalAssigneeId,
                    CreatedAt = t.CreatedAt,
                    DueAt = stageInstance.DueAt,
                    IsOverdue = stageInstance.DueAt is { } due && due <= now,
                    PendingHours = (now - t.CreatedAt).TotalHours,
                };
            })
            .OrderBy(i => i.DueAt ?? DateTimeOffset.MaxValue)
            .ToList();

        return Ok(items);
    }

    // ---- Decide ------------------------------------------------------------------------------

    [HttpPost("api/approval-tasks/{id:guid}/decide")]
    public async Task<ActionResult<DecisionDto>> Decide(Guid id, [FromBody] DecideRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        Decision decision;
        try
        {
            decision = await engine.DecideAsync(tenantId, id, CurrentUserId, request.Outcome, request.Comment);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(ToDecisionDto(decision));
    }

    // ---- Comments ------------------------------------------------------------------------------

    [HttpPost("api/workflow-instances/{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        CommentThread comment;
        try
        {
            comment = await engine.AddCommentAsync(tenantId, id, CurrentUserId, request.Body);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(ToCommentDto(comment));
    }

    // ---- Detail ------------------------------------------------------------------------------

    [HttpGet("api/workflow-instances/{id:guid}")]
    public async Task<ActionResult<WorkflowInstanceDetailDto>> Detail(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
        if (instance is null) return NotFound();

        var stageInstances = await db.StageInstances
            .Where(s => s.TenantId == tenantId && s.WorkflowInstanceId == id)
            .ToListAsync(ct);

        var stageDefIds = stageInstances.Select(s => s.StageDefinitionId).Distinct().ToList();
        var stageDefs = await db.StageDefinitions
            .Where(sd => sd.TenantId == tenantId && stageDefIds.Contains(sd.Id))
            .ToListAsync(ct);
        var stageDefById = stageDefs.ToDictionary(sd => sd.Id);

        var stageInstanceIds = stageInstances.Select(s => s.Id).ToList();
        var tasks = await db.ApprovalTasks
            .Where(t => t.TenantId == tenantId && stageInstanceIds.Contains(t.StageInstanceId))
            .ToListAsync(ct);

        var taskIds = tasks.Select(t => t.Id).ToList();
        var decisions = await db.Decisions
            .Where(d => d.TenantId == tenantId && taskIds.Contains(d.ApprovalTaskId))
            .ToListAsync(ct);
        var decisionsByTask = decisions.GroupBy(d => d.ApprovalTaskId).ToDictionary(g => g.Key, g => g.ToList());

        var tasksByStageInstance = tasks.GroupBy(t => t.StageInstanceId).ToDictionary(g => g.Key, g => g.ToList());

        // Sqlite's EF provider can't translate ORDER BY on a DateTimeOffset column — materialize
        // first, order in memory (same pattern as LicenseService.cs and DocumentsController.cs).
        var comments = (await db.CommentThreads
                .Where(c => c.TenantId == tenantId && c.WorkflowInstanceId == id)
                .ToListAsync(ct))
            .OrderBy(c => c.CreatedAt)
            .ToList();

        var dto = new WorkflowInstanceDetailDto
        {
            Id = instance.Id,
            WorkflowDefinitionId = instance.WorkflowDefinitionId,
            SubjectType = instance.SubjectType,
            SubjectId = instance.SubjectId,
            InitiatorId = instance.InitiatorId,
            Status = instance.Status,
            CurrentStageDefinitionId = instance.CurrentStageDefinitionId,
            StartedAt = instance.StartedAt,
            CompletedAt = instance.CompletedAt,
            Stages = stageInstances
                .OrderBy(s => stageDefById.TryGetValue(s.StageDefinitionId, out var sd) ? sd.SortOrder : int.MaxValue)
                .Select(s => new StageInstanceDto
                {
                    Id = s.Id,
                    StageDefinitionId = s.StageDefinitionId,
                    StageName = stageDefById.TryGetValue(s.StageDefinitionId, out var sd) ? sd.Name : null,
                    Status = s.Status,
                    StartedAt = s.StartedAt,
                    DueAt = s.DueAt,
                    CompletedAt = s.CompletedAt,
                    Tasks = (tasksByStageInstance.TryGetValue(s.Id, out var stageTasks) ? stageTasks : new List<ApprovalTask>())
                        .Select(t => new ApprovalTaskDto
                        {
                            Id = t.Id,
                            AssigneeId = t.AssigneeId,
                            OriginalAssigneeId = t.OriginalAssigneeId,
                            Status = t.Status,
                            CreatedAt = t.CreatedAt,
                            CompletedAt = t.CompletedAt,
                            Decisions = (decisionsByTask.TryGetValue(t.Id, out var taskDecisions) ? taskDecisions : new List<Decision>())
                                .Select(ToDecisionDto).ToList(),
                        }).ToList(),
                }).ToList(),
            Comments = comments.Select(ToCommentDto).ToList(),
        };

        return Ok(dto);
    }

    // ---- Delegations -------------------------------------------------------------------------

    [HttpPost("api/delegations")]
    public async Task<ActionResult<DelegationDto>> CreateDelegation([FromBody] CreateDelegationRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var delegation = await engine.DelegateAsync(
            tenantId, CurrentUserId, request.DelegateUserId, request.StartsAt, request.EndsAt, request.Reason);

        return Ok(ToDelegationDto(delegation));
    }

    [HttpDelete("api/delegations/{id:guid}")]
    public async Task<IActionResult> DeleteDelegation(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var delegation = await db.Delegations
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);
        if (delegation is null) return NotFound();
        if (delegation.PrincipalUserId != CurrentUserId) return Forbid();

        try
        {
            await engine.RevokeDelegationAsync(tenantId, id);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return NoContent();
    }

    [HttpGet("api/delegations")]
    public async Task<ActionResult<List<DelegationDto>>> ListDelegations(CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var delegations = await db.Delegations
            .Where(d => d.TenantId == tenantId && d.PrincipalUserId == CurrentUserId)
            .ToListAsync(ct);

        return Ok(delegations.Select(ToDelegationDto).ToList());
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static DecisionDto ToDecisionDto(Decision d) => new()
    {
        Id = d.Id,
        ApprovalTaskId = d.ApprovalTaskId,
        ActorId = d.ActorId,
        Outcome = d.Outcome,
        Comment = d.Comment,
        DecidedAt = d.DecidedAt,
    };

    private static CommentDto ToCommentDto(CommentThread c) => new()
    {
        Id = c.Id,
        AuthorId = c.AuthorId,
        Body = c.Body,
        CreatedAt = c.CreatedAt,
    };

    private static DelegationDto ToDelegationDto(Delegation d) => new()
    {
        Id = d.Id,
        PrincipalUserId = d.PrincipalUserId,
        DelegateUserId = d.DelegateUserId,
        StartsAt = d.StartsAt,
        EndsAt = d.EndsAt,
        Reason = d.Reason,
        IsActive = d.IsActive,
    };
}
