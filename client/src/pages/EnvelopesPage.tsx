import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Sign/Envelope.cs EnvelopeStatus. */
type EnvelopeStatus = 'Draft' | 'Sent' | 'Completed' | 'Declined' | 'Voided'

/** Mirrors server/Domain/Sign/Recipient.cs RecipientRole. */
type RecipientRole = 'Signer' | 'Cc'

/** Mirrors server/Domain/Sign/SignatureField.cs SignatureFieldKind. */
type SignatureFieldKind = 'Signature' | 'Initial' | 'DateSigned' | 'Text' | 'Checkbox'

/** Mirrors server/Models/SignDtos.cs EnvelopeListItemDto. */
interface EnvelopeListItemDto {
  id: string
  name: string
  status: EnvelopeStatus
  sourceDocumentId: string
  sourceDocumentFileName: string | null
  recipientCount: number
  createdAt: string
  sentAt: string | null
  completedAt: string | null
}

/** Mirrors server/Models/SignDtos.cs EnvelopeDetailDto (subset used here). */
interface EnvelopeDetailDto {
  id: string
  status: EnvelopeStatus
}

/** Mirrors server/Models/CabinetDtos.cs CabinetDto. */
interface CabinetDto {
  id: string
  name: string
  description: string | null
}

/** Mirrors server/Models/DocumentDtos.cs DocumentVersionDto. */
interface DocumentVersionDto {
  id: string
  versionNumber: number
  originalFileName: string
  sizeBytes: number
  createdAt: string
}

/** Mirrors server/Models/DocumentDtos.cs DocumentDto (subset used here). */
interface DocumentDto {
  id: string
  cabinetId: string
  documentTypeId: string
  status: 'Draft' | 'Active' | 'Superseded' | 'Disposed'
  currentVersion: DocumentVersionDto | null
}

/** Mirrors server/Models/DocumentDtos.cs DocumentSearchResultDto. */
interface DocumentSearchResultDto {
  items: DocumentDto[]
  total: number
  page: number
  pageSize: number
}

interface RecipientDraft {
  email: string
  name: string
  role: RecipientRole
  signingOrder: number
}

interface FieldDraft {
  recipientIndex: number
  kind: SignatureFieldKind
  pageNumber: number
  x: string
  y: string
  width: string
  height: string
}

async function fetchEnvelopes(): Promise<EnvelopeListItemDto[]> {
  const { data } = await api.get<EnvelopeListItemDto[]>('/api/envelopes')
  return data
}

async function fetchCabinets(): Promise<CabinetDto[]> {
  const { data } = await api.get<CabinetDto[]>('/api/cabinets')
  return data
}

async function searchDocuments(cabinetId: string): Promise<DocumentSearchResultDto> {
  const params = new URLSearchParams()
  params.set('cabinetId', cabinetId)
  const { data } = await api.get<DocumentSearchResultDto>(`/api/documents/search?${params.toString()}`)
  return data
}

interface CreateEnvelopeParams {
  sourceDocumentId: string
  name: string
  message: string
  recipients: RecipientDraft[]
  fields: FieldDraft[]
}

async function createEnvelope(params: CreateEnvelopeParams): Promise<EnvelopeDetailDto> {
  const { data } = await api.post<EnvelopeDetailDto>('/api/envelopes', {
    sourceDocumentId: params.sourceDocumentId,
    name: params.name,
    message: params.message || null,
    recipients: params.recipients.map((r) => ({
      email: r.email,
      name: r.name,
      role: r.role,
      signingOrder: r.signingOrder,
    })),
    fields: params.fields.map((f) => ({
      recipientIndex: f.recipientIndex,
      kind: f.kind,
      pageNumber: f.pageNumber,
      x: Number(f.x),
      y: Number(f.y),
      width: Number(f.width),
      height: Number(f.height),
    })),
  })
  return data
}

async function sendEnvelope(id: string): Promise<EnvelopeDetailDto> {
  const { data } = await api.post<EnvelopeDetailDto>(`/api/envelopes/${id}/send`)
  return data
}

function statusLabel(status: EnvelopeStatus): string {
  return status
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

let recipientKeySeq = 0
function newRecipient(): RecipientDraft & { key: number } {
  return { key: recipientKeySeq++, email: '', name: '', role: 'Signer', signingOrder: 1 }
}

export default function EnvelopesPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)
  const [createdEnvelopeId, setCreatedEnvelopeId] = useState<string | null>(null)

  const envelopesQuery = useQuery({ queryKey: ['envelopes'], queryFn: fetchEnvelopes })

  const sendMutation = useMutation({
    mutationFn: sendEnvelope,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['envelopes'] })
      if (createdEnvelopeId) navigate(`/envelopes/${createdEnvelopeId}`)
    },
  })

  function closeCreateModal() {
    setCreateOpen(false)
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Sign</h1>
          <div className="page-sub">Envelopes for signature — create, send, and track signing ceremonies.</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New envelope
        </button>
      </div>

      {envelopesQuery.isLoading && <div className="empty-state">Loading envelopes…</div>}
      {envelopesQuery.isError && <div className="empty-state">Could not load envelopes.</div>}

      {envelopesQuery.data && (
        <div className="panel">
          {envelopesQuery.data.length === 0 && (
            <div className="empty-state">No envelopes yet. Create the first one to get started.</div>
          )}
          {envelopesQuery.data.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Source document</th>
                  <th>Status</th>
                  <th>Recipients</th>
                  <th>Created</th>
                  <th>Sent</th>
                </tr>
              </thead>
              <tbody>
                {envelopesQuery.data.map((env) => (
                  <tr key={env.id} onClick={() => navigate(`/envelopes/${env.id}`)}>
                    <td>{env.name}</td>
                    <td>{env.sourceDocumentFileName ?? '—'}</td>
                    <td><span className="status">{statusLabel(env.status)}</span></td>
                    <td>{env.recipientCount}</td>
                    <td>{formatDate(env.createdAt)}</td>
                    <td>{formatDate(env.sentAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {isCreateOpen && (
        <CreateEnvelopeModal
          onClose={closeCreateModal}
          onCreated={(id) => {
            setCreateOpen(false)
            setCreatedEnvelopeId(id)
            queryClient.invalidateQueries({ queryKey: ['envelopes'] })
          }}
        />
      )}

      {createdEnvelopeId && !isCreateOpen && (
        <div className="modal-overlay" onClick={() => setCreatedEnvelopeId(null)}>
          <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
            <h2>Envelope created</h2>
            <p className="modal-sub">
              The envelope was created as a draft. Send it now to activate the recipients' ceremony links, or open
              it later from the list to send it.
            </p>
            {sendMutation.isError && <p className="form-error">Could not send the envelope. Please try again.</p>}
            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setCreatedEnvelopeId(null)}>
                Not now
              </button>
              <button
                type="button"
                className="btn primary"
                disabled={sendMutation.isPending}
                onClick={() => sendMutation.mutate(createdEnvelopeId)}
              >
                {sendMutation.isPending ? 'Sending…' : 'Send now'}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}

interface CreateEnvelopeModalProps {
  onClose: () => void
  onCreated: (id: string) => void
}

function CreateEnvelopeModal({ onClose, onCreated }: CreateEnvelopeModalProps) {
  const [cabinetId, setCabinetId] = useState('')
  const [sourceDocumentId, setSourceDocumentId] = useState('')
  const [name, setName] = useState('')
  const [message, setMessage] = useState('')
  const [recipients, setRecipients] = useState<(RecipientDraft & { key: number })[]>([newRecipient()])
  const [fields, setFields] = useState<FieldDraft[]>([])
  const [formError, setFormError] = useState<string | null>(null)

  const cabinetsQuery = useQuery({ queryKey: ['cabinets'], queryFn: fetchCabinets })
  const documentsQuery = useQuery({
    queryKey: ['documents', cabinetId],
    queryFn: () => searchDocuments(cabinetId),
    enabled: !!cabinetId,
  })

  const createMutation = useMutation({
    mutationFn: createEnvelope,
    onSuccess: (data) => onCreated(data.id),
    onError: (error: unknown) => {
      const msg =
        (error as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Could not create the envelope. Please try again.'
      setFormError(msg)
    },
  })

  function updateRecipient(index: number, patch: Partial<RecipientDraft>) {
    setRecipients((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  }

  function addRecipient() {
    setRecipients((prev) => [...prev, newRecipient()])
  }

  function removeRecipient(index: number) {
    setRecipients((prev) => prev.filter((_, i) => i !== index))
    setFields((prev) => prev.filter((f) => f.recipientIndex !== index))
  }

  function addField(recipientIndex: number) {
    setFields((prev) => [
      ...prev,
      { recipientIndex, kind: 'Signature', pageNumber: 1, x: '0.1', y: '0.1', width: '0.3', height: '0.08' },
    ])
  }

  function updateField(index: number, patch: Partial<FieldDraft>) {
    setFields((prev) => prev.map((f, i) => (i === index ? { ...f, ...patch } : f)))
  }

  function removeField(index: number) {
    setFields((prev) => prev.filter((_, i) => i !== index))
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!sourceDocumentId) {
      setFormError('Choose a source document.')
      return
    }
    if (!name.trim()) {
      setFormError('Name is required.')
      return
    }
    const cleanRecipients = recipients.filter((r) => r.email.trim() && r.name.trim())
    if (cleanRecipients.length === 0) {
      setFormError('Add at least one recipient with an email and name.')
      return
    }
    const signerIndexes = new Set(
      recipients
        .map((r, i) => ({ r, i }))
        .filter(({ r }) => r.role === 'Signer' && r.email.trim() && r.name.trim())
        .map(({ i }) => i),
    )
    for (const idx of signerIndexes) {
      const hasSignature = fields.some((f) => f.recipientIndex === idx && f.kind === 'Signature')
      if (!hasSignature) {
        setFormError('Every Signer needs at least one Signature field.')
        return
      }
    }
    setFormError(null)
    createMutation.mutate({
      sourceDocumentId,
      name: name.trim(),
      message: message.trim(),
      recipients: cleanRecipients.map((r) => ({ email: r.email.trim(), name: r.name.trim(), role: r.role, signingOrder: r.signingOrder })),
      fields,
    })
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel wide" onClick={(e) => e.stopPropagation()}>
        <h2>New envelope</h2>
        <p className="modal-sub">Pick a source document, name the envelope, and add recipients.</p>
        <form className="form-grid" onSubmit={handleSubmit}>
          <div>
            <label className="field-label" htmlFor="env-cabinet">Cabinet</label>
            <select
              id="env-cabinet"
              className="field-input"
              value={cabinetId}
              onChange={(e) => {
                setCabinetId(e.target.value)
                setSourceDocumentId('')
              }}
            >
              <option value="">Select a cabinet…</option>
              {cabinetsQuery.data?.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>

          {cabinetId && (
            <div>
              <label className="field-label" htmlFor="env-document">Source document</label>
              {documentsQuery.isLoading && <div className="empty-state">Loading documents…</div>}
              {documentsQuery.data && (
                <select
                  id="env-document"
                  className="field-input"
                  value={sourceDocumentId}
                  onChange={(e) => setSourceDocumentId(e.target.value)}
                >
                  <option value="">Select a document…</option>
                  {documentsQuery.data.items.map((doc) => (
                    <option key={doc.id} value={doc.id}>
                      {doc.currentVersion?.originalFileName ?? doc.id}
                    </option>
                  ))}
                </select>
              )}
              {documentsQuery.data && documentsQuery.data.items.length === 0 && (
                <div className="empty-state">No documents in this cabinet.</div>
              )}
            </div>
          )}

          <div>
            <label className="field-label" htmlFor="env-name">Name</label>
            <input
              id="env-name"
              className="field-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. NDA — Acme Corp"
              autoFocus
            />
          </div>

          <div>
            <label className="field-label" htmlFor="env-message">Message</label>
            <input
              id="env-message"
              className="field-input"
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Optional"
            />
          </div>

          <div>
            <label className="field-label">Recipients</label>
            <div className="approver-list">
              {recipients.map((recipient, index) => (
                <div key={recipient.key} className="stage-card tile">
                  <div className="field-row">
                    <div>
                      <label className="field-label">Email</label>
                      <input
                        className="field-input"
                        value={recipient.email}
                        onChange={(e) => updateRecipient(index, { email: e.target.value })}
                        placeholder="name@example.com"
                      />
                    </div>
                    <div>
                      <label className="field-label">Name</label>
                      <input
                        className="field-input"
                        value={recipient.name}
                        onChange={(e) => updateRecipient(index, { name: e.target.value })}
                        placeholder="Full name"
                      />
                    </div>
                  </div>
                  <div className="field-row">
                    <div>
                      <label className="field-label">Role</label>
                      <select
                        className="field-input"
                        value={recipient.role}
                        onChange={(e) => updateRecipient(index, { role: e.target.value as RecipientRole })}
                      >
                        <option value="Signer">Signer</option>
                        <option value="Cc">Cc</option>
                      </select>
                    </div>
                    <div>
                      <label className="field-label">Signing order</label>
                      <input
                        className="field-input"
                        type="number"
                        min={1}
                        value={recipient.signingOrder}
                        onChange={(e) => updateRecipient(index, { signingOrder: Number(e.target.value) || 1 })}
                      />
                    </div>
                  </div>
                  <div className="page-sub">All recipients default to order 1 (parallel signing) — raise the order to require sequential signing.</div>

                  {recipient.role === 'Signer' && (
                    <div>
                      <div className="stage-card-head">
                        <span className="field-label" style={{ marginBottom: 0 }}>Signature fields</span>
                        <button type="button" className="btn sm" onClick={() => addField(index)}>
                          + Add field
                        </button>
                      </div>
                      <div className="page-sub">0,0 is the top-left of the page; 1,1 is the bottom-right.</div>
                      {fields.map((field, fieldIndex) =>
                        field.recipientIndex === index ? (
                          <div key={fieldIndex} className="stage-card tile" style={{ marginTop: 8 }}>
                            <div className="field-row">
                              <div>
                                <label className="field-label">Kind</label>
                                <select
                                  className="field-input"
                                  value={field.kind}
                                  onChange={(e) => updateField(fieldIndex, { kind: e.target.value as SignatureFieldKind })}
                                >
                                  <option value="Signature">Signature</option>
                                  <option value="Initial">Initial</option>
                                  <option value="DateSigned">Date signed</option>
                                  <option value="Text">Text</option>
                                  <option value="Checkbox">Checkbox</option>
                                </select>
                              </div>
                              <div>
                                <label className="field-label">Page</label>
                                <input
                                  className="field-input"
                                  type="number"
                                  min={1}
                                  value={field.pageNumber}
                                  onChange={(e) => updateField(fieldIndex, { pageNumber: Number(e.target.value) || 1 })}
                                />
                              </div>
                            </div>
                            <div className="field-row">
                              <div>
                                <label className="field-label">X</label>
                                <input
                                  className="field-input"
                                  type="number"
                                  min={0}
                                  max={1}
                                  step="0.01"
                                  value={field.x}
                                  onChange={(e) => updateField(fieldIndex, { x: e.target.value })}
                                />
                              </div>
                              <div>
                                <label className="field-label">Y</label>
                                <input
                                  className="field-input"
                                  type="number"
                                  min={0}
                                  max={1}
                                  step="0.01"
                                  value={field.y}
                                  onChange={(e) => updateField(fieldIndex, { y: e.target.value })}
                                />
                              </div>
                            </div>
                            <div className="field-row">
                              <div>
                                <label className="field-label">Width</label>
                                <input
                                  className="field-input"
                                  type="number"
                                  min={0}
                                  max={1}
                                  step="0.01"
                                  value={field.width}
                                  onChange={(e) => updateField(fieldIndex, { width: e.target.value })}
                                />
                              </div>
                              <div>
                                <label className="field-label">Height</label>
                                <input
                                  className="field-input"
                                  type="number"
                                  min={0}
                                  max={1}
                                  step="0.01"
                                  value={field.height}
                                  onChange={(e) => updateField(fieldIndex, { height: e.target.value })}
                                />
                              </div>
                            </div>
                            <div className="modal-actions" style={{ marginTop: 0 }}>
                              <button type="button" className="btn sm danger" onClick={() => removeField(fieldIndex)}>
                                Remove field
                              </button>
                            </div>
                          </div>
                        ) : null,
                      )}
                    </div>
                  )}

                  {recipients.length > 1 && (
                    <div className="modal-actions" style={{ marginTop: 0 }}>
                      <button type="button" className="btn sm danger" onClick={() => removeRecipient(index)}>
                        Remove recipient
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
            <button type="button" className="btn sm" onClick={addRecipient}>
              + Add recipient
            </button>
          </div>

          {formError && <p className="form-error">{formError}</p>}

          <div className="modal-actions">
            <button type="button" className="btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn primary" disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Creating…' : 'Create envelope'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
