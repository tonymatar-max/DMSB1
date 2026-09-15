using NexusDocs.Api.Domain.Sign;

namespace NexusDocs.Api.Models;

/// <summary>
/// DTOs for the public (unauthenticated) signing ceremony and verify endpoints
/// (Api/SigningCeremonyController.cs, Api/VerifyController.cs). Kept separate from
/// Models/SignDtos.cs (the authenticated envelope-authoring DTOs owned by another agent's
/// controller) to avoid a file collision, per the task brief.
/// </summary>

public record CeremonySignatureFieldDto(
    Guid Id,
    SignatureFieldKind Kind,
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height,
    string? Value);

public record CeremonyRecipientDto(
    Guid Id,
    string Name,
    string Email,
    RecipientRole Role,
    RecipientStatus Status);

/// <summary>
/// GET /api/ceremony/{token} response: everything the ceremony page needs to render - the
/// envelope/recipient state and this recipient's own fields. The source document's bytes are
/// fetched separately via GET /api/ceremony/{token}/document (streamed, not embedded here).
/// </summary>
public record CeremonyContextDto(
    Guid EnvelopeId,
    string EnvelopeName,
    string? Message,
    EnvelopeStatus EnvelopeStatus,
    CeremonyRecipientDto Recipient,
    IReadOnlyList<CeremonySignatureFieldDto> Fields);

public record CeremonyFieldValueRequest(Guid FieldId, string Value, string? SignatureImageBase64);

public record CeremonyCompleteRequest(IReadOnlyList<CeremonyFieldValueRequest> Fields);

public record CeremonyDeclineRequest(string? Reason);

/// <summary>
/// Response for consent/complete/decline: current recipient status plus whether this action
/// finished the whole envelope, so the client can show a "fully executed" state rather than just
/// "your part is done".
/// </summary>
public record CeremonyActionResultDto(
    RecipientStatus RecipientStatus,
    EnvelopeStatus EnvelopeStatus,
    bool EnvelopeCompleted);

/// <summary>
/// GET /api/verify/{envelopeId} response (ARCHITECTURE.md section 6.2's public verify endpoint).
/// Re-hashes the sealed blob fresh from IBlobStore and compares it to the stored DocumentVersion
/// hash - Valid is false if they diverge (tamper/corruption) even though both are returned.
/// </summary>
public record VerifyResultDto(
    bool Valid,
    string EnvelopeName,
    DateTimeOffset? CompletedAt,
    int RecipientCount,
    string SealedBlobHash);
