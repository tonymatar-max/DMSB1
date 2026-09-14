using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

public enum RenditionKind
{
    PdfA,
    Thumbnail,
    TextLayer,
}

/// <summary>A derived artifact of a <see cref="DocumentVersion"/> - PDF/A copy, thumbnail, or extracted text layer.</summary>
public class Rendition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public RenditionKind Kind { get; set; }

    /// <summary>sha256 hex digest of the stored blob.</summary>
    public string BlobHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
