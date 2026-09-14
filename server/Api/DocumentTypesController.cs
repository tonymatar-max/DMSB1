using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// Routes live under two different prefixes (nested under a cabinet for list/create, flat for
/// get-by-id), so each action carries its own full route instead of a single controller-level
/// [Route].
/// </summary>
[ApiController]
[Authorize]
[RequiresModule("ARCHIVE")]
public class DocumentTypesController(NexusDocsDbContext db) : ControllerBase
{
    [HttpGet("api/cabinets/{cabinetId:guid}/document-types")]
    public async Task<ActionResult<IReadOnlyList<DocumentTypeDto>>> ListForCabinet(Guid cabinetId)
    {
        if (!await db.Cabinets.AnyAsync(c => c.Id == cabinetId))
        {
            return NotFound();
        }

        var types = await db.DocumentTypes
            .Where(t => t.CabinetId == cabinetId)
            .Include(t => t.IndexFields)
            .OrderBy(t => t.Name)
            .ToListAsync();

        return Ok(types.Select(ToDto).ToList());
    }

    [HttpPost("api/cabinets/{cabinetId:guid}/document-types")]
    public async Task<ActionResult<DocumentTypeDto>> Create(Guid cabinetId, CreateDocumentTypeRequest request)
    {
        if (!await db.Cabinets.AnyAsync(c => c.Id == cabinetId))
        {
            return NotFound();
        }

        var documentType = new DocumentType
        {
            CabinetId = cabinetId,
            Name = request.Name,
            NamingRule = request.NamingRule,
        };

        foreach (var field in request.IndexFields)
        {
            documentType.IndexFields.Add(new IndexFieldDefinition
            {
                DocumentTypeId = documentType.Id,
                Code = field.Code,
                Label = field.Label,
                FieldType = field.FieldType,
                Required = field.Required,
                Filterable = field.Filterable,
                PicklistOptionsJson = field.PicklistOptionsJson,
                ErpLookupObjectType = field.ErpLookupObjectType,
                SortOrder = field.SortOrder,
            });
        }

        // TenantId on both the DocumentType and each IndexFieldDefinition is stamped by
        // NexusDocsDbContext.SaveChanges from the ambient tenant accessor.
        db.DocumentTypes.Add(documentType);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = documentType.Id }, ToDto(documentType));
    }

    [HttpGet("api/document-types/{id:guid}")]
    public async Task<ActionResult<DocumentTypeDto>> GetById(Guid id)
    {
        var documentType = await db.DocumentTypes
            .Include(t => t.IndexFields)
            .FirstOrDefaultAsync(t => t.Id == id);

        return documentType is null ? NotFound() : Ok(ToDto(documentType));
    }

    private static DocumentTypeDto ToDto(DocumentType t) => new(
        t.Id,
        t.CabinetId,
        t.Name,
        t.NamingRule,
        t.CreatedAt,
        t.IndexFields
            .OrderBy(f => f.SortOrder)
            .Select(f => new IndexFieldDefinitionDto(
                f.Id, f.Code, f.Label, f.FieldType, f.Required, f.Filterable,
                f.PicklistOptionsJson, f.ErpLookupObjectType, f.SortOrder))
            .ToList());
}
