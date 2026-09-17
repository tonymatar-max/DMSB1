using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Capture;
using NexusDocs.Api.Infrastructure.Licensing;
using NexusDocs.Api.Infrastructure.Secrets;
using NexusDocs.Api.Models;

namespace NexusDocs.Api.Api;

/// <summary>
/// Manages a tenant's CAPTURE ingest sources (ARCHITECTURE.md section 7 step 1) — a monitored
/// HotFolder (FileSystemWatcher, fully real/testable here) or an Imap mailbox (real MailKit-backed
/// polling, but not run against a live mailbox in this environment — see
/// Infrastructure/Capture/ImapIngestPoller.cs's own disclosure). The IMAP password never appears in
/// any response — same ISecretStore + CredentialsRef convention as ErpConnectionsController.
/// </summary>
[ApiController]
[Route("api/ingest-sources")]
[Authorize]
[RequiresModule("CAPTURE")]
public class IngestSourcesController(
    NexusDocsDbContext db,
    ISecretStore secretStore,
    NexusDocs.Api.Infrastructure.Tenancy.ICurrentTenantAccessor currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IngestSourceDto>>> List()
    {
        var sources = await db.IngestSources.OrderBy(s => s.Name).ToListAsync();
        return Ok(sources.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IngestSourceDto>> GetById(Guid id)
    {
        var source = await db.IngestSources.FirstOrDefaultAsync(s => s.Id == id);
        return source is null ? NotFound() : Ok(ToDto(source));
    }

    [HttpPost]
    public async Task<ActionResult<IngestSourceDto>> Create(UpsertIngestSourceRequest request)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name_required" });

        if (request.Kind == IngestSourceKind.HotFolder && string.IsNullOrWhiteSpace(request.HotFolderPath))
            return BadRequest(new { error = "hot_folder_path_required" });

        if (request.Kind == IngestSourceKind.Imap && string.IsNullOrWhiteSpace(request.ImapHost))
            return BadRequest(new { error = "imap_host_required" });

        var source = new IngestSource
        {
            TenantId = tenantId,
            Kind = request.Kind,
            Name = request.Name,
            IsActive = request.IsActive,
        };

        if (request.Kind == IngestSourceKind.HotFolder)
        {
            source.HotFolderPath = request.HotFolderPath;

            // Create the folder (and the "processed" subfolder HotFolderWatcher moves completed
            // files into — see Infrastructure/Capture/HotFolderWatcher.cs's StartWatching) so the
            // source is immediately usable without the operator having to pre-create it by hand.
            try
            {
                Directory.CreateDirectory(request.HotFolderPath!);
                Directory.CreateDirectory(Path.Combine(request.HotFolderPath!, "processed"));
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "hot_folder_path_could_not_be_created", detail = ex.Message });
            }
        }
        else
        {
            source.ImapHost = request.ImapHost;
            source.ImapPort = request.ImapPort;
            source.ImapUseSsl = request.ImapUseSsl ?? true;
            source.ImapUsername = request.ImapUsername;
            source.ImapFolderName = string.IsNullOrWhiteSpace(request.ImapFolderName) ? "INBOX" : request.ImapFolderName;

            // CredentialsRef is a key into ISecretStore, never the password itself — same pattern
            // as ErpConnection.CredentialsRef, set once the source's own Id exists so the key is
            // stable and traceable back to this row.
            source.ImapCredentialsRef = $"ingest-source:{source.Id}";
            db.IngestSources.Add(source);
            await secretStore.SetAsync(tenantId, source.ImapCredentialsRef, request.ImapPassword ?? string.Empty);
            await db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = source.Id }, ToDto(source));
        }

        db.IngestSources.Add(source);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = source.Id }, ToDto(source));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<IngestSourceDto>> Update(Guid id, UpsertIngestSourceRequest request)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var source = await db.IngestSources.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return NotFound();

        source.Name = request.Name;
        source.IsActive = request.IsActive;

        if (source.Kind == IngestSourceKind.HotFolder)
        {
            source.HotFolderPath = request.HotFolderPath;

            if (!string.IsNullOrWhiteSpace(request.HotFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(request.HotFolderPath);
                    Directory.CreateDirectory(Path.Combine(request.HotFolderPath, "processed"));
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = "hot_folder_path_could_not_be_created", detail = ex.Message });
                }
            }
        }
        else
        {
            source.ImapHost = request.ImapHost;
            source.ImapPort = request.ImapPort;
            source.ImapUseSsl = request.ImapUseSsl ?? source.ImapUseSsl;
            source.ImapUsername = request.ImapUsername;
            source.ImapFolderName = string.IsNullOrWhiteSpace(request.ImapFolderName) ? source.ImapFolderName : request.ImapFolderName;

            // Password is optional on update, same as ErpConnectionsController.Update — only
            // overwrite the stored secret when a new one was actually supplied.
            if (!string.IsNullOrEmpty(request.ImapPassword))
            {
                source.ImapCredentialsRef ??= $"ingest-source:{source.Id}";
                await secretStore.SetAsync(tenantId, source.ImapCredentialsRef, request.ImapPassword);
            }
        }

        await db.SaveChangesAsync();
        return Ok(ToDto(source));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (currentTenant.TenantId is not { } tenantId) return Unauthorized();

        var source = await db.IngestSources.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return NotFound();

        if (!string.IsNullOrEmpty(source.ImapCredentialsRef))
        {
            await secretStore.DeleteAsync(tenantId, source.ImapCredentialsRef);
        }

        db.IngestSources.Remove(source);
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static IngestSourceDto ToDto(IngestSource s) => new(
        s.Id, s.Kind, s.Name, s.IsActive, s.HotFolderPath,
        s.ImapHost, s.ImapPort, s.ImapUseSsl, s.ImapUsername, s.ImapFolderName,
        HasImapCredentials: !string.IsNullOrEmpty(s.ImapCredentialsRef),
        s.LastPolledAt, s.CreatedAt);
}
