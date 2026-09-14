namespace NexusDocs.Api.Infrastructure.Tenancy;

/// <summary>
/// Ambient tenant for the current request/scope. Populated once by
/// <see cref="TenantResolutionMiddleware"/> and consumed structurally by
/// <c>NexusDocsDbContext</c> for its global query filters and tenant stamping, and by
/// <c>LicenseService</c> / <c>RequiresModuleAttribute</c> for licence checks.
///
/// Registered as scoped (one instance per HTTP request / per DI scope). The property name
/// <see cref="TenantId"/> is load-bearing: other agents' code (notably the DbContext) depends on
/// this exact name.
/// </summary>
public interface ICurrentTenantAccessor
{
    /// <summary>
    /// The resolved tenant for this request, or <c>null</c> when no JWT "tenant" claim and no
    /// "X-Tenant" header were present. Endpoints that require a tenant should fail their own
    /// authorization (e.g. via <c>[RequiresModule]</c> or an explicit check) rather than relying
    /// on this being non-null.
    /// </summary>
    Guid? TenantId { get; set; }
}
