using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

public enum IndexFieldType
{
    Text,
    Number,
    Date,
    Boolean,
    Picklist,
    ErpLookup,
}

/// <summary>One column of the index-field schema for a <see cref="DocumentType"/>.</summary>
public class IndexFieldDefinition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public IndexFieldType FieldType { get; set; }
    public bool Required { get; set; }
    public bool Filterable { get; set; }

    /// <summary>JSON array of allowed values; only meaningful when <see cref="FieldType"/> is Picklist.</summary>
    public string? PicklistOptionsJson { get; set; }

    /// <summary>SAP B1 object code (e.g. OCRD, OITM) the field looks up; only meaningful when <see cref="FieldType"/> is ErpLookup.</summary>
    public int? ErpLookupObjectType { get; set; }

    public int SortOrder { get; set; }
}
