using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Erp;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Infrastructure.Secrets;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// Manages a tenant's ERP connections (ARCHITECTURE.md section 4). The password never appears in
/// any response — it is written straight to <see cref="ISecretStore"/> and the connection row
/// only ever holds the resulting <c>CredentialsRef</c> key.
/// </summary>
[ApiController]
[Route("api/erp-connections")]
[Authorize]
[RequiresModule("ERP")]
public class ErpConnectionsController(
    NexusDocsDbContext db,
    ISecretStore secretStore,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ErpConnectionDto>>> List()
    {
        var connections = await db.ErpConnections.OrderBy(c => c.Name).ToListAsync();
        return Ok(connections.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ErpConnectionDto>> GetById(Guid id)
    {
        var connection = await db.ErpConnections.FirstOrDefaultAsync(c => c.Id == id);
        return connection is null ? NotFound() : Ok(ToDto(connection));
    }

    [HttpPost]
    public async Task<ActionResult<ErpConnectionDto>> Create(CreateErpConnectionRequest request)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.BaseUrl) && string.IsNullOrWhiteSpace(request.GatewayId))
        {
            return BadRequest(new { error = "either baseUrl or gatewayId is required" });
        }

        var connection = new ErpConnection
        {
            SystemType = ErpSystemType.SapBusinessOne,
            Name = request.Name,
            CompanyDb = request.CompanyDb,
            UserName = request.UserName,
            GatewayId = request.GatewayId,
            BaseUrl = request.BaseUrl,
            IsActive = true,
        };
        // CredentialsRef is a key into ISecretStore, never the password itself — set once the
        // connection's own Id exists, so the key is stable and traceable back to this row.
        connection.CredentialsRef = $"erp-connection:{connection.Id}";

        db.ErpConnections.Add(connection);
        await secretStore.SetAsync(tenantId, connection.CredentialsRef, request.Password);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = connection.Id }, ToDto(connection));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ErpConnectionDto>> Update(Guid id, UpdateErpConnectionRequest request)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var connection = await db.ErpConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (connection is null) return NotFound();

        connection.Name = request.Name;
        connection.CompanyDb = request.CompanyDb;
        connection.UserName = request.UserName;
        connection.GatewayId = request.GatewayId;
        connection.BaseUrl = request.BaseUrl;
        connection.IsActive = request.IsActive;

        if (!string.IsNullOrEmpty(request.Password))
        {
            await secretStore.SetAsync(tenantId, connection.CredentialsRef, request.Password);
        }

        await db.SaveChangesAsync();
        return Ok(ToDto(connection));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var connection = await db.ErpConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (connection is null) return NotFound();

        await secretStore.DeleteAsync(tenantId, connection.CredentialsRef);
        db.ErpConnections.Remove(connection);
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static ErpConnectionDto ToDto(ErpConnection c) => new(
        c.Id, c.SystemType, c.Name, c.CompanyDb, c.UserName, c.GatewayId, c.BaseUrl,
        HasCredentials: !string.IsNullOrEmpty(c.CredentialsRef),
        c.IsActive, c.CreatedAt);
}
