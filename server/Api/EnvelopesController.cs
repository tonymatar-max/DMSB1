using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Domain.Sign;
using NexusDocs.Api.Infrastructure.Audit;
using NexusDocs.Api.Infrastructure.Flow;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Infrastructure.Sign;
using NexusDocs.Api.Infrastructure.Tenancy;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// The authenticated create/send/manage surface for SIGN envelopes (ARCHITECTURE.md section 6.3
/// "The ceremony"). The public, unauthenticated ceremony itself (view/consent/complete/decline by
/// token) lives in a separate controller, not here - this controller runs behind the normal JWT +
/// ambient tenant filter like the rest of the authenticated API.
///
/// SCOPE NOTE: there is no outbound email/SMS delivery integration in this codebase yet, so
/// "sending" an envelope does not email anything. Create and Send both return the full envelope
/// detail, including each activated recipient's ceremonyUrl, so the sender can copy/share the
/// signing links manually. See SigningCeremonyService.SendEnvelopeAsync's remarks for the same
/// note next to where "send" is actually implemented.
/// </summary>
[ApiController]
[Route("api/envelopes")]
[Authorize]
[RequiresModule("SIGN")]
public class EnvelopesController(
    NexusDocsDbContext db,
    SigningCeremonyService ceremonyService,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant,
    AuditService audit) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    // Recipient statuses whose ceremony link is still usable - a Signed or Declined recipient's
    // token is spent, so we stop exposing it once past this set (per the task brief: "don't
    // re-expose ceremony tokens for already-signed/declined recipients").
    private static readonly HashSet<RecipientStatus> ActiveCeremonyStatuses =
        [RecipientStatus.Pending, RecipientStatus.Sent, RecipientStatus.Viewed];

    // ---- Create --------------------------------------------------------------------------------

    [HttpPost]
    public async Task<ActionResult<EnvelopeDetailDto>> Create([FromBody] CreateEnvelopeRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name_required" });
        if (request.Recipients is null || request.Recipients.Count == 0)
            return BadRequest(new { error = "recipients_required" });

        try
        {
            var envelope = await ceremonyService.CreateEnvelopeAsync(
                tenantId,
                CurrentUserId,
                request.SourceDocumentId,
                request.Name,
                request.Message,
                request.Recipients
                    .Select(r => new RecipientInput(r.Email, r.Name, r.Role, r.SigningOrder))
                    .ToList(),
                (request.Fields ?? [])
                    .Select(f => new SignatureFieldInput(f.RecipientIndex, f.Kind, f.PageNumber, f.X, f.Y, f.Width, f.Height))
                    .ToList());

            var dto = await BuildDetailDtoAsync(tenantId, envelope.Id, ct);
            return CreatedAtAction(nameof(Get), new { id = envelope.Id }, dto);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ---- Send ----------------------------------------------------------------------------------

    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<EnvelopeDetailDto>> Send(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        try
        {
            await ceremonyService.SendEnvelopeAsync(tenantId, id);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var dto = await BuildDetailDtoAsync(tenantId, id, ct);
        return Ok(dto);
    }

    // ---- Void ------------------------------------------------------------------------------------

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<EnvelopeDetailDto>> Void(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var envelope = await db.Envelopes.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);
        if (envelope is null) return NotFound();

        if (envelope.Status is EnvelopeStatus.Completed or EnvelopeStatus.Declined or EnvelopeStatus.Voided)
            return BadRequest(new { error = "envelope_not_voidable", status = envelope.Status.ToString() });

        envelope.Status = EnvelopeStatus.Voided;

        db.CeremonyEvents.Add(new CeremonyEvent
        {
            TenantId = tenantId,
            EnvelopeId = envelope.Id,
            RecipientId = null,
            EventType = CeremonyEventType.EnvelopeVoided,
        });

        await audit.RecordAsync(tenantId, CurrentUserId.ToString(), "sign.envelope_voided", $"Envelope/{envelope.Id}", new { envelope.Id });

        await db.SaveChangesAsync(ct);

        var dto = await BuildDetailDtoAsync(tenantId, id, ct);
        return Ok(dto);
    }

    // ---- List ------------------------------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<List<EnvelopeListItemDto>>> List(CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        // Sqlite can't translate ORDER BY on a DateTimeOffset column - narrow to a plain equality
        // predicate in SQL first, then order in memory after materializing (same pattern as
        // RequisitionsController.List / WorkflowEngine).
        var envelopes = await db.Envelopes
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(ct);

        var recipientCounts = await db.Recipients
            .Where(r => r.TenantId == tenantId)
            .GroupBy(r => r.EnvelopeId)
            .Select(g => new { EnvelopeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Count, ct);

        var documentIds = envelopes.Select(e => e.SourceDocumentId).Distinct().ToList();
        var documents = await db.Documents
            .Where(d => d.TenantId == tenantId && documentIds.Contains(d.Id))
            .ToListAsync(ct);
        var currentVersionIds = documents
            .Where(d => d.CurrentVersionId.HasValue)
            .Select(d => d.CurrentVersionId!.Value)
            .ToList();
        var fileNamesByVersionId = await db.DocumentVersions
            .Where(v => v.TenantId == tenantId && currentVersionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.OriginalFileName, ct);
        var fileNamesByDocumentId = documents.ToDictionary(
            d => d.Id,
            d => d.CurrentVersionId.HasValue && fileNamesByVersionId.TryGetValue(d.CurrentVersionId.Value, out var fn) ? fn : null);

        var result = envelopes
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EnvelopeListItemDto(
                e.Id,
                e.Name,
                e.Status,
                e.SourceDocumentId,
                fileNamesByDocumentId.GetValueOrDefault(e.SourceDocumentId),
                recipientCounts.GetValueOrDefault(e.Id),
                e.CreatedAt,
                e.SentAt,
                e.CompletedAt))
            .ToList();

        return Ok(result);
    }

    // ---- Detail ----------------------------------------------------------------------------------

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EnvelopeDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var exists = await db.Envelopes.AnyAsync(e => e.TenantId == tenantId && e.Id == id, ct);
        if (!exists) return NotFound();

        return Ok(await BuildDetailDtoAsync(tenantId, id, ct));
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private async Task<EnvelopeDetailDto> BuildDetailDtoAsync(Guid tenantId, Guid envelopeId, CancellationToken ct)
    {
        var envelope = await db.Envelopes.FirstAsync(e => e.TenantId == tenantId && e.Id == envelopeId, ct);

        var recipients = await db.Recipients
            .Where(r => r.TenantId == tenantId && r.EnvelopeId == envelopeId)
            .ToListAsync(ct);

        var fields = await db.SignatureFields
            .Where(f => f.TenantId == tenantId && f.EnvelopeId == envelopeId)
            .ToListAsync(ct);

        var events = await db.CeremonyEvents
            .Where(e => e.TenantId == tenantId && e.EnvelopeId == envelopeId)
            .ToListAsync(ct);

        var recipientDtos = recipients
            .OrderBy(r => r.SigningOrder)
            .Select(r =>
            {
                var ceremonyActive = ActiveCeremonyStatuses.Contains(r.Status);
                return new EnvelopeRecipientDto(
                    r.Id,
                    r.Email,
                    r.Name,
                    r.Role,
                    r.SigningOrder,
                    r.Status,
                    ceremonyActive ? r.CeremonyToken : null,
                    ceremonyActive ? $"/sign/{r.CeremonyToken}" : null,
                    r.ViewedAt,
                    r.ConsentedAt,
                    r.SignedAt,
                    r.DeclinedAt,
                    r.DeclineReason);
            })
            .ToList();

        var fieldDtos = fields
            .Select(f => new EnvelopeFieldDto(f.Id, f.RecipientId, f.Kind, f.PageNumber, f.X, f.Y, f.Width, f.Height, f.Value))
            .ToList();

        var eventDtos = events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new CeremonyEventDto(e.Id, e.RecipientId, e.EventType, e.OccurredAt, e.IpAddress, e.UserAgent, e.Detail))
            .ToList();

        return new EnvelopeDetailDto(
            envelope.Id,
            envelope.SourceDocumentId,
            envelope.Name,
            envelope.Message,
            envelope.SenderId,
            envelope.Status,
            envelope.CreatedAt,
            envelope.SentAt,
            envelope.CompletedAt,
            envelope.SealedDocumentVersionId,
            recipientDtos,
            fieldDtos,
            eventDtos);
    }
}
