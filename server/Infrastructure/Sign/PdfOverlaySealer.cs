using System.Security.Cryptography;
using NexusDocs.Api.Domain.Sign;
using NexusDocs.Api.Infrastructure.Files;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using NexusDocs.Api.Infrastructure.Flow;

namespace NexusDocs.Api.Infrastructure.Sign;

/// <summary>
/// IMPORTANT SCOPE NOTE (ARCHITECTURE.md section 6.2): the architecture calls for a PAdES (target
/// B-LTA) cryptographic signature — RFC 3161 timestamp, HSM-held signing certificate, PDF/A-3b
/// output, embedded audit XML — over the document. That requires a PDF-signing library decision
/// (iText 8 AGPL+paid licence vs Apryse vs a from-scratch BouncyCastle PAdES signer) that has
/// explicitly NOT been made yet (see the "Build decision to settle early" note in section 6.2 and
/// item #1 in section 11's Open decisions table).
///
/// This class does NOT implement PAdES/cryptographic signing. It uses PDFsharp (MIT-licensed, no
/// commercial decision needed) to produce SES-level assurance only (ARCHITECTURE.md section 6.1:
/// "drawn/typed signature + email-verified identity + full audit trail"): a visible signature/text
/// overlay drawn onto each completed field's position, plus an appended "Certificate of Completion"
/// page listing every recipient and ceremony event. The result's SHA-256 hash is recorded via the
/// existing content-addressed IBlobStore and the hash-chained AuditService, which is what gives this
/// SES-level seal its integrity proof today.
///
/// TODO(ARCHITECTURE.md section 6.2): once the PDF library decision is made, this class is what
/// gets replaced (or wrapped) by a real PAdES signer. IPdfSealer's shape (source blob hash in,
/// sealed blob hash out) is intentionally generic enough that no caller needs to change when that
/// happens.
/// </summary>
public class PdfOverlaySealer : IPdfSealer
{
    private static readonly XFont CaptionFont = new("Helvetica", 7, XFontStyleEx.Regular);
    private static readonly XFont FieldValueFont = new("Helvetica", 10, XFontStyleEx.Regular);
    private static readonly XFont CertTitleFont = new("Helvetica", 18, XFontStyleEx.Bold);
    private static readonly XFont CertHeadingFont = new("Helvetica", 11, XFontStyleEx.Bold);
    private static readonly XFont CertBodyFont = new("Courier New", 8, XFontStyleEx.Regular);

    private readonly IBlobStore _blobStore;

    public PdfOverlaySealer(IBlobStore blobStore)
    {
        _blobStore = blobStore;
    }

    public async Task<string> SealAsync(
        Guid tenantId,
        string sourceBlobHash,
        Envelope envelope,
        IReadOnlyList<SignatureField> completedFields,
        IReadOnlyList<Recipient> recipients,
        IReadOnlyList<CeremonyEvent> events)
    {
        byte[] sourceBytes;
        await using (var sourceStream = await _blobStore.GetAsync(tenantId, sourceBlobHash))
        await using (var buffer = new MemoryStream())
        {
            await sourceStream.CopyToAsync(buffer);
            sourceBytes = buffer.ToArray();
        }

        PdfDocument document;
        try
        {
            document = PdfReader.Open(new MemoryStream(sourceBytes), PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A malformed/corrupt source PDF (e.g. a hand-crafted or truncated file that slipped
            // past upload) must not surface PdfSharp's internal parser exception + stack trace to
            // the (unauthenticated, in the ceremony-complete case) caller as an unhandled 500.
            // Bug found via QA: completing a ceremony whose source document is not a valid PDF
            // threw PdfReaderException straight through to a 500 response.
            throw new ValidationException(
                "The source document could not be sealed: it is not a valid PDF file.");
        }
        using (document)
        {

        var recipientsById = recipients.ToDictionary(r => r.Id);

        foreach (var field in completedFields)
        {
            if (string.IsNullOrEmpty(field.Value)) continue;
            await DrawFieldAsync(document, tenantId, field, recipientsById);
        }

        AppendCertificateOfCompletion(document, envelope, recipients, events);

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        output.Position = 0;

        // Compute the hash ourselves rather than trust IBlobStore to hand it back unchanged, so the
        // returned value always matches exactly what was written to storage.
        string sealedHash;
        using (var sha256 = SHA256.Create())
        {
            sealedHash = Convert.ToHexString(sha256.ComputeHash(output.ToArray())).ToLowerInvariant();
        }

        output.Position = 0;
        await _blobStore.PutAsync(tenantId, output);

        return sealedHash;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Field overlay
    // -------------------------------------------------------------------------------------------

    private async Task DrawFieldAsync(PdfDocument document, Guid tenantId, SignatureField field, IReadOnlyDictionary<Guid, Recipient> recipientsById)
    {
        // Domain pages are 1-based (SignatureField.PageNumber); PdfSharp's Pages collection is
        // 0-indexed. A field pointing past the end of the (possibly shorter, e.g. re-uploaded)
        // source document is skipped rather than throwing — better a missing overlay than a failed
        // seal for the whole envelope.
        var pageIndex = field.PageNumber - 1;
        if (pageIndex < 0 || pageIndex >= document.PageCount) return;

        var page = document.Pages[pageIndex];
        using var gfx = XGraphics.FromPdfPage(page);

        // --- Coordinate conversion --------------------------------------------------------------
        // SignatureField.X/Y/Width/Height are normalized 0-1 fractions of the page, with origin at
        // the TOP-LEFT of the page (same convention as Domain/Archive/Annotation.cs, so the client's
        // field-placement UI and the sealed PDF agree on where a field sits).
        //
        // XGraphics.FromPdfPage(page), when drawing directly onto an existing page (as opposed to a
        // freshly created page you also size yourself), uses PDF's native coordinate space: origin
        // at the BOTTOM-LEFT of the page, Y increasing upward. So a top-left-origin normalized
        // rectangle has to be (a) scaled by the page's point dimensions, then (b) flipped vertically
        // by subtracting from the page height.
        //
        // page.Width / page.Height are XUnit (points, 1/72 inch) for the page's own MediaBox size.
        // Given normalized (x, y, w, h) with y measured DOWN from the top:
        //   pxLeft   = x * pageWidthPt
        //   pxTop    = y * pageHeightPt                     (distance from the TOP, still top-origin)
        //   pxBottom = pageHeightPt - (y + h) * pageHeightPt (flip: PDF Y is measured UP from bottom)
        //   pxWidth  = w * pageWidthPt
        //   pxHeight = h * pageHeightPt
        // The rectangle passed to DrawImage/DrawString below is (pxLeft, pxBottom, pxWidth, pxHeight)
        // in PDF bottom-left-origin points, which is what XGraphics expects for direct page drawing.
        var pageWidthPt = page.Width.Point;
        var pageHeightPt = page.Height.Point;

        var pxLeft = field.X * pageWidthPt;
        var pxWidth = field.Width * pageWidthPt;
        var pxHeight = field.Height * pageHeightPt;
        var pxBottom = pageHeightPt - (field.Y + field.Height) * pageHeightPt;

        var rect = new XRect(pxLeft, pxBottom, pxWidth, pxHeight);

        var recipient = recipientsById.GetValueOrDefault(field.RecipientId);
        var signerName = recipient?.Name ?? "Unknown signer";

        switch (field.Kind)
        {
            case SignatureFieldKind.Signature:
            case SignatureFieldKind.Initial:
                await DrawSignatureImageAsync(gfx, tenantId, field, rect);
                DrawCaption(gfx, rect, signerName, recipient?.SignedAt);
                break;

            case SignatureFieldKind.Text:
            case SignatureFieldKind.DateSigned:
            case SignatureFieldKind.Checkbox:
                gfx.DrawString(field.Value, FieldValueFont, XBrushes.Black, rect, XStringFormats.CenterLeft);
                break;
        }
    }

    private async Task DrawSignatureImageAsync(XGraphics gfx, Guid tenantId, SignatureField field, XRect rect)
    {
        // field.Value holds the SignatureCapture's ImageBlobHash for Signature/Initial fields (see
        // SignatureField.Value doc comment) — the caller is expected to have already resolved Value
        // to the capture's ImageBlobHash before invoking SealAsync.
        if (string.IsNullOrEmpty(field.Value)) return;

        try
        {
            using var buffer = new MemoryStream();
            await using (var imageStream = await _blobStore.GetAsync(tenantId, field.Value))
            {
                await imageStream.CopyToAsync(buffer);
            }
            buffer.Position = 0;
            using var image = XImage.FromStream(buffer);
            gfx.DrawImage(image, rect);
        }
        catch (Exception)
        {
            // A missing/unreadable signature image must not abort sealing the whole envelope; fall
            // back to a text placeholder so the gap is visible instead of silently dropped.
            gfx.DrawString("[signature image unavailable]", FieldValueFont, XBrushes.Black, rect, XStringFormats.CenterLeft);
        }
    }

    private void DrawCaption(XGraphics gfx, XRect fieldRect, string signerName, DateTimeOffset? signedAt)
    {
        var timestamp = signedAt?.ToString("u") ?? "";
        var caption = $"{signerName} - Signed via Nexus Docs - {timestamp}";
        var captionRect = new XRect(fieldRect.X, fieldRect.Y - 10, Math.Max(fieldRect.Width, 150), 10);
        gfx.DrawString(caption, CaptionFont, XBrushes.DimGray, captionRect, XStringFormats.TopLeft);
    }

    // -------------------------------------------------------------------------------------------
    // Certificate of Completion
    // -------------------------------------------------------------------------------------------

    private void AppendCertificateOfCompletion(
        PdfDocument document,
        Envelope envelope,
        IReadOnlyList<Recipient> recipients,
        IReadOnlyList<CeremonyEvent> events)
    {
        const double marginPt = 40;
        const double lineHeightPt = 14;

        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        double y = marginPt;

        y = DrawCertLine(gfx, page, "Certificate of Completion", CertTitleFont, marginPt, y, lineHeightPt * 1.5);
        y = DrawCertLine(gfx, page, $"Envelope: {envelope.Name} ({envelope.Id})", CertHeadingFont, marginPt, y, lineHeightPt);
        y = DrawCertLine(gfx, page, $"Source document: {envelope.SourceDocumentId}", CertBodyFont, marginPt, y, lineHeightPt);
        y = DrawCertLine(gfx, page, $"Status: {envelope.Status}   Completed: {envelope.CompletedAt?.ToString("u") ?? "-"}", CertBodyFont, marginPt, y, lineHeightPt * 1.5);

        y = DrawCertLine(gfx, page, "Recipients", CertHeadingFont, marginPt, y, lineHeightPt);
        foreach (var recipient in recipients)
        {
            (gfx, page, y) = EnsureRoom(document, gfx, page, y, lineHeightPt, marginPt);
            var line = $"{recipient.Name} <{recipient.Email}>  role={recipient.Role}  order={recipient.SigningOrder}  status={recipient.Status}";
            y = DrawCertLine(gfx, page, line, CertBodyFont, marginPt, y, lineHeightPt);
        }

        y += lineHeightPt / 2;
        (gfx, page, y) = EnsureRoom(document, gfx, page, y, lineHeightPt, marginPt);
        y = DrawCertLine(gfx, page, "Ceremony events", CertHeadingFont, marginPt, y, lineHeightPt);

        var recipientsById = recipients.ToDictionary(r => r.Id);
        foreach (var evt in events.OrderBy(e => e.OccurredAt))
        {
            (gfx, page, y) = EnsureRoom(document, gfx, page, y, lineHeightPt, marginPt);

            var who = evt.RecipientId is Guid rid && recipientsById.TryGetValue(rid, out var r) ? r.Name : "(envelope)";
            var ip = string.IsNullOrEmpty(evt.IpAddress) ? "-" : evt.IpAddress;
            var ua = string.IsNullOrEmpty(evt.UserAgent) ? "-" : Truncate(evt.UserAgent, 60);
            var line = $"{evt.OccurredAt:u}  {evt.EventType,-18} {who,-24} ip={ip,-15} ua={ua}";
            y = DrawCertLine(gfx, page, line, CertBodyFont, marginPt, y, lineHeightPt);
        }
    }

    /// <summary>Draws one line of text and returns the Y position for the next line.</summary>
    private static double DrawCertLine(XGraphics gfx, PdfPage page, string text, XFont font, double x, double y, double lineHeightPt)
    {
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y + font.Height));
        return y + lineHeightPt;
    }

    /// <summary>Appends a new certificate page and returns fresh (gfx, page, y) when the current page is full.</summary>
    private static (XGraphics Gfx, PdfPage Page, double Y) EnsureRoom(
        PdfDocument document, XGraphics gfx, PdfPage page, double y, double lineHeightPt, double marginPt)
    {
        if (y + lineHeightPt <= page.Height.Point - marginPt)
        {
            return (gfx, page, y);
        }

        gfx.Dispose();
        var newPage = document.AddPage();
        var newGfx = XGraphics.FromPdfPage(newPage);
        return (newGfx, newPage, marginPt);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
