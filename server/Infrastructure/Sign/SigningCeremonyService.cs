using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Domain.Sign;
using NexusDocs.Api.Infrastructure.Audit;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Infrastructure.Flow;

namespace NexusDocs.Api.Infrastructure.Sign;

// ---------------------------------------------------------------------------------------------
// Request-shape DTOs for CreateEnvelopeAsync. Kept as simple records here (rather than in a
// separate Contracts project) since this service is the only thing that constructs them today;
// the API layer maps its own request DTOs onto these.
// ---------------------------------------------------------------------------------------------

public record RecipientInput(string Email, string Name, RecipientRole Role, int SigningOrder);

public record SignatureFieldInput(
    int RecipientIndex,
    SignatureFieldKind Kind,
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height);

public record FieldValueInput(Guid FieldId, string Value, byte[]? SignatureImageBytes);

/// <summary>
/// Everything <see cref="SigningCeremonyService.GetCeremonyContextAsync"/> hands back to the
/// (unauthenticated, public) ceremony API layer to render the signing page. The API layer resolves
/// the actual PDF bytes itself (via IBlobStore, using SourceDocumentBlobHash) - this DTO only
/// carries domain data.
/// </summary>
public record CeremonyContext(
    Envelope Envelope,
    Recipient Recipient,
    string SourceDocumentBlobHash,
    IReadOnlyList<SignatureField> Fields);

/// <summary>
/// The SIGN module's ceremony engine (ARCHITECTURE.md section 6.3 "The ceremony"): creating and
/// sending envelopes, and driving each recipient's public, tokenised, unauthenticated ceremony
/// through consent -> field completion -> sealing. Mirrors the house style of
/// server/Infrastructure/Flow/WorkflowEngine.cs (ARCHITECTURE.md section 5) - every state-changing
/// operation records its CeremonyEvent(s)/AuditService entry in the same SaveChangesAsync call as
/// the rest of its work, relying on EF Core's own change tracking for the "same transaction as the
/// decision" guarantee, exactly as WorkflowEngine does.
///
/// IMPORTANT - public ceremony endpoints have no ambient tenant: GetCeremonyContextAsync,
/// RecordConsentAsync, CompleteSignatureAsync and DeclineAsync are all reached by ceremony token
/// alone, from unauthenticated public controller actions (ARCHITECTURE.md section 6.3's "no account
/// required" link). There is therefore no JWT "tenant" claim for TenantResolutionMiddleware to
/// resolve, so every query in those methods uses .IgnoreQueryFilters() and matches TenantId
/// explicitly once it's known (via the Recipient row itself) - the same fix already applied in
/// Infrastructure/Secrets/DataProtectionSecretStore.cs and Infrastructure/Erp/SapB1ServiceLayerAdapter.cs
/// for the same reason. CreateEnvelopeAsync/SendEnvelopeAsync run behind the authenticated SPA, so
/// they rely on the normal ambient tenant filter like the rest of the codebase.
///
/// NOTE on the Sqlite/query-filter DateTimeOffset bug: TokenExpiresAt validation in
/// GetCeremonyContextAsync narrows to the token-equality predicate in SQL first
/// (.Where(...).FirstOrDefaultAsync()), then compares TokenExpiresAt against DateTimeOffset.UtcNow
/// in memory, per the pattern documented in WorkflowEngine.cs / LicenseService.cs.
/// </summary>
public class SigningCeremonyService(
    NexusDocsDbContext db,
    IBlobStore blobStore,
    IPdfSealer pdfSealer,
    AuditService audit)
{
    // ---------------------------------------------------------------------------------------
    // Create / Send
    // ---------------------------------------------------------------------------------------

    public async Task<Envelope> CreateEnvelopeAsync(
        Guid tenantId,
        Guid senderId,
        Guid sourceDocumentId,
        string name,
        string? message,
        IReadOnlyList<RecipientInput> recipients,
        IReadOnlyList<SignatureFieldInput> fields)
    {
        if (recipients.Count == 0)
            throw new ValidationException("An envelope requires at least one recipient.");

        var sourceDocument = await db.Documents
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == sourceDocumentId)
            ?? throw new ValidationException($"Document {sourceDocumentId} was not found.");

        var envelope = new Envelope
        {
            TenantId = tenantId,
            SourceDocumentId = sourceDocument.Id,
            Name = name,
            Message = message,
            SenderId = senderId,
            Status = EnvelopeStatus.Draft,
        };
        db.Envelopes.Add(envelope);

        var recipientEntities = new List<Recipient>(recipients.Count);
        foreach (var r in recipients)
        {
            var recipient = new Recipient
            {
                TenantId = tenantId,
                EnvelopeId = envelope.Id,
                Email = r.Email,
                Name = r.Name,
                Role = r.Role,
                SigningOrder = r.SigningOrder,
                Status = RecipientStatus.Pending,
                CeremonyToken = GenerateCeremonyToken(),
                TokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            };
            db.Recipients.Add(recipient);
            envelope.Recipients.Add(recipient);
            recipientEntities.Add(recipient);
        }

        foreach (var f in fields)
        {
            if (f.RecipientIndex < 0 || f.RecipientIndex >= recipientEntities.Count)
                throw new ValidationException($"Field references RecipientIndex {f.RecipientIndex}, which is out of range for {recipientEntities.Count} recipient(s).");

            var field = new SignatureField
            {
                TenantId = tenantId,
                EnvelopeId = envelope.Id,
                RecipientId = recipientEntities[f.RecipientIndex].Id,
                Kind = f.Kind,
                PageNumber = f.PageNumber,
                X = f.X,
                Y = f.Y,
                Width = f.Width,
                Height = f.Height,
            };
            db.SignatureFields.Add(field);
            envelope.Fields.Add(field);
        }

        await db.SaveChangesAsync();
        return envelope;
    }

    /// <summary>
    /// Marks the envelope Sent and activates the first (lowest SigningOrder) tier of Signer
    /// recipients - sequential routing per ARCHITECTURE.md section 6.3. Does NOT send any email:
    /// no outbound email/SMS delivery integration exists in this codebase yet (see the phase's
    /// scope note). The caller (the API layer) is responsible for returning each activated
    /// recipient's ceremony URL (built from CeremonyToken) to the sender so it can be shared
    /// manually.
    /// </summary>
    public async Task<Envelope> SendEnvelopeAsync(Guid tenantId, Guid envelopeId)
    {
        var envelope = await db.Envelopes
            .Include(e => e.Recipients)
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == envelopeId)
            ?? throw new ValidationException($"Envelope {envelopeId} was not found.");

        if (envelope.Status != EnvelopeStatus.Draft)
            throw new ValidationException($"Envelope {envelopeId} has already been sent (status {envelope.Status}).");

        envelope.Status = EnvelopeStatus.Sent;
        envelope.SentAt = DateTimeOffset.UtcNow;

        var firstTierOrder = envelope.Recipients
            .Where(r => r.Role == RecipientRole.Signer)
            .Select(r => (int?)r.SigningOrder)
            .DefaultIfEmpty(null)
            .Min();

        var activated = envelope.Recipients
            .Where(r => r.Role == RecipientRole.Signer && r.SigningOrder == firstTierOrder)
            // Cc recipients are informational only in this phase - not gated by SigningOrder,
            // activated alongside the first tier so they can view immediately.
            .Concat(envelope.Recipients.Where(r => r.Role == RecipientRole.Cc))
            .ToList();

        foreach (var recipient in activated)
        {
            recipient.Status = RecipientStatus.Sent;
            db.CeremonyEvents.Add(new CeremonyEvent
            {
                TenantId = tenantId,
                EnvelopeId = envelope.Id,
                RecipientId = recipient.Id,
                EventType = CeremonyEventType.Sent,
            });
        }

        await audit.RecordAsync(tenantId, envelope.SenderId.ToString(), "sign.envelope_sent", $"Envelope/{envelope.Id}", new
        {
            envelope.Id,
            envelope.SourceDocumentId,
            ActivatedRecipientIds = activated.Select(r => r.Id),
        });

        await db.SaveChangesAsync();
        return envelope;
    }

    // ---------------------------------------------------------------------------------------
    // Public ceremony (unauthenticated, reached by token only)
    // ---------------------------------------------------------------------------------------

    public async Task<CeremonyContext> GetCeremonyContextAsync(string ceremonyToken)
    {
        var recipient = await FindRecipientByTokenAsync(ceremonyToken);
        ValidateCeremonyAccess(recipient);

        var envelope = await db.Envelopes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == recipient.TenantId && e.Id == recipient.EnvelopeId)
            ?? throw new ValidationException($"Envelope for recipient {recipient.Id} was not found.");

        var sourceDocument = await db.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == recipient.TenantId && d.Id == envelope.SourceDocumentId)
            ?? throw new ValidationException($"Source document {envelope.SourceDocumentId} was not found.");

        var currentVersion = await db.DocumentVersions
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == recipient.TenantId && v.Id == sourceDocument.CurrentVersionId)
            .FirstOrDefaultAsync()
            ?? throw new ValidationException($"Document {sourceDocument.Id} has no current version.");

        if (recipient.Status == RecipientStatus.Sent)
        {
            recipient.Status = RecipientStatus.Viewed;
            recipient.ViewedAt = DateTimeOffset.UtcNow;
            db.CeremonyEvents.Add(new CeremonyEvent
            {
                TenantId = recipient.TenantId,
                EnvelopeId = envelope.Id,
                RecipientId = recipient.Id,
                EventType = CeremonyEventType.Viewed,
            });
            await db.SaveChangesAsync();
        }

        var fields = await db.SignatureFields
            .IgnoreQueryFilters()
            .Where(f => f.TenantId == recipient.TenantId && f.RecipientId == recipient.Id)
            .ToListAsync();

        return new CeremonyContext(envelope, recipient, currentVersion.BlobHash, fields);
    }

    public async Task RecordConsentAsync(string ceremonyToken, string? ipAddress, string? userAgent)
    {
        var recipient = await FindRecipientByTokenAsync(ceremonyToken);
        ValidateCeremonyAccess(recipient);

        recipient.Status = RecipientStatus.Consented;
        recipient.ConsentedAt = DateTimeOffset.UtcNow;

        db.CeremonyEvents.Add(new CeremonyEvent
        {
            TenantId = recipient.TenantId,
            EnvelopeId = recipient.EnvelopeId,
            RecipientId = recipient.Id,
            EventType = CeremonyEventType.ConsentGiven,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        });

        await db.SaveChangesAsync();
    }

    public async Task<Envelope> CompleteSignatureAsync(
        string ceremonyToken,
        string? ipAddress,
        string? userAgent,
        IReadOnlyList<FieldValueInput> fieldValues)
    {
        var recipient = await FindRecipientByTokenAsync(ceremonyToken);
        ValidateCeremonyAccess(recipient);

        if (recipient.Status != RecipientStatus.Consented)
            throw new ValidationException("The recipient must consent to electronic signature before completing the ceremony.");

        var envelope = await db.Envelopes
            .IgnoreQueryFilters()
            .Include(e => e.Recipients)
            .FirstOrDefaultAsync(e => e.TenantId == recipient.TenantId && e.Id == recipient.EnvelopeId)
            ?? throw new ValidationException($"Envelope for recipient {recipient.Id} was not found.");

        var recipientFields = await db.SignatureFields
            .IgnoreQueryFilters()
            .Where(f => f.TenantId == recipient.TenantId && f.RecipientId == recipient.Id)
            .ToListAsync();

        var fieldsById = recipientFields.ToDictionary(f => f.Id);

        foreach (var fv in fieldValues)
        {
            if (!fieldsById.TryGetValue(fv.FieldId, out var field))
                throw new ValidationException($"Field {fv.FieldId} does not belong to this recipient's ceremony.");

            field.Value = fv.Value;

            if (field.Kind is SignatureFieldKind.Signature or SignatureFieldKind.Initial)
            {
                if (fv.SignatureImageBytes is null || fv.SignatureImageBytes.Length == 0)
                    throw new ValidationException($"Field {fv.FieldId} requires a captured signature image.");

                using var imageStream = new MemoryStream(fv.SignatureImageBytes);
                var imageBlobHash = await blobStore.PutAsync(recipient.TenantId, imageStream);

                db.SignatureCaptures.Add(new SignatureCapture
                {
                    TenantId = recipient.TenantId,
                    RecipientId = recipient.Id,
                    SignatureFieldId = field.Id,
                    // Phase 3 only captures drawn signatures end-to-end; Typed/Uploaded are modelled
                    // (SignatureCaptureMethod) for a later UI but not distinguished here yet.
                    Method = SignatureCaptureMethod.Drawn,
                    ImageBlobHash = imageBlobHash,
                });

                // Field.Value doubles as the image reference for signature/initial kinds so a
                // renderer only needs SignatureField to draw the overlay.
                field.Value = imageBlobHash;
            }
        }

        recipient.Status = RecipientStatus.Signed;
        recipient.SignedAt = DateTimeOffset.UtcNow;

        db.CeremonyEvents.Add(new CeremonyEvent
        {
            TenantId = recipient.TenantId,
            EnvelopeId = envelope.Id,
            RecipientId = recipient.Id,
            EventType = CeremonyEventType.FieldsCompleted,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        });
        db.CeremonyEvents.Add(new CeremonyEvent
        {
            TenantId = recipient.TenantId,
            EnvelopeId = envelope.Id,
            RecipientId = recipient.Id,
            EventType = CeremonyEventType.Signed,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        });

        var currentTierOrder = recipient.SigningOrder;
        var currentTierSigners = envelope.Recipients
            .Where(r => r.Role == RecipientRole.Signer && r.SigningOrder == currentTierOrder)
            .ToList();

        var currentTierComplete = currentTierSigners.All(r =>
            r.Id == recipient.Id ? true : r.Status == RecipientStatus.Signed);

        if (currentTierComplete)
        {
            var nextTierOrder = envelope.Recipients
                .Where(r => r.Role == RecipientRole.Signer
                    && r.SigningOrder > currentTierOrder
                    && r.Status != RecipientStatus.Signed
                    && r.Status != RecipientStatus.Declined)
                .Select(r => (int?)r.SigningOrder)
                .DefaultIfEmpty(null)
                .Min();

            if (nextTierOrder is not null)
            {
                var nextTier = envelope.Recipients
                    .Where(r => r.Role == RecipientRole.Signer && r.SigningOrder == nextTierOrder)
                    .ToList();

                foreach (var next in nextTier)
                {
                    next.Status = RecipientStatus.Sent;
                    db.CeremonyEvents.Add(new CeremonyEvent
                    {
                        TenantId = recipient.TenantId,
                        EnvelopeId = envelope.Id,
                        RecipientId = next.Id,
                        EventType = CeremonyEventType.Sent,
                    });
                }
            }
            else
            {
                await SealEnvelopeAsync(envelope);
            }
        }

        await db.SaveChangesAsync();
        return envelope;
    }

    public async Task DeclineAsync(string ceremonyToken, string? reason, string? ipAddress, string? userAgent)
    {
        var recipient = await FindRecipientByTokenAsync(ceremonyToken);
        ValidateCeremonyAccess(recipient);

        recipient.Status = RecipientStatus.Declined;
        recipient.DeclinedAt = DateTimeOffset.UtcNow;
        recipient.DeclineReason = reason;

        db.CeremonyEvents.Add(new CeremonyEvent
        {
            TenantId = recipient.TenantId,
            EnvelopeId = recipient.EnvelopeId,
            RecipientId = recipient.Id,
            EventType = CeremonyEventType.Declined,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Detail = reason,
        });

        // A single decline stops the whole envelope (simplest correct behaviour for Phase 3) -
        // kept at the recipient-level Declined event plus this envelope status change rather than
        // adding a separate envelope-level event type, per the task brief.
        var envelope = await db.Envelopes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == recipient.TenantId && e.Id == recipient.EnvelopeId)
            ?? throw new ValidationException($"Envelope for recipient {recipient.Id} was not found.");

        envelope.Status = EnvelopeStatus.Declined;

        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------------------
    // Sealing (runs inline at the end of CompleteSignatureAsync, same SaveChangesAsync)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Seals the completed envelope: draws visible signature overlays + appends a certificate of
    /// completion via IPdfSealer (PDFsharp - see ARCHITECTURE.md section 6.2 for the deferred
    /// cryptographic PAdES decision, TODO tracked there), stores the result as a new
    /// Archive.DocumentVersion on the source Document, and marks the envelope Completed. Only adds
    /// tracked entities / awaits the sealer - never calls SaveChangesAsync itself, so it lands in
    /// the same transaction as the caller's save (CompleteSignatureAsync), matching the
    /// WorkflowEngine "same transaction as the decision" pattern.
    /// </summary>
    private async Task SealEnvelopeAsync(Envelope envelope)
    {
        var sourceDocument = await db.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == envelope.TenantId && d.Id == envelope.SourceDocumentId)
            ?? throw new ValidationException($"Source document {envelope.SourceDocumentId} was not found.");

        var currentVersion = await db.DocumentVersions
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == envelope.TenantId && v.Id == sourceDocument.CurrentVersionId)
            .FirstOrDefaultAsync()
            ?? throw new ValidationException($"Document {sourceDocument.Id} has no current version.");

        var allFields = await db.SignatureFields
            .IgnoreQueryFilters()
            .Where(f => f.TenantId == envelope.TenantId && f.EnvelopeId == envelope.Id)
            .ToListAsync();

        var recipients = await db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == envelope.TenantId && r.EnvelopeId == envelope.Id)
            .ToListAsync();

        // Range comparisons against "now" don't apply here (Seq/date equality only), so no
        // in-memory-filter step is needed for this query beyond the simple FK predicate above.
        var events = await db.CeremonyEvents
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == envelope.TenantId && e.EnvelopeId == envelope.Id)
            .ToListAsync();

        // Envelope status/CompletedAt must be set BEFORE sealing, not after: IPdfSealer reads them
        // straight off this same Envelope instance to print the Certificate of Completion's
        // "Status: ... Completed: ..." line (see PdfOverlaySealer.AppendCertificateOfCompletion).
        // Setting them afterwards (as originally written) sealed a certificate that permanently
        // read "Status: Sent  Completed: -" into every completed envelope's PDF.
        envelope.Status = EnvelopeStatus.Completed;
        envelope.CompletedAt = DateTimeOffset.UtcNow;

        var sealedBlobHash = await pdfSealer.SealAsync(
            envelope.TenantId,
            currentVersion.BlobHash,
            envelope,
            allFields,
            recipients,
            events);

        var nextVersionNumber = await db.DocumentVersions
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == envelope.TenantId && v.DocumentId == sourceDocument.Id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync() is { } maxVersion ? maxVersion + 1 : 1;

        var sealedVersion = new DocumentVersion
        {
            TenantId = envelope.TenantId,
            DocumentId = sourceDocument.Id,
            VersionNumber = nextVersionNumber,
            BlobHash = sealedBlobHash,
            OriginalFileName = currentVersion.OriginalFileName,
            SizeBytes = (await blobStore.GetAsync(envelope.TenantId, sealedBlobHash)).Length,
            AuthorId = envelope.SenderId,
            Comment = "Sealed via Nexus Docs e-signature",
        };
        db.DocumentVersions.Add(sealedVersion);

        sourceDocument.CurrentVersionId = sealedVersion.Id;
        sourceDocument.UpdatedAt = DateTimeOffset.UtcNow;

        envelope.SealedDocumentVersionId = sealedVersion.Id;

        db.CeremonyEvents.Add(new CeremonyEvent
        {
            TenantId = envelope.TenantId,
            EnvelopeId = envelope.Id,
            RecipientId = null,
            EventType = CeremonyEventType.EnvelopeCompleted,
        });

        await audit.RecordAsync(envelope.TenantId, envelope.SenderId.ToString(), "sign.envelope_completed", $"Envelope/{envelope.Id}", new
        {
            envelope.Id,
            envelope.SourceDocumentId,
            SealedDocumentVersionId = sealedVersion.Id,
            SealedBlobHash = sealedBlobHash,
        });
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Generates a cryptographically random, URL-safe ceremony token: 32 random bytes,
    /// base64url-encoded (RFC 4648 §5 - '+'/'/' replaced, no padding) so it drops cleanly into a
    /// link's path segment with no further escaping.
    /// </summary>
    private static string GenerateCeremonyToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Looks up a Recipient by ceremony token with no ambient tenant filter (the public ceremony
    /// endpoints are unauthenticated - see the class remarks). IgnoreQueryFilters + an explicit
    /// equality predicate on CeremonyToken only; the recipient row itself is what tells every
    /// caller which TenantId to then filter the rest of its queries by.
    /// </summary>
    private async Task<Recipient> FindRecipientByTokenAsync(string ceremonyToken)
    {
        return await db.Recipients
            .IgnoreQueryFilters()
            .Where(r => r.CeremonyToken == ceremonyToken)
            .FirstOrDefaultAsync()
            ?? throw new ValidationException("This signing link is invalid.");
    }

    /// <summary>
    /// Common validity checks for every public ceremony step: not expired, not already in a
    /// terminal state. TokenExpiresAt is compared in memory against DateTimeOffset.UtcNow (the
    /// recipient row is already materialized by FindRecipientByTokenAsync's simple-equality query)
    /// per the Sqlite DateTimeOffset-range-comparison note at the top of this file.
    /// </summary>
    private static void ValidateCeremonyAccess(Recipient recipient)
    {
        if (recipient.TokenExpiresAt < DateTimeOffset.UtcNow)
            throw new ValidationException("This signing link has expired.");

        if (recipient.Status is RecipientStatus.Signed or RecipientStatus.Declined)
            throw new ValidationException($"This signing link has already been used (status {recipient.Status}).");
    }
}
