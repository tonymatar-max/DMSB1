using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Capture;

/// <summary>Lifecycle status of an ingest batch (one poll/scan run of an IngestSource).</summary>
public enum IngestBatchStatus
{
    Processing = 0,
    Completed = 1,
    Failed = 2,
}

/// <summary>
/// One run of pulling files from an <see cref="IngestSource"/> (one hot-folder scan, or one
/// IMAP unseen-message check) and the items it produced. See ARCHITECTURE.md section 7 step 1.
/// </summary>
public class IngestBatch : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid IngestSourceId { get; set; }

    public IngestBatchStatus Status { get; set; } = IngestBatchStatus.Processing;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public int ItemCount { get; set; }

    public ICollection<IngestItem> Items { get; set; } = new List<IngestItem>();
}
