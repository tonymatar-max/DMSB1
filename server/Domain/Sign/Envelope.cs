using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Sign;

public enum EnvelopeStatus
{
    Draft,
    Sent,
    Completed,
    Declined,
    Voided,
}

/// <summary>
/// A signing envelope targeting the current version of an existing Archive.Document. On completion,
/// the sealed PDF is stored as a NEW Archive.DocumentVersion on that same Document (Phase 3 deliberately
/// reuses Archive's existing versioning rather than inventing parallel SIGN storage - see ARCHITECTURE.md
/// section 6.3).
/// </summary>
public class Envelope : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>The Archive.Document being signed (its current version's PDF at time of send).</summary>
    public Guid SourceDocumentId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional message shown to recipients during the ceremony.</summary>
    public string? Message { get; set; }

    /// <summary>The User who created/sent this envelope.</summary>
    public Guid SenderId { get; set; }

    public EnvelopeStatus Status { get; set; } = EnvelopeStatus.Draft;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Set once sealed - FK to the new Archive.DocumentVersion created holding the sealed PDF
    /// (visible-signature overlay + certificate of completion page, per PDFsharp; see
    /// ARCHITECTURE.md section 6.2 for the deferred cryptographic PAdES decision).
    /// </summary>
    public Guid? SealedDocumentVersionId { get; set; }

    public ICollection<Recipient> Recipients { get; set; } = new List<Recipient>();
    public ICollection<SignatureField> Fields { get; set; } = new List<SignatureField>();
    public ICollection<CeremonyEvent> Events { get; set; } = new List<CeremonyEvent>();
}
