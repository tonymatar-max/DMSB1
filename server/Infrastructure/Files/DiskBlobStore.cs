using System.Security.Cryptography;

namespace NexusDocs.Api.Infrastructure.Files;

/// <summary>
/// On-prem <see cref="IBlobStore"/> implementation. Blobs are stored under
/// <c>{root}/{tenantId}/{hash prefix}/{hash}</c> so that a single tenant folder never ends up with
/// millions of files in one directory, and a leaked path cannot be used to reach another tenant's
/// blobs (the tenant id is always the first path segment).
/// </summary>
public class DiskBlobStore : IBlobStore
{
    private readonly string _root;

    /// <summary>Number of hex characters of the hash used as the fan-out directory prefix.</summary>
    private const int PrefixLength = 2;

    public DiskBlobStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["BlobStore:RootPath"];

        // Path.GetFullPath(relativeValue) resolves against the OS process's current directory,
        // NOT IWebHostEnvironment.ContentRootPath - the two are the same folder under `dotnet run`
        // or a manually-launched exe (which is why this went unnoticed for a long time), but not
        // for a real Windows Service, which Windows starts with its working directory at
        // %SystemRoot%\System32 by default: a relative "data\blobs" landed at
        // C:\Windows\System32\data\blobs instead of the install folder's own data\ subfolder, with
        // no error anywhere. Anchor explicitly at ContentRootPath instead; only an already-rooted
        // configured value passes through Path.GetFullPath (a no-op there) unchanged.
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "blobs")
            : Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(environment.ContentRootPath, configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> PutAsync(Guid tenantId, Stream content)
    {
        // A straightforward buffer-then-write: compute the SHA-256 hash of the whole payload in
        // memory, then write it to its content-addressed path. True streaming hash+write (hashing
        // chunks as they are written to a temp file, then moving the temp file into place once the
        // hash is known) would avoid holding the whole blob in memory, but adds meaningful
        // complexity for this phase; acceptable given expected document sizes.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        buffer.Position = 0;

        var hashBytes = await SHA256.HashDataAsync(buffer);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var path = PathFor(tenantId, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            buffer.Position = 0;
            await using var file = File.Create(path);
            await buffer.CopyToAsync(file);
        }

        return hash;
    }

    public Task<Stream> GetAsync(Guid tenantId, string hash)
    {
        var path = PathFor(tenantId, hash);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Blob '{hash}' not found for tenant '{tenantId}'.");

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(Guid tenantId, string hash)
    {
        return Task.FromResult(File.Exists(PathFor(tenantId, hash)));
    }

    private string PathFor(Guid tenantId, string hash)
    {
        var normalisedHash = hash.ToLowerInvariant();
        var prefix = normalisedHash.Length >= PrefixLength
            ? normalisedHash[..PrefixLength]
            : normalisedHash;

        return Path.Combine(_root, tenantId.ToString("N"), prefix, normalisedHash);
    }
}
