using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Sign;

public enum SignatureCaptureMethod
{
    Drawn,
    Typed,
    Uploaded,
}

/// <summary>
/// The captured signature image for one recipient/field, stored as a PNG via the existing
/// content-addressed IBlobStore. This is SES-level assurance (drawn/typed signature + email-verified
/// identity + full audit trail, per ARCHITECTURE.md section 6.1) - NOT a cryptographic seal.
/// TODO(ARCHITECTURE.md section 6.2): once the PDF signing library decision is made (iText 8 AGPL+paid
/// licence / Apryse / from-scratch BouncyCastle PAdES), this capture may additionally feed a real
/// cryptographic signature; today it only supplies the visible overlay drawn onto the sealed PDF.
/// </summary>
public class SignatureCapture : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid SignatureFieldId { get; set; }

    public SignatureCaptureMethod Method { get; set; }

    /// <summary>SHA-256 content hash of the PNG signature image in IBlobStore.</summary>
    public string ImageBlobHash { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
