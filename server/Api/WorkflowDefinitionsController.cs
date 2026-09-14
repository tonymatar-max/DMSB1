using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Flow;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

[ApiController]
[Route("api/workflow-definitions")]
[Authorize]
[RequiresModule("FLOW")]
public class WorkflowDefinitionsController(NexusDocsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowDefinitionSummaryDto>>> List()
    {
        // Tenant-scoped automatically via NexusDocsDbContext's global query filter. Materialize
        // first, then reduce to "latest version per family" in memory rather than as one complex
        // LINQ query, per this codebase's Sqlite-safety convention.
        var all = await db.WorkflowDefinitions
            .OrderBy(w => w.Name)
            .ToListAsync();

        var latestPerFamily = all
            .GroupBy(w => w.WorkflowFamilyId)
            .Select(g => g.OrderByDescending(w => w.Version).First())
            .OrderBy(w => w.Name)
            .ToList();

        return Ok(latestPerFamily.Select(ToSummaryDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> GetById(Guid id)
    {
        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id);
        if (definition is null) return NotFound();

        var detail = await LoadDetailAsync(definition);
        return Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> Create(WorkflowDefinitionRequest request)
    {
        var definition = new WorkflowDefinition
        {
            Name = request.Name,
            DocumentTypeId = request.DocumentTypeId,
            WorkflowFamilyId = Guid.NewGuid(),
            Version = 1,
            IsActive = true,
        };

        db.WorkflowDefinitions.Add(definition);
        BuildStages(definition, request.Stages);

        await db.SaveChangesAsync();

        var detail = await LoadDetailAsync(definition);
        return CreatedAtAction(nameof(GetById), new { id = definition.Id }, detail);
    }

    [HttpPost("{familyId:guid}/new-version")]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> NewVersion(Guid familyId, WorkflowDefinitionRequest request)
    {
        // Materialize the family's versions, pick the latest in memory (same convention as List()).
        var versions = await db.WorkflowDefinitions
            .Where(w => w.WorkflowFamilyId == familyId)
            .ToListAsync();

        if (versions.Count == 0) return NotFound();

        var previous = versions.OrderByDescending(w => w.Version).First();

        var next = new WorkflowDefinition
        {
            Name = request.Name,
            DocumentTypeId = request.DocumentTypeId,
            WorkflowFamilyId = familyId,
            Version = previous.Version + 1,
            IsActive = true,
        };

        previous.IsActive = false;
        previous.UpdatedAt = DateTimeOffset.UtcNow;

        db.WorkflowDefinitions.Add(next);
        BuildStages(next, request.Stages);

        await db.SaveChangesAsync();

        var detail = await LoadDetailAsync(next);
        return CreatedAtAction(nameof(GetById), new { id = next.Id }, detail);
    }

    /// <summary>
    /// Two-pass build: create all StageDefinitions (with their ApproverSpecs) first so every stage
    /// has an Id, then create RoutingRules, resolving each request's TargetStageName to the
    /// corresponding StageDefinitionId within this same batch.
    /// </summary>
    private static void BuildStages(WorkflowDefinition definition, List<StageDefinitionRequest> stageRequests)
    {
        var stagesByName = new Dictionary<string, StageDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var stageRequest in stageRequests)
        {
            var stage = new StageDefinition
            {
                WorkflowDefinitionId = definition.Id,
                Name = stageRequest.Name,
                SortOrder = stageRequest.SortOrder,
                ApprovalMode = stageRequest.ApprovalMode,
                QuorumCount = stageRequest.QuorumCount,
                SlaHours = stageRequest.SlaHours,
            };

            foreach (var approverRequest in stageRequest.Approvers)
            {
                stage.Approvers.Add(new ApproverSpec
                {
                    StageDefinitionId = stage.Id,
                    Kind = approverRequest.Kind,
                    NamedUserId = approverRequest.NamedUserId,
                    RoleId = approverRequest.RoleId,
                    ManagerHierarchyLevels = approverRequest.ManagerHierarchyLevels,
                    ErpOwnerField = approverRequest.ErpOwnerField,
                    FieldExpression = approverRequest.FieldExpression,
                });
            }

            definition.Stages.Add(stage);
            stagesByName[stage.Name] = stage;
        }

        foreach (var stageRequest in stageRequests)
        {
            var stage = stagesByName[stageRequest.Name];

            foreach (var ruleRequest in stageRequest.OutgoingRules)
            {
                Guid? targetStageId = null;
                if (!string.IsNullOrEmpty(ruleRequest.TargetStageName))
                {
                    if (!stagesByName.TryGetValue(ruleRequest.TargetStageName, out var targetStage))
                    {
                        throw new InvalidOperationException(
                            $"Routing rule on stage '{stage.Name}' references unknown target stage '{ruleRequest.TargetStageName}'.");
                    }

                    targetStageId = targetStage.Id;
                }

                stage.OutgoingRules.Add(new RoutingRule
                {
                    FromStageDefinitionId = stage.Id,
                    ConditionFieldCode = ruleRequest.ConditionFieldCode,
                    ConditionOperator = ruleRequest.ConditionOperator,
                    ConditionValue = ruleRequest.ConditionValue,
                    TargetStageDefinitionId = targetStageId,
                    Priority = ruleRequest.Priority,
                });
            }
        }
    }

    private async Task<WorkflowDefinitionDetailDto> LoadDetailAsync(WorkflowDefinition definition)
    {
        var stages = await db.StageDefinitions
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var stageIds = stages.Select(s => s.Id).ToList();

        var approvers = await db.ApproverSpecs
            .Where(a => stageIds.Contains(a.StageDefinitionId))
            .ToListAsync();

        var rules = await db.RoutingRules
            .Where(r => r.FromStageDefinitionId != null && stageIds.Contains(r.FromStageDefinitionId!.Value))
            .ToListAsync();

        var stagesById = stages.ToDictionary(s => s.Id);

        var stageDtos = stages.Select(s => new StageDefinitionDto(
            s.Id,
            s.Name,
            s.SortOrder,
            s.ApprovalMode,
            s.QuorumCount,
            s.SlaHours,
            s.EscalateToApproverSpecId,
            approvers.Where(a => a.StageDefinitionId == s.Id).Select(ToApproverDto).ToList(),
            rules.Where(r => r.FromStageDefinitionId == s.Id)
                .Select(r => ToRuleDto(r, stagesById))
                .ToList()
        )).ToList();

        return new WorkflowDefinitionDetailDto(
            definition.Id,
            definition.WorkflowFamilyId,
            definition.Name,
            definition.DocumentTypeId,
            definition.Version,
            definition.IsActive,
            definition.CreatedAt,
            stageDtos);
    }

    private static WorkflowDefinitionSummaryDto ToSummaryDto(WorkflowDefinition w) =>
        new(w.Id, w.WorkflowFamilyId, w.Name, w.DocumentTypeId, w.Version, w.IsActive, w.CreatedAt);

    private static ApproverSpecDto ToApproverDto(ApproverSpec a) =>
        new(a.Id, a.Kind, a.NamedUserId, a.RoleId, a.ManagerHierarchyLevels, a.ErpOwnerField, a.FieldExpression);

    private static RoutingRuleDto ToRuleDto(RoutingRule r, Dictionary<Guid, StageDefinition> stagesById)
    {
        string? targetName = r.TargetStageDefinitionId.HasValue && stagesById.TryGetValue(r.TargetStageDefinitionId.Value, out var target)
            ? target.Name
            : null;

        return new RoutingRuleDto(r.Id, r.ConditionFieldCode, r.ConditionOperator, r.ConditionValue, targetName, r.Priority);
    }
}
