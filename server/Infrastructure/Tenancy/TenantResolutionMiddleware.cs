namespace NexusDocs.Api.Infrastructure.Tenancy;

/// <summary>
/// Resolves the tenant for every request and stamps it onto the scoped
/// <see cref="ICurrentTenantAccessor"/> before anything else in the pipeline (controllers, the
/// DbContext's global query filters, licence checks) runs.
///
/// Resolution order:
///  1. The JWT "tenant" claim on <c>HttpContext.User</c> (the normal, authenticated path).
///  2. The "X-Tenant" header, parsed as a <see cref="Guid"/> (service-to-service calls that don't
///     carry a user JWT).
///
/// If neither is present, <see cref="ICurrentTenantAccessor.TenantId"/> is left <c>null</c>.
/// This middleware does not itself reject the request — endpoints that require a tenant must
/// enforce that themselves (e.g. via <c>[RequiresModule]</c> or an explicit authorization check).
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantClaimType = "tenant";
    public const string TenantHeaderName = "X-Tenant";

    public async Task InvokeAsync(HttpContext context, ICurrentTenantAccessor currentTenant)
    {
        var tenantClaim = context.User?.FindFirst(TenantClaimType)?.Value;
        if (Guid.TryParse(tenantClaim, out var tenantFromClaim))
        {
            currentTenant.TenantId = tenantFromClaim;
        }
        else if (context.Request.Headers.TryGetValue(TenantHeaderName, out var headerValue)
                 && Guid.TryParse(headerValue.ToString(), out var tenantFromHeader))
        {
            currentTenant.TenantId = tenantFromHeader;
        }
        else
        {
            currentTenant.TenantId = null;
        }

        await next(context);
    }
}

/// <summary>Registers <see cref="TenantResolutionMiddleware"/> in the request pipeline.</summary>
public static class TenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
