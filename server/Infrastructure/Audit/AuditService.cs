using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Platform;

namespace NexusDocs.Api.Infrastructure.Audit;

/// <summary>
/// Writes and verifies the per-tenant, append-only, hash-chained audit trail described in
/// ARCHITECTURE.md 2.5. Each <see cref="AuditEvent"/> row's <c>Hash</c> is
/// SHA256(PrevHash || canonicalJson(payload)); because each row's hash folds in the previous
/// row's hash, any retroactive edit or deletion of history breaks the chain from that point on,
/// which <see cref="VerifyChainAsync"/> detects.
/// </summary>
public class AuditService(NexusDocsDbContext db)
{
    private const string GenesisHash = "GENESIS";

    /// <summary>
    /// Appends a new audit event to the tenant's chain.
    /// </summary>
    /// <remarks>
    /// Concurrency: the next <c>Seq</c> is computed as max(existing Seq) + 1 (or 1) without an
    /// explicit lock. A unique index on (TenantId, Seq) at the database level (see the DbContext
    /// agent's model configuration) is what actually prevents two concurrent writers for the same
    /// tenant from both claiming the same Seq/PrevHash pair — one of the two inserts will fail
    /// with a unique-constraint violation. Callers (or a thin retry wrapper around this method)
    /// should catch that failure and retry the whole read-max-seq + insert once. We deliberately
    /// do not add a distributed lock or serializable transaction here; for this phase's write
    /// volume, "detect the race via the unique index and retry" is sufficient and much simpler.
    /// </remarks>
    public async Task RecordAsync(Guid tenantId, string actor, string action, string subject, object payload)
    {
        var canonicalPayloadJson = CanonicalizeJson(payload);

        var lastSeq = await db.AuditEvents
            .Where(e => e.TenantId == tenantId)
            .Select(e => (long?)e.Seq)
            .MaxAsync() ?? 0L;

        var prevHash = lastSeq == 0
            ? GenesisHash
            : await db.AuditEvents
                .Where(e => e.TenantId == tenantId && e.Seq == lastSeq)
                .Select(e => e.Hash)
                .SingleAsync();

        var evt = new AuditEvent
        {
            TenantId = tenantId,
            Seq = lastSeq + 1,
            Actor = actor,
            Action = action,
            Subject = subject,
            PayloadJson = canonicalPayloadJson,
            PrevHash = prevHash,
            Hash = ComputeHash(prevHash, canonicalPayloadJson),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.AuditEvents.Add(evt);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Walks a tenant's audit chain in Seq order, recomputing each row's hash from its stored
    /// PrevHash + PayloadJson and comparing against the stored Hash. Returns false as soon as a
    /// mismatch is found (broken chain, or a row that was ever tampered with).
    /// </summary>
    public async Task<bool> VerifyChainAsync(Guid tenantId)
    {
        var events = await db.AuditEvents
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Seq)
            .ToListAsync();

        var expectedPrevHash = GenesisHash;
        foreach (var evt in events)
        {
            if (evt.PrevHash != expectedPrevHash)
                return false;

            var recomputedHash = ComputeHash(evt.PrevHash, evt.PayloadJson);
            if (recomputedHash != evt.Hash)
                return false;

            expectedPrevHash = evt.Hash;
        }

        return true;
    }

    private static string ComputeHash(string prevHash, string canonicalPayloadJson)
    {
        var bytes = Encoding.UTF8.GetBytes(prevHash + canonicalPayloadJson);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Serializes <paramref name="payload"/> to JSON, then re-emits it with every JSON object's
    /// properties in ordinal-sorted key order (recursively, including nested objects and objects
    /// inside arrays) so that two payloads which are logically identical but were constructed with
    /// properties in a different order always canonicalize to the exact same string, and therefore
    /// hash identically.
    /// </summary>
    internal static string CanonicalizeJson(object payload)
    {
        using var document = JsonSerializer.SerializeToDocument(payload);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
