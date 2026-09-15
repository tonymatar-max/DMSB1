using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Sign;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// The public "verify a sealed document" surface (ARCHITECTURE.md section 6.2): lets a
/// counterparty who received a sealed PDF confirm it is genuinely the document Nexus Docs sealed
/// for a given envelope, without a Nexus account. No [Authorize] - same reasoning and the same
/// .IgnoreQueryFilters() + explicit TenantId convention as SigningCeremonyController, since there
/// is no JWT "tenant" claim on this unauthenticated endpoint. Here the envelope itself (rather than
/// a recipient row) is what tells us the TenantId - looked up with IgnoreQueryFilters() by
/// EnvelopeId alone, then every subsequent query filters explicitly by that TenantId.
/// </summary>
[ApiController]
[Route("api/verify")]
public class VerifyController(NexusDocsDbContext db, IBlobStore blobStore) : ControllerBase
{
    [HttpGet("{envelopeId:guid}")]
    public async Task<ActionResult<VerifyResultDto>> Verify(Guid envelopeId, CancellationToken ct)
    {
        var envelope = await db.Envelopes
            .IgnoreQueryFilters()
            .Where(e => e.Id == envelopeId)
            .FirstOrDefaultAsync(ct);

        // Nothing to verify yet for an envelope that hasn't sealed (or doesn't exist) - both are a
        // plain 404 rather than distinguishing them, matching the ceremony controller's stance of
        // not leaking extra detail through an unauthenticated endpoint.
        if (envelope is null || envelope.Status != EnvelopeStatus.Completed || envelope.SealedDocumentVersionId is not { } sealedVersionId)
            return NotFound();

        var sealedVersion = await db.DocumentVersions
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == envelope.TenantId && v.Id == sealedVersionId)
            .FirstOrDefaultAsync(ct);
        if (sealedVersion is null) return NotFound();

        var recipientCount = await db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == envelope.TenantId && r.EnvelopeId == envelope.Id)
            .Select(r => r.Id)
            .CountAsync(ct);

        // Re-hash the blob fresh from storage (rather than trusting the stored BlobHash column) so
        // a corrupted or substituted blob on disk is caught, not just a mismatched database row.
        bool valid;
        try
        {
            await using var blob = await blobStore.GetAsync(envelope.TenantId, sealedVersion.BlobHash);
            var actualHash = await ComputeSha256HexAsync(blob, ct);
            valid = string.Equals(actualHash, sealedVersion.BlobHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException)
        {
            valid = false;
        }

        return Ok(new VerifyResultDto(
            valid,
            envelope.Name,
            envelope.CompletedAt,
            recipientCount,
            sealedVersion.BlobHash));
    }

    private static async Task<string> ComputeSha256HexAsync(Stream stream, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
