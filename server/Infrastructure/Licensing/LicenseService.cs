using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Platform;

namespace NexusDocs.Api.Infrastructure.Licensing;

/// <summary>
/// Single source of truth for what a tenant has bought (ARCHITECTURE.md 2.4). Reads
/// <see cref="TenantLicense"/> rows directly — no caching layer here (Phase 1: keep it simple;
/// add a TTL cache the way Nexus Ops does if this becomes a hot path).
///
/// Every query here narrows to tenant + module + Active status in SQL, then checks the
/// ValidFrom/ValidTo window client-side. The straightforward version — one predicate combining
/// tenant, module, status and a DateTimeOffset range, layered under the global tenant query
/// filter (NexusDocsDbContext.OnModelCreating) — fails to translate on the Sqlite provider
/// (InvalidOperationException at query time). A tenant's row count per module is at most a
/// handful, so the client-side date check costs nothing real.
/// </summary>
public class LicenseService(NexusDocsDbContext db)
{
    /// <summary>
    /// True when the tenant has a <see cref="TenantLicense"/> for <paramref name="moduleCode"/>
    /// that is <see cref="TenantLicenseStatus.Active"/> and currently within its validity window.
    /// </summary>
    public async Task<bool> IsModuleEnabledAsync(Guid tenantId, string moduleCode)
    {
        var candidates = await db.TenantLicenses.AsNoTracking()
            .Where(l =>
                l.TenantId == tenantId &&
                l.ModuleCode == moduleCode &&
                l.Status == TenantLicenseStatus.Active)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        return candidates.Any(l => l.ValidFrom <= now && now <= l.ValidTo);
    }

    /// <summary>
    /// Compares the tenant's active user count against the module's <c>SeatsLicensed</c>.
    /// A <c>null</c> <c>SeatsLicensed</c> is treated as unlimited. Throws
    /// <see cref="LicenseLimitExceededException"/> when the seat count has been reached or
    /// exceeded; otherwise returns <c>true</c>.
    /// </summary>
    public async Task<bool> AssertSeatAvailableAsync(Guid tenantId, string moduleCode)
    {
        var candidates = await db.TenantLicenses.AsNoTracking()
            .Where(l =>
                l.TenantId == tenantId &&
                l.ModuleCode == moduleCode &&
                l.Status == TenantLicenseStatus.Active)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var license = candidates.FirstOrDefault(l => l.ValidFrom <= now && now <= l.ValidTo);

        if (license?.SeatsLicensed is not { } seatsLicensed)
        {
            // No active licence at all, or no seat cap set on it: nothing to enforce here.
            // (Module-enablement itself is LicenseService.IsModuleEnabledAsync's job.)
            return true;
        }

        var seatsUsed = await db.Users.AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && u.IsActive);

        if (seatsUsed >= seatsLicensed)
            throw new LicenseLimitExceededException(moduleCode, seatsLicensed, seatsUsed);

        return true;
    }

    /// <summary>
    /// All currently-active, in-window licences for the tenant (see <see cref="IsModuleEnabledAsync"/>
    /// for the same Active/ValidFrom/ValidTo check applied per row).
    /// </summary>
    public async Task<IReadOnlyList<TenantLicense>> GetEntitlementsAsync(Guid tenantId)
    {
        var candidates = await db.TenantLicenses.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Status == TenantLicenseStatus.Active)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        return candidates.Where(l => l.ValidFrom <= now && now <= l.ValidTo).ToList();
    }
}
