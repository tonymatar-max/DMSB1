import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Capture/IngestItem.cs IngestItemStatus. */
type IngestItemStatus = 'Received' | 'Extracting' | 'Extracted' | 'Matched' | 'MatchedWithExceptions' | 'Posted' | 'Failed'

/** Mirrors server/Domain/Capture/MatchResult.cs MatchOutcome. */
type MatchOutcome = 'CleanMatch' | 'Exception'

/** Mirrors server/Models/CaptureDtos.cs ExtractionResultDto. */
interface ExtractionResultDto {
  id: string
  ocrProviderUsed: string
  rawText: string
  documentTypeGuess: string | null
  supplierTaxId: string | null
  supplierName: string | null
  invoiceNumber: string | null
  invoiceDate: string | null
  currency: string | null
  netAmount: number | null
  taxAmount: number | null
  totalAmount: number | null
  poReference: string | null
  extractedAt: string
}

/** Mirrors server/Models/CaptureDtos.cs MatchResultDto. */
interface MatchResultDto {
  id: string
  outcome: MatchOutcome
  matchedCardCode: string | null
  matchedPoDocEntry: number | null
  matchedPoDocNum: string | null
  matchedGrpoDocEntry: number | null
  matchedGrpoDocNum: string | null
  varianceReasons: string | null
  matchedAt: string
}

/** Mirrors server/Models/CaptureDtos.cs IngestItemDetailDto. */
interface IngestItemDetailDto {
  id: string
  ingestBatchId: string
  ingestSourceId: string
  originalFileName: string
  contentHash: string
  status: IngestItemStatus
  errorMessage: string | null
  archivedDocumentId: string | null
  workflowInstanceId: string | null
  workflowInstanceStatus: string | null
  receivedAt: string
  processedAt: string | null
  extractions: ExtractionResultDto[]
  matches: MatchResultDto[]
}

async function fetchIngestItem(id: string): Promise<IngestItemDetailDto> {
  const { data } = await api.get<IngestItemDetailDto>(`/api/ingest-items/${id}`)
  return data
}

function statusPillClass(status: IngestItemStatus): string {
  switch (status) {
    case 'MatchedWithExceptions':
      return 'status warn'
    case 'Failed':
      return 'status danger'
    case 'Matched':
    case 'Posted':
      return 'status ok'
    default:
      return 'status'
  }
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

function formatAmount(amount: number | null, currency: string | null): string {
  if (amount == null) return '—'
  return `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency ?? ''}`.trim()
}

function parseVarianceReasons(raw: string | null): string[] {
  if (!raw) return []
  return raw
    .split(/[\n,]/)
    .map((r) => r.trim())
    .filter((r) => r.length > 0)
}

export default function IngestItemDetailPage() {
  const { id } = useParams<{ id: string }>()

  const itemQuery = useQuery({
    queryKey: ['ingestItem', id],
    queryFn: () => fetchIngestItem(id!),
    enabled: !!id,
  })

  const item = itemQuery.data
  const latestExtraction = item?.extractions[0] ?? null
  const latestMatch = item?.matches[0] ?? null

  return (
    <section>
      <div className="page-head">
        <div>
          <Link className="crumb" to="/ingest-items">&larr; AP automation inbox</Link>
          <h1>{item?.originalFileName ?? 'Ingest item'}</h1>
          {item && <div className="page-sub">Received {formatDate(item.receivedAt)} · Processed {formatDate(item.processedAt)}</div>}
        </div>
        {item && <span className={statusPillClass(item.status)}>{item.status}</span>}
      </div>

      {itemQuery.isLoading && <div className="empty-state">Loading ingest item…</div>}
      {itemQuery.isError && <div className="empty-state">Could not load this ingest item.</div>}

      {item && (
        <div style={{ padding: '0 16px 16px' }}>
          {item.errorMessage && (
            <div className="callout" style={{ background: '#fbecec', borderColor: '#f0c7ca' }}>
              <div className="callout-text">
                <strong>Error</strong>
                {item.errorMessage}
              </div>
            </div>
          )}

          {/* The payoff of the whole pipeline: get straight to the ERP-linked document or the
              pending approval from here, not buried under the extraction/match detail below. */}
          {item.archivedDocumentId && (
            <div className="callout">
              <div className="callout-text">
                <strong>Filed in the archive</strong>
                This invoice's image and index data are stored as an ARCHIVE document, one click from the B1 record forever.
              </div>
              <Link className="btn primary" to={`/documents/${item.archivedDocumentId}`}>View document</Link>
            </div>
          )}
          {item.workflowInstanceId && (
            <div className="callout">
              <div className="callout-text">
                <strong>Routed for approval</strong>
                {item.workflowInstanceStatus
                  ? `This exception is being handled as a FLOW approval — currently ${item.workflowInstanceStatus}.`
                  : 'This exception was routed into FLOW as an approval workflow.'}
              </div>
              <Link className="btn primary" to={`/workflow-instances/${item.workflowInstanceId}`}>View approval</Link>
            </div>
          )}

          <div className="detail-grid">
            <div className="panel" style={{ margin: 0 }}>
              <h2 className="panel-title">Extraction result</h2>
              <div className="panel-body">
                {!latestExtraction && <div className="empty-state">No extraction has run for this item yet.</div>}
                {latestExtraction && (
                  <>
                    <div className="kv-list">
                      <div className="kv-row"><span className="kv-key">OCR provider</span><span className="kv-value">{latestExtraction.ocrProviderUsed}</span></div>
                      <div className="kv-row"><span className="kv-key">Document type guess</span><span className="kv-value">{latestExtraction.documentTypeGuess ?? '—'}</span></div>
                      <div className="kv-row"><span className="kv-key">Supplier name</span><span className="kv-value">{latestExtraction.supplierName ?? '—'}</span></div>
                      <div className="kv-row"><span className="kv-key">Supplier tax ID</span><span className="kv-value">{latestExtraction.supplierTaxId ?? '—'}</span></div>
                      <div className="kv-row"><span className="kv-key">Invoice number</span><span className="kv-value">{latestExtraction.invoiceNumber ?? '—'}</span></div>
                      <div className="kv-row"><span className="kv-key">Invoice date</span><span className="kv-value">{latestExtraction.invoiceDate ?? '—'}</span></div>
                      <div className="kv-row"><span className="kv-key">PO reference</span><span className="kv-value">{latestExtraction.poReference ?? '—'}</span></div>
                      <div className="kv-row"><span className="kv-key">Net amount</span><span className="kv-value">{formatAmount(latestExtraction.netAmount, latestExtraction.currency)}</span></div>
                      <div className="kv-row"><span className="kv-key">Tax amount</span><span className="kv-value">{formatAmount(latestExtraction.taxAmount, latestExtraction.currency)}</span></div>
                      <div className="kv-row"><span className="kv-key">Total amount</span><span className="kv-value">{formatAmount(latestExtraction.totalAmount, latestExtraction.currency)}</span></div>
                      <div className="kv-row"><span className="kv-key">Extracted at</span><span className="kv-value">{formatDate(latestExtraction.extractedAt)}</span></div>
                    </div>

                    <details className="collapsible" style={{ marginTop: 14 }}>
                      <summary>Raw OCR text</summary>
                      <div className="raw-text-box">{latestExtraction.rawText || '(empty)'}</div>
                    </details>
                  </>
                )}
              </div>
            </div>

            <div className="panel" style={{ margin: 0 }}>
              <h2 className="panel-title">Match result</h2>
              <div className="panel-body">
                {!latestMatch && <div className="empty-state">No match attempt has run for this item yet.</div>}
                {latestMatch && (
                  <div className="kv-list">
                    <div className="kv-row">
                      <span className="kv-key">Outcome</span>
                      <span className="kv-value">
                        <span className={latestMatch.outcome === 'CleanMatch' ? 'status ok' : 'status warn'}>
                          {latestMatch.outcome === 'CleanMatch' ? 'Clean match' : 'Exception'}
                        </span>
                      </span>
                    </div>
                    <div className="kv-row"><span className="kv-key">Matched supplier (CardCode)</span><span className="kv-value">{latestMatch.matchedCardCode ?? '—'}</span></div>
                    <div className="kv-row"><span className="kv-key">Matched PO</span><span className="kv-value">{latestMatch.matchedPoDocNum ?? '—'}</span></div>
                    <div className="kv-row"><span className="kv-key">Matched GRPO</span><span className="kv-value">{latestMatch.matchedGrpoDocNum ?? '—'}</span></div>
                    <div className="kv-row"><span className="kv-key">Matched at</span><span className="kv-value">{formatDate(latestMatch.matchedAt)}</span></div>

                    {latestMatch.outcome === 'Exception' && parseVarianceReasons(latestMatch.varianceReasons).length > 0 && (
                      <div>
                        <span className="kv-key">Variance reasons</span>
                        <ul className="variance-list">
                          {parseVarianceReasons(latestMatch.varianceReasons).map((reason, idx) => (
                            <li key={idx}>{reason}</li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}
