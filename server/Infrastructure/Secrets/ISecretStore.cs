namespace NexusDocs.Api.Infrastructure.Secrets;

/// <summary>
/// Stores and resolves tenant-scoped secret values (ERP connection passwords, API tokens, ...) so
/// nothing that owns a <c>CredentialsRef</c>-style key ever holds the raw value itself. See
/// <see cref="Domain.Platform.TenantSecret"/> for the storage shape and its caveats.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Stores <paramref name="plaintextValue"/> under <paramref name="key"/> for the tenant,
    /// creating or overwriting as needed, and returns the key the caller should persist as its
    /// own "CredentialsRef" (today this is just <paramref name="key"/> echoed back, but keeping
    /// it as a return value leaves room for a future implementation that generates its own key).
    /// </summary>
    Task<string> SetAsync(Guid tenantId, string key, string plaintextValue);

    /// <summary>Resolves a previously stored secret, or null if no value exists for that key.</summary>
    Task<string?> GetAsync(Guid tenantId, string key);

    Task DeleteAsync(Guid tenantId, string key);
}
