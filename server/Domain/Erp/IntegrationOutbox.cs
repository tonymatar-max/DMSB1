using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Erp;

/// <summary>Lifecycle of one outbox entry as it is pushed toward the ERP system.</summary>
public enum IntegrationOutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Delivered = 3,
}

/// <summary>
/// The outbox pattern used to write back to SAP B1 (ARCHITECTURE.md section 4.4). Every write
/// Nexus Docs needs to make against B1 (creating an object via Service Layer, recording an
/// approval decision, pushing an attachment) is first recorded here, then processed by a
/// background dispatcher so writes are durable, retryable and auditable independent of
/// Service Layer or gateway availability at the moment the business event happened.
/// </summary>
public class IntegrationOutbox : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>FK to the ERP connection this entry must be dispatched against.</summary>
    public Guid ErpConnectionId { get; set; }

    /// <summary>
    /// The operation to perform, e.g. "CreatePurchaseInvoice", "CreatePurchaseOrder",
    /// "RecordApprovalDecision", "PushAttachment".
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>The Service Layer request payload (or equivalent), serialised as JSON.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public IntegrationOutboxStatus Status { get; set; } = IntegrationOutboxStatus.Pending;

    /// <summary>How many dispatch attempts have been made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>The most recent error message, when <see cref="Status"/> is Failed.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The external key (e.g. DocEntry) recorded once the operation is confirmed delivered by
    /// the ERP. Null until then.
    /// </summary>
    public string? ErpKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the entry last reached a terminal state (Delivered or Failed).</summary>
    public DateTimeOffset? ProcessedAt { get; set; }
}
