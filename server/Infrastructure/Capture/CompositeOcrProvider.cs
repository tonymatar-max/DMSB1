namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// The <see cref="IOcrProvider"/> the ingest pipeline should actually depend on (see DI
/// registration in Program.cs). Tries <see cref="PdfPigTextProvider"/> first; only attempts a
/// <see cref="TesseractCliOcrProvider"/> fallback when the primary result looks like a scanned
/// image AND tesseract is actually confirmed available on PATH (checked up front — not
/// discovered via a thrown/caught exception used as control flow).
///
/// Because <see cref="TesseractCliOcrProvider"/> does not rasterize PDF pages to images in this
/// phase (see its own doc comment), the fallback branch here cannot currently be exercised for a
/// PDF-shaped input either — the availability check exists so the code is honestly structured for
/// when rasterization is added, but until then this composite provider effectively always returns
/// the PdfPig result for PDF input. That is called out explicitly by the ProviderName below rather
/// than silently pretending OCR happened.
/// </summary>
public class CompositeOcrProvider(
    PdfPigTextProvider primaryProvider,
    TesseractCliOcrProvider fallbackProvider) : IOcrProvider
{
    private string _lastProviderName = primaryProvider.ProviderName;

    public string ProviderName => _lastProviderName;

    public async Task<OcrResult> ExtractTextAsync(Stream pdfContent)
    {
        var primaryResult = await primaryProvider.ExtractTextAsync(pdfContent);
        _lastProviderName = primaryProvider.ProviderName;

        if (!primaryResult.LooksLikeScannedImage)
            return primaryResult;

        // Primary looks like a scanned image (little/no embedded text). Only attempt the
        // fallback when tesseract is actually available — checked explicitly, not via
        // try/catch-as-control-flow.
        var tesseractAvailable = await TesseractCliOcrProvider.IsTesseractAvailableAsync();
        if (!tesseractAvailable)
        {
            _lastProviderName = "PdfPig (no OCR fallback available)";
            return primaryResult;
        }

        // NOTE: as of this phase, TesseractCliOcrProvider.ExtractTextAsync(Stream) always throws
        // NotSupportedException for PDF input because PDF-to-image rasterization is not
        // implemented yet (see that class's doc comment). Until a rasterization step is wired in,
        // this branch cannot succeed for PDF content even though tesseract itself is present — so
        // it is caught here and treated the same as "no fallback available", rather than letting
        // the NotSupportedException bubble up and crash the pipeline.
        try
        {
            var fallbackResult = await fallbackProvider.ExtractTextAsync(pdfContent);
            _lastProviderName = fallbackProvider.ProviderName;
            return fallbackResult;
        }
        catch (NotSupportedException)
        {
            _lastProviderName = "PdfPig (tesseract available, but PDF rasterization not implemented yet)";
            return primaryResult;
        }
    }
}
