using NexusDocs.Api.Domain.Common;

namespace NexusDocs.Api.Domain.Erp;

/// <summary>
/// A cached master-data record read from SAP B1 (ARCHITECTURE.md section 4.2) — business
/// partners, items, employees, projects, cost centres, UDF picklists, etc. Populated and
/// incrementally refreshed from Service Layer / read-only SQL, so index-field pickers and
/// approval routing rules resolve instantly and keep working while B1 is unreachable.
/// </summary>
public class ErpLookupCacheEntry : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>FK to the ERP connection this cached record was read from.</summary>
    public Guid ErpConnectionId { get; set; }

    /// <summary>SAP B1 object code the record belongs to (see ErpObjectLink for the table).</summary>
    public int ObjectType { get; set; }

    /// <summary>The natural key in B1, e.g. CardCode or ItemCode.</summary>
    public string ExternalKey { get; set; } = string.Empty;

    /// <summary>Human-readable label for pickers, e.g. "C00123 - Al Sayer Trading".</summary>
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>The cached fields for this record, serialised as JSON.</summary>
    public string DataJson { get; set; } = string.Empty;

    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
}
