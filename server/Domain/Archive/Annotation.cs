using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

public enum AnnotationKind
{
    Note,
    Stamp,
    Highlight,
    Redaction,
}

/// <summary>
/// An overlay on one page of a <see cref="DocumentVersion"/>: a note, stamp, highlight, or redaction.
/// Coordinates are normalized (0-1) relative to page width/height so they render independent of DPI.
/// Redactions are overlays only - they mark a region as sensitive but are never burned into the
/// underlying blob, so the original content remains recoverable to anyone with rights to the
/// unredacted version (see ARCHITECTURE.md section 2.2).
/// </summary>
public class Annotation : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public AnnotationKind Kind { get; set; }

    /// <summary>The User who created this annotation.</summary>
    public Guid AuthorId { get; set; }

    public int PageNumber { get; set; }

    /// <summary>Normalized (0-1) position/size relative to the page.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Note text or stamp label; not applicable to Highlight/Redaction.</summary>
    public string? Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
