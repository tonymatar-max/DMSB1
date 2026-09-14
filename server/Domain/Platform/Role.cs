using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

public class Role : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>True for built-in roles (e.g. "Tenant Admin") that cannot be deleted by tenant admins.</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    public ICollection<UserRole> Users { get; set; } = new List<UserRole>();
}
