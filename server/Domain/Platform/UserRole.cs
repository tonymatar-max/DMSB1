using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

/// <summary>Join entity assigning a role to a user.</summary>
public class UserRole : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}
