namespace NexusDocs.Api.Domain.Common;

/// <summary>
/// Marker interface for entities the save-changes audit interceptor should log to
/// <c>AuditEvent</c>. The interceptor uses <see cref="Id"/> plus EF Core's own change-tracker
/// entry (added/modified/deleted properties) to describe what happened, so this interface only
/// needs to identify the row and its kind — it carries no behaviour of its own.
/// </summary>
public interface IAuditable
{
    Guid Id { get; }

    /// <summary>
    /// Short entity name used as the audit subject prefix (e.g. "Document", "TenantLicense"),
    /// so <c>AuditEvent.Subject</c> can be built as "{AuditSubjectType}/{Id}".
    /// </summary>
    string AuditSubjectType { get; }
}
