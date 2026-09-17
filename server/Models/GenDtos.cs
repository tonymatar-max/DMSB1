namespace NexusDocs.Api.Models;

// ---- DocumentTemplate DTOs ----------------------------------------------------------------

public record DocumentTemplateListItemDto(
    Guid Id,
    string Name,
    string? Description,
    int? SourceObjectType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record DocumentTemplateDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string TemplateHtml,
    int? SourceObjectType,
    string? SampleDataJson,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public class CreateDocumentTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TemplateHtml { get; set; } = string.Empty;
    public int? SourceObjectType { get; set; }
    public string? SampleDataJson { get; set; }
}

public class UpdateDocumentTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TemplateHtml { get; set; } = string.Empty;
    public int? SourceObjectType { get; set; }
    public string? SampleDataJson { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Optional preview data - falls back to the template's own SampleDataJson when omitted.</summary>
public class PreviewDocumentTemplateRequest
{
    public string? DataJson { get; set; }
}

// ---- GeneratedDocument DTOs ---------------------------------------------------------------

/// <summary>
/// Where the phase supports pushing a generated PDF, per ARCHITECTURE.md 1.2's own list ("pushed
/// straight into an archive cabinet, a workflow or a signing envelope"). This phase implements the
/// ARCHIVE target only - pushing directly into a FLOW workflow or a SIGN envelope in the same call
/// is a reasonable follow-up, not built here (see GeneratedDocumentsController remarks).
/// </summary>
public enum GenerationPushTarget
{
    None = 0,
    Archive = 1,
}

public class CreateGeneratedDocumentRequest
{
    public Guid DocumentTemplateId { get; set; }
    public string DataJson { get; set; } = string.Empty;
    public GenerationPushTarget PushTo { get; set; } = GenerationPushTarget.None;
    public Guid? CabinetId { get; set; }
    public Guid? DocumentTypeId { get; set; }
}

public record GeneratedDocumentListItemDto(
    Guid Id,
    Guid DocumentTemplateId,
    string TemplateName,
    Guid GeneratedByUserId,
    DateTimeOffset GeneratedAt,
    bool IsArchived,
    Guid? ArchivedDocumentId);

public record GeneratedDocumentDetailDto(
    Guid Id,
    Guid DocumentTemplateId,
    string TemplateName,
    Guid GeneratedByUserId,
    string InputDataJson,
    DateTimeOffset GeneratedAt,
    Guid? ArchivedDocumentId,
    Guid? EnvelopeId);
