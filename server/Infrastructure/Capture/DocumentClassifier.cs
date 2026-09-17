namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Pragmatic keyword-heuristic classifier for the CAPTURE ingest pipeline (ARCHITECTURE.md section
/// 7 step 3, SCOPE NOTE 3 in this phase's task brief). There is no labelled invoice dataset in this
/// environment to train or evaluate a real model against, so this is a deliberate, honestly-scoped
/// Phase 4 simplification: presence of a small set of keywords (in English, plus a couple of common
/// French/Arabic-transliteration variants a Kuwait/GCC tenant is likely to see) against the raw
/// extracted text decides <see cref="DocumentTypeGuess"/>. A real ML/LLM-based classifier is a
/// natural upgrade path once real customer invoices are available to tune against — swap the body
/// of <see cref="Classify"/> for that call without touching any caller.
/// </summary>
public class DocumentClassifier
{
    private static readonly string[] InvoiceKeywords =
    [
        "invoice", "tax invoice", "facture", "sales invoice", "commercial invoice",
    ];

    private static readonly string[] CreditNoteKeywords =
    [
        "credit note", "credit memo", "avoir",
    ];

    private static readonly string[] PurchaseOrderKeywords =
    [
        "purchase order", "po number", "bon de commande",
    ];

    private static readonly string[] DeliveryNoteKeywords =
    [
        "delivery note", "goods receipt", "packing slip", "bon de livraison",
    ];

    /// <summary>
    /// Returns a short classifier label (matches <see cref="Domain.Capture.ExtractionResult.DocumentTypeGuess"/>)
    /// or "Unknown" when no keyword set matches. Checked in a fixed priority order — most specific
    /// (CreditNote) before the more generic "Invoice" bucket — since a credit note's text commonly
    /// also contains the word "invoice" (e.g. "credit note against invoice 12345").
    /// </summary>
    public string Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Unknown";

        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, CreditNoteKeywords)) return "CreditNote";
        if (ContainsAny(lower, PurchaseOrderKeywords)) return "PurchaseOrder";
        if (ContainsAny(lower, DeliveryNoteKeywords)) return "DeliveryNote";
        if (ContainsAny(lower, InvoiceKeywords)) return "Invoice";

        return "Unknown";
    }

    private static bool ContainsAny(string haystackLower, string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystackLower.Contains(needle, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
