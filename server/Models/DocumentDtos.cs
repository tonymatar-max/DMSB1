using NexusDocs.Api.Domain.Archive;

namespace NexusDocs.Api.Models;

/// <summary>One index field's value as returned to/accepted from the client — always the field's
/// logical value as a string; <see cref="DocumentsController"/> parses/formats it against the
/// field's <see cref="IndexFieldDefinition.FieldType"/> on the way in and out.</summary>
public record IndexFieldValueDto(string FieldCode, string? Value);

/// <summary>Summary shape returned for a single document, including its current version and index values.</summary>
public class DocumentDto
{
    public Guid Id { get; set; }
    public Guid CabinetId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public DocumentStatus Status { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public DocumentVersionDto? CurrentVersion { get; set; }
    public List<IndexFieldValueDto> IndexValues { get; set; } = [];
}

public class DocumentVersionDto
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string BlobHash { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int? PageCount { get; set; }
    public Guid AuthorId { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Paged envelope for <see cref="DocumentsController.Search"/>, matching the list-endpoint
/// shape used elsewhere in Nexus (items/total/page/pageSize).</summary>
public class DocumentSearchResultDto
{
    public List<DocumentDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
