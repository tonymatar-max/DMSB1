using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

[ApiController]
[Route("api/cabinets")]
[Authorize]
[RequiresModule("ARCHIVE")]
public class CabinetsController(NexusDocsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CabinetDto>>> List()
    {
        // Tenant-scoped automatically via NexusDocsDbContext's global query filter. Projected to
        // DTOs client-side (ToDto isn't SQL-translatable) after materializing the (small) list.
        var cabinets = await db.Cabinets
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(cabinets.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CabinetDto>> Create(CreateCabinetRequest request)
    {
        var cabinet = new Cabinet
        {
            Name = request.Name,
            Description = request.Description,
            DefaultRetentionPolicyId = request.DefaultRetentionPolicyId,
        };

        // TenantId is stamped by NexusDocsDbContext.SaveChanges from the ambient tenant accessor —
        // deliberately not set here.
        db.Cabinets.Add(cabinet);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = cabinet.Id }, ToDto(cabinet));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CabinetDto>> GetById(Guid id)
    {
        var cabinet = await db.Cabinets.FirstOrDefaultAsync(c => c.Id == id);
        return cabinet is null ? NotFound() : Ok(ToDto(cabinet));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CabinetDto>> Update(Guid id, UpdateCabinetRequest request)
    {
        var cabinet = await db.Cabinets.FirstOrDefaultAsync(c => c.Id == id);
        if (cabinet is null) return NotFound();

        cabinet.Name = request.Name;
        cabinet.Description = request.Description;
        cabinet.DefaultRetentionPolicyId = request.DefaultRetentionPolicyId;
        await db.SaveChangesAsync();

        return Ok(ToDto(cabinet));
    }

    private static CabinetDto ToDto(Cabinet c) =>
        new(c.Id, c.Name, c.Description, c.DefaultRetentionPolicyId, c.CreatedAt);
}
