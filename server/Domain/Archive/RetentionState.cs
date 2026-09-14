using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

/// <summary>The applied retention schedule for one document: which policy, when it was triggered, when disposition is due, and any legal hold.</summary>
public class RetentionState : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid RetentionPolicyId { get; set; }
    public DateTimeOffset TriggerDate { get; set; }
    public DateTimeOffset DispositionDueDate { get; set; }

    /// <summary>When true, disposition is blocked regardless of DispositionDueDate.</summary>
    public bool LegalHold { get; set; }

    public string? LegalHoldReason { get; set; }
}
