using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Sign;

public enum CeremonyEventType
{
    Sent,
    Viewed,
    ConsentGiven,
    FieldsCompleted,
    Signed,
    Declined,
    EnvelopeCompleted,
    EnvelopeVoided,
}

/// <summary>
/// One entry in the envelope's audit trail: every recipient, every ceremony event, with timestamp,
/// IP, and user agent. This is the source data the "Certificate of Completion" page (appended to the
/// sealed PDF via PDFsharp - see ARCHITECTURE.md section 6.2) is built from.
/// </summary>
public class CeremonyEvent : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EnvelopeId { get; set; }

    /// <summary>Null for envelope-level events (e.g. EnvelopeVoided) that aren't tied to one recipient.</summary>
    public Guid? RecipientId { get; set; }

    public CeremonyEventType EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Free-form detail, e.g. identity-proofing method, decline reason context, etc.</summary>
    public string? Detail { get; set; }
}
