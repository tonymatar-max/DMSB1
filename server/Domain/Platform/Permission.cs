namespace NexusDocs.Api.Domain.Platform;

/// <summary>
/// Permission codes used by <see cref="RolePermission"/> and the endpoint authorization handler.
/// Codes are stable strings (not an enum) so new modules can add codes without a migration to a
/// shared type, and so a licence-gated module's codes can be introduced only when that module
/// ships.
/// </summary>
public static class Permission
{
    public const string DocumentsRead = "documents.read";
    public const string DocumentsWrite = "documents.write";
    public const string DocumentsDelete = "documents.delete";
    public const string CabinetsManage = "cabinets.manage";
    public const string UsersManage = "users.manage";
    public const string LicensesManage = "licenses.manage";
    public const string AuditRead = "audit.read";

    // Phase 2+ (FLOW / SIGN) permission codes land here once those modules ship, e.g.
    // "flow.approve", "sign.send" — kept out of Phase 1 since CORE/ARCHIVE don't need them yet.
}
