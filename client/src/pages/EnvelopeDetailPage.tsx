import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Sign/Envelope.cs EnvelopeStatus. */
type EnvelopeStatus = 'Draft' | 'Sent' | 'Completed' | 'Declined' | 'Voided'

/** Mirrors server/Domain/Sign/Recipient.cs RecipientRole. */
type RecipientRole = 'Signer' | 'Cc'

/** Mirrors server/Domain/Sign/Recipient.cs RecipientStatus. */
type RecipientStatus = 'Pending' | 'Sent' | 'Viewed' | 'Consented' | 'Signed' | 'Declined'

/** Mirrors server/Domain/Sign/CeremonyEvent.cs CeremonyEventType. */
type CeremonyEventType =
  | 'Sent'
  | 'Viewed'
  | 'ConsentGiven'
  | 'FieldsCompleted'
  | 'Signed'
  | 'Declined'
  | 'EnvelopeCompleted'
  | 'EnvelopeVoided'

/** Mirrors server/Models/SignDtos.cs EnvelopeRecipientDto. */
interface EnvelopeRecipientDto {
  id: string
  email: string
  name: string
  role: RecipientRole
  signingOrder: number
  status: RecipientStatus
  ceremonyToken: string | null
  ceremonyUrl: string | null
  viewedAt: string | null
  consentedAt: string | null
  signedAt: string | null
  declinedAt: string | null
  declineReason: string | null
}

/** Mirrors server/Models/SignDtos.cs CeremonyEventDto. */
interface CeremonyEventDto {
  id: string
  recipientId: string | null
  eventType: CeremonyEventType
  occurredAt: string
  ipAddress: string | null
  userAgent: string | null
  detail: string | null
}

/** Mirrors server/Models/SignDtos.cs EnvelopeDetailDto. */
interface EnvelopeDetailDto {
  id: string
  sourceDocumentId: string
  name: string
  message: string | null
  senderId: string
  status: EnvelopeStatus
  createdAt: string
  sentAt: string | null
  completedAt: string | null
  sealedDocumentVersionId: string | null
  recipients: EnvelopeRecipientDto[]
  fields: unknown[]
  events: CeremonyEventDto[]
}

/** Mirrors server/Models/SignDtos.cs VerifyResultDto. */
interface VerifyResultDto {
  valid: boolean
  envelopeName: string
  completedAt: string | null
  recipientCount: number
  sealedBlobHash: string
}

async function fetchEnvelope(id: string): Promise<EnvelopeDetailDto> {
  const { data } = await api.get<EnvelopeDetailDto>(`/api/envelopes/${id}`)
  return data
}

async function sendEnvelope(id: string): Promise<EnvelopeDetailDto> {
  const { data } = await api.post<EnvelopeDetailDto>(`/api/envelopes/${id}/send`)
  return data
}

async function voidEnvelope(id: string): Promise<EnvelopeDetailDto> {
  const { data } = await api.post<EnvelopeDetailDto>(`/api/envelopes/${id}/void`)
  return data
}

async function verifyEnvelope(id: string): Promise<VerifyResultDto> {
  const { data } = await api.get<VerifyResultDto>(`/api/verify/${id}`)
  return data
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

const ceremonyEventLabels: Record<CeremonyEventType, string> = {
  Sent: 'Sent',
  Viewed: 'Viewed',
  ConsentGiven: 'Consent given',
  FieldsCompleted: 'Fields completed',
  Signed: 'Signed',
  Declined: 'Declined',
  EnvelopeCompleted: 'Envelope completed',
  EnvelopeVoided: 'Envelope voided',
}

export default function EnvelopeDetailPage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()
  const [copiedRecipientId, setCopiedRecipientId] = useState<string | null>(null)
  const [verifyResult, setVerifyResult] = useState<VerifyResultDto | null>(null)
  const [verifyError, setVerifyError] = useState<string | null>(null)

  const envelopeQuery = useQuery({
    queryKey: ['envelope', id],
    queryFn: () => fetchEnvelope(id!),
    enabled: !!id,
  })

  const sendMutation = useMutation({
    mutationFn: sendEnvelope,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['envelope', id] }),
  })

  const voidMutation = useMutation({
    mutationFn: voidEnvelope,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['envelope', id] }),
  })

  const verifyMutation = useMutation({
    mutationFn: verifyEnvelope,
    onSuccess: (data) => {
      setVerifyResult(data)
      setVerifyError(null)
    },
    onError: () => {
      setVerifyResult(null)
      setVerifyError('Could not verify this envelope.')
    },
  })

  async function copyLink(recipient: EnvelopeRecipientDto) {
    if (!recipient.ceremonyUrl) return
    const fullUrl = window.location.origin + recipient.ceremonyUrl
    try {
      await navigator.clipboard.writeText(fullUrl)
      setCopiedRecipientId(recipient.id)
      setTimeout(() => setCopiedRecipientId((prev) => (prev === recipient.id ? null : prev)), 2000)
    } catch {
      // Clipboard access can be denied by the browser — the link is still shown on screen to copy manually.
    }
  }

  if (envelopeQuery.isLoading) return <div className="empty-state">Loading envelope…</div>
  if (envelopeQuery.isError || !envelopeQuery.data) return <div className="empty-state">Could not load this envelope.</div>

  const envelope = envelopeQuery.data
  const canSend = envelope.status === 'Draft'
  const canVoid = envelope.status === 'Draft' || envelope.status === 'Sent'
  const activeCeremonyStatuses: RecipientStatus[] = ['Pending', 'Sent', 'Viewed']

  return (
    <section>
      <div className="page-head">
        <div>
          <Link className="crumb" to="/envelopes">&larr; Envelopes</Link>
          <h1>{envelope.name}</h1>
          {envelope.message && <div className="page-sub">{envelope.message}</div>}
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          {canSend && (
            <button type="button" className="btn primary" disabled={sendMutation.isPending} onClick={() => sendMutation.mutate(envelope.id)}>
              {sendMutation.isPending ? 'Sending…' : 'Send now'}
            </button>
          )}
          {canVoid && (
            <button type="button" className="btn danger" disabled={voidMutation.isPending} onClick={() => voidMutation.mutate(envelope.id)}>
              {voidMutation.isPending ? 'Voiding…' : 'Void'}
            </button>
          )}
        </div>
      </div>

      <div className="panel" style={{ margin: 16 }}>
        <div className="panel-body" style={{ display: 'flex', gap: 24, flexWrap: 'wrap', alignItems: 'center' }}>
          <span className="status">{envelope.status}</span>
          <span className="page-sub">Created {formatDate(envelope.createdAt)}</span>
          <span className="page-sub">Sent {formatDate(envelope.sentAt)}</span>
          <span className="page-sub">Completed {formatDate(envelope.completedAt)}</span>
        </div>
      </div>

      <div className="detail-grid" style={{ padding: '0 16px 16px' }}>
        <div className="panel" style={{ margin: 0 }}>
          <h2 className="panel-title">Recipients</h2>
          <div className="panel-body">
            <div className="empty-state" style={{ padding: '0 0 14px', textAlign: 'left' }}>
              Nexus Docs does not send emails yet — copy each link and share it manually.
            </div>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Role</th>
                  <th>Order</th>
                  <th>Status</th>
                  <th>Link</th>
                </tr>
              </thead>
              <tbody>
                {envelope.recipients.map((recipient) => (
                  <tr key={recipient.id}>
                    <td>
                      <div>{recipient.name}</div>
                      <div className="page-sub">{recipient.email}</div>
                    </td>
                    <td>{recipient.role}</td>
                    <td>{recipient.signingOrder}</td>
                    <td><span className="status">{recipient.status}</span></td>
                    <td>
                      {activeCeremonyStatuses.includes(recipient.status) && recipient.ceremonyUrl ? (
                        <button type="button" className="btn sm" onClick={() => copyLink(recipient)}>
                          {copiedRecipientId === recipient.id ? 'Copied!' : 'Copy link'}
                        </button>
                      ) : (
                        <span className="page-sub">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {envelope.status === 'Completed' && (
            <div className="panel" style={{ margin: 0 }}>
              <h2 className="panel-title">Sealed document</h2>
              <div className="panel-body">
                <div className="kv-list">
                  <div className="kv-row">
                    <span className="kv-key">Sealed version</span>
                    <span className="kv-value">{envelope.sealedDocumentVersionId ?? '—'}</span>
                  </div>
                </div>
                <div className="modal-actions" style={{ marginTop: 12, justifyContent: 'flex-start' }}>
                  <button
                    type="button"
                    className="btn"
                    disabled={verifyMutation.isPending}
                    onClick={() => verifyMutation.mutate(envelope.id)}
                  >
                    {verifyMutation.isPending ? 'Verifying…' : 'Verify'}
                  </button>
                </div>
                {verifyError && <p className="form-error">{verifyError}</p>}
                {verifyResult && (
                  <div className="kv-list" style={{ marginTop: 12 }}>
                    <div className="kv-row">
                      <span className="kv-key">Result</span>
                      <span className="kv-value">{verifyResult.valid ? 'Valid' : 'Invalid'}</span>
                    </div>
                    <div className="kv-row">
                      <span className="kv-key">Completed</span>
                      <span className="kv-value">{formatDate(verifyResult.completedAt)}</span>
                    </div>
                    <div className="kv-row">
                      <span className="kv-key">Recipients</span>
                      <span className="kv-value">{verifyResult.recipientCount}</span>
                    </div>
                    <div className="kv-row">
                      <span className="kv-key">Hash</span>
                      <span className="kv-value" style={{ wordBreak: 'break-all' }}>{verifyResult.sealedBlobHash}</span>
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}

          <div className="panel" style={{ margin: 0 }}>
            <h2 className="panel-title">Audit trail</h2>
            <div className="panel-body">
              {envelope.events.length === 0 && <div className="empty-state">No events yet.</div>}
              {envelope.events.length > 0 && (
                <div className="kv-list">
                  {envelope.events.map((event) => {
                    const recipient = envelope.recipients.find((r) => r.id === event.recipientId)
                    return (
                      <div key={event.id} className="kv-row" style={{ alignItems: 'flex-start' }}>
                        <span className="kv-key">{formatDate(event.occurredAt)}</span>
                        <span className="kv-value" style={{ textAlign: 'right' }}>
                          {ceremonyEventLabels[event.eventType]}
                          {recipient && <div className="page-sub">{recipient.name}</div>}
                        </span>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
