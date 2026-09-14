import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Flow/StageDefinition.cs ApprovalMode. */
type ApprovalMode = 'All' | 'Any' | 'Quorum'

/** Mirrors server/Domain/Flow/ApproverSpec.cs ApproverKind. */
type ApproverKind = 'NamedUser' | 'Role' | 'Group' | 'ManagerOfInitiator' | 'OwnerOfLinkedErpObject' | 'FieldExpression'

/** Mirrors server/Models/FlowDtos.cs WorkflowDefinitionSummaryDto. */
interface WorkflowDefinitionSummaryDto {
  id: string
  workflowFamilyId: string
  name: string
  documentTypeId: string | null
  version: number
  isActive: boolean
  createdAt: string
}

/** Mirrors server/Models/FlowDtos.cs StageDefinitionDto (subset used by the list view). */
interface StageDefinitionDto {
  id: string
  name: string
  sortOrder: number
  approvalMode: ApprovalMode
  quorumCount: number | null
  slaHours: number | null
}

/** Mirrors server/Models/FlowDtos.cs WorkflowDefinitionDetailDto (subset). */
interface WorkflowDefinitionDetailDto {
  id: string
  name: string
  version: number
  isActive: boolean
  stages: StageDefinitionDto[]
}

/** Draft shape mirrored client-side while the wizard builds up the request. */
interface DraftApprover {
  kind: ApproverKind
  namedUserId: string
  roleId: string
  managerHierarchyLevels: string
  erpOwnerField: string
  fieldExpression: string
}

interface DraftStage {
  name: string
  approvalMode: ApprovalMode
  quorumCount: string
  slaHours: string
  approvers: DraftApprover[]
}

/** Mirrors server/Models/FlowDtos.cs ApproverSpecRequest. */
interface ApproverSpecRequestPayload {
  kind: ApproverKind
  namedUserId: string | null
  roleId: string | null
  managerHierarchyLevels: number | null
  erpOwnerField: string | null
  fieldExpression: string | null
}

/** Mirrors server/Models/FlowDtos.cs StageDefinitionRequest. */
interface StageDefinitionRequestPayload {
  name: string
  sortOrder: number
  approvalMode: ApprovalMode
  quorumCount: number | null
  slaHours: number | null
  approvers: ApproverSpecRequestPayload[]
  outgoingRules: []
}

/** Mirrors server/Models/FlowDtos.cs WorkflowDefinitionRequest. */
interface WorkflowDefinitionRequestPayload {
  name: string
  documentTypeId: string | null
  stages: StageDefinitionRequestPayload[]
}

function newApprover(): DraftApprover {
  return {
    kind: 'NamedUser',
    namedUserId: '',
    roleId: '',
    managerHierarchyLevels: '1',
    erpOwnerField: '',
    fieldExpression: '',
  }
}

function newStage(): DraftStage {
  return {
    name: '',
    approvalMode: 'All',
    quorumCount: '',
    slaHours: '',
    approvers: [newApprover()],
  }
}

async function fetchWorkflowDefinitions(): Promise<WorkflowDefinitionSummaryDto[]> {
  const { data } = await api.get<WorkflowDefinitionSummaryDto[]>('/api/workflow-definitions')
  return data
}

async function fetchWorkflowDefinitionDetail(id: string): Promise<WorkflowDefinitionDetailDto> {
  const { data } = await api.get<WorkflowDefinitionDetailDto>(`/api/workflow-definitions/${id}`)
  return data
}

async function createWorkflowDefinition(payload: WorkflowDefinitionRequestPayload): Promise<WorkflowDefinitionDetailDto> {
  const { data } = await api.post<WorkflowDefinitionDetailDto>('/api/workflow-definitions', payload)
  return data
}

export default function WorkflowDefinitionsPage() {
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)

  const definitionsQuery = useQuery({ queryKey: ['workflowDefinitions'], queryFn: fetchWorkflowDefinitions })

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Workflow Designer</h1>
          <div className="page-sub">Define approval workflows: stages, approvers, and routing.</div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New workflow
        </button>
      </div>

      {definitionsQuery.isLoading && <div className="empty-state">Loading workflow definitions…</div>}
      {definitionsQuery.isError && <div className="empty-state">Could not load workflow definitions.</div>}

      {definitionsQuery.data && (
        <div className="panel">
          {definitionsQuery.data.length === 0 && (
            <div className="empty-state">No workflow definitions yet. Create the first one to get started.</div>
          )}
          {definitionsQuery.data.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Version</th>
                  <th>Status</th>
                  <th>Stages</th>
                </tr>
              </thead>
              <tbody>
                {definitionsQuery.data.map((def) => (
                  <WorkflowDefinitionRow key={def.id} definition={def} />
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {isCreateOpen && (
        <NewWorkflowModal
          onClose={() => setCreateOpen(false)}
          onCreated={() => {
            setCreateOpen(false)
            queryClient.invalidateQueries({ queryKey: ['workflowDefinitions'] })
          }}
        />
      )}
    </section>
  )
}

function WorkflowDefinitionRow({ definition }: { definition: WorkflowDefinitionSummaryDto }) {
  const detailQuery = useQuery({
    queryKey: ['workflowDefinitionDetail', definition.id],
    queryFn: () => fetchWorkflowDefinitionDetail(definition.id),
  })

  return (
    <tr>
      <td>{definition.name}</td>
      <td>v{definition.version}</td>
      <td><span className="status">{definition.isActive ? 'Active' : 'Inactive'}</span></td>
      <td>{detailQuery.data ? detailQuery.data.stages.length : '…'}</td>
    </tr>
  )
}

interface NewWorkflowModalProps {
  onClose: () => void
  onCreated: () => void
}

function NewWorkflowModal({ onClose, onCreated }: NewWorkflowModalProps) {
  const [name, setName] = useState('')
  const [stages, setStages] = useState<DraftStage[]>([newStage()])
  const [formError, setFormError] = useState<string | null>(null)

  const createMutation = useMutation({
    mutationFn: createWorkflowDefinition,
    onSuccess: onCreated,
    onError: () => setFormError('Could not create the workflow. Please check the stages and try again.'),
  })

  function addStage() {
    setStages((prev) => [...prev, newStage()])
  }

  function removeStage(index: number) {
    setStages((prev) => prev.filter((_, i) => i !== index))
  }

  function updateStage(index: number, patch: Partial<DraftStage>) {
    setStages((prev) => prev.map((stage, i) => (i === index ? { ...stage, ...patch } : stage)))
  }

  function addApprover(stageIndex: number) {
    setStages((prev) =>
      prev.map((stage, i) => (i === stageIndex ? { ...stage, approvers: [...stage.approvers, newApprover()] } : stage))
    )
  }

  function removeApprover(stageIndex: number, approverIndex: number) {
    setStages((prev) =>
      prev.map((stage, i) =>
        i === stageIndex ? { ...stage, approvers: stage.approvers.filter((_, ai) => ai !== approverIndex) } : stage
      )
    )
  }

  function updateApprover(stageIndex: number, approverIndex: number, patch: Partial<DraftApprover>) {
    setStages((prev) =>
      prev.map((stage, i) =>
        i === stageIndex
          ? {
              ...stage,
              approvers: stage.approvers.map((approver, ai) => (ai === approverIndex ? { ...approver, ...patch } : approver)),
            }
          : stage
      )
    )
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()

    if (!name.trim()) {
      setFormError('Workflow name is required.')
      return
    }
    if (stages.length === 0) {
      setFormError('Add at least one stage.')
      return
    }
    for (const stage of stages) {
      if (!stage.name.trim()) {
        setFormError('Every stage needs a name.')
        return
      }
      if (stage.approvalMode === 'Quorum' && !stage.quorumCount.trim()) {
        setFormError(`Stage "${stage.name}" uses Quorum mode and needs a quorum count.`)
        return
      }
      if (stage.approvers.length === 0) {
        setFormError(`Stage "${stage.name}" needs at least one approver.`)
        return
      }
    }

    setFormError(null)

    const payload: WorkflowDefinitionRequestPayload = {
      name: name.trim(),
      documentTypeId: null,
      stages: stages.map((stage, index) => ({
        name: stage.name.trim(),
        sortOrder: index,
        approvalMode: stage.approvalMode,
        quorumCount: stage.approvalMode === 'Quorum' && stage.quorumCount.trim() ? Number(stage.quorumCount) : null,
        slaHours: stage.slaHours.trim() ? Number(stage.slaHours) : null,
        approvers: stage.approvers.map((approver) => ({
          kind: approver.kind,
          namedUserId: approver.kind === 'NamedUser' ? approver.namedUserId.trim() || null : null,
          roleId: approver.kind === 'Role' ? approver.roleId.trim() || null : null,
          managerHierarchyLevels:
            approver.kind === 'ManagerOfInitiator' && approver.managerHierarchyLevels.trim()
              ? Number(approver.managerHierarchyLevels)
              : null,
          erpOwnerField: approver.kind === 'OwnerOfLinkedErpObject' ? approver.erpOwnerField.trim() || null : null,
          fieldExpression: approver.kind === 'FieldExpression' ? approver.fieldExpression.trim() || null : null,
        })),
        outgoingRules: [],
      })),
    }

    createMutation.mutate(payload)
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel wide" onClick={(e) => e.stopPropagation()}>
        <h2>New workflow</h2>
        <p className="modal-sub">Name the workflow, then add stages and their approvers.</p>
        <form className="form-grid" onSubmit={handleSubmit}>
          <div>
            <label className="field-label" htmlFor="workflow-name">Workflow name</label>
            <input
              id="workflow-name"
              className="field-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Purchase Requisition Approval"
              autoFocus
            />
          </div>

          <div className="stage-list">
            {stages.map((stage, stageIndex) => (
              <div key={stageIndex} className="tile stage-card">
                <div className="stage-card-head">
                  <span className="field-label" style={{ marginBottom: 0 }}>Stage {stageIndex + 1}</span>
                  {stages.length > 1 && (
                    <button type="button" className="btn danger sm" onClick={() => removeStage(stageIndex)}>
                      Remove stage
                    </button>
                  )}
                </div>

                <div className="form-grid">
                  <div>
                    <label className="field-label" htmlFor={`stage-name-${stageIndex}`}>Name</label>
                    <input
                      id={`stage-name-${stageIndex}`}
                      className="field-input"
                      value={stage.name}
                      onChange={(e) => updateStage(stageIndex, { name: e.target.value })}
                      placeholder="e.g. Manager approval"
                    />
                  </div>

                  <div className="field-row">
                    <div>
                      <label className="field-label" htmlFor={`stage-mode-${stageIndex}`}>Approval mode</label>
                      <select
                        id={`stage-mode-${stageIndex}`}
                        className="field-input"
                        value={stage.approvalMode}
                        onChange={(e) => updateStage(stageIndex, { approvalMode: e.target.value as ApprovalMode })}
                      >
                        <option value="All">All approvers</option>
                        <option value="Any">Any approver</option>
                        <option value="Quorum">Quorum</option>
                      </select>
                    </div>
                    {stage.approvalMode === 'Quorum' && (
                      <div>
                        <label className="field-label" htmlFor={`stage-quorum-${stageIndex}`}>Quorum count</label>
                        <input
                          id={`stage-quorum-${stageIndex}`}
                          className="field-input"
                          type="number"
                          min={1}
                          value={stage.quorumCount}
                          onChange={(e) => updateStage(stageIndex, { quorumCount: e.target.value })}
                        />
                      </div>
                    )}
                    <div>
                      <label className="field-label" htmlFor={`stage-sla-${stageIndex}`}>SLA hours</label>
                      <input
                        id={`stage-sla-${stageIndex}`}
                        className="field-input"
                        type="number"
                        min={0}
                        value={stage.slaHours}
                        onChange={(e) => updateStage(stageIndex, { slaHours: e.target.value })}
                        placeholder="Optional"
                      />
                    </div>
                  </div>

                  <div>
                    <div className="field-label">Approvers</div>
                    <div className="approver-list">
                      {stage.approvers.map((approver, approverIndex) => (
                        <div key={approverIndex} className="approver-row">
                          <select
                            className="field-input"
                            value={approver.kind}
                            onChange={(e) =>
                              updateApprover(stageIndex, approverIndex, { kind: e.target.value as ApproverKind })
                            }
                          >
                            <option value="NamedUser">Named user</option>
                            <option value="Role">Role</option>
                            <option value="Group">Group</option>
                            <option value="ManagerOfInitiator">Manager of initiator</option>
                            <option value="OwnerOfLinkedErpObject">Owner of linked ERP object</option>
                            <option value="FieldExpression">Field expression</option>
                          </select>

                          {approver.kind === 'NamedUser' && (
                            <input
                              className="field-input"
                              placeholder="User email or id"
                              value={approver.namedUserId}
                              onChange={(e) => updateApprover(stageIndex, approverIndex, { namedUserId: e.target.value })}
                            />
                          )}
                          {approver.kind === 'Role' && (
                            <input
                              className="field-input"
                              placeholder="Role name or id"
                              value={approver.roleId}
                              onChange={(e) => updateApprover(stageIndex, approverIndex, { roleId: e.target.value })}
                            />
                          )}
                          {approver.kind === 'Group' && (
                            <input
                              className="field-input"
                              placeholder="Group id"
                              value={approver.roleId}
                              onChange={(e) => updateApprover(stageIndex, approverIndex, { roleId: e.target.value })}
                            />
                          )}
                          {approver.kind === 'ManagerOfInitiator' && (
                            <input
                              className="field-input"
                              type="number"
                              min={1}
                              placeholder="Levels up"
                              value={approver.managerHierarchyLevels}
                              onChange={(e) =>
                                updateApprover(stageIndex, approverIndex, { managerHierarchyLevels: e.target.value })
                              }
                            />
                          )}
                          {approver.kind === 'OwnerOfLinkedErpObject' && (
                            <input
                              className="field-input"
                              placeholder="ERP owner field"
                              value={approver.erpOwnerField}
                              onChange={(e) => updateApprover(stageIndex, approverIndex, { erpOwnerField: e.target.value })}
                            />
                          )}
                          {approver.kind === 'FieldExpression' && (
                            <input
                              className="field-input"
                              placeholder="Field expression"
                              value={approver.fieldExpression}
                              onChange={(e) => updateApprover(stageIndex, approverIndex, { fieldExpression: e.target.value })}
                            />
                          )}

                          {stage.approvers.length > 1 && (
                            <button
                              type="button"
                              className="btn danger sm"
                              onClick={() => removeApprover(stageIndex, approverIndex)}
                            >
                              Remove
                            </button>
                          )}
                        </div>
                      ))}
                    </div>
                    <button type="button" className="btn sm" onClick={() => addApprover(stageIndex)}>
                      + Add approver
                    </button>
                  </div>
                </div>
              </div>
            ))}

            <button type="button" className="btn" onClick={addStage}>
              + Add stage
            </button>
          </div>

          {formError && <p className="form-error">{formError}</p>}
          <div className="modal-actions">
            <button type="button" className="btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn primary" disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Creating…' : 'Create workflow'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
