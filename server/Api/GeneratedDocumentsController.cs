using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Domain.Gen;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Infrastructure.Gen;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// Real generation runs (as opposed to DocumentTemplatesController's scratch preview): renders a
/// <see cref="DocumentTemplate"/> against caller data, stores the PDF via <see cref="IBlobStore"/>,
/// and records a <see cref="GeneratedDocument"/> row.
///
/// SCOPE NOTE (ARCHITECTURE.md section 1.2 lists three push targets - "an archive cabinet, a
/// workflow or a signing envelope"): this phase only implements the ARCHIVE target. Pushing a fresh
/// generation directly into a FLOW workflow or straight into a SIGN envelope in the same call is a
/// reasonable follow-up, not required for this phase - a generated document can still be picked up
/// by FLOW/SIGN afterwards via its resulting Document/DocumentVersion, same as any other archived
/// document.
/// </summary>
[ApiController]
[Route("api/generated-documents")]
[Authorize]
[RequiresModule("GEN")]
public class GeneratedDocumentsController(
    NexusDocsDbContext db,
    IBlobStore blobStore,
    ITemplateRenderer templateRenderer,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    // ---- Create --------------------------------------------------------------------------------

    [HttpPost]
    public async Task<ActionResult<GeneratedDocumentDetailDto>> Create(
        [FromBody] CreateGeneratedDocumentRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var template = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == request.DocumentTemplateId, ct);
        if (template is null) return BadRequest(new { error = "template_not_found" });

        if (request.PushTo == GenerationPushTarget.Archive)
        {
            if (request.CabinetId is null || request.DocumentTypeId is null)
                return BadRequest(new { error = "cabinet_and_document_type_required" });

            var cabinet = await db.Cabinets.AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == request.CabinetId, ct);
            if (cabinet is null) return BadRequest(new { error = "cabinet_not_found" });

            var documentType = await db.DocumentTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == request.DocumentTypeId, ct);
            if (documentType is null || documentType.CabinetId != request.CabinetId)
                return BadRequest(new { error = "document_type_not_found" });
        }

        var data = GenDataBinder.ParseDataJson(request.DataJson);
        if (data is null) return BadRequest(new { error = "invalid_data_json" });

        byte[] pdfBytes;
        try
        {
            pdfBytes = await templateRenderer.RenderToPdfAsync(template.TemplateHtml, data);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "template_render_failed", detail = ex.Message });
        }

        string hash;
        await using (var stream = new MemoryStream(pdfBytes))
        {
            hash = await blobStore.PutAsync(tenantId, stream);
        }

        var generated = new GeneratedDocument
        {
            TenantId = tenantId,
            DocumentTemplateId = template.Id,
            GeneratedByUserId = CurrentUserId,
            InputDataJson = request.DataJson,
            RenderedBlobHash = hash,
        };

        if (request.PushTo == GenerationPushTarget.Archive)
        {
            var fileName = $"{template.Name}.pdf";

            // Same Document+DocumentVersion shape DocumentsController.Create uses for a normal
            // upload - a generated PDF becomes an ordinary archived document from ARCHIVE's point
            // of view, just with its bytes coming from a template render instead of a form upload.
            var document = new Document
            {
                CabinetId = request.CabinetId!.Value,
                DocumentTypeId = request.DocumentTypeId!.Value,
                Status = DocumentStatus.Active,
                OwnerId = CurrentUserId,
            };

            var version = new DocumentVersion
            {
                DocumentId = document.Id,
                VersionNumber = 1,
                BlobHash = hash,
                OriginalFileName = fileName,
                SizeBytes = pdfBytes.LongLength,
                AuthorId = CurrentUserId,
            };

            document.CurrentVersionId = version.Id;

            db.Documents.Add(document);
            db.DocumentVersions.Add(version);

            generated.ArchivedDocumentId = document.Id;
        }

        db.GeneratedDocuments.Add(generated);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetContent), new { id = generated.Id },
            ToDetailDto(generated, template.Name));
    }

    // ---- List ------------------------------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<List<GeneratedDocumentListItemDto>>> List(CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var generated = await db.GeneratedDocuments
            .Where(g => g.TenantId == tenantId)
            .ToListAsync(ct);

        var templateIds = generated.Select(g => g.DocumentTemplateId).Distinct().ToList();
        var templateNames = await db.DocumentTemplates
            .Where(t => t.TenantId == tenantId && templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var result = generated
            .OrderByDescending(g => g.GeneratedAt)
            .Select(g => new GeneratedDocumentListItemDto(
                g.Id,
                g.DocumentTemplateId,
                templateNames.GetValueOrDefault(g.DocumentTemplateId, "(deleted template)"),
                g.GeneratedByUserId,
                g.GeneratedAt,
                g.ArchivedDocumentId.HasValue,
                g.ArchivedDocumentId))
            .ToList();

        return Ok(result);
    }

    // ---- Content ---------------------------------------------------------------------------------

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var generated = await db.GeneratedDocuments
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct);
        if (generated is null) return NotFound();

        var template = await db.DocumentTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == generated.DocumentTemplateId, ct);

        Stream blob;
        try
        {
            blob = await blobStore.GetAsync(tenantId, generated.RenderedBlobHash);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "blob_missing" });
        }

        var fileName = $"{template?.Name ?? "generated-document"}.pdf";
        return File(blob, "application/pdf", fileName);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static GeneratedDocumentDetailDto ToDetailDto(GeneratedDocument g, string templateName) => new(
        g.Id, g.DocumentTemplateId, templateName, g.GeneratedByUserId, g.InputDataJson, g.GeneratedAt,
        g.ArchivedDocumentId, g.EnvelopeId);
}
