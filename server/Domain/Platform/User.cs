using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

public class User : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Self-reference used to walk the HR hierarchy for FLOW routing (e.g. "route to my manager").
    /// Null for a user with no manager on file.
    /// </summary>
    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }

    // CreatedAt is inherited from BaseEntity.

    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
}
