using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Platform;

namespace NexusDocs.Api.Infrastructure.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> backed by <see cref="TenantSecret"/> rows, encrypted with ASP.NET
/// Core Data Protection. See <see cref="TenantSecret"/> for the deployment caveats (single-host
/// key ring by default; point Data Protection at a shared key store, or swap this implementation
/// for a real KMS, before running more than one instance or leaving local development).
/// </summary>
public class DataProtectionSecretStore : ISecretStore
{
    private const string Purpose = "NexusDocs.Secrets.v1";

    private readonly NexusDocsDbContext _db;
    private readonly IDataProtector _protector;

    public DataProtectionSecretStore(NexusDocsDbContext db, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public async Task<string> SetAsync(Guid tenantId, string key, string plaintextValue)
    {
        var encrypted = _protector.Protect(plaintextValue);

        var existing = await _db.TenantSecrets
            .IgnoreQueryFilters() // may run with no ambient tenant (background worker) — filter explicitly instead.
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key);

        if (existing is null)
        {
            _db.TenantSecrets.Add(new TenantSecret
            {
                TenantId = tenantId,
                Key = key,
                EncryptedValue = encrypted,
            });
        }
        else
        {
            existing.EncryptedValue = encrypted;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync();
        return key;
    }

    public async Task<string?> GetAsync(Guid tenantId, string key)
    {
        var row = await _db.TenantSecrets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key);

        if (row is null) return null;

        try
        {
            return _protector.Unprotect(row.EncryptedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The Data Protection key ring rotated/was lost (e.g. a fresh dev machine reusing an
            // old database). Treat as "no secret" rather than crashing the caller — surfaces as a
            // clear "please reconfigure this connection's credentials" situation instead.
            return null;
        }
    }

    public async Task DeleteAsync(Guid tenantId, string key)
    {
        var row = await _db.TenantSecrets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key);

        if (row is not null)
        {
            _db.TenantSecrets.Remove(row);
            await _db.SaveChangesAsync();
        }
    }
}
