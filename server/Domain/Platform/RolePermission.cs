using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

/// <summary>Join entity granting one permission code (see <see cref="Permission"/>) to a role.</summary>
public class RolePermission : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public string PermissionCode { get; set; } = "";
}
