namespace NexusDocs.Api.Infrastructure.Tenancy;

/// <summary>
/// Default scoped implementation of <see cref="ICurrentTenantAccessor"/>. A plain settable
/// property is all that's needed — it lives for one DI scope (one HTTP request), is set once by
/// <see cref="TenantResolutionMiddleware"/> near the start of the pipeline, and is read by
/// everything downstream in that same scope (DbContext, LicenseService, controllers).
/// </summary>
public sealed class CurrentTenantAccessor : ICurrentTenantAccessor
{
    public Guid? TenantId { get; set; }
}
