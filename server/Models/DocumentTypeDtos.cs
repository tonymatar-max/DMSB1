using NexusDocs.Api.Domain.Archive;

namespace NexusDocs.Api.Models;

/// <summary>Response shape for one <see cref="IndexFieldDefinition"/> belonging to a document type.</summary>
public record IndexFieldDefinitionDto(
    Guid Id,
    string Code,
    string Label,
    IndexFieldType FieldType,
    bool Required,
    bool Filterable,
    string? PicklistOptionsJson,
    int? ErpLookupObjectType,
    int SortOrder);

/// <summary>Request shape for one index field to create alongside a new <c>DocumentType</c>.</summary>
public record CreateIndexFieldRequest(
    string Code,
    string Label,
    IndexFieldType FieldType,
    bool Required,
    bool Filterable,
    string? PicklistOptionsJson,
    int? ErpLookupObjectType,
    int SortOrder);

public record DocumentTypeDto(
    Guid Id,
    Guid CabinetId,
    string Name,
    string? NamingRule,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IndexFieldDefinitionDto> IndexFields);

public record CreateDocumentTypeRequest(
    string Name,
    string? NamingRule,
    IReadOnlyList<CreateIndexFieldRequest> IndexFields);
