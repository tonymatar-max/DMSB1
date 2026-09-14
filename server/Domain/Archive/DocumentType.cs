using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

/// <summary>The index-field schema, naming rule and workflow binding for one class of document within a cabinet.</summary>
public class DocumentType : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CabinetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NamingRule { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<IndexFieldDefinition> IndexFields { get; set; } = new List<IndexFieldDefinition>();
}
