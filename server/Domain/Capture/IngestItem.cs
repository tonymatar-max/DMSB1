using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Capture;

/// <summary>
/// Lifecycle status of a single captured document as it moves through the pipeline
/// (ARCHITECTURE.md section 7): received, OCR/extraction, then either a clean match (auto-filed
/// and posted) or an exception routed into FLOW.
/// </summary>
public enum IngestItemStatus
{
    Received = 0,
    Extracting = 1,
    Extracted = 2,
    Matched = 3,
    MatchedWithExceptions = 4,
    Posted = 5,
    Failed = 6,
}

/// <summary>
/// One captured file (typically one PDF invoice) within an <see cref="IngestBatch"/>. This is
/// the subject that flows through extraction (<see cref="ExtractionResult"/>) and matching
/// (<see cref="MatchResult"/>) and ends up either archived (ArchivedDocumentId) or routed into
/// FLOW as an A/P exception (WorkflowInstanceId) - see ARCHITECTURE.md section 7.
/// </summary>
public class IngestItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid IngestBatchId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the file content, used to de-dupe re-ingested files (section 7 step 2).</summary>
    public string ContentHash { get; set; } = string.Empty;

    public IngestItemStatus Status { get; set; } = IngestItemStatus.Received;

    /// <summary>Set once this item is filed into ARCHIVE as a Document.</summary>
    public Guid? ArchivedDocumentId { get; set; }

    /// <summary>Set if this item was routed into FLOW as an A/P exception workflow.</summary>
    public Guid? WorkflowInstanceId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Usually one entry; kept as a collection in case re-extraction is retried.</summary>
    public ICollection<ExtractionResult> Extractions { get; set; } = new List<ExtractionResult>();

    /// <summary>Usually one entry; kept as a collection in case re-matching is retried.</summary>
    public ICollection<MatchResult> Matches { get; set; } = new List<MatchResult>();
}
