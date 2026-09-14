using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Flow;

/// <summary>Lifecycle status of a purchase requisition.</summary>
public enum RequisitionStatus
{
    Draft = 0,
    InApproval = 1,
    Approved = 2,
    Rejected = 3,
    PostedToErp = 4,
}

/// <summary>
/// The Pattern A demo subject (ARCHITECTURE.md section 4.4): a purchase requisition that
/// originates in Nexus, runs a full approval workflow here, and on final approval creates the
/// corresponding B1 object via Service Layer through the <see cref="Erp.IntegrationOutbox"/>.
/// </summary>
public class Requisition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>The User who raised this requisition.</summary>
    public Guid RequestedById { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Amount { get; set; }

    /// <summary>ISO currency code. Defaults to KWD, this product's primary GCC market.</summary>
    public string Currency { get; set; } = "KWD";

    public string? CostCentre { get; set; }

    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;

    /// <summary>Set once this requisition is submitted into a workflow instance.</summary>
    public Guid? WorkflowInstanceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
