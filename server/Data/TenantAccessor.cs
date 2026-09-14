namespace NexusDocs.Api.Data;

/// <summary>
/// Ambient tenant for the current request/operation, consumed by <see cref="NexusDocsDbContext"/>'s
/// global query filters and its SaveChanges tenant stamping. Resolved once per request by the
/// tenancy middleware (added alongside JWT auth wiring in Program.cs) and registered per-scope, so
/// every DbContext instance in that scope sees the same tenant.
/// </summary>
public interface ICurrentTenantAccessor
{
    /// <summary>The authenticated caller's tenant, or null when no tenant is in scope (e.g. an
    /// unauthenticated request, a platform-admin operation, or a design-time/tooling context).</summary>
    Guid? TenantId { get; }
}

/// <summary>
/// Placeholder accessor with no tenant in scope. Used for design-time tooling (migrations) and as
/// a safe default registration; the tenancy middleware replaces this with a request-scoped
/// implementation that reads the tenant from the JWT once auth is wired up in the Api project.
/// </summary>
public sealed class NullCurrentTenantAccessor : ICurrentTenantAccessor
{
    public Guid? TenantId => null;
}
