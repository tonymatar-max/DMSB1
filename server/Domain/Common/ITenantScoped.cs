namespace NexusDocs.Api.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one tenant. <c>NexusDocsDbContext</c> applies a global
/// query filter per entity type implementing this interface and stamps <see cref="TenantId"/> on
/// insert, so cross-tenant leakage is not reachable from application code.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
