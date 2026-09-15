using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Sign;

public enum SignatureFieldKind
{
    Signature,
    Initial,
    DateSigned,
    Text,
    Checkbox,
}

/// <summary>
/// A field placed on one page of the envelope's source document for one recipient to fill in.
/// Coordinates are normalized (0-1) relative to page width/height, matching the convention used by
/// Domain/Archive/Annotation.cs for consistency across the codebase.
/// </summary>
public class SignatureField : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EnvelopeId { get; set; }
    public Guid RecipientId { get; set; }

    public SignatureFieldKind Kind { get; set; }

    /// <summary>1-based page number within the source document.</summary>
    public int PageNumber { get; set; }

    /// <summary>Normalized (0-1) position/size relative to the page.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// Filled in once completed: for Signature/Initial, a reference (blob hash) to the captured
    /// signature image; for Text, the typed value; for DateSigned, the ISO date; for Checkbox,
    /// "true"/"false".
    /// </summary>
    public string? Value { get; set; }
}
