using NexusDocs.Api.Domain.Erp;

namespace NexusDocs.Api.Models;

/// <summary>Never carries the password — only whether one has been set.</summary>
public record ErpConnectionDto(
    Guid Id,
    ErpSystemType SystemType,
    string Name,
    string CompanyDb,
    string UserName,
    string? GatewayId,
    string? BaseUrl,
    bool HasCredentials,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateErpConnectionRequest(
    string Name,
    string CompanyDb,
    string UserName,
    string Password,
    string? GatewayId,
    string? BaseUrl);

/// <summary>
/// <see cref="Password"/> is optional on update — omit it (null) to leave the stored credential
/// unchanged; every other field is always overwritten with what's supplied.
/// </summary>
public record UpdateErpConnectionRequest(
    string Name,
    string CompanyDb,
    string UserName,
    string? Password,
    string? GatewayId,
    string? BaseUrl,
    bool IsActive);

public record ErpConnectionTestResultDto(bool Success, string Message);
