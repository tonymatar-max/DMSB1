namespace NexusDocs.Api.Models;

public record LoginRequest(string Email, string Password);

public record UserProfileDto(Guid Id, string Email, string DisplayName, Guid TenantId);

public record LoginResponse(string AccessToken, UserProfileDto User);
