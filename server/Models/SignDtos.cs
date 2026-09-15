using NexusDocs.Api.Domain.Sign;

namespace NexusDocs.Api.Models;

// ---------------------------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------------------------

public record CreateEnvelopeRecipientRequest(string Email, string Name, RecipientRole Role, int SigningOrder);

public record CreateEnvelopeFieldRequest(
    int RecipientIndex,
    SignatureFieldKind Kind,
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height);

public record CreateEnvelopeRequest(
    Guid SourceDocumentId,
    string Name,
    string? Message,
    List<CreateEnvelopeRecipientRequest> Recipients,
    List<CreateEnvelopeFieldRequest> Fields);

// ---------------------------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------------------------

/// <summary>
/// One recipient as returned by the create/send/detail endpoints. CeremonyUrl/CeremonyToken are
/// only populated while the recipient's ceremony is still usable (Pending/Sent/Viewed) - per the
/// task brief, a completed ceremony's token is spent and is not re-exposed once the recipient has
/// Signed or Declined.
/// </summary>
public record EnvelopeRecipientDto(
    Guid Id,
    string Email,
    string Name,
    RecipientRole Role,
    int SigningOrder,
    RecipientStatus Status,
    string? CeremonyToken,
    string? CeremonyUrl,
    DateTimeOffset? ViewedAt,
    DateTimeOffset? ConsentedAt,
    DateTimeOffset? SignedAt,
    DateTimeOffset? DeclinedAt,
    string? DeclineReason);

public record EnvelopeFieldDto(
    Guid Id,
    Guid RecipientId,
    SignatureFieldKind Kind,
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height,
    string? Value);

public record CeremonyEventDto(
    Guid Id,
    Guid? RecipientId,
    CeremonyEventType EventType,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string? UserAgent,
    string? Detail);

/// <summary>
/// Full envelope detail, returned by create/send (so the sender can immediately copy every
/// activated recipient's ceremony URL - there is no outbound email/SMS delivery integration in
/// this codebase yet, so this response body IS the delivery mechanism for Phase 3) and by the
/// detail GET.
/// </summary>
public record EnvelopeDetailDto(
    Guid Id,
    Guid SourceDocumentId,
    string Name,
    string? Message,
    Guid SenderId,
    EnvelopeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt,
    Guid? SealedDocumentVersionId,
    List<EnvelopeRecipientDto> Recipients,
    List<EnvelopeFieldDto> Fields,
    List<CeremonyEventDto> Events);

/// <summary>Summary row for the envelope list.</summary>
public record EnvelopeListItemDto(
    Guid Id,
    string Name,
    EnvelopeStatus Status,
    Guid SourceDocumentId,
    string? SourceDocumentFileName,
    int RecipientCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt);
