import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Flow/Requisition.cs RequisitionStatus. */
type RequisitionStatus = 'Draft' | 'InApproval' | 'Approved' | 'Rejected' | 'PostedToErp'

/** Mirrors server/Models/RequisitionDtos.cs RequisitionDto. */
interface RequisitionDto {
  id: string
  requestedById: string
  title: string
  description: string | null
  amount: number
  currency: string
  costCentre: string | null
  status: RequisitionStatus
  workflowInstanceId: string | null
  createdAt: string
}

/** Mirrors server/Models/FlowDtos.cs WorkflowDefinitionSummaryDto (subset used here). */
interface WorkflowDefinitionSummaryDto {
  id: string
  name: string
  version: number
  isActive: boolean
}

interface CreateRequisitionForm {
  title: string
  description: string
  amount: string
  currency: string
  costCentre: string
}

async function fetchRequisitions(): Promise<RequisitionDto[]> {
  const { data } = await api.get<RequisitionDto[]>('/api/requisitions')
  return data
}

async function createRequisition(form: CreateRequisitionForm): Promise<RequisitionDto> {
  const { data } = await api.post<RequisitionDto>('/api/requisitions', {
    title: form.title,
    description: form.description || null,
    amount: form.amount ? Number(form.amount) : 0,
    currency: form.currency || 'KWD',
    costCentre: form.costCentre || null,
  })
  return data
}

async function fetchWorkflowDefinitions(): Promise<WorkflowDefinitionSummaryDto[]> {
  const { data } = await api.get<WorkflowDefinitionSummaryDto[]>('/api/workflow-definitions')
  return data
}

async function submitRequisition(params: { id: string; workflowDefinitionId: string }): Promise<RequisitionDto> {
  const { data } = await api.post<RequisitionDto>(`/api/requisitions/${params.id}/submit`, {
    workflowDefinitionId: params.workflowDefinitionId,
  })
  return data
}

function statusLabel(status: RequisitionStatus): string {
  switch (status) {
    case 'InApproval':
      return 'In approval'
    case 'PostedToErp':
      return 'Posted to ERP'
    default:
      return status
  }
}

export default function RequisitionsPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)
  const [submitTargetId, setSubmitTargetId] = useState<string | null>(null)

  const requisitionsQuery = useQuery({ queryKey: ['requisitions'], queryFn: fetchRequisitions })

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('KWD')
  const [costCentre, setCostCentre] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const createMutation = useMutation({
    mutationFn: createRequisition,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requisitions'] })
      closeCreateModal()
    },
    onError: () => setFormError('Could not create the requisition. Please try again.'),
  })

  function closeCreateModal() {
    setCreateOpen(false)
    setTitle('')
    setDescription('')
    setAmount('')
    setCurrency('KWD')
    setCostCentre('')
    setFormError(null)
  }

  function handleCreateSubmit(event: FormEvent) {
    event.preventDefault()
    if (!title.trim()) {
      setFormError('Title is required.')
      return
    }
    setFormError(null)
    createMutation.mutate({ title: title.trim(), description: description.trim(), amount, currency: currency.trim(), costCentre: costCentre.trim() })
  }

  function handleRowClick(req: RequisitionDto) {
    if (req.workflowInstanceId) {
      navigate(`/workflow-instances/${req.workflowInstanceId}`)
    }
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Requisitions</h1>
          <div className="page-sub">Purchase requisitions routed through the FLOW approval engine.</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New requisition
        </button>
      </div>

      {requisitionsQuery.isLoading && <div className="empty-state">Loading requisitions…</div>}
      {requisitionsQuery.isError && <div className="empty-state">Could not load requisitions.</div>}

      {requisitionsQuery.data && (
        <div className="panel">
          {requisitionsQuery.data.length === 0 && (
            <div className="empty-state">No requisitions yet. Create the first one to get started.</div>
          )}
          {requisitionsQuery.data.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Amount</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {requisitionsQuery.data.map((req) => (
                  <tr key={req.id} onClick={() => handleRowClick(req)}>
                    <td>
                      <div>{req.title}</div>
                      {req.costCentre && <div className="page-sub">{req.costCentre}</div>}
                    </td>
                    <td>{req.amount.toLocaleString(undefined, { minimumFractionDigits: 2 })} {req.currency}</td>
                    <td><span className="status">{statusLabel(req.status)}</span></td>
                    <td>
                      {req.status === 'Draft' && (
                        <button
                          type="button"
                          className="btn sm"
                          onClick={(e) => {
                            e.stopPropagation()
                            setSubmitTargetId(req.id)
                          }}
                        >
                          Submit for approval
                        </button>
                      )}
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
            <h2>New requisition</h2>
            <p className="modal-sub">Draft a purchase requisition. You can submit it for approval afterwards.</p>
            <form className="form-grid" onSubmit={handleCreateSubmit}>
              <div>
                <label className="field-label" htmlFor="req-title">Title</label>
                <input
                  id="req-title"
                  className="field-input"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder="e.g. Laptops for new hires"
                  autoFocus
                />
              </div>
              <div>
                <label className="field-label" htmlFor="req-description">Description</label>
                <input
                  id="req-description"
                  className="field-input"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Optional"
                />
              </div>
              <div className="field-row">
                <div>
                  <label className="field-label" htmlFor="req-amount">Amount</label>
                  <input
                    id="req-amount"
                    className="field-input"
                    type="number"
                    min={0}
                    step="0.01"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                  />
                </div>
                <div>
                  <label className="field-label" htmlFor="req-currency">Currency</label>
                  <input
                    id="req-currency"
                    className="field-input"
                    value={currency}
                    onChange={(e) => setCurrency(e.target.value)}
                    placeholder="KWD"
                  />
                </div>
              </div>
              <div>
                <label className="field-label" htmlFor="req-cost-centre">Cost centre</label>
                <input
                  id="req-cost-centre"
                  className="field-input"
                  value={costCentre}
                  onChange={(e) => setCostCentre(e.target.value)}
                  placeholder="Optional"
                />
              </div>
              {formError && <p className="form-error">{formError}</p>}
              <div className="modal-actions">
                <button type="button" className="btn" onClick={closeCreateModal}>Cancel</button>
                <button type="submit" className="btn primary" disabled={createMutation.isPending}>
                  {createMutation.isPending ? 'Creating…' : 'Create requisition'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {submitTargetId && (
        <SubmitModal
          requisitionId={submitTargetId}
          onClose={() => setSubmitTargetId(null)}
          onSubmitted={() => {
            setSubmitTargetId(null)
            queryClient.invalidateQueries({ queryKey: ['requisitions'] })
          }}
        />
      )}
    </section>
  )
}

interface SubmitModalProps {
  requisitionId: string
  onClose: () => void
  onSubmitted: () => void
}

function SubmitModal({ requisitionId, onClose, onSubmitted }: SubmitModalProps) {
  const [workflowDefinitionId, setWorkflowDefinitionId] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const definitionsQuery = useQuery({ queryKey: ['workflowDefinitions'], queryFn: fetchWorkflowDefinitions })

  const submitMutation = useMutation({
    mutationFn: submitRequisition,
    onSuccess: onSubmitted,
    onError: () => setFormError('Could not submit the requisition. Please try again.'),
  })

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!workflowDefinitionId) {
      setFormError('Choose a workflow to route this requisition through.')
      return
    }
    setFormError(null)
    submitMutation.mutate({ id: requisitionId, workflowDefinitionId })
  }

  const activeDefinitions = (definitionsQuery.data ?? []).filter((d) => d.isActive)

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
        <h2>Submit for approval</h2>
        <p className="modal-sub">Pick the workflow definition to route this requisition through.</p>
        <form className="form-grid" onSubmit={handleSubmit}>
          <div>
            <label className="field-label" htmlFor="submit-workflow">Workflow</label>
            {definitionsQuery.isLoading && <div className="empty-state">Loading workflows…</div>}
            {definitionsQuery.data && (
              <select
                id="submit-workflow"
                className="field-input"
                value={workflowDefinitionId}
                onChange={(e) => setWorkflowDefinitionId(e.target.value)}
                autoFocus
              >
                <option value="">Select…</option>
                {activeDefinitions.map((def) => (
                  <option key={def.id} value={def.id}>{def.name} (v{def.version})</option>
                ))}
              </select>
            )}
          </div>
          {formError && <p className="form-error">{formError}</p>}
          <div className="modal-actions">
            <button type="button" className="btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn primary" disabled={submitMutation.isPending}>
              {submitMutation.isPending ? 'Submitting…' : 'Submit'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
