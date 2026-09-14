namespace NexusDocs.Api.Infrastructure.Files;

/// <summary>
/// Content-addressed blob storage, partitioned per tenant (see ARCHITECTURE.md §2.2 "Storage
/// model"). The key returned by <see cref="PutAsync"/> is the lowercase SHA-256 hex digest of the
/// content — identical bytes written twice for the same tenant land on the same key, so storage
/// and the audit-log integrity proof are the same value.
///
/// On-prem ships <see cref="DiskBlobStore"/>; cloud deployments swap in an S3/Azure-backed
/// implementation behind this same interface (ARCHITECTURE.md §2.2 lists `S3BlobStore` and
/// `AzureBlobStore` as the other planned implementations — not part of this phase).
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Writes <paramref name="content"/> to the tenant's blob area and returns its SHA-256 hex
    /// hash, which is both the storage key and the integrity proof recorded elsewhere (audit log,
    /// certificate of completion).
    /// </summary>
    Task<string> PutAsync(Guid tenantId, Stream content);

    /// <summary>Opens the blob identified by <paramref name="hash"/> for reading.</summary>
    Task<Stream> GetAsync(Guid tenantId, string hash);

    /// <summary>Returns whether a blob with the given hash already exists for the tenant.</summary>
    Task<bool> ExistsAsync(Guid tenantId, string hash);
}
