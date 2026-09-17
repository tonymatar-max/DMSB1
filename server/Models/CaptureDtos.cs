using NexusDocs.Api.Domain.Capture;

namespace NexusDocs.Api.Models;

// ---------------------------------------------------------------------------------------------
// IngestSource DTOs (ARCHITECTURE.md section 7 step 1 "capture")
// ---------------------------------------------------------------------------------------------

public record IngestSourceDto(
    Guid Id,
    IngestSourceKind Kind,
    string Name,
    bool IsActive,
    string? HotFolderPath,
    string? ImapHost,
    int? ImapPort,
    bool ImapUseSsl,
    string? ImapUsername,
    string? ImapFolderName,
    bool HasImapCredentials,
    DateTimeOffset? LastPolledAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Shared create/update request shape for both IngestSource kinds. Fields not relevant to
/// <see cref="Kind"/> are simply ignored server-side (e.g. HotFolderPath on an Imap source) —
/// same "one flexible request DTO" convention as elsewhere in this API.
/// </summary>
public record UpsertIngestSourceRequest(
    IngestSourceKind Kind,
    string Name,
    bool IsActive,
    string? HotFolderPath,
    string? ImapHost,
    int? ImapPort,
    bool? ImapUseSsl,
    string? ImapUsername,
    string? ImapPassword,
    string? ImapFolderName);

// ---------------------------------------------------------------------------------------------
// IngestItem / "AP automation inbox" DTOs (ARCHITECTURE.md section 7)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Row shape for the primary AP automation inbox list (GET /api/ingest-items). Extraction and
/// match summary fields are pulled from the item's latest ExtractionResult / MatchResult, if any
/// exist yet (an item that failed before extraction has both as null).
/// </summary>
public record IngestItemListDto(
    Guid Id,
    Guid IngestBatchId,
    Guid IngestSourceId,
    string OriginalFileName,
    IngestItemStatus Status,
    string? ErrorMessage,
    string? SupplierName,
    string? InvoiceNumber,
    decimal? TotalAmount,
    string? Currency,
    MatchOutcome? MatchOutcome,
    string? VarianceReasons,
    Guid? WorkflowInstanceId,
    Guid? ArchivedDocumentId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt);

public record ExtractionResultDto(
    Guid Id,
    string OcrProviderUsed,
    string RawText,
    string? DocumentTypeGuess,
    string? SupplierTaxId,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? Currency,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    string? PoReference,
    DateTimeOffset ExtractedAt);

public record MatchResultDto(
    Guid Id,
    MatchOutcome Outcome,
    string? MatchedCardCode,
    int? MatchedPoDocEntry,
    string? MatchedPoDocNum,
    int? MatchedGrpoDocEntry,
    string? MatchedGrpoDocNum,
    string? VarianceReasons,
    DateTimeOffset MatchedAt);

/// <summary>
/// Full detail for one IngestItem (GET /api/ingest-items/{id}): the item itself, every extraction
/// and match attempt made on it (usually one of each — see the "kept as a collection in case
/// re-extraction/re-matching is retried" note on IngestItem), and enough of the linked
/// WorkflowInstance for the client to route to the existing FLOW instance detail view when this
/// item was routed as an A/P exception.
/// </summary>
public record IngestItemDetailDto(
    Guid Id,
    Guid IngestBatchId,
    Guid IngestSourceId,
    string OriginalFileName,
    string ContentHash,
    IngestItemStatus Status,
    string? ErrorMessage,
    Guid? ArchivedDocumentId,
    Guid? WorkflowInstanceId,
    string? WorkflowInstanceStatus,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    IReadOnlyList<ExtractionResultDto> Extractions,
    IReadOnlyList<MatchResultDto> Matches);
