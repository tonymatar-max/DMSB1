namespace NexusDocs.Api.Infrastructure.Erp;

/// <summary>
/// Result of resolving an ERP object reference to something the UI can render without needing the
/// ERP online (see ARCHITECTURE.md §4.1's `Label` denormalisation and §4.2 `ErpLookupCache`).
/// </summary>
/// <param name="Key">The canonical external key of the object (e.g. DocEntry, or a UDO code).</param>
/// <param name="Label">Denormalised human-readable label, e.g. "PO 4711 — Al Sayer Trading".</param>
/// <param name="DataJson">Raw object payload as returned by the ERP, serialised as JSON.</param>
public record ErpLookupResult(string Key, string Label, string DataJson);

/// <summary>
/// Adapter surface for a connected ERP system. Phase 1 defines the shape only — the concrete
/// Service Layer calls (and, per ARCHITECTURE.md §4.3, the Nexus B1 Gateway tunnel they may need
/// to go through for on-prem estates) are filled in by a later phase. Registering an
/// implementation in DI keyed by <see cref="SystemType"/> is what lets the outbox worker and
/// index-field pickers stay ERP-agnostic.
/// </summary>
public interface IErpAdapter
{
    /// <summary>
    /// Identifies which ERP this adapter talks to, e.g. "SapBusinessOne". Used to select the right
    /// adapter for a tenant's configured ERP connection.
    /// </summary>
    string SystemType { get; }

    /// <summary>
    /// Looks up a single ERP object (by <paramref name="objectType"/> — the B1 <c>ObjectType</c>
    /// numeric code per ARCHITECTURE.md §4.1's link table — and its external key) for display or
    /// index-field resolution. Returns null when the object cannot be found.
    /// </summary>
    Task<ErpLookupResult?> LookupObjectAsync(Guid tenantId, int objectType, string externalKey);

    /// <summary>
    /// Reads a handful of real rows for a B1 object type - a read-only exploration/testing aid,
    /// not used by any production pipeline. Never writes anything.
    /// </summary>
    Task<IReadOnlyList<ErpLookupResult>> SampleAsync(Guid tenantId, int objectType, int top);

    /// <summary>
    /// Pushes a single pending <c>OutboxItem</c> to the ERP (object creation, attachment bridge,
    /// UDF stamp, etc. — see ARCHITECTURE.md §4.4/§4.5 for the write-back patterns this ultimately
    /// serves). The outbox worker calls this per item and handles retry/backoff around it.
    /// </summary>
    Task PushOutboxItemAsync(Guid tenantId, Guid outboxItemId);

    /// <summary>
    /// Verifies a specific <see cref="ErpConnection"/> actually works: logs in and performs one
    /// harmless read. Never writes anything. Used by ErpConnectionsController's "test connection"
    /// action so a real login can be exercised without side effects.
    /// </summary>
    Task<ErpConnectionTestResult> TestConnectionAsync(Guid tenantId, Guid erpConnectionId);
}

public record ErpConnectionTestResult(bool Success, string Message);
