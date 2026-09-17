import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Capture/IngestSource.cs IngestSourceKind. */
type IngestSourceKind = 'HotFolder' | 'Imap'

/** Mirrors server/Models/CaptureDtos.cs IngestSourceDto. */
interface IngestSourceDto {
  id: string
  kind: IngestSourceKind
  name: string
  isActive: boolean
  hotFolderPath: string | null
  imapHost: string | null
  imapPort: number | null
  imapUseSsl: boolean
  imapUsername: string | null
  imapFolderName: string | null
  hasImapCredentials: boolean
  lastPolledAt: string | null
  createdAt: string
}

interface CreateIngestSourceForm {
  kind: IngestSourceKind
  name: string
  hotFolderPath: string
  imapHost: string
  imapPort: string
  imapUseSsl: boolean
  imapUsername: string
  imapPassword: string
  imapFolderName: string
}

async function fetchSources(): Promise<IngestSourceDto[]> {
  const { data } = await api.get<IngestSourceDto[]>('/api/ingest-sources')
  return data
}

async function createSource(form: CreateIngestSourceForm): Promise<IngestSourceDto> {
  const { data } = await api.post<IngestSourceDto>('/api/ingest-sources', {
    kind: form.kind,
    name: form.name,
    isActive: true,
    hotFolderPath: form.kind === 'HotFolder' ? form.hotFolderPath || null : null,
    imapHost: form.kind === 'Imap' ? form.imapHost || null : null,
    imapPort: form.kind === 'Imap' && form.imapPort ? Number(form.imapPort) : null,
    imapUseSsl: form.kind === 'Imap' ? form.imapUseSsl : null,
    imapUsername: form.kind === 'Imap' ? form.imapUsername || null : null,
    imapPassword: form.kind === 'Imap' ? form.imapPassword || null : null,
    imapFolderName: form.kind === 'Imap' ? form.imapFolderName || 'INBOX' : null,
  })
  return data
}

async function deleteSource(id: string): Promise<void> {
  await api.delete(`/api/ingest-sources/${id}`)
}

function kindLabel(kind: IngestSourceKind): string {
  return kind === 'HotFolder' ? 'Hot folder' : 'IMAP'
}

function formatDate(value: string | null): string {
  if (!value) return 'Never'
  return new Date(value).toLocaleString()
}

const emptyForm: CreateIngestSourceForm = {
  kind: 'HotFolder',
  name: '',
  hotFolderPath: '',
  imapHost: '',
  imapPort: '993',
  imapUseSsl: true,
  imapUsername: '',
  imapPassword: '',
  imapFolderName: 'INBOX',
}

export default function IngestSourcesPage() {
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)
  const [form, setForm] = useState<CreateIngestSourceForm>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)

  const sourcesQuery = useQuery({ queryKey: ['ingestSources'], queryFn: fetchSources })

  const createMutation = useMutation({
    mutationFn: createSource,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ingestSources'] })
      closeCreateModal()
    },
    onError: () => setFormError('Could not save the source. Please try again.'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteSource,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ingestSources'] }),
  })

  function closeCreateModal() {
    setCreateOpen(false)
    setForm(emptyForm)
    setFormError(null)
  }

  function handleCreateSubmit(event: FormEvent) {
    event.preventDefault()
    if (!form.name.trim()) {
      setFormError('Name is required.')
      return
    }
    if (form.kind === 'HotFolder' && !form.hotFolderPath.trim()) {
      setFormError('Folder path is required.')
      return
    }
    if (form.kind === 'Imap' && !form.imapHost.trim()) {
      setFormError('Host is required.')
      return
    }
    setFormError(null)
    createMutation.mutate({
      ...form,
      name: form.name.trim(),
      hotFolderPath: form.hotFolderPath.trim(),
      imapHost: form.imapHost.trim(),
      imapUsername: form.imapUsername.trim(),
      imapFolderName: form.imapFolderName.trim() || 'INBOX',
    })
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Capture</h1>
          <div className="page-sub">
            Ingest sources feed the A/P automation inbox — a watched hot folder or a polled IMAP
            mailbox, either one dropping invoices in for OCR extraction and matching.
          </div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New source
        </button>
      </div>

      {sourcesQuery.isLoading && <div className="empty-state">Loading sources…</div>}
      {sourcesQuery.isError && <div className="empty-state">Could not load sources.</div>}

      {sourcesQuery.data && (
        <div className="panel">
          {sourcesQuery.data.length === 0 && (
            <div className="empty-state">
              No ingest sources yet. Add a hot folder or mailbox so incoming invoices have
              somewhere to land.
            </div>
          )}
          {sourcesQuery.data.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Kind</th>
                  <th>Status</th>
                  <th>Last polled</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {sourcesQuery.data.map((source) => (
                  <tr key={source.id}>
                    <td>
                      <div>{source.name}</div>
                      <div className="page-sub">
                        {source.kind === 'HotFolder' ? source.hotFolderPath : `${source.imapHost ?? ''}${source.imapHost ? ':' : ''}${source.imapPort ?? ''}`}
                      </div>
                    </td>
                    <td><span className="status">{kindLabel(source.kind)}</span></td>
                    <td><span className="status">{source.isActive ? 'Active' : 'Inactive'}</span></td>
                    <td>{formatDate(source.lastPolledAt)}</td>
                    <td>
                      <button
                        type="button"
                        className="btn sm"
                        onClick={() => deleteMutation.mutate(source.id)}
                        disabled={deleteMutation.isPending}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {isCreateOpen && (
        <div className="modal-overlay" onClick={closeCreateModal}>
          <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
            <h2>New ingest source</h2>
            <p className="modal-sub">
              An IMAP password is written straight to the encrypted secret store and never shown
              again — only whether one is set.
            </p>
            <form className="form-grid" onSubmit={handleCreateSubmit}>
              <div>
                <label className="field-label" htmlFor="ingest-kind">Kind</label>
                <select
                  id="ingest-kind"
                  className="field-input"
                  value={form.kind}
                  onChange={(e) => setForm({ ...form, kind: e.target.value as IngestSourceKind })}
                >
                  <option value="HotFolder">Hot folder</option>
                  <option value="Imap">IMAP mailbox</option>
                </select>
              </div>

              <div>
                <label className="field-label" htmlFor="ingest-name">Name</label>
                <input
                  id="ingest-name"
                  className="field-input"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="e.g. AP Invoices - Kuwait"
                  autoFocus
                />
              </div>

              {form.kind === 'HotFolder' && (
                <div>
                  <label className="field-label" htmlFor="ingest-folder-path">Folder path</label>
                  <input
                    id="ingest-folder-path"
                    className="field-input"
                    value={form.hotFolderPath}
                    onChange={(e) => setForm({ ...form, hotFolderPath: e.target.value })}
                    placeholder="C:\NexusDocs\Capture\ap-invoices"
                  />
                  <p className="modal-sub">
                    This folder will be created automatically and watched for new PDF invoices.
                  </p>
                </div>
              )}

              {form.kind === 'Imap' && (
                <>
                  <p className="modal-sub">
                    IMAP ingestion has not been tested against a real mailbox in this environment
                    yet — hot folder ingestion is the proven path.
                  </p>
                  <div className="field-row">
                    <div>
                      <label className="field-label" htmlFor="ingest-imap-host">Host</label>
                      <input
                        id="ingest-imap-host"
                        className="field-input"
                        value={form.imapHost}
                        onChange={(e) => setForm({ ...form, imapHost: e.target.value })}
                        placeholder="imap.example.com"
                      />
                    </div>
                    <div>
                      <label className="field-label" htmlFor="ingest-imap-port">Port</label>
                      <input
                        id="ingest-imap-port"
                        className="field-input"
                        type="number"
                        value={form.imapPort}
                        onChange={(e) => setForm({ ...form, imapPort: e.target.value })}
                        placeholder="993"
                      />
                    </div>
                  </div>
                  <div>
                    <label className="field-label" htmlFor="ingest-imap-ssl">
                      <input
                        id="ingest-imap-ssl"
                        type="checkbox"
                        checked={form.imapUseSsl}
                        onChange={(e) => setForm({ ...form, imapUseSsl: e.target.checked })}
                        style={{ marginRight: 6 }}
                      />
                      Use SSL
                    </label>
                  </div>
                  <div className="field-row">
                    <div>
                      <label className="field-label" htmlFor="ingest-imap-username">Username</label>
                      <input
                        id="ingest-imap-username"
                        className="field-input"
                        value={form.imapUsername}
                        onChange={(e) => setForm({ ...form, imapUsername: e.target.value })}
                        placeholder="ap-invoices@example.com"
                      />
                    </div>
                    <div>
                      <label className="field-label" htmlFor="ingest-imap-password">Password</label>
                      <input
                        id="ingest-imap-password"
                        className="field-input"
                        type="password"
                        value={form.imapPassword}
                        onChange={(e) => setForm({ ...form, imapPassword: e.target.value })}
                      />
                    </div>
                  </div>
                  <div>
                    <label className="field-label" htmlFor="ingest-imap-folder">Folder name</label>
                    <input
                      id="ingest-imap-folder"
                      className="field-input"
                      value={form.imapFolderName}
                      onChange={(e) => setForm({ ...form, imapFolderName: e.target.value })}
                      placeholder="INBOX"
                    />
                  </div>
                </>
              )}

              {formError && <p className="form-error">{formError}</p>}
              <div className="modal-actions">
                <button type="button" className="btn" onClick={closeCreateModal}>Cancel</button>
                <button type="submit" className="btn primary" disabled={createMutation.isPending}>
                  {createMutation.isPending ? 'Saving…' : 'Save source'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </section>
  )
}
