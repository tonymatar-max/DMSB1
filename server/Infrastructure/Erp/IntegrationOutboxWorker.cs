using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Erp;

namespace NexusDocs.Api.Infrastructure.Erp;

/// <summary>
/// Drains <see cref="IntegrationOutbox"/> to the tenant's ERP connection, per ARCHITECTURE.md
/// section 2.6's "IntegrationOutboxWorker, 1 min" row. Runs across all tenants (background work
/// has no ambient HTTP tenant context, so every query here uses IgnoreQueryFilters() and an
/// explicit TenantId, same convention as FlowTimerWorker).
/// </summary>
public class IntegrationOutboxWorker(IServiceScopeFactory scopeFactory, ILogger<IntegrationOutboxWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        // Fire once immediately on startup rather than waiting a full interval, same as
        // FlowTimerWorker, so a freshly-started instance doesn't leave the first tick's worth of
        // outbox rows sitting for up to a minute with nothing obviously wrong.
        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusDocsDbContext>();
            var adapter = scope.ServiceProvider.GetRequiredService<IErpAdapter>();

            // Sqlite can't translate a DateTimeOffset range predicate under the tenant query
            // filter (see LicenseService.cs for the pattern this follows) — narrow to simple
            // equality/int comparisons in SQL, do the backoff-window date math in memory.
            var candidates = await db.IntegrationOutbox
                .IgnoreQueryFilters()
                .Where(o =>
                    o.Status == IntegrationOutboxStatus.Pending ||
                    (o.Status == IntegrationOutboxStatus.Failed && o.Attempts < MaxAttempts))
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var due = candidates.Where(o => IsDue(o, now)).ToList();

            if (due.Count == 0) return;

            logger.LogInformation("IntegrationOutboxWorker: {Count} entr{Suffix} due for dispatch.",
                due.Count, due.Count == 1 ? "y" : "ies");

            foreach (var item in due)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await adapter.PushOutboxItemAsync(item.TenantId, item.Id);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    // PushOutboxItemAsync already catches and records adapter-level failures onto
                    // the row itself; a throw escaping it means something more fundamental broke
                    // (DI, DB connectivity). Log and move on to the next item rather than let one
                    // bad row take down the whole tick.
                    logger.LogError(ex, "IntegrationOutboxWorker: unhandled error dispatching outbox {OutboxId}.", item.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IntegrationOutboxWorker tick failed.");
        }
    }

    /// <summary>
    /// Pending rows are always due. A previously-Failed row backs off exponentially by attempt
    /// count (2, 4, 8, 16 minutes, ...) measured from its last processed time, capped by
    /// <see cref="MaxAttempts"/> above (a row that has exhausted its attempts is simply never
    /// selected again by the query in <see cref="RunOnceAsync"/> and needs a human to look at it).
    /// </summary>
    private static bool IsDue(IntegrationOutbox item, DateTimeOffset now)
    {
        if (item.Status == IntegrationOutboxStatus.Pending) return true;

        if (item.ProcessedAt is not { } processedAt) return true; // Failed but never timestamped: retry now.

        var backoff = TimeSpan.FromMinutes(Math.Pow(2, item.Attempts));
        return now >= processedAt + backoff;
    }
}
