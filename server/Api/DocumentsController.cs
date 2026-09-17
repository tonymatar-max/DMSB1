using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Infrastructure.Audit;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Infrastructure.Tenancy;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// The archive's core upload/version/search surface (ARCHITECTURE.md 1.2 ARCHIVE, 2.2, 2.3, 3):
/// create a document with its first version and index values, add subsequent versions, fetch a
/// document, stream a version's content, and search by cabinet/document type/index field.
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize]
[RequiresModule("ARCHIVE")]
public class DocumentsController(
    NexusDocsDbContext db,
    IBlobStore blobStore,
    AuditService auditService,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    private string CurrentActor => User.FindFirst(ClaimTypes.Email)?.Value ?? CurrentUserId.ToString();

    // ---- Create --------------------------------------------------------------------------

    [HttpPost]
    public async Task<ActionResult<DocumentDto>> Create(
        [FromForm] Guid cabinetId,
        [FromForm] Guid documentTypeId,
        [FromForm] string? indexValues,
        IFormFile file,
        CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest(new { error = "no_file" });

        var cabinet = await db.Cabinets.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cabinetId, ct);
        if (cabinet is null) return BadRequest(new { error = "cabinet_not_found" });

        var documentType = await db.DocumentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == documentTypeId, ct);
        if (documentType is null || documentType.CabinetId != cabinetId)
            return BadRequest(new { error = "document_type_not_found" });

        var fieldDefs = await db.IndexFieldDefinitions.AsNoTracking()
            .Where(f => f.DocumentTypeId == documentTypeId)
            .ToListAsync(ct);

        var parsedValues = ParseIndexValuesJson(indexValues);

        var (indexRows, error) = BuildIndexValueRows(fieldDefs, parsedValues);
        if (error is not null) return BadRequest(new { error });

        await using var stream = file.OpenReadStream();
        var hash = await blobStore.PutAsync(tenantId, stream);

        var document = new Document
        {
            CabinetId = cabinetId,
            DocumentTypeId = documentTypeId,
            Status = DocumentStatus.Active,
            OwnerId = CurrentUserId,
        };

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = 1,
            BlobHash = hash,
            OriginalFileName = Path.GetFileName(file.FileName),
            SizeBytes = file.Length,
            AuthorId = CurrentUserId,
        };

        document.CurrentVersionId = version.Id;

        foreach (var row in indexRows)
        {
            row.DocumentId = document.Id;
        }

        db.Documents.Add(document);
        db.DocumentVersions.Add(version);
        db.DocumentIndexValues.AddRange(indexRows);
        await db.SaveChangesAsync(ct);

        await auditService.RecordAsync(tenantId, CurrentActor, "document.created", document.Id.ToString(), new
        {
            documentId = document.Id,
            cabinetId,
            documentTypeId,
            versionId = version.Id,
            blobHash = hash,
            fileName = version.OriginalFileName,
        });

        var dto = ToDto(document, version, indexRows);
        return CreatedAtAction(nameof(Get), new { id = document.Id }, dto);
    }

    // ---- New version -----------------------------------------------------------------------

    [HttpPost("{id:guid}/versions")]
    public async Task<ActionResult<DocumentVersionDto>> AddVersion(
        Guid id, [FromForm] string? comment, IFormFile file, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest(new { error = "no_file" });

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return NotFound();

        var maxVersionNumber = await db.DocumentVersions
            .Where(v => v.DocumentId == id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct) ?? 0;

        await using var stream = file.OpenReadStream();
        var hash = await blobStore.PutAsync(tenantId, stream);

        var version = new DocumentVersion
        {
            DocumentId = id,
            VersionNumber = maxVersionNumber + 1,
            BlobHash = hash,
            OriginalFileName = Path.GetFileName(file.FileName),
            SizeBytes = file.Length,
            AuthorId = CurrentUserId,
            Comment = comment,
        };

        document.CurrentVersionId = version.Id;

        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(ct);

        await auditService.RecordAsync(tenantId, CurrentActor, "document.version_added", document.Id.ToString(), new
        {
            documentId = document.Id,
            versionId = version.Id,
            versionNumber = version.VersionNumber,
            blobHash = hash,
            fileName = version.OriginalFileName,
        });

        return Ok(ToVersionDto(version));
    }

    // ---- Get -------------------------------------------------------------------------------

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> Get(Guid id, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return NotFound();

        var currentVersion = document.CurrentVersionId is { } versionId
            ? await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct)
            : null;

        var indexValues = await db.DocumentIndexValues.AsNoTracking()
            .Where(v => v.DocumentId == id)
            .ToListAsync(ct);

        return Ok(ToDto(document, currentVersion, indexValues));
    }

    // ---- Content ---------------------------------------------------------------------------

    [HttpGet("{id:guid}/versions/{versionId:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, Guid versionId, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var version = await db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.DocumentId == id, ct);
        if (version is null) return NotFound();

        Stream blob;
        try
        {
            blob = await blobStore.GetAsync(tenantId, version.BlobHash);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "blob_missing" });
        }

        if (!ContentTypeProvider.TryGetContentType(version.OriginalFileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return File(blob, contentType, version.OriginalFileName);
    }

    // ---- Search ------------------------------------------------------------------------------

    /// <summary>
    /// Pragmatic subset of ARCHITECTURE.md 2.3's metadata search: exact match on Text/Bool index
    /// values via <c>field.{code}</c>, and range comparisons on Number/Date index values via
    /// <c>field.{code}.gte</c> / <c>field.{code}.lte</c>. NOT supported yet: lt/gt (exclusive),
    /// ne, contains/starts-with on text, multi-value (in) filters, combining two conditions on the
    /// same field (e.g. both gte and lte in one call is fine, but two gte's on the same field
    /// overwrite rather than intersect via the naive dictionary read below — use gte+lte together
    /// instead), and the full-text index (a separate search surface per 2.3, not built this phase).
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<DocumentSearchResultDto>> Search(
        [FromQuery] Guid? cabinetId,
        [FromQuery] Guid? documentTypeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Documents.AsNoTracking().AsQueryable();

        if (cabinetId is { } cab) query = query.Where(d => d.CabinetId == cab);
        if (documentTypeId is { } type) query = query.Where(d => d.DocumentTypeId == type);

        foreach (var (key, value) in Request.Query)
        {
            if (!key.StartsWith("field.", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(value)) continue;

            var rest = key["field.".Length..];
            var dotIndex = rest.LastIndexOf('.');
            string fieldCode;
            string? op = null;
            if (dotIndex >= 0 && (rest[(dotIndex + 1)..] is "gte" or "lte"))
            {
                fieldCode = rest[..dotIndex];
                op = rest[(dotIndex + 1)..];
            }
            else
            {
                fieldCode = rest;
            }

            var rawValue = value.ToString();

            // Date gte/lte needs its own path: comparing DateTimeOffset inside the correlated
            // Any() subquery below fails to translate on Sqlite (same bug family as the ORDER BY
            // fix a few lines down — found by QA actually running a date-range search, not by
            // inspection: it 500'd instead of translating). Number/Text/Boolean all compare fine
            // inside Any() and are untouched. Resolve matching document ids with a translatable
            // query first (just equality/non-null), then compare dates in memory.
            if (op is "gte" or "lte"
                && !decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                && DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateBound))
            {
                var candidates = await db.DocumentIndexValues.AsNoTracking()
                    .Where(v => v.FieldCode == fieldCode && v.DateValue != null)
                    .Select(v => new { v.DocumentId, v.DateValue })
                    .ToListAsync(ct);

                var matchingIds = (op == "gte"
                        ? candidates.Where(v => v.DateValue!.Value >= dateBound)
                        : candidates.Where(v => v.DateValue!.Value <= dateBound))
                    .Select(v => v.DocumentId)
                    .ToHashSet();

                query = query.Where(d => matchingIds.Contains(d.Id));
                continue;
            }

            query = ApplyIndexFilter(query, fieldCode, op, rawValue);
        }

        var total = await query.CountAsync(ct);

        // Sqlite's EF provider can't translate ORDER BY on a DateTimeOffset column (not just
        // WHERE range predicates — same family of issue as LicenseService.cs). Select just the
        // two columns needed to sort/page, materialize, then order and paginate in memory.
        var documentIds = (await query
                .Select(d => new { d.Id, d.CreatedAt })
                .ToListAsync(ct))
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => d.Id)
            .ToList();

        var documents = await db.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToListAsync(ct);

        var versionIds = documents.Where(d => d.CurrentVersionId.HasValue)
            .Select(d => d.CurrentVersionId!.Value).ToList();
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var indexValues = await db.DocumentIndexValues.AsNoTracking()
            .Where(v => documentIds.Contains(v.DocumentId))
            .ToListAsync(ct);
        var indexValuesByDocument = indexValues.GroupBy(v => v.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Preserve the ordering computed above rather than the Where's own order.
        var orderedDocuments = documentIds
            .Select(docId => documents.First(d => d.Id == docId))
            .ToList();

        var items = orderedDocuments.Select(d =>
        {
            DocumentVersion? version = d.CurrentVersionId is { } vid && versions.TryGetValue(vid, out var v) ? v : null;
            var values = indexValuesByDocument.TryGetValue(d.Id, out var list) ? list : [];
            return ToDto(d, version, values);
        }).ToList();

        return Ok(new DocumentSearchResultDto
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static IQueryable<Document> ApplyIndexFilter(
        IQueryable<Document> query, string fieldCode, string? op, string rawValue)
    {
        switch (op)
        {
            case "gte":
                if (decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var numGte))
                {
                    return query.Where(d => d.IndexValues.Any(v =>
                        v.FieldCode == fieldCode && v.NumberValue != null && v.NumberValue >= numGte));
                }
                if (DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateGte))
                {
                    return query.Where(d => d.IndexValues.Any(v =>
                        v.FieldCode == fieldCode && v.DateValue != null && v.DateValue >= dateGte));
                }
                return query.Where(_ => false);

            case "lte":
                if (decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var numLte))
                {
                    return query.Where(d => d.IndexValues.Any(v =>
                        v.FieldCode == fieldCode && v.NumberValue != null && v.NumberValue <= numLte));
                }
                if (DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateLte))
                {
                    return query.Where(d => d.IndexValues.Any(v =>
                        v.FieldCode == fieldCode && v.DateValue != null && v.DateValue <= dateLte));
                }
                return query.Where(_ => false);

            default:
                // Exact match: try bool first (so "true"/"false" hits BoolValue), else text.
                if (bool.TryParse(rawValue, out var boolValue))
                {
                    return query.Where(d => d.IndexValues.Any(v =>
                        v.FieldCode == fieldCode && v.BoolValue == boolValue));
                }
                return query.Where(d => d.IndexValues.Any(v =>
                    v.FieldCode == fieldCode && v.TextValue == rawValue));
        }
    }

    private static Dictionary<string, string> ParseIndexValuesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Validates that every required <see cref="IndexFieldDefinition"/> has a value, then routes
    /// each provided value into the DocumentIndexValue column matching its field's FieldType.
    /// Keys that don't match a defined field on this document type are ignored.
    /// </summary>
    private static (List<DocumentIndexValue> Rows, string? Error) BuildIndexValueRows(
        List<IndexFieldDefinition> fieldDefs, Dictionary<string, string> values)
    {
        var missing = fieldDefs
            .Where(f => f.Required && (!values.TryGetValue(f.Code, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(f => f.Code)
            .ToList();
        if (missing.Count > 0)
        {
            return (new List<DocumentIndexValue>(), $"missing_required_fields: {string.Join(", ", missing)}");
        }

        var rows = new List<DocumentIndexValue>();
        foreach (var field in fieldDefs)
        {
            if (!values.TryGetValue(field.Code, out var raw) || raw is null) continue;

            var row = new DocumentIndexValue { FieldCode = field.Code };

            switch (field.FieldType)
            {
                case IndexFieldType.Number:
                    if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var numberValue))
                        return (new List<DocumentIndexValue>(), $"invalid_number_field: {field.Code}");
                    row.NumberValue = numberValue;
                    break;

                case IndexFieldType.Date:
                    if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue))
                        return (new List<DocumentIndexValue>(), $"invalid_date_field: {field.Code}");
                    row.DateValue = dateValue;
                    break;

                case IndexFieldType.Boolean:
                    if (!bool.TryParse(raw, out var boolValue))
                        return (new List<DocumentIndexValue>(), $"invalid_boolean_field: {field.Code}");
                    row.BoolValue = boolValue;
                    break;

                // Text, Picklist and ErpLookup are all stored as plain text; picklist membership
                // and ERP lookup resolution are left to the SPA / a future validation pass.
                default:
                    row.TextValue = raw;
                    break;
            }

            rows.Add(row);
        }

        return (rows, null);
    }

    private static DocumentDto ToDto(Document document, DocumentVersion? currentVersion, List<DocumentIndexValue> indexValues)
    {
        return new DocumentDto
        {
            Id = document.Id,
            CabinetId = document.CabinetId,
            DocumentTypeId = document.DocumentTypeId,
            Status = document.Status,
            OwnerId = document.OwnerId,
            CurrentVersionId = document.CurrentVersionId,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            CurrentVersion = currentVersion is null ? null : ToVersionDto(currentVersion),
            IndexValues = indexValues.Select(v => new IndexFieldValueDto(v.FieldCode, FormatValue(v))).ToList(),
        };
    }

    private static DocumentVersionDto ToVersionDto(DocumentVersion version) => new()
    {
        Id = version.Id,
        VersionNumber = version.VersionNumber,
        BlobHash = version.BlobHash,
        OriginalFileName = version.OriginalFileName,
        SizeBytes = version.SizeBytes,
        PageCount = version.PageCount,
        AuthorId = version.AuthorId,
        Comment = version.Comment,
        CreatedAt = version.CreatedAt,
    };

    private static string? FormatValue(DocumentIndexValue value)
    {
        if (value.TextValue is not null) return value.TextValue;
        if (value.NumberValue is not null) return value.NumberValue.Value.ToString(CultureInfo.InvariantCulture);
        if (value.DateValue is not null) return value.DateValue.Value.ToString("O", CultureInfo.InvariantCulture);
        if (value.BoolValue is not null) return value.BoolValue.Value ? "true" : "false";
        return null;
    }
}
