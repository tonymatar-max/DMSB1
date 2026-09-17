using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Capture;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// The primary "AP automation inbox" surface (ARCHITECTURE.md section 7): every captured
/// IngestItem across all of the tenant's IngestSources/IngestBatches, with its latest extraction
/// and match summary, so the client can show one flat worklist rather than the operator having to
/// drill into batches. Named IngestBatchesController per this phase's task split even though the
/// routes it exposes are under /api/ingest-items (batches are represented only as a grouping field
/// on the item, not surfaced with their own endpoints in this phase).
/// </summary>
[ApiController]
[Route("api/ingest-items")]
[Authorize]
[RequiresModule("CAPTURE")]
public class IngestBatchesController(
    NexusDocsDbContext db,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    // ---- List (AP automation inbox) -----------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<List<IngestItemListDto>>> List([FromQuery] IngestItemStatus? status, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        // Simple equality predicates only in SQL (the Sqlite DateTimeOffset ORDER BY / range-
        // comparison caveat documented on FlowTimerWorker / RequisitionsController.List) — narrow
        // here, then order by ReceivedAt in memory after materializing.
        var query = db.IngestItems.Where(i => i.TenantId == tenantId);
        if (status is { } s) query = query.Where(i => i.Status == s);

        var items = await query.ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();

        var batchIds = items.Select(i => i.IngestBatchId).Distinct().ToList();
        var batches = await db.IngestBatches
            .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
            .ToListAsync(ct);
        var sourceIdByBatchId = batches.ToDictionary(b => b.Id, b => b.IngestSourceId);

        var latestExtractionByItemId = await LatestExtractionsAsync(tenantId, itemIds, ct);
        var latestMatchByItemId = await LatestMatchesAsync(tenantId, itemIds, ct);

        var result = items
            .OrderByDescending(i => i.ReceivedAt)
            .Select(i =>
            {
                latestExtractionByItemId.TryGetValue(i.Id, out var extraction);
                latestMatchByItemId.TryGetValue(i.Id, out var match);
                sourceIdByBatchId.TryGetValue(i.IngestBatchId, out var sourceId);

                return new IngestItemListDto(
                    i.Id,
                    i.IngestBatchId,
                    sourceId,
                    i.OriginalFileName,
                    i.Status,
                    i.ErrorMessage,
                    extraction?.SupplierName,
                    extraction?.InvoiceNumber,
                    extraction?.TotalAmount,
                    extraction?.Currency,
                    match?.Outcome,
                    match?.VarianceReasons,
                    i.WorkflowInstanceId,
                    i.ArchivedDocumentId,
                    i.ReceivedAt,
                    i.ProcessedAt);
            })
            .ToList();

        return Ok(result);
    }

    // ---- Detail ---------------------------------------------------------------------------------

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IngestItemDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var item = await db.IngestItems.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
        if (item is null) return NotFound();

        var extractions = await db.ExtractionResults
            .Where(e => e.TenantId == tenantId && e.IngestItemId == id)
            .ToListAsync(ct);
        var matches = await db.MatchResults
            .Where(m => m.TenantId == tenantId && m.IngestItemId == id)
            .ToListAsync(ct);

        string? workflowStatus = null;
        if (item.WorkflowInstanceId is { } instanceId)
        {
            workflowStatus = await db.WorkflowInstances
                .Where(w => w.TenantId == tenantId && w.Id == instanceId)
                .Select(w => w.Status.ToString())
                .FirstOrDefaultAsync(ct);
        }

        var dto = new IngestItemDetailDto(
            item.Id,
            item.IngestBatchId,
            (await db.IngestBatches.Where(b => b.TenantId == tenantId && b.Id == item.IngestBatchId)
                .Select(b => b.IngestSourceId).FirstOrDefaultAsync(ct)),
            item.OriginalFileName,
            item.ContentHash,
            item.Status,
            item.ErrorMessage,
            item.ArchivedDocumentId,
            item.WorkflowInstanceId,
            workflowStatus,
            item.ReceivedAt,
            item.ProcessedAt,
            extractions.OrderByDescending(e => e.ExtractedAt).Select(ToDto).ToList(),
            matches.OrderByDescending(m => m.MatchedAt).Select(ToDto).ToList());

        return Ok(dto);
    }

    // ---- Reprocess ------------------------------------------------------------------------------

    /// <summary>
    /// Re-runs the capture pipeline for this item. NOT IMPLEMENTED in this phase: IngestPipelineService
    /// only persists the original file bytes into the blob store for items that reach a clean match
    /// (see IngestPipelineService.FileAndPostAsync) — an item that failed earlier, or ended up as a
    /// match exception, has no bytes retained anywhere the API can get back at. Rather than silently
    /// no-op, this returns 409 with a clear message; manual re-upload via the hot folder / mailbox
    /// is the supported path for this phase. A real "reprocess" would need IngestPipelineService (or
    /// this controller) to retain the original bytes for every item, not only clean matches.
    /// </summary>
    [HttpPost("{id:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var item = await db.IngestItems.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, ct);
        if (item is null) return NotFound();

        return Conflict(new
        {
            error = "reprocess_not_supported",
            message = "This phase's ingest pipeline does not retain the original file bytes for items " +
                      "that didn't reach a clean match, so it cannot re-run extraction/matching here. " +
                      "Drop the file again into the hot folder (or resend the email) to re-ingest it.",
        });
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private async Task<Dictionary<Guid, ExtractionResult>> LatestExtractionsAsync(Guid tenantId, List<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return new();

        var all = await db.ExtractionResults
            .Where(e => e.TenantId == tenantId && itemIds.Contains(e.IngestItemId))
            .ToListAsync(ct);

        return all
            .GroupBy(e => e.IngestItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ExtractedAt).First());
    }

    private async Task<Dictionary<Guid, MatchResult>> LatestMatchesAsync(Guid tenantId, List<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return new();

        var all = await db.MatchResults
            .Where(m => m.TenantId == tenantId && itemIds.Contains(m.IngestItemId))
            .ToListAsync(ct);

        return all
            .GroupBy(m => m.IngestItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.MatchedAt).First());
    }

    private static ExtractionResultDto ToDto(ExtractionResult e) => new(
        e.Id, e.OcrProviderUsed, e.RawText, e.DocumentTypeGuess, e.SupplierTaxId, e.SupplierName,
        e.InvoiceNumber, e.InvoiceDate, e.Currency, e.NetAmount, e.TaxAmount, e.TotalAmount,
        e.PoReference, e.ExtractedAt);

    private static MatchResultDto ToDto(MatchResult m) => new(
        m.Id, m.Outcome, m.MatchedCardCode, m.MatchedPoDocEntry, m.MatchedPoDocNum,
        m.MatchedGrpoDocEntry, m.MatchedGrpoDocNum, m.VarianceReasons, m.MatchedAt);
}
