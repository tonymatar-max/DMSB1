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
/// The "Pattern A" demo end to end (ARCHITECTURE.md section 4.4): a Requisition originates in
/// Nexus, is submitted into a FLOW workflow instance, and on final approval the outbox row that
/// pushes it to the ERP is enqueued by <see cref="WorkflowEngine"/> itself.
/// </summary>
[ApiController]
[Route("api/requisitions")]
[Authorize]
[RequiresModule("FLOW")]
public class RequisitionsController(
    NexusDocsDbContext db,
    WorkflowEngine engine,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    // ---- Create ------------------------------------------------------------------------------

    [HttpPost]
    public async Task<ActionResult<RequisitionDto>> Create([FromBody] CreateRequisitionRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "title_required" });

        var requisition = new Requisition
        {
            TenantId = tenantId,
            RequestedById = CurrentUserId,
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "KWD" : request.Currency,
            CostCentre = request.CostCentre,
            Status = RequisitionStatus.Draft,
        };

        db.Requisitions.Add(requisition);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = requisition.Id }, ToDto(requisition));
    }

    // ---- Submit ------------------------------------------------------------------------------

    /// <summary>
    /// Phase 2 simplification: there is no "which workflow applies to which subject type" rule
    /// engine yet, so the caller states the WorkflowDefinition to start against explicitly rather
    /// than it being inferred from the requisition. A future phase would resolve this from the
    /// requisition's shape (amount/cost centre/etc.) or a per-subject-type default.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<RequisitionDto>> Submit(Guid id, [FromBody] SubmitRequisitionRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var requisition = await db.Requisitions
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
        if (requisition is null) return NotFound();

        if (requisition.Status != RequisitionStatus.Draft)
            return BadRequest(new { error = "requisition_not_in_draft" });

        var workflowDefinition = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == request.WorkflowDefinitionId, ct);
        if (workflowDefinition is null) return BadRequest(new { error = "workflow_definition_not_found" });
        if (!workflowDefinition.IsActive) return BadRequest(new { error = "workflow_definition_not_active" });

        var instance = await engine.StartInstanceAsync(
            tenantId, workflowDefinition.Id, "Requisition", requisition.Id, CurrentUserId);

        requisition.WorkflowInstanceId = instance.Id;
        requisition.Status = RequisitionStatus.InApproval;
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(requisition));
    }

    // ---- List --------------------------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<List<RequisitionDto>>> List(CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        // Sqlite can't translate ORDER BY on a DateTimeOffset column (same caveat as the
        // DateTimeOffset range-comparison issue documented on LicenseService), so narrow in SQL
        // first and order in memory after materializing.
        var requisitions = await db.Requisitions
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);

        return Ok(requisitions.OrderByDescending(r => r.CreatedAt).Select(ToDto).ToList());
    }

    // ---- Detail ------------------------------------------------------------------------------

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RequisitionDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var requisition = await db.Requisitions
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);
        if (requisition is null) return NotFound();

        var dto = new RequisitionDetailDto
        {
            Id = requisition.Id,
            RequestedById = requisition.RequestedById,
            Title = requisition.Title,
            Description = requisition.Description,
            Amount = requisition.Amount,
            Currency = requisition.Currency,
            CostCentre = requisition.CostCentre,
            Status = requisition.Status,
            WorkflowInstanceId = requisition.WorkflowInstanceId,
            CreatedAt = requisition.CreatedAt,
        };

        if (requisition.WorkflowInstanceId is { } instanceId)
        {
            var instance = await db.WorkflowInstances
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == instanceId, ct);
            if (instance is not null)
            {
                dto.WorkflowStatus = instance.Status;
                if (instance.CurrentStageDefinitionId is { } stageDefId)
                {
                    dto.CurrentStageName = await db.StageDefinitions
                        .Where(sd => sd.TenantId == tenantId && sd.Id == stageDefId)
                        .Select(sd => sd.Name)
                        .FirstOrDefaultAsync(ct);
                }
            }
        }

        return Ok(dto);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static RequisitionDto ToDto(Requisition r) => new()
    {
        Id = r.Id,
        RequestedById = r.RequestedById,
        Title = r.Title,
        Description = r.Description,
        Amount = r.Amount,
        Currency = r.Currency,
        CostCentre = r.CostCentre,
        Status = r.Status,
        WorkflowInstanceId = r.WorkflowInstanceId,
        CreatedAt = r.CreatedAt,
    };
}
