using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Infrastructure.Auth;
using NexusDocs.Api.Infrastructure.Tenancy;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

[ApiController]
[Route("api/auth")]
public class AuthController(NexusDocsDbContext db, JwtTokenService jwtTokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        // Known simplification (Phase 1): login necessarily happens before the tenant is resolved
        // (there is no JWT yet to carry the "tenant" claim that TenantResolutionMiddleware reads),
        // so this lookup can't be scoped to a tenant the way every other query in this app is. We
        // look the user up by email globally, bypassing NexusDocsDbContext's tenant query filter
        // with IgnoreQueryFilters(). Email is only guaranteed unique per-tenant (see the
        // (TenantId, Email) unique index in NexusDocsDbContext), so if the same email exists in
        // more than one tenant this returns whichever row the database returns first. Acceptable
        // for Phase 1 (single dev tenant); a later phase should add a tenant/slug to the login
        // request to disambiguate.
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            return Unauthorized(new { error = "invalid_credentials" });
        }

        var accessToken = jwtTokenService.CreateAccessToken(user);
        var profile = new UserProfileDto(user.Id, user.Email, user.DisplayName, user.TenantId);
        return Ok(new LoginResponse(accessToken, profile));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserProfileDto> Me()
    {
        // Per spec: derived from the JWT claims only (sub/tenant/email — see JwtTokenService), no
        // database round-trip. DisplayName isn't carried in the token's claim set, so it comes
        // back empty here; callers that need it should fetch the user record separately.
        var idClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var emailClaim = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? User.FindFirst(ClaimTypes.Email)?.Value
            ?? "";
        var tenantClaim = User.FindFirst(TenantResolutionMiddleware.TenantClaimType)?.Value;

        if (!Guid.TryParse(idClaim, out var userId))
        {
            return Unauthorized();
        }

        Guid.TryParse(tenantClaim, out var tenantId);

        return Ok(new UserProfileDto(userId, emailClaim, "", tenantId));
    }
}
