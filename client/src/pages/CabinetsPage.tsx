import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Models/CabinetDtos.cs CabinetDto. */
interface CabinetDto {
  id: string
  name: string
  description: string | null
  defaultRetentionPolicyId: string | null
  createdAt: string
}

interface CreateCabinetForm {
  name: string
  description: string
}

async function fetchCabinets(): Promise<CabinetDto[]> {
  const { data } = await api.get<CabinetDto[]>('/api/cabinets')
  return data
}

async function createCabinet(form: CreateCabinetForm): Promise<CabinetDto> {
  const { data } = await api.post<CabinetDto>('/api/cabinets', {
    name: form.name,
    description: form.description || null,
    defaultRetentionPolicyId: null,
  })
  return data
}

export default function CabinetsPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const cabinetsQuery = useQuery({ queryKey: ['cabinets'], queryFn: fetchCabinets })

  const createMutation = useMutation({
    mutationFn: createCabinet,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['cabinets'] })
      closeModal()
    },
    onError: () => setFormError('Could not create the cabinet. Please try again.'),
  })

  function closeModal() {
    setCreateOpen(false)
    setName('')
    setDescription('')
    setFormError(null)
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim()) {
      setFormError('Name is required.')
      return
    }
    setFormError(null)
    createMutation.mutate({ name: name.trim(), description: description.trim() })
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Cabinets</h1>
          <div className="page-sub">Archive cabinets group document types and their documents.</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New cabinet
        </button>
      </div>

      {cabinetsQuery.isLoading && <div className="empty-state">Loading cabinets…</div>}
      {cabinetsQuery.isError && <div className="empty-state">Could not load cabinets.</div>}

      {cabinetsQuery.data && (
        <div className="cabinet-grid">
          {cabinetsQuery.data.map((cabinet) => (
            <button
              key={cabinet.id}
              type="button"
              className="tile cabinet-tile"
              onClick={() => navigate(`/cabinets/${cabinet.id}`)}
            >
              <span className="cabinet-name">{cabinet.name}</span>
              <span className="cabinet-desc">{cabinet.description || 'No description'}</span>
              {/* Document count isn't returned by GET /api/cabinets today — placeholder until a
                  count is added to CabinetDto (or a per-cabinet search count call is wired up). */}
              <span className="cabinet-count">— documents</span>
            </button>
          ))}

          <button type="button" className="tile cabinet-tile new-cabinet" onClick={() => setCreateOpen(true)}>
            + New cabinet
          </button>

          {cabinetsQuery.data.length === 0 && (
            <div className="empty-state">No cabinets yet. Create the first one to get started.</div>
          )}
        </div>
      )}

      {isCreateOpen && (
        <div className="modal-overlay" onClick={closeModal}>
          <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
            <h2>New cabinet</h2>
            <p className="modal-sub">Cabinets hold document types and the documents filed under them.</p>
            <form className="form-grid" onSubmit={handleSubmit}>
              <div>
                <label className="field-label" htmlFor="cabinet-name">Name</label>
                <input
                  id="cabinet-name"
                  className="field-input"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Accounts Payable"
                  autoFocus
                />
              </div>
              <div>
                <label className="field-label" htmlFor="cabinet-description">Description</label>
                <input
                  id="cabinet-description"
                  className="field-input"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Optional"
                />
              </div>
              {formError && <p className="form-error">{formError}</p>}
              <div className="modal-actions">
                <button type="button" className="btn" onClick={closeModal}>Cancel</button>
                <button type="submit" className="btn primary" disabled={createMutation.isPending}>
                  {createMutation.isPending ? 'Creating…' : 'Create cabinet'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </section>
  )
}
