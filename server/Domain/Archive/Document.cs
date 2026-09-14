using NexusDocs.Api.Domain.Common;
using NexusDocs.Api.Domain.Erp;

namespace NexusDocs.Api.Domain.Archive;

public enum DocumentStatus
{
    Draft,
    Active,
    Superseded,
    Disposed,
}

/// <summary>The record itself: type, cabinet, status, owner, and a pointer to its current version.</summary>
public class Document : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CabinetId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    /// <summary>The User who owns this document.</summary>
    public Guid OwnerId { get; set; }

    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public ICollection<DocumentIndexValue> IndexValues { get; set; } = new List<DocumentIndexValue>();
    public ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();
    public ICollection<ErpObjectLink> ErpLinks { get; set; } = new List<ErpObjectLink>();
}
