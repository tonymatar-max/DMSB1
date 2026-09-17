using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Gen;

/// <summary>
/// One rendering of a <see cref="DocumentTemplate"/>: the exact data merged in (kept for audit and
/// reproducibility), the resulting PDF (stored via the existing IBlobStore, referenced by hash), and
/// - per ARCHITECTURE.md section 1.2 - where it ended up: pushed into an ARCHIVE cabinet as a new
/// Document (via the existing DocumentVersion pattern), fed into a SIGN Envelope, or left standalone
/// (both nullable; neither, either, or the generation flow may set one and later the other).
/// </summary>
public class GeneratedDocument : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid DocumentTemplateId { get; set; }

    /// <summary>The User who triggered this generation.</summary>
    public Guid GeneratedByUserId { get; set; }

    /// <summary>The actual data merged into the template for this specific generation (JSON).</summary>
    public string InputDataJson { get; set; } = string.Empty;

    /// <summary>Content hash of the rendered PDF, as stored via IBlobStore.</summary>
    public string RenderedBlobHash { get; set; } = string.Empty;

    /// <summary>Set if this generation was pushed into ARCHIVE as a new Document.</summary>
    public Guid? ArchivedDocumentId { get; set; }

    /// <summary>Set if this generation was pushed into a SIGN Envelope for signature.</summary>
    public Guid? EnvelopeId { get; set; }

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public DocumentTemplate? DocumentTemplate { get; set; }
}
