using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Capture;
using NexusDocs.Api.Domain.Erp;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Implements the three-way match (ARCHITECTURE.md section 7 steps 4-5): resolve the supplier, the
/// referenced Purchase Order, and its Goods Receipt PO from the cached ERP master data
/// (<see cref="ErpLookupCacheEntry"/>, populated by the ERP module per ARCHITECTURE.md section 4.2),
/// then compare invoice total against PO total within a tolerance. This deliberately reads from the
/// cache rather than calling <c>IErpAdapter.LookupObjectAsync</c> live, since a three-way match needs
/// to check several related objects quickly for every ingested invoice and should not require a
/// live B1 round-trip per document.
///
/// PHASE 4 ASSUMPTIONS ABOUT CACHED DataJson SHAPE (no live B1 connection exists in this
/// environment to observe the real Service Layer payload shape, so these are documented,
/// reasonable assumptions the build-fix stage should seed synthetic ErpLookupCacheEntry rows
/// against):
///   - BusinessPartner (ObjectType=2): DataJson is a flat JSON object with a "TaxId" string field
///     (SAP B1's LicTradNum on OCRD, denormalised into the cache under this name).
///   - PurchaseOrder (ObjectType=22): DataJson is a flat JSON object with a "DocTotal" numeric
///     field (the PO's document total, in document currency) used for the price-variance check.
///     ErpLookupCacheEntry.ExternalKey / DisplayLabel carry the DocNum used to match
///     ExtractionResult.PoReference (compared as strings).
///   - GoodsReceiptPO (ObjectType=20): DataJson is a flat JSON object with a "BaseEntry" numeric
///     field pointing at the base PO's DocEntry (SAP B1's own PCH1.BaseEntry convention), which is
///     how this service associates a cached GRPO with its cached PO. The PO's own DocEntry is
///     expected to be present in ErpLookupCacheEntry.ExternalKey for the PO row (parsed as int).
///
/// The tolerance for price variance is a fixed constant for this phase (see
/// <see cref="PriceVarianceTolerance"/>) — no per-tenant configuration yet.
/// </summary>
public class ThreeWayMatchService(NexusDocsDbContext db) : IThreeWayMatchService
{
    private const int ObjectTypeBusinessPartner = 2;
    private const int ObjectTypeGoodsReceiptPo = 20;
    private const int ObjectTypePurchaseOrder = 22;

    /// <summary>Default price-variance tolerance: 5% of the matched PO's DocTotal.</summary>
    private const decimal PriceVarianceTolerance = 0.05m;

    public async Task<MatchResult> MatchAsync(Guid tenantId, ExtractionResult extraction, Guid ingestItemId)
    {
        var result = new MatchResult
        {
            TenantId = tenantId,
            IngestItemId = ingestItemId,
        };

        var reasons = new List<string>();

        // Step 5 (checked first per the task spec: cheapest and most definitive check) — a
        // duplicate invoice number is an exception regardless of how the rest of the match goes.
        if (!string.IsNullOrWhiteSpace(extraction.InvoiceNumber))
        {
            // Background-worker-safe: this service has no ambient HTTP tenant context when called
            // from the ingest pipeline processor, so bypass the global query filter and filter by
            // an explicit TenantId instead (see FlowTimerWorker.cs / IntegrationOutboxWorker.cs).
            var duplicateExists = await db.Set<ExtractionResult>()
                .IgnoreQueryFilters()
                .Where(e => e.TenantId == tenantId
                            && e.InvoiceNumber == extraction.InvoiceNumber
                            && e.IngestItemId != ingestItemId)
                .AnyAsync();

            if (duplicateExists)
            {
                result.Outcome = MatchOutcome.Exception;
                result.VarianceReasons = $"Duplicate invoice number {extraction.InvoiceNumber}";
                return result;
            }
        }

        // Step 4a: resolve the supplier by tax ID.
        BusinessPartnerLookup? supplier = null;
        if (string.IsNullOrWhiteSpace(extraction.SupplierTaxId))
        {
            reasons.Add("No supplier tax ID found on the invoice");
        }
        else
        {
            supplier = await ResolveSupplierAsync(tenantId, extraction.SupplierTaxId);
            if (supplier is null)
                reasons.Add($"Unknown supplier - no business partner matches tax ID {extraction.SupplierTaxId}");
        }

        // Step 4b: resolve the PO by DocNum reference on the invoice.
        PurchaseOrderLookup? po = null;
        if (string.IsNullOrWhiteSpace(extraction.PoReference))
        {
            reasons.Add("No PO reference found on the invoice");
        }
        else
        {
            po = await ResolvePurchaseOrderAsync(tenantId, extraction.PoReference);
            if (po is null)
                reasons.Add($"No matching Purchase Order found for reference {extraction.PoReference}");
        }

        // Step 4c: resolve a GRPO linked to the matched PO (only possible once the PO is known).
        GoodsReceiptPoLookup? grpo = null;
        if (po is not null)
        {
            grpo = await ResolveGoodsReceiptForPoAsync(tenantId, po.Value.DocEntry);
            if (grpo is null)
                reasons.Add($"No Goods Receipt PO found for matched Purchase Order {po.Value.DocNum}");
        }

        if (supplier is null || po is null || grpo is null)
        {
            result.Outcome = MatchOutcome.Exception;
            if (supplier is not null)
                result.MatchedCardCode = supplier.Value.CardCode;
            if (po is not null)
            {
                result.MatchedPoDocEntry = po.Value.DocEntry;
                result.MatchedPoDocNum = po.Value.DocNum;
            }
            result.VarianceReasons = string.Join("; ", reasons);
            return result;
        }

        // Step 4d: price variance check within tolerance.
        if (extraction.TotalAmount is not { } invoiceTotal)
        {
            result.Outcome = MatchOutcome.Exception;
            result.MatchedCardCode = supplier.Value.CardCode;
            result.MatchedPoDocEntry = po.Value.DocEntry;
            result.MatchedPoDocNum = po.Value.DocNum;
            result.MatchedGrpoDocEntry = grpo.Value.DocEntry;
            result.MatchedGrpoDocNum = grpo.Value.DocNum;
            result.VarianceReasons = "No total amount extracted from the invoice - cannot compare against PO total";
            return result;
        }

        var poTotal = po.Value.DocTotal;
        if (poTotal is null || poTotal.Value == 0)
        {
            result.Outcome = MatchOutcome.Exception;
            result.MatchedCardCode = supplier.Value.CardCode;
            result.MatchedPoDocEntry = po.Value.DocEntry;
            result.MatchedPoDocNum = po.Value.DocNum;
            result.MatchedGrpoDocEntry = grpo.Value.DocEntry;
            result.MatchedGrpoDocNum = grpo.Value.DocNum;
            result.VarianceReasons = $"Matched Purchase Order {po.Value.DocNum} has no cached DocTotal to compare against";
            return result;
        }

        var variancePct = Math.Abs(invoiceTotal - poTotal.Value) / poTotal.Value;

        result.MatchedCardCode = supplier.Value.CardCode;
        result.MatchedPoDocEntry = po.Value.DocEntry;
        result.MatchedPoDocNum = po.Value.DocNum;
        result.MatchedGrpoDocEntry = grpo.Value.DocEntry;
        result.MatchedGrpoDocNum = grpo.Value.DocNum;

        if (variancePct > PriceVarianceTolerance)
        {
            result.Outcome = MatchOutcome.Exception;
            result.VarianceReasons =
                $"Price variance {variancePct:P1} exceeds {PriceVarianceTolerance:P0} tolerance " +
                $"(invoice total {invoiceTotal}, PO total {poTotal.Value})";
        }
        else
        {
            result.Outcome = MatchOutcome.CleanMatch;
        }

        return result;
    }

    private async Task<BusinessPartnerLookup?> ResolveSupplierAsync(Guid tenantId, string supplierTaxId)
    {
        // Small candidate set per tenant - fine to materialize and compare in memory. Sqlite's
        // limited JSON querying makes in-memory comparison the simplest correct approach here
        // (see the class doc comment for the assumed "TaxId" field name).
        var candidates = await db.Set<ErpLookupCacheEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.ObjectType == ObjectTypeBusinessPartner)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            var taxId = TryGetString(candidate.DataJson, "TaxId");
            if (taxId is not null && string.Equals(taxId, supplierTaxId, StringComparison.OrdinalIgnoreCase))
                return new BusinessPartnerLookup(candidate.ExternalKey);
        }

        return null;
    }

    private async Task<PurchaseOrderLookup?> ResolvePurchaseOrderAsync(Guid tenantId, string poReference)
    {
        var candidates = await db.Set<ErpLookupCacheEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.ObjectType == ObjectTypePurchaseOrder)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            // ExternalKey carries the PO's DocNum (its natural/document key); matched as strings
            // since the invoice reference is free-text extracted from the PDF.
            if (!string.Equals(candidate.ExternalKey, poReference, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(candidate.ExternalKey, out var docEntry))
            {
                // DocNum and DocEntry can differ in B1; if ExternalKey isn't numeric we can't
                // derive a DocEntry to link a GRPO against, so this candidate can't be used.
                continue;
            }

            var docTotal = TryGetDecimal(candidate.DataJson, "DocTotal");
            return new PurchaseOrderLookup(docEntry, candidate.ExternalKey, docTotal);
        }

        return null;
    }

    private async Task<GoodsReceiptPoLookup?> ResolveGoodsReceiptForPoAsync(Guid tenantId, int poDocEntry)
    {
        var candidates = await db.Set<ErpLookupCacheEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.ObjectType == ObjectTypeGoodsReceiptPo)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            var baseEntry = TryGetInt(candidate.DataJson, "BaseEntry");
            if (baseEntry == poDocEntry)
            {
                int.TryParse(candidate.ExternalKey, out var grpoDocEntry);
                return new GoodsReceiptPoLookup(grpoDocEntry, candidate.ExternalKey);
            }
        }

        return null;
    }

    private static string? TryGetString(string dataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryGetDecimal(string dataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var d) => d,
                JsonValueKind.String when decimal.TryParse(value.GetString(), out var d) => d,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryGetInt(string dataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var i) => i,
                JsonValueKind.String when int.TryParse(value.GetString(), out var i) => i,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct BusinessPartnerLookup(string CardCode);

    private readonly record struct PurchaseOrderLookup(int DocEntry, string DocNum, decimal? DocTotal);

    private readonly record struct GoodsReceiptPoLookup(int DocEntry, string DocNum);
}
