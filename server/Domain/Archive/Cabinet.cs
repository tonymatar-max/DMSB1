using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Archive;

/// <summary>A filing room: carries the default ACL and retention policy for the document types it contains.</summary>
public class Cabinet : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? DefaultRetentionPolicyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
