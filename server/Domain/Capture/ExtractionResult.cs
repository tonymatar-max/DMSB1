using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Capture;

/// <summary>
/// Output of running an <see cref="IngestItem"/>'s file through OCR/text extraction
/// (IOcrProvider, see Infrastructure/Capture) and the keyword/regex header extraction described
/// in ARCHITECTURE.md section 7 step 3. <see cref="RawText"/> and <see cref="OcrProviderUsed"/>
/// are kept for later accuracy analysis - see the open item on OCR accuracy in
/// ARCHITECTURE.md section 11.
///
/// The header fields below are produced by pragmatic regex/keyword heuristics, not a trained
/// model - there is no labelled invoice dataset in this environment to train or evaluate one.
/// A real ML/LLM-based extractor is a natural upgrade path once real customer invoices are
/// available to tune against.
/// </summary>
public class ExtractionResult : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid IngestItemId { get; set; }

    /// <summary>Which IOcrProvider actually produced the text, e.g. "PdfPig" or "Tesseract".</summary>
    public string OcrProviderUsed { get; set; } = string.Empty;

    /// <summary>Full extracted text, kept for debugging bad extractions.</summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>Classifier output, e.g. "Invoice".</summary>
    public string? DocumentTypeGuess { get; set; }

    public string? SupplierTaxId { get; set; }

    public string? SupplierName { get; set; }

    public string? InvoiceNumber { get; set; }

    public DateOnly? InvoiceDate { get; set; }

    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }

    public decimal? TaxAmount { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? PoReference { get; set; }

    public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.UtcNow;
}
