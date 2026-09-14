using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Platform;

public enum TenantStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>A customer organisation. The root of every tenant-scoped query filter.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = "";

    /// <summary>Unique, URL/sub-domain-safe identifier for the tenant.</summary>
    public string Slug { get; set; } = "";

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<TenantLicense> Licenses { get; set; } = new List<TenantLicense>();
}
