namespace NexusDocs.Api.Models;

/// <summary>Response shape for a single <c>Cabinet</c> (ARCHIVE module).</summary>
public record CabinetDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? DefaultRetentionPolicyId,
    DateTimeOffset CreatedAt);

public record CreateCabinetRequest(string Name, string? Description, Guid? DefaultRetentionPolicyId);

public record UpdateCabinetRequest(string Name, string? Description, Guid? DefaultRetentionPolicyId);
