using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusDocs.Api.Data;
using NexusDocs.Api.Domain.Archive;
using NexusDocs.Api.Domain.Capture;
using NexusDocs.Api.Domain.Erp;
using NexusDocs.Api.Infrastructure.Audit;
using NexusDocs.Api.Infrastructure.Files;
using NexusDocs.Api.Infrastructure.Flow;

namespace NexusDocs.Api.Infrastructure.Capture;

/// <summary>
/// Orchestrates the A/P invoice capture pipeline for one ingested file (ARCHITECTURE.md section 7
/// steps 1-6). Called by <see cref="HotFolderWatcher"/> (and, when Phase 4+'s IMAP poller lands,
/// by it too) rather than duplicating pipeline logic in each ingest source's own code.
///
/// NO AMBIENT TENANT: this service is invoked from background workers with no HTTP request in
/// flight, so — same convention as FlowTimerWorker/IntegrationOutboxWorker — every query here uses
/// <c>IgnoreQueryFilters()</c> plus an explicit <c>TenantId</c> predicate rather than relying on
/// NexusDocsDbContext's ambient-tenant query filter, which would otherwise silently return nothing.
///
/// BATCHING: this phase uses a simple one-batch-per-file model (each call to
/// <see cref="ProcessFileAsync"/> creates its own <see cref="IngestBatch"/>) rather than grouping
/// multiple files from one poll/scan into a single batch — a deliberate Phase 4 simplification;
/// real multi-file batching (e.g. one IngestBatch per hot-folder scan tick covering N files found
/// in that tick) is a natural follow-up once there's a caller that actually needs it.
/// </summary>
public class IngestPipelineService(
    NexusDocsDbContext db,
    IBlobStore blobStore,
    IOcrProvider ocrProvider,
    IThreeWayMatchService matchService,
    WorkflowEngine workflowEngine,
    AuditService auditService,
    ILogger<IngestPipelineService> logger)
{
    /// <summary>
    /// Well-known WorkflowDefinition.Name an admin must configure (via the existing Workflow
    /// Designer) for A/P invoice exceptions to route anywhere. Documented here since there is no
    /// other discovery mechanism in this phase — see the class remarks on why (same simplification
    /// pattern as RequisitionsController.Submit's explicit WorkflowDefinitionId).
    /// </summary>
    public const string ApInvoiceExceptionsWorkflowName = "AP Invoice Exceptions";

    private const string CapturedInvoicesCabinetName = "Captured Invoices";
    private const string ApInvoiceDocumentTypeName = "A/P Invoice";

    public async Task<IngestItem?> ProcessFileAsync(
        Guid tenantId,
        Guid ingestSourceId,
        string fileName,
        byte[] fileBytes,
        string contentHash,
        CancellationToken ct = default)
    {
        // ---- Step 2: de-dupe -----------------------------------------------------------------
        var existing = await db.IngestItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.ContentHash == contentHash)
            .Join(db.IngestBatches.IgnoreQueryFilters().Where(b => b.TenantId == tenantId && b.IngestSourceId == ingestSourceId),
                item => item.IngestBatchId, batch => batch.Id, (item, _) => item)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            logger.LogInformation(
                "Ingest de-dupe: file {FileName} (hash {ContentHash}) for source {IngestSourceId} matches existing IngestItem {IngestItemId}; skipping.",
                fileName, contentHash, ingestSourceId, existing.Id);
            return null;
        }

        // ---- Step 1-2: batch + item -----------------------------------------------------------
        var batch = new IngestBatch
        {
            TenantId = tenantId,
            IngestSourceId = ingestSourceId,
            Status = IngestBatchStatus.Processing,
        };
        db.IngestBatches.Add(batch);

        var item = new IngestItem
        {
            TenantId = tenantId,
            IngestBatchId = batch.Id,
            OriginalFileName = fileName,
            ContentHash = contentHash,
            Status = IngestItemStatus.Received,
        };
        db.IngestItems.Add(item);
        batch.ItemCount = 1;
        await db.SaveChangesAsync(ct);

        await auditService.RecordAsync(tenantId, "system", "capture.ingest_received", $"IngestItem/{item.Id}", new
        {
            item.Id,
            IngestSourceId = ingestSourceId,
            item.OriginalFileName,
            item.ContentHash,
        });

        try
        {
            await RunPipelineAsync(tenantId, item, fileBytes, ct);
        }
        catch (Exception ex)
        {
            item.Status = IngestItemStatus.Failed;
            item.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            item.ProcessedAt = DateTimeOffset.UtcNow;
            batch.Status = IngestBatchStatus.Failed;
            batch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Ingest pipeline failed for IngestItem {IngestItemId} ({FileName}).", item.Id, fileName);
            return item;
        }

        batch.Status = IngestBatchStatus.Completed;
        batch.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return item;
    }

    private async Task RunPipelineAsync(Guid tenantId, IngestItem item, byte[] fileBytes, CancellationToken ct)
    {
        // ---- Step 3: extraction ---------------------------------------------------------------
        item.Status = IngestItemStatus.Extracting;
        await db.SaveChangesAsync(ct);

        OcrResult ocrResult;
        await using (var stream = new MemoryStream(fileBytes, writable: false))
        {
            ocrResult = await ocrProvider.ExtractTextAsync(stream);
        }

        var classifier = new DocumentClassifier();
        var extractor = new InvoiceFieldExtractor();
        var fields = extractor.Extract(ocrResult.Text);

        var extraction = new ExtractionResult
        {
            TenantId = tenantId,
            IngestItemId = item.Id,
            OcrProviderUsed = ocrProvider.ProviderName,
            RawText = ocrResult.Text,
            DocumentTypeGuess = classifier.Classify(ocrResult.Text),
            SupplierTaxId = fields.SupplierTaxId,
            SupplierName = fields.SupplierName,
            InvoiceNumber = fields.InvoiceNumber,
            InvoiceDate = fields.InvoiceDate,
            Currency = fields.Currency,
            NetAmount = fields.NetAmount,
            TaxAmount = fields.TaxAmount,
            TotalAmount = fields.TotalAmount,
            PoReference = fields.PoReference,
        };
        db.ExtractionResults.Add(extraction);
        item.Extractions.Add(extraction);
        item.Status = IngestItemStatus.Extracted;
        await db.SaveChangesAsync(ct);

        // ---- Step 4-5: three-way match ---------------------------------------------------------
        var matchResult = await matchService.MatchAsync(tenantId, extraction, item.Id);
        matchResult.TenantId = tenantId;
        matchResult.IngestItemId = item.Id;
        db.MatchResults.Add(matchResult);
        item.Matches.Add(matchResult);
        await db.SaveChangesAsync(ct);

        await auditService.RecordAsync(tenantId, "system", "capture.matched", $"IngestItem/{item.Id}", new
        {
            item.Id,
            matchResult.Outcome,
            matchResult.MatchedCardCode,
            matchResult.MatchedPoDocNum,
            matchResult.MatchedGrpoDocNum,
            matchResult.VarianceReasons,
        });

        // ---- Step 6: branch on outcome ---------------------------------------------------------
        if (matchResult.Outcome == MatchOutcome.CleanMatch)
        {
            await FileAndPostAsync(tenantId, item, extraction, matchResult, fileBytes, ct);
        }
        else
        {
            await RouteExceptionAsync(tenantId, item, ct);
        }

        item.ProcessedAt = DateTimeOffset.UtcNow;
    }

    // ---------------------------------------------------------------------------------------
    // Clean match: file into ARCHIVE + enqueue ERP outbox row
    // ---------------------------------------------------------------------------------------

    private async Task FileAndPostAsync(
        Guid tenantId, IngestItem item, ExtractionResult extraction, MatchResult matchResult, byte[] fileBytes, CancellationToken ct)
    {
        var documentType = await FindOrCreateCapturedInvoiceDocumentTypeAsync(tenantId, ct);

        string hash;
        await using (var stream = new MemoryStream(fileBytes, writable: false))
        {
            hash = await blobStore.PutAsync(tenantId, stream);
        }

        var document = new Document
        {
            TenantId = tenantId,
            CabinetId = documentType.CabinetId,
            DocumentTypeId = documentType.Id,
            Status = DocumentStatus.Active,
            OwnerId = Guid.Empty, // Captured by the system, not a human user - see FlowTimerWorker.SystemActorId convention.
        };

        var version = new DocumentVersion
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            VersionNumber = 1,
            BlobHash = hash,
            OriginalFileName = item.OriginalFileName,
            SizeBytes = fileBytes.LongLength,
            AuthorId = Guid.Empty,
        };
        document.CurrentVersionId = version.Id;

        db.Documents.Add(document);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(ct);

        item.ArchivedDocumentId = document.Id;
        item.Status = IngestItemStatus.Matched;

        await auditService.RecordAsync(tenantId, "system", "document.created", document.Id.ToString(), new
        {
            documentId = document.Id,
            cabinetId = document.CabinetId,
            documentTypeId = document.DocumentTypeId,
            versionId = version.Id,
            blobHash = hash,
            fileName = version.OriginalFileName,
            sourceIngestItemId = item.Id,
        });

        var erpConnectionId = await db.ErpConnections
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.IsActive && c.SystemType == ErpSystemType.SapBusinessOne)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (erpConnectionId is null)
        {
            logger.LogWarning(
                "Tenant {TenantId} has no active SAP Business One ErpConnection; IngestItem {IngestItemId} was " +
                "matched and archived but no IntegrationOutbox entry could be enqueued to post the A/P invoice.",
                tenantId, item.Id);
            item.Status = IngestItemStatus.Matched; // Not Posted - nothing was queued to post it.
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            IngestItemId = item.Id,
            ArchivedDocumentId = document.Id,
            MatchedGrpoDocEntry = matchResult.MatchedGrpoDocEntry,
            MatchedGrpoDocNum = matchResult.MatchedGrpoDocNum,
            MatchedCardCode = matchResult.MatchedCardCode,
            extraction.SupplierName,
            extraction.InvoiceNumber,
            extraction.InvoiceDate,
            extraction.Currency,
            extraction.NetAmount,
            extraction.TaxAmount,
            extraction.TotalAmount,
        });

        db.IntegrationOutbox.Add(new IntegrationOutbox
        {
            TenantId = tenantId,
            ErpConnectionId = erpConnectionId.Value,
            OperationType = "CreatePurchaseInvoiceFromGrpo",
            PayloadJson = payload,
            Status = IntegrationOutboxStatus.Pending,
        });

        item.Status = IngestItemStatus.Posted; // Enqueued for async delivery by IntegrationOutboxWorker - not awaited here.
    }

    /// <summary>
    /// Find-or-create a default Cabinet "Captured Invoices" / DocumentType "A/P Invoice" (no
    /// required index fields) for this tenant, per the task brief's "keep this simple" direction —
    /// a real deployment would let an admin configure which Cabinet/DocumentType captured invoices
    /// land in via the existing ARCHIVE admin UI instead.
    /// </summary>
    private async Task<DocumentType> FindOrCreateCapturedInvoiceDocumentTypeAsync(Guid tenantId, CancellationToken ct)
    {
        var documentType = await db.DocumentTypes
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Name == ApInvoiceDocumentTypeName)
            .FirstOrDefaultAsync(ct);
        if (documentType is not null) return documentType;

        var cabinet = await db.Cabinets
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Name == CapturedInvoicesCabinetName)
            .FirstOrDefaultAsync(ct);

        if (cabinet is null)
        {
            cabinet = new Cabinet { TenantId = tenantId, Name = CapturedInvoicesCabinetName, Description = "Auto-created by the CAPTURE ingest pipeline for cleanly-matched A/P invoices." };
            db.Cabinets.Add(cabinet);
        }

        documentType = new DocumentType { TenantId = tenantId, CabinetId = cabinet.Id, Name = ApInvoiceDocumentTypeName };
        db.DocumentTypes.Add(documentType);
        await db.SaveChangesAsync(ct);
        return documentType;
    }

    // ---------------------------------------------------------------------------------------
    // Exception: route into FLOW
    // ---------------------------------------------------------------------------------------

    private async Task RouteExceptionAsync(Guid tenantId, IngestItem item, CancellationToken ct)
    {
        var workflowDefinition = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && w.Name == ApInvoiceExceptionsWorkflowName && w.IsActive)
            .FirstOrDefaultAsync(ct);

        if (workflowDefinition is null)
        {
            logger.LogWarning(
                "No active WorkflowDefinition named '{WorkflowName}' found for tenant {TenantId}; IngestItem " +
                "{IngestItemId} has match exceptions but cannot be routed into FLOW until an admin creates one " +
                "via the Workflow Designer.",
                ApInvoiceExceptionsWorkflowName, tenantId, item.Id);
            item.Status = IngestItemStatus.MatchedWithExceptions;
            return;
        }

        // WorkflowEngine.StartInstanceAsync does its own SaveChangesAsync; there is no human
        // initiator here (the system routed this, not a person), so - same convention as
        // FlowTimerWorker.SystemActorId - Guid.Empty stands in for "the system acted".
        var instance = await workflowEngine.StartInstanceAsync(
            tenantId, workflowDefinition.Id, "ApInvoiceException", item.Id, initiatorId: Guid.Empty);

        item.WorkflowInstanceId = instance.Id;
        item.Status = IngestItemStatus.MatchedWithExceptions;

        await auditService.RecordAsync(tenantId, "system", "capture.exception_routed", $"IngestItem/{item.Id}", new
        {
            item.Id,
            WorkflowInstanceId = instance.Id,
            WorkflowDefinitionId = workflowDefinition.Id,
        });
    }
}
