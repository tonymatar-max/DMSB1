using UglyToad.PdfPig;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Primary <see cref="IOcrProvider"/> implementation: extracts the embedded text layer of a PDF
/// using the free, MIT-licensed, pure-C# "PdfPig" (UglyToad.PdfPig) NuGet package. No external
/// binary, no account, no network call — fully real and fully testable in this environment.
///
/// This only works for PDFs that already carry a text layer (i.e. almost every PDF produced by
/// accounting/invoicing software, Word/Excel "print to PDF", etc.). A scanned/photographed
/// invoice with no text layer will extract to empty or near-empty text; see
/// <see cref="LowTextCharacterThreshold"/> and <see cref="CompositeOcrProvider"/> for how the
/// pipeline detects and handles that case.
/// </summary>
public class PdfPigTextProvider : IOcrProvider
{
    /// <summary>
    /// Below this total extracted character count (across all pages), the PDF is treated as
    /// having no meaningful text layer — i.e. likely a scanned image. Chosen small and
    /// conservative: a genuine invoice with real text almost always produces far more than this
    /// many characters even on a single short page, while a fully scanned page with no text layer
    /// extracts to exactly 0 (or occasionally a handful of stray characters from PDF metadata/
    /// artifacts PdfPig picks up).
    /// </summary>
    public const int LowTextCharacterThreshold = 20;

    public string ProviderName => "PdfPig";

    public Task<OcrResult> ExtractTextAsync(Stream pdfContent)
    {
        // PdfPig needs a seekable stream; callers should pass one (e.g. a MemoryStream, or a
        // FileStream opened for read) rather than a forward-only network stream.
        using var document = PdfDocument.Open(pdfContent);

        var textBuilder = new System.Text.StringBuilder();
        foreach (var page in document.GetPages())
        {
            // page.Text already concatenates the page's words/content in reading order; this is
            // the standard PdfPig way to get "the text of the page" without hand-rolling
            // word-joining logic.
            textBuilder.AppendLine(page.Text);
        }

        var text = textBuilder.ToString();
        var trimmedLength = text.Trim().Length;

        return Task.FromResult(new OcrResult(
            Text: text,
            LooksLikeScannedImage: trimmedLength < LowTextCharacterThreshold));
    }
}
