using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Gen;
using NexusDocs.Api.Infrastructure.Gen;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// CRUD and scratch-preview surface for GEN's <see cref="DocumentTemplate"/>s (ARCHITECTURE.md
/// section 1.2, module GEN). Preview renders a template against caller-supplied or sample data and
/// hands back PDF bytes directly - it does not create any <see cref="GeneratedDocument"/> row; that
/// only happens through GeneratedDocumentsController's real generation endpoint.
/// </summary>
[ApiController]
[Route("api/document-templates")]
[Authorize]
[RequiresModule("GEN")]
public class DocumentTemplatesController(
    NexusDocsDbContext db,
    ITemplateRenderer templateRenderer,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    // ---- List --------------------------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<List<DocumentTemplateListItemDto>>> List(CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var templates = await db.DocumentTemplates
            .Where(t => t.TenantId == tenantId)
            .ToListAsync(ct);

        var result = templates
            .OrderByDescending(t => t.CreatedAt)
            .Select(ToListItemDto)
            .ToList();

        return Ok(result);
    }

    // ---- Get ---------------------------------------------------------------------------------

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentTemplateDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var template = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);
        if (template is null) return NotFound();

        return Ok(ToDetailDto(template));
    }

    // ---- Create --------------------------------------------------------------------------------

    [HttpPost]
    public async Task<ActionResult<DocumentTemplateDetailDto>> Create(
        [FromBody] CreateDocumentTemplateRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name_required" });
        if (string.IsNullOrWhiteSpace(request.TemplateHtml))
            return BadRequest(new { error = "template_html_required" });

        var template = new DocumentTemplate
        {
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            TemplateHtml = request.TemplateHtml,
            SourceObjectType = request.SourceObjectType,
            SampleDataJson = request.SampleDataJson,
        };

        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = template.Id }, ToDetailDto(template));
    }

    // ---- Update --------------------------------------------------------------------------------

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentTemplateDetailDto>> Update(
        Guid id, [FromBody] UpdateDocumentTemplateRequest request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name_required" });
        if (string.IsNullOrWhiteSpace(request.TemplateHtml))
            return BadRequest(new { error = "template_html_required" });

        var template = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);
        if (template is null) return NotFound();

        template.Name = request.Name;
        template.Description = request.Description;
        template.TemplateHtml = request.TemplateHtml;
        template.SourceObjectType = request.SourceObjectType;
        template.SampleDataJson = request.SampleDataJson;
        template.IsActive = request.IsActive;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(ToDetailDto(template));
    }

    // ---- Delete --------------------------------------------------------------------------------

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var template = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);
        if (template is null) return NotFound();

        db.DocumentTemplates.Remove(template);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---- Preview -------------------------------------------------------------------------------

    /// <summary>
    /// Scratch preview - renders the template with caller-supplied <see cref="PreviewDocumentTemplateRequest.DataJson"/>
    /// (falling back to the template's own <see cref="DocumentTemplate.SampleDataJson"/> when
    /// omitted) and returns the resulting PDF inline. Never creates a <see cref="GeneratedDocument"/>
    /// row - this is purely for the template author to check their markup renders as intended.
    /// </summary>
    [HttpPost("{id:guid}/preview")]
    public async Task<IActionResult> Preview(
        Guid id, [FromBody] PreviewDocumentTemplateRequest? request, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var template = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);
        if (template is null) return NotFound();

        var dataJson = !string.IsNullOrWhiteSpace(request?.DataJson) ? request!.DataJson! : template.SampleDataJson;
        if (string.IsNullOrWhiteSpace(dataJson))
            return BadRequest(new { error = "no_data_available" });

        var data = GenDataBinder.ParseDataJson(dataJson);
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

        return File(pdfBytes, "application/pdf", $"{template.Name}-preview.pdf");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static DocumentTemplateListItemDto ToListItemDto(DocumentTemplate t) => new(
        t.Id, t.Name, t.Description, t.SourceObjectType, t.IsActive, t.CreatedAt, t.UpdatedAt);

    private static DocumentTemplateDetailDto ToDetailDto(DocumentTemplate t) => new(
        t.Id, t.Name, t.Description, t.TemplateHtml, t.SourceObjectType, t.SampleDataJson,
        t.IsActive, t.CreatedAt, t.UpdatedAt);
}
