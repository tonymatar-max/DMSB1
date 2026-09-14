using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

public enum RetentionTriggerEvent
{
    DocumentCreated,
    DocumentTypeSpecificDate,
}

public enum RetentionDispositionAction
{
    Review,
    AutoDispose,
}

/// <summary>A reusable retention rule: how long to keep a document and what to do once retention expires.</summary>
public class RetentionPolicy : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public RetentionTriggerEvent TriggerEvent { get; set; }
    public int RetentionYears { get; set; }
    public RetentionDispositionAction DispositionAction { get; set; }
}
