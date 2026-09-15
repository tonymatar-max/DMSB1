using NexusDocs.Api.Domain.Sign;

namespace NexusDocs.Api.Infrastructure.Sign;

/// <summary>
/// Produces the sealed copy of an envelope's source PDF: a visible signature overlay per completed
/// field plus an appended "Certificate of Completion" page (ARCHITECTURE.md section 6.2). See
/// <see cref="PdfOverlaySealer"/> for the SES-vs-PAdES scope note. The interface is deliberately
/// generic (source blob hash in, sealed blob hash out) so a future PAdES-capable implementation can
/// replace or wrap this one without any caller change.
/// </summary>
public interface IPdfSealer
{
    /// <summary>
    /// Seals <paramref name="sourceBlobHash"/> for <paramref name="envelope"/> and returns the new
    /// sealed PDF's blob hash. The sealed PDF is already written to <c>IBlobStore</c> for
    /// <paramref name="tenantId"/> by the time this returns — callers do not handle storage.
    /// </summary>
    Task<string> SealAsync(
        Guid tenantId,
        string sourceBlobHash,
        Envelope envelope,
        IReadOnlyList<SignatureField> completedFields,
        IReadOnlyList<Recipient> recipients,
        IReadOnlyList<CeremonyEvent> events);
}
