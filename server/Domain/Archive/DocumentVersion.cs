using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

/// <summary>An immutable version of a <see cref="Document"/>'s content, identified by the sha256 hash of its blob.</summary>
public class DocumentVersion : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }

    /// <summary>sha256 hex digest of the stored blob.</summary>
    public string BlobHash { get; set; } = string.Empty;

    /// <summary>The uploaded file's original name (e.g. "invoice.pdf"), kept so downloads and
    /// content-type inference don't depend on the content-addressed blob key.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public int? PageCount { get; set; }

    /// <summary>The User who authored this version.</summary>
    public Guid AuthorId { get; set; }

    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Rendition> Renditions { get; set; } = new List<Rendition>();
}
