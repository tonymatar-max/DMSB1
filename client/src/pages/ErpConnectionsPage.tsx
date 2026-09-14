import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Models/ErpConnectionDtos.cs ErpConnectionDto. */
interface ErpConnectionDto {
  id: string
  systemType: 'SapBusinessOne'
  name: string
  companyDb: string
  userName: string
  gatewayId: string | null
  baseUrl: string | null
  hasCredentials: boolean
  isActive: boolean
  createdAt: string
}

interface CreateErpConnectionForm {
  name: string
  companyDb: string
  userName: string
  password: string
  baseUrl: string
}

async function fetchConnections(): Promise<ErpConnectionDto[]> {
  const { data } = await api.get<ErpConnectionDto[]>('/api/erp-connections')
  return data
}

async function createConnection(form: CreateErpConnectionForm): Promise<ErpConnectionDto> {
  const { data } = await api.post<ErpConnectionDto>('/api/erp-connections', {
    name: form.name,
    companyDb: form.companyDb,
    userName: form.userName,
    password: form.password,
    baseUrl: form.baseUrl || null,
    gatewayId: null,
  })
  return data
}

async function deleteConnection(id: string): Promise<void> {
  await api.delete(`/api/erp-connections/${id}`)
}

export default function ErpConnectionsPage() {
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)

  const connectionsQuery = useQuery({ queryKey: ['erpConnections'], queryFn: fetchConnections })

  const [name, setName] = useState('')
  const [companyDb, setCompanyDb] = useState('')
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [baseUrl, setBaseUrl] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const createMutation = useMutation({
    mutationFn: createConnection,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['erpConnections'] })
      closeCreateModal()
    },
    onError: () => setFormError('Could not save the connection. Please try again.'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteConnection,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['erpConnections'] }),
  })

  function closeCreateModal() {
    setCreateOpen(false)
    setName('')
    setCompanyDb('')
    setUserName('')
    setPassword('')
    setBaseUrl('')
    setFormError(null)
  }

  function handleCreateSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || !companyDb.trim() || !userName.trim() || !password || !baseUrl.trim()) {
      setFormError('All fields are required.')
      return
    }
    setFormError(null)
    createMutation.mutate({
      name: name.trim(),
      companyDb: companyDb.trim(),
      userName: userName.trim(),
      password,
      baseUrl: baseUrl.trim(),
    })
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>ERP Connections</h1>
          <div className="page-sub">
            SAP Business One Service Layer connections. Approved requisitions post here via the
            integration outbox.
          </div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New connection
        </button>
      </div>

      {connectionsQuery.isLoading && <div className="empty-state">Loading connections…</div>}
      {connectionsQuery.isError && <div className="empty-state">Could not load connections.</div>}

      {connectionsQuery.data && (
        <div className="panel">
          {connectionsQuery.data.length === 0 && (
            <div className="empty-state">
              No ERP connections yet. Without one, approved requisitions stay Approved and the
              outbox logs that there's nowhere to post them — add a connection to close the loop.
            </div>
          )}
          {connectionsQuery.data.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Company DB</th>
                  <th>Endpoint</th>
                  <th>Credentials</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {connectionsQuery.data.map((conn) => (
                  <tr key={conn.id}>
                    <td>{conn.name}</td>
                    <td>{conn.companyDb}</td>
                    <td>{conn.baseUrl ?? `via gateway ${conn.gatewayId}`}</td>
                    <td>
                      <span className="status">{conn.hasCredentials ? 'Set' : 'Not set'}</span>
                    </td>
                    <td>
                      <span className="status">{conn.isActive ? 'Active' : 'Inactive'}</span>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn sm"
                        onClick={() => deleteMutation.mutate(conn.id)}
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
            <h2>New ERP connection</h2>
            <p className="modal-sub">
              The password is written straight to the encrypted secret store and never shown again
              — only whether one is set.
            </p>
            <form className="form-grid" onSubmit={handleCreateSubmit}>
              <div>
                <label className="field-label" htmlFor="erp-name">Name</label>
                <input
                  id="erp-name"
                  className="field-input"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. SAP B1 - Kuwait Live"
                  autoFocus
                />
              </div>
              <div className="field-row">
                <div>
                  <label className="field-label" htmlFor="erp-company-db">Company DB</label>
                  <input
                    id="erp-company-db"
                    className="field-input"
                    value={companyDb}
                    onChange={(e) => setCompanyDb(e.target.value)}
                    placeholder="SBODEMOKW"
                  />
                </div>
                <div>
                  <label className="field-label" htmlFor="erp-username">Username</label>
                  <input
                    id="erp-username"
                    className="field-input"
                    value={userName}
                    onChange={(e) => setUserName(e.target.value)}
                    placeholder="manager"
                  />
                </div>
              </div>
              <div>
                <label className="field-label" htmlFor="erp-password">Password</label>
                <input
                  id="erp-password"
                  className="field-input"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>
              <div>
                <label className="field-label" htmlFor="erp-base-url">Service Layer URL</label>
                <input
                  id="erp-base-url"
                  className="field-input"
                  value={baseUrl}
                  onChange={(e) => setBaseUrl(e.target.value)}
                  placeholder="https://b1.customer.com:50000"
                />
              </div>
              {formError && <p className="form-error">{formError}</p>}
              <div className="modal-actions">
                <button type="button" className="btn" onClick={closeCreateModal}>Cancel</button>
                <button type="submit" className="btn primary" disabled={createMutation.isPending}>
                  {createMutation.isPending ? 'Saving…' : 'Save connection'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </section>
  )
}
