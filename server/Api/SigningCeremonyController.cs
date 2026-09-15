using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Domain.Sign;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Infrastructure.Flow;
using NexusDocs.Api.Infrastructure.Sign;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// The public, unauthenticated signing-ceremony surface a recipient reaches by ceremony token
/// alone (ARCHITECTURE.md section 6.3 "no account required"). Deliberately has no [Authorize] and
/// no [RequiresModule]: a signer has no Nexus account and no licence context - the licence was
/// already checked when the sender created the envelope. Every query below goes through
/// SigningCeremonyService, which is itself responsible for the .IgnoreQueryFilters() + explicit
/// TenantId convention (see that file's class remarks) since there is no JWT "tenant" claim for
/// TenantResolutionMiddleware to resolve on these endpoints. The one place this controller reads
/// the database directly (GetDocument, below) follows the same convention explicitly.
/// </summary>
[ApiController]
[Route("api/ceremony")]
public class SigningCeremonyController(
    NexusDocsDbContext db,
    SigningCeremonyService ceremonyService,
    IBlobStore blobStore) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? ClientUserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    // ---- Context -----------------------------------------------------------------------------

    [HttpGet("{token}")]
    public async Task<ActionResult<CeremonyContextDto>> GetContext(string token, CancellationToken ct)
    {
        CeremonyContext context;
        try
        {
            context = await ceremonyService.GetCeremonyContextAsync(token);
        }
        catch (ValidationException)
        {
            // Don't distinguish "invalid" from "expired" from "already used" here - a 404 for any
            // of these keeps this endpoint from leaking whether a near-miss token almost matched.
            return NotFound();
        }

        var dto = new CeremonyContextDto(
            context.Envelope.Id,
            context.Envelope.Name,
            context.Envelope.Message,
            context.Envelope.Status,
            new CeremonyRecipientDto(
                context.Recipient.Id,
                context.Recipient.Name,
                context.Recipient.Email,
                context.Recipient.Role,
                context.Recipient.Status),
            context.Fields.Select(f => new CeremonySignatureFieldDto(
                f.Id, f.Kind, f.PageNumber, f.X, f.Y, f.Width, f.Height, f.Value)).ToList());

        return Ok(dto);
    }

    // ---- Source document (for the ceremony viewer to render) ---------------------------------

    [HttpGet("{token}/document")]
    public async Task<IActionResult> GetDocument(string token, CancellationToken ct)
    {
        // Same token validity rules as GetContext (not expired, not already Signed/Declined) -
        // resolve the recipient ourselves here since SigningCeremonyService doesn't expose a blob
        // stream (its CeremonyContext only carries the blob hash, per its own doc comment: "the
        // API layer resolves the actual PDF bytes itself").
        var recipient = await db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.CeremonyToken == token)
            .FirstOrDefaultAsync(ct);
        if (recipient is null || recipient.TokenExpiresAt < DateTimeOffset.UtcNow) return NotFound();
        if (recipient.Status is RecipientStatus.Signed or RecipientStatus.Declined) return NotFound();

        var envelope = await db.Envelopes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == recipient.TenantId && e.Id == recipient.EnvelopeId, ct);
        if (envelope is null) return NotFound();

        var sourceDocument = await db.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == recipient.TenantId && d.Id == envelope.SourceDocumentId, ct);
        if (sourceDocument is null) return NotFound();

        var currentVersion = await db.DocumentVersions
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == recipient.TenantId && v.Id == sourceDocument.CurrentVersionId)
            .FirstOrDefaultAsync(ct);
        if (currentVersion is null) return NotFound();

        Stream blob;
        try
        {
            blob = await blobStore.GetAsync(recipient.TenantId, currentVersion.BlobHash);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "blob_missing" });
        }

        if (!ContentTypeProvider.TryGetContentType(currentVersion.OriginalFileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return File(blob, contentType, currentVersion.OriginalFileName);
    }

    // ---- Consent -------------------------------------------------------------------------------

    [HttpPost("{token}/consent")]
    public async Task<ActionResult<CeremonyActionResultDto>> Consent(string token, CancellationToken ct)
    {
        try
        {
            await ceremonyService.RecordConsentAsync(token, ClientIp, ClientUserAgent);
        }
        catch (ValidationException)
        {
            return NotFound();
        }

        return await BuildActionResult(token, ct);
    }

    // ---- Complete ------------------------------------------------------------------------------

    [HttpPost("{token}/complete")]
    public async Task<ActionResult<CeremonyActionResultDto>> Complete(
        string token, [FromBody] CeremonyCompleteRequest request, CancellationToken ct)
    {
        var fieldValues = request.Fields.Select(f => new FieldValueInput(
            f.FieldId,
            f.Value,
            DecodeSignatureImage(f.SignatureImageBase64))).ToList();

        Envelope envelope;
        try
        {
            envelope = await ceremonyService.CompleteSignatureAsync(token, ClientIp, ClientUserAgent, fieldValues);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // The recipient's own status was just advanced by CompleteSignatureAsync; re-resolve it
        // (rather than re-deriving from the input list) so the response reflects what was saved.
        var recipient = await db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.CeremonyToken == token)
            .FirstOrDefaultAsync(ct);

        return Ok(new CeremonyActionResultDto(
            recipient?.Status ?? RecipientStatus.Signed,
            envelope.Status,
            envelope.Status == EnvelopeStatus.Completed));
    }

    // ---- Decline -------------------------------------------------------------------------------

    [HttpPost("{token}/decline")]
    public async Task<ActionResult<CeremonyActionResultDto>> Decline(
        string token, [FromBody] CeremonyDeclineRequest request, CancellationToken ct)
    {
        try
        {
            await ceremonyService.DeclineAsync(token, request.Reason, ClientIp, ClientUserAgent);
        }
        catch (ValidationException)
        {
            return NotFound();
        }

        return await BuildActionResult(token, ct);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private async Task<ActionResult<CeremonyActionResultDto>> BuildActionResult(string token, CancellationToken ct)
    {
        var recipient = await db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.CeremonyToken == token)
            .FirstOrDefaultAsync(ct);
        if (recipient is null) return NotFound();

        var envelope = await db.Envelopes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == recipient.TenantId && e.Id == recipient.EnvelopeId, ct);
        if (envelope is null) return NotFound();

        return Ok(new CeremonyActionResultDto(
            recipient.Status,
            envelope.Status,
            envelope.Status == EnvelopeStatus.Completed));
    }

    /// <summary>
    /// Accepts either a raw base64 string or a "data:image/png;base64,...." data URL from the
    /// client's signature-capture canvas, per the task brief.
    /// </summary>
    private static byte[]? DecodeSignatureImage(string? base64OrDataUrl)
    {
        if (string.IsNullOrWhiteSpace(base64OrDataUrl)) return null;

        var commaIndex = base64OrDataUrl.IndexOf(',');
        var raw = base64OrDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0
            ? base64OrDataUrl[(commaIndex + 1)..]
            : base64OrDataUrl;

        try
        {
            return Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
