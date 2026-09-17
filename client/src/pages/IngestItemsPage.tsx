import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Capture/IngestItem.cs IngestItemStatus. */
type IngestItemStatus = 'Received' | 'Extracting' | 'Extracted' | 'Matched' | 'MatchedWithExceptions' | 'Posted' | 'Failed'

/** Mirrors server/Domain/Capture/MatchResult.cs MatchOutcome. */
type MatchOutcome = 'CleanMatch' | 'Exception'

/** Mirrors server/Models/CaptureDtos.cs IngestItemListDto. */
interface IngestItemListDto {
  id: string
  ingestBatchId: string
  ingestSourceId: string
  originalFileName: string
  status: IngestItemStatus
  errorMessage: string | null
  supplierName: string | null
  invoiceNumber: string | null
  totalAmount: number | null
  currency: string | null
  matchOutcome: MatchOutcome | null
  varianceReasons: string | null
  workflowInstanceId: string | null
  archivedDocumentId: string | null
  receivedAt: string
  processedAt: string | null
}

const STATUS_TABS: Array<{ value: IngestItemStatus | 'all'; label: string }> = [
  { value: 'all', label: 'All' },
  { value: 'Received', label: 'Received' },
  { value: 'Extracted', label: 'Extracted' },
  { value: 'Matched', label: 'Matched' },
  { value: 'MatchedWithExceptions', label: 'Exceptions' },
  { value: 'Posted', label: 'Posted' },
  { value: 'Failed', label: 'Failed' },
]

async function fetchIngestItems(status: IngestItemStatus | 'all'): Promise<IngestItemListDto[]> {
  const params = new URLSearchParams()
  if (status !== 'all') params.set('status', status)
  const { data } = await api.get<IngestItemListDto[]>(`/api/ingest-items?${params.toString()}`)
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

function matchOutcomeLabel(outcome: MatchOutcome | null): string {
  if (!outcome) return '—'
  return outcome === 'CleanMatch' ? 'Clean match' : 'Exception'
}

function formatAmount(amount: number | null, currency: string | null): string {
  if (amount == null) return '—'
  return `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency ?? ''}`.trim()
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

export default function IngestItemsPage() {
  const navigate = useNavigate()

  const [statusFilter, setStatusFilter] = useState<IngestItemStatus | 'all'>('all')

  const itemsQuery = useQuery({
    queryKey: ['ingestItems', statusFilter],
    queryFn: () => fetchIngestItems(statusFilter),
  })

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>AP automation inbox</h1>
          <div className="page-sub">Every captured invoice — extracted, matched, and either auto-posted or routed for approval.</div>
        </div>
        {itemsQuery.data && <span className="page-sub">{itemsQuery.data.length} item(s)</span>}
      </div>

      <div className="tabs">
        {STATUS_TABS.map((tab) => (
          <button
            key={tab.value}
            type="button"
            className={`tab ${statusFilter === tab.value ? 'active' : ''}`}
            onClick={() => setStatusFilter(tab.value)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="panel">
        {itemsQuery.isLoading && <div className="empty-state">Loading ingest items…</div>}
        {itemsQuery.isError && <div className="empty-state">Could not load ingest items.</div>}
        {itemsQuery.data && itemsQuery.data.length === 0 && (
          <div className="empty-state">No captured items in this view yet.</div>
        )}
        {itemsQuery.data && itemsQuery.data.length > 0 && (
          <table className="data-table">
            <thead>
              <tr>
                <th>File</th>
                <th>Status</th>
                <th>Supplier</th>
                <th>Invoice #</th>
                <th>Amount</th>
                <th>Match outcome</th>
                <th>Received</th>
              </tr>
            </thead>
            <tbody>
              {itemsQuery.data.map((item) => (
                <tr key={item.id} onClick={() => navigate(`/ingest-items/${item.id}`)}>
                  <td>{item.originalFileName}</td>
                  <td><span className={statusPillClass(item.status)}>{item.status}</span></td>
                  <td>{item.supplierName ?? '—'}</td>
                  <td>{item.invoiceNumber ?? '—'}</td>
                  <td>{formatAmount(item.totalAmount, item.currency)}</td>
                  <td>{matchOutcomeLabel(item.matchOutcome)}</td>
                  <td>{formatDate(item.receivedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </section>
  )
}
