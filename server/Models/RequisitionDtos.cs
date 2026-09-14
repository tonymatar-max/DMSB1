using NexusDocs.Api.Domain.Flow;

namespace NexusDocs.Api.Models;

public class CreateRequisitionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KWD";
    public string? CostCentre { get; set; }
}

/// <summary>
/// Phase 2 simplification (see RequisitionsController.Submit): there is no "which workflow
/// applies to which subject type" rule engine yet, so the caller states the WorkflowDefinition to
/// start explicitly.
/// </summary>
public class SubmitRequisitionRequest
{
    public Guid WorkflowDefinitionId { get; set; }
}

public class RequisitionDto
{
    public Guid Id { get; set; }
    public Guid RequestedById { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CostCentre { get; set; }
    public RequisitionStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Requisition detail including its WorkflowInstance's current stage/status, when submitted.</summary>
public class RequisitionDetailDto : RequisitionDto
{
    public WorkflowInstanceStatus? WorkflowStatus { get; set; }
    public string? CurrentStageName { get; set; }
}
