using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

/// <summary>
/// One row per index field value per document. A typed column per <see cref="IndexFieldDefinition.FieldType"/>
/// keeps values queryable/filterable without a schemaless JSON blob; only the column matching the field's
/// type is populated for a given row.
/// </summary>
public class DocumentIndexValue : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>The owning DocumentType's <see cref="IndexFieldDefinition.Code"/> this value belongs to.</summary>
    public string FieldCode { get; set; } = string.Empty;

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public DateTimeOffset? DateValue { get; set; }
    public bool? BoolValue { get; set; }
}
