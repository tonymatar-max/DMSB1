using System.Globalization;
using System.Text.RegularExpressions;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Pragmatic regex-heuristic header extractor for the CAPTURE ingest pipeline (ARCHITECTURE.md
/// section 7 step 3, SCOPE NOTE 3 in this phase's task brief). Like <see cref="DocumentClassifier"/>,
/// this is a deliberate Phase 4 simplification, not a corner cut silently taken: there is no
/// labelled invoice dataset in this environment to train or evaluate a real model against, so
/// header fields are pulled out of the raw OCR/PdfPig text with regexes tuned against a handful of
/// realistic invoice layouts (English-language, Gregorian dates, GCC-style tax/VAT IDs). Real
/// invoices vary hugely in layout, so this will miss fields on many real-world documents — that is
/// exactly the gap a real ML/LLM-based extractor (a natural upgrade path once real customer
/// invoices are available to tune against) is meant to close. Every field here is nullable on
/// <see cref="Domain.Capture.ExtractionResult"/> precisely because "not found" is an expected,
/// routine outcome, not a bug — <see cref="IngestPipelineService"/>'s downstream match/exception
/// logic must already treat missing fields as a normal exception reason, not a crash.
/// </summary>
public class InvoiceFieldExtractor
{
    // [:\-] is MANDATORY here, not optional. With it optional, "TAX INVOICE Supplier Tax ID: ..."
    // matched "invoice" from the document's own title heading (nothing to do with the actual
    // "Invoice Number:" label further down) and then - since the capture group accepts any
    // alphanumeric token, not just digit-containing ones - grabbed the very next word ("Supplier")
    // as if it were the invoice number, with no colon/dash ever required between label and value.
    // Requiring a real separator character rules out that false match at the title heading (which
    // has no separator) and forces the regex to fall through to the real "Invoice Number: ..."
    // label, which does. Found by actually running a real invoice through the pipeline and getting
    // "Supplier" back as the invoice number - not by inspection.
    private static readonly Regex InvoiceNumberRegex = new(
        @"(?:invoice|inv)\s*(?:no\.?|number|#)?\s*[:\-]\s*([A-Za-z0-9][A-Za-z0-9\-\/]{2,20})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Same mandatory-separator fix as InvoiceNumberRegex, plus "reference"/"ref" added to the
    // accepted label words - "PO Reference:" is a very common invoice phrasing and the original
    // label list (no/number/#) didn't include it, so the optional label group matched nothing and
    // the mandatory separator then found "Reference" itself with no colon before it, causing the
    // same wrong-word-captured bug this fixes for InvoiceNumberRegex (confirmed: this exact
    // phrasing returned "Reference" as the PO reference in a real end-to-end run).
    private static readonly Regex PoReferenceRegex = new(
        @"(?:p\.?o\.?|purchase\s*order)\s*(?:no\.?|number|#|reference|ref\.?)?\s*[:\-]\s*([A-Za-z0-9][A-Za-z0-9\-\/]{2,20})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // GCC-style tax/VAT registration numbers are commonly 10-15 digit strings, sometimes with
    // dashes. This is a shape heuristic, not a per-country checksum validator.
    private static readonly Regex TaxIdRegex = new(
        @"(?:tax\s*id|vat\s*(?:no\.?|number)?|tax\s*registration\s*(?:no\.?|number)?)\s*[:\-]?\s*([0-9][0-9\-]{7,18}[0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DateRegex = new(
        @"(?:invoice\s*date|date)\s*[:\-]?\s*(\d{1,2}[\/\-.]\d{1,2}[\/\-.]\d{2,4}|\d{4}[\/\-.]\d{1,2}[\/\-.]\d{1,2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrencyRegex = new(
        @"\b(KWD|USD|EUR|GBP|SAR|AED|QAR|BHD|OMR)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "total\s*amount" (bare, no "due") was missing from the alternatives - the plain "Total
    // Amount: 997.50" phrasing (arguably the single most common one) fell through to the bare
    // "total" alternative, which then had no way to skip past the label word "Amount" sitting
    // between "Total" and the number, so the whole regex failed to match at all and TotalAmount
    // came back null even though the text plainly contained it. Confirmed via a real end-to-end
    // run before adding this alternative.
    private static readonly Regex TotalAmountRegex = new(
        @"(?:grand\s*total|total\s*amount\s*due|total\s*amount|total\s*due|total)\s*[:\-]?\s*(?:[A-Z]{3}\s*)?([0-9][0-9,]*\.?[0-9]{0,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NetAmountRegex = new(
        @"(?:sub\s*total|net\s*amount|net\s*total)\s*[:\-]?\s*(?:[A-Z]{3}\s*)?([0-9][0-9,]*\.?[0-9]{0,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TaxAmountRegex = new(
        @"(?:vat|tax)\s*(?:amount)?\s*[:\-]?\s*(?:[A-Z]{3}\s*)?([0-9][0-9,]*\.?[0-9]{0,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Supplier name: best-effort "first non-empty line of the document" heuristic — many invoices
    // put the issuing company's name at the very top, above any "Invoice" label. Deliberately
    // crude; a real extractor would use layout/position information PdfPig also exposes but this
    // phase does not consume.
    public string? ExtractSupplierName(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length is >= 3 and <= 100 && !LooksLikeLabelLine(trimmed))
                return trimmed;
        }
        return null;
    }

    public InvoiceFields Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new InvoiceFields(null, null, null, null, null, null, null, null, null);

        var invoiceNumber = Match(InvoiceNumberRegex, text);
        var poReference = Match(PoReferenceRegex, text);
        var supplierTaxId = Match(TaxIdRegex, text);
        var currency = Match(CurrencyRegex, text)?.ToUpperInvariant();
        var invoiceDate = ParseDate(Match(DateRegex, text));
        var totalAmount = ParseDecimal(Match(TotalAmountRegex, text));
        var netAmount = ParseDecimal(Match(NetAmountRegex, text));
        var taxAmount = ParseDecimal(Match(TaxAmountRegex, text));
        var supplierName = ExtractSupplierName(text);

        return new InvoiceFields(
            SupplierTaxId: supplierTaxId,
            SupplierName: supplierName,
            InvoiceNumber: invoiceNumber,
            InvoiceDate: invoiceDate,
            Currency: currency,
            NetAmount: netAmount,
            TaxAmount: taxAmount,
            TotalAmount: totalAmount,
            PoReference: poReference);
    }

    private static bool LooksLikeLabelLine(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.StartsWith("invoice") || lower.StartsWith("tax invoice") || lower.StartsWith("date")
            || lower.StartsWith("bill to") || lower.StartsWith("ship to") || lower.StartsWith("po ")
            || lower.StartsWith("p.o.");
    }

    private static string? Match(Regex regex, string text)
    {
        var m = regex.Match(text);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (raw is null) return null;

        string[] formats =
        [
            "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yy", "d-M-yy",
            "M/d/yyyy", "yyyy-M-d", "yyyy/M/d",
        ];

        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(raw, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }

        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback
            : null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (raw is null) return null;
        var cleaned = raw.Replace(",", "");
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

/// <summary>Result bag for <see cref="InvoiceFieldExtractor.Extract"/> — one field per matching column on <see cref="Domain.Capture.ExtractionResult"/>.</summary>
public record InvoiceFields(
    string? SupplierTaxId,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? Currency,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? TotalAmount,
    string? PoReference);
