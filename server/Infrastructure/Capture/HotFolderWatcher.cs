using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Capture;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Watches every active HotFolder <see cref="IngestSource"/> across all tenants (ARCHITECTURE.md
/// section 2.6's "IngestWorker" row, section 7 step 1 "capture") using
/// <see cref="System.IO.FileSystemWatcher"/>, and hands new files off to
/// <see cref="IngestPipelineService"/>. This is the fully real, testable-with-zero-external-
/// dependencies ingest path (unlike the IMAP source, which needs MailKit against a real mailbox
/// this environment doesn't have — see Infrastructure/Capture/ImapIngestPoller.cs's own
/// disclosure once that agent's work lands).
///
/// NO AMBIENT TENANT: background service, singleton, no HTTP request in flight — every DB query
/// here uses <c>IgnoreQueryFilters()</c> plus an explicit TenantId predicate rather than relying on
/// the ambient-tenant query filter, same convention as FlowTimerWorker/IntegrationOutboxWorker.
///
/// RECONCILIATION: the set of active HotFolder IngestSource rows is re-read every
/// <see cref="ReconcileInterval"/> (30s) rather than assumed static for the process lifetime, so a
/// newly-created/activated/deactivated/renamed source is picked up (or torn down) without a
/// restart. Each active source gets exactly one long-lived FileSystemWatcher; sources that
/// disappear or go inactive have their watcher disposed.
/// </summary>
public class HotFolderWatcher(IServiceScopeFactory scopeFactory, ILogger<HotFolderWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);

    // Debounce: a large file being copied into the folder fires Created immediately, well before
    // the copy finishes. We poll the file's size until it stops changing for this long before
    // treating it as "settled" and safe to read.
    private static readonly TimeSpan SettleCheckInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SettleStableDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, WatchedSource> _watched = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ReconcileInterval);

        await ReconcileSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileSafelyAsync(stoppingToken);
        }

        // Shutting down: dispose every live watcher.
        foreach (var watched in _watched.Values)
        {
            watched.Watcher.Dispose();
        }
        _watched.Clear();
    }

    private async Task ReconcileSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HotFolderWatcher reconcile tick failed; will retry next tick.");
        }
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusDocsDbContext>();

        var activeSources = await db.IngestSources
            .IgnoreQueryFilters()
            .Where(s => s.IsActive && s.Kind == IngestSourceKind.HotFolder)
            .ToListAsync(stoppingToken);

        var activeIds = activeSources.Select(s => s.Id).ToHashSet();

        // Tear down watchers for sources that no longer exist / went inactive.
        foreach (var (sourceId, watched) in _watched)
        {
            if (activeIds.Contains(sourceId)) continue;

            watched.Watcher.Dispose();
            _watched.TryRemove(sourceId, out _);
            logger.LogInformation("HotFolderWatcher: stopped watching source {IngestSourceId} (no longer active).", sourceId);
        }

        // Start watchers for newly-active sources, and restart any whose folder path changed.
        foreach (var source in activeSources)
        {
            if (string.IsNullOrWhiteSpace(source.HotFolderPath))
            {
                logger.LogWarning("HotFolder IngestSource {IngestSourceId} ('{Name}') is active but has no HotFolderPath configured; skipping.", source.Id, source.Name);
                continue;
            }

            // existing.Path is already normalized (see StartWatching); source.HotFolderPath is
            // whatever raw string is stored, so normalize it the same way before comparing - or
            // this would never match and restart the watcher (dropping any file currently mid-
            // settle-wait) on every single 30s reconcile tick, forever.
            var normalizedConfiguredPath = Path.GetFullPath(source.HotFolderPath);
            if (_watched.TryGetValue(source.Id, out var existing) &&
                string.Equals(existing.Path, normalizedConfiguredPath, StringComparison.OrdinalIgnoreCase))
                continue; // Already watching the right path.

            if (existing is not null)
            {
                existing.Watcher.Dispose();
                _watched.TryRemove(source.Id, out _);
            }

            StartWatching(source);
        }
    }

    private void StartWatching(IngestSource source)
    {
        // Normalize ONCE, up front, to the OS-canonical absolute form, and use that everywhere
        // below (directory creation, the FileSystemWatcher itself, the _watched dictionary key).
        // Directory.CreateDirectory silently normalizes an unusual path (e.g. a bare "/tmp/..."
        // with no drive letter resolves to the current drive's \tmp\...) but FileSystemWatcher's
        // constructor does NOT do the same normalization internally - passing it the raw,
        // un-normalized string produced a watcher that never fired a single Created event, with
        // no error anywhere, for any path shape that wasn't already fully-qualified. Found by
        // actually dropping a file into a configured folder and watching nothing happen, not by
        // reading the code - a build-only or unit-test check would never have caught this.
        var folderPath = Path.GetFullPath(source.HotFolderPath!);

        try
        {
            Directory.CreateDirectory(folderPath);
            Directory.CreateDirectory(Path.Combine(folderPath, "processed"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HotFolder IngestSource {IngestSourceId} ('{Name}'): could not create folder(s) at '{Path}'; not watching.", source.Id, source.Name, folderPath);
            return;
        }

        var watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        watcher.Created += (_, e) => OnFileCreated(source.Id, folderPath, e.FullPath);
        watcher.Error += (_, e) => logger.LogError(e.GetException(), "HotFolderWatcher: FileSystemWatcher error for source {IngestSourceId} ('{Name}').", source.Id, source.Name);
        watcher.EnableRaisingEvents = true;

        _watched[source.Id] = new WatchedSource(folderPath, watcher);
        logger.LogInformation("HotFolderWatcher: watching '{Path}' for IngestSource {IngestSourceId} ('{Name}').", folderPath, source.Id, source.Name);

        // Pick up anything already sitting in the folder from before this process started (e.g. a
        // file dropped while the service was down) — FileSystemWatcher only reports events for
        // changes that happen while it is running.
        foreach (var existingFile in SafeEnumerateFiles(folderPath))
        {
            _ = ProcessDroppedFileAsync(source.Id, folderPath, existingFile);
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void OnFileCreated(Guid ingestSourceId, string folderPath, string fullPath)
    {
        // Fire-and-forget from the FileSystemWatcher event thread: the event handler itself must
        // not block (or throw), and ProcessDroppedFileAsync already wraps its own body in
        // try/catch so a failure here is logged, not lost or crashing the process.
        _ = ProcessDroppedFileAsync(ingestSourceId, folderPath, fullPath);
    }

    private async Task ProcessDroppedFileAsync(Guid ingestSourceId, string folderPath, string fullPath)
    {
        try
        {
            // Compare fully-resolved, normalized paths rather than raw strings: fullPath (from
            // FileSystemWatcher's event args) is always the OS-canonical absolute form, but
            // folderPath is whatever the user typed into IngestSource.HotFolderPath (which may
            // have a trailing slash, forward slashes, or - as with a bare "/tmp/..." style path on
            // Windows - resolve to a different drive/casing than its literal text). Comparing the
            // raw strings meant a configured path that wasn't already in that exact canonical form
            // caused every single file drop to be silently discarded here as "unrelated", with no
            // error anywhere - this was found by actually dropping a file in and watching nothing
            // happen, not by inspection alone. Path.GetFullPath() normalizes both sides the same
            // way; OrdinalIgnoreCase because Windows paths are case-insensitive.
            var eventFileDirectory = Path.GetDirectoryName(fullPath);
            var configuredDirectory = Path.GetFullPath(folderPath).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(eventFileDirectory, configuredDirectory, StringComparison.OrdinalIgnoreCase))
                return; // A file inside our own "processed" subfolder, or unrelated — ignore.

            if (!await WaitUntilSettledAsync(fullPath))
            {
                logger.LogWarning("HotFolderWatcher: '{Path}' never settled (still changing after {Timeout}); skipping this pass.", fullPath, SettleTimeout);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(fullPath);
            }
            catch (IOException)
            {
                // File vanished (moved/deleted by something else) or is still locked despite the
                // settle wait above — log and move on; a real re-drop will retrigger Created.
                logger.LogWarning("HotFolderWatcher: could not read '{Path}' (in use or gone); skipping.", fullPath);
                return;
            }

            if (bytes.Length == 0)
            {
                logger.LogWarning("HotFolderWatcher: '{Path}' is empty; skipping.", fullPath);
                return;
            }

            var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var fileName = Path.GetFileName(fullPath);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusDocsDbContext>();
            var pipeline = scope.ServiceProvider.GetRequiredService<IngestPipelineService>();

            var source = await db.IngestSources
                .IgnoreQueryFilters()
                .Where(s => s.Id == ingestSourceId)
                .FirstOrDefaultAsync();

            if (source is null)
            {
                logger.LogWarning("HotFolderWatcher: IngestSource {IngestSourceId} no longer exists; skipping '{Path}'.", ingestSourceId, fullPath);
                return;
            }

            await pipeline.ProcessFileAsync(source.TenantId, source.Id, fileName, bytes, contentHash);

            source.LastPolledAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            MoveToProcessed(folderPath, fullPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HotFolderWatcher: unhandled error processing '{Path}' for source {IngestSourceId}.", fullPath, ingestSourceId);
        }
    }

    /// <summary>
    /// Moves the source file into a "processed" subfolder next to the watched folder (never
    /// deletes) so a restart doesn't re-ingest the same file — FileSystemWatcher only fires for
    /// events during its own lifetime, but the folder's own contents persist across restarts, so
    /// leaving the file in place would otherwise cause it to be picked up again by the
    /// "already sitting in the folder at startup" scan in <see cref="StartWatching"/>.
    /// </summary>
    private void MoveToProcessed(string folderPath, string fullPath)
    {
        try
        {
            var processedDir = Path.Combine(folderPath, "processed");
            Directory.CreateDirectory(processedDir);

            var destination = Path.Combine(processedDir, Path.GetFileName(fullPath));
            if (File.Exists(destination))
            {
                // Avoid clobbering an earlier same-named file (e.g. re-ingested with different
                // content that still de-duped, or a name collision) — suffix with a timestamp.
                var stem = Path.GetFileNameWithoutExtension(fullPath);
                var ext = Path.GetExtension(fullPath);
                destination = Path.Combine(processedDir, $"{stem}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{ext}");
            }

            File.Move(fullPath, destination);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HotFolderWatcher: processed '{Path}' but could not move it into the processed subfolder; it may be re-ingested (and de-duped) next time.", fullPath);
        }
    }

    /// <summary>
    /// Waits until <paramref name="fullPath"/>'s file size stops changing for
    /// <see cref="SettleStableDuration"/>, up to <see cref="SettleTimeout"/> total. Returns false
    /// if the file never settles (or disappears) within the timeout.
    /// </summary>
    private static async Task<bool> WaitUntilSettledAsync(string fullPath)
    {
        var deadline = DateTimeOffset.UtcNow + SettleTimeout;
        long lastSize = -1;
        var stableSince = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow < deadline)
        {
            long currentSize;
            try
            {
                currentSize = new FileInfo(fullPath).Length;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                currentSize = -1; // Still locked for exclusive write; treat as "not settled" and keep waiting.
            }

            if (currentSize == lastSize && currentSize >= 0)
            {
                if (DateTimeOffset.UtcNow - stableSince >= SettleStableDuration)
                    return true;
            }
            else
            {
                lastSize = currentSize;
                stableSince = DateTimeOffset.UtcNow;
            }

            await Task.Delay(SettleCheckInterval);
        }

        return false;
    }

    private sealed record WatchedSource(string Path, FileSystemWatcher Watcher);
}
