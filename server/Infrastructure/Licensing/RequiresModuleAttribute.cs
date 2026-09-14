using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NexusDocs.Api.Infrastructure.Tenancy;

namespace NexusDocs.Api.Infrastructure.Licensing;

/// <summary>
/// Server-side licence gate (ARCHITECTURE.md 2.4). Hiding a module in the SPA is convenience;
/// this is the enforcement. Apply to a controller or action, e.g. <c>[RequiresModule("ARCHIVE")]</c>.
/// Returns 402 Payment Required with a small machine-readable body so the SPA can raise an
/// upgrade prompt instead of a generic error.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresModuleAttribute(string moduleCode) : Attribute, IAsyncActionFilter
{
    public string ModuleCode { get; } = moduleCode;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var currentTenant = services.GetRequiredService<ICurrentTenantAccessor>();
        var licenses = services.GetRequiredService<LicenseService>();

        if (currentTenant.TenantId is not { } tenantId)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!await licenses.IsModuleEnabledAsync(tenantId, ModuleCode))
        {
            context.Result = new ObjectResult(new
            {
                error = "module_not_licensed",
                module = ModuleCode
            })
            {
                StatusCode = StatusCodes.Status402PaymentRequired
            };
            return;
        }

        await next();
    }
}
