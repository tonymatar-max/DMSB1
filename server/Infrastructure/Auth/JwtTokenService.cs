using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NexusDocs.Api.Domain.Platform;

namespace NexusDocs.Api.Infrastructure.Auth;

/// <summary>
/// Issues short-lived JWT access tokens and opaque refresh tokens (ARCHITECTURE.md 2.5).
/// Signing key is read from configuration ("Jwt:SigningKey") rather than hard-coded so it can
/// come from an environment variable / key vault in real deployments.
///
/// Access tokens carry: "sub" (user id), "tenant" (tenant id — the same claim type
/// <see cref="Tenancy.TenantResolutionMiddleware"/> reads), and "email". HS256, ~30 minute
/// expiry.
/// </summary>
public class JwtTokenService(IConfiguration configuration)
{
    private const int AccessTokenMinutes = 30;
    private const int RefreshTokenBytes = 48;

    public string CreateAccessToken(User user)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Configuration value 'Jwt:SigningKey' is not set.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant", user.TenantId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>A cryptographically random opaque refresh token. Persisted and looked up by the
    /// caller (not decoded) — it carries no claims of its own.</summary>
    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
}
