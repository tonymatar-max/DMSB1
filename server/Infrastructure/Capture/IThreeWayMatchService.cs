using NexusDocs.Api.Domain.Capture;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Runs the three-way (invoice/PO/GRPO) match described in ARCHITECTURE.md section 7 steps 4-5
/// against the cached ERP master data (<see cref="Domain.Erp.ErpLookupCacheEntry"/>), producing a
/// <see cref="MatchResult"/> that is either a clean match (auto-file/post) or an exception (routed
/// into FLOW) with human-readable reasons.
///
/// Shape agreed with the parallel hotfolder-pipeline agent so both sides compile against the same
/// contract without needing to see each other's work.
/// </summary>
public interface IThreeWayMatchService
{
    Task<MatchResult> MatchAsync(Guid tenantId, ExtractionResult extraction, Guid ingestItemId);
}
