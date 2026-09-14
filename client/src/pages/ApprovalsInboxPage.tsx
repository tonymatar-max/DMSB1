import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Models/FlowDtos.cs InboxItemDto. */
interface InboxItemDto {
  approvalTaskId: string
  stageInstanceId: string
  workflowInstanceId: string
  subjectType: string
  subjectId: string
  stageName: string | null
  originalAssigneeId: string | null
  createdAt: string
  dueAt: string | null
  isOverdue: boolean
  pendingHours: number
}

/** Mirrors server/Domain/Flow/Decision.cs DecisionOutcome. */
type DecisionOutcome = 'Approved' | 'Rejected'

interface DecideForm {
  approvalTaskId: string
  outcome: DecisionOutcome
  comment?: string
}

async function fetchInbox(): Promise<InboxItemDto[]> {
  const { data } = await api.get<InboxItemDto[]>('/api/workflow-instances/inbox')
  return data
}

async function decide(form: DecideForm) {
  const { data } = await api.post(`/api/approval-tasks/${form.approvalTaskId}/decide`, {
    outcome: form.outcome,
    comment: form.comment || null,
  })
  return data
}

function formatDate(value: string | null): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

export default function ApprovalsInboxPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [rejectingId, setRejectingId] = useState<string | null>(null)
  const [rejectComment, setRejectComment] = useState('')
  const [rejectError, setRejectError] = useState<string | null>(null)

  const inboxQuery = useQuery({ queryKey: ['approvalsInbox'], queryFn: fetchInbox })

  const decideMutation = useMutation({
    mutationFn: decide,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['approvalsInbox'] })
      setRejectingId(null)
      setRejectComment('')
      setRejectError(null)
    },
  })

  function handleApprove(taskId: string) {
    decideMutation.mutate({ approvalTaskId: taskId, outcome: 'Approved' })
  }

  function openReject(taskId: string) {
    setRejectingId(taskId)
    setRejectComment('')
    setRejectError(null)
  }

  function submitReject(taskId: string) {
    if (!rejectComment.trim()) {
      setRejectError('A comment is required to reject.')
      return
    }
    decideMutation.mutate({ approvalTaskId: taskId, outcome: 'Rejected', comment: rejectComment.trim() })
  }

  const now = new Date()

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Approvals</h1>
          <div className="page-sub">Pending approval tasks assigned to you.</div>
        </div>
      </div>

      {inboxQuery.isLoading && <div className="empty-state">Loading approvals…</div>}
      {inboxQuery.isError && <div className="empty-state">Could not load approvals.</div>}

      {inboxQuery.data && inboxQuery.data.length === 0 && (
        <div className="empty-state">You have no pending approvals.</div>
      )}

      {inboxQuery.data && inboxQuery.data.length > 0 && (
        <div className="panel">
          <table className="data-table">
            <thead>
              <tr>
                <th>Subject</th>
                <th>Stage</th>
                <th>Due</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {inboxQuery.data.map((item) => {
                const overdue = item.isOverdue || (item.dueAt !== null && new Date(item.dueAt) <= now)
                const isRejecting = rejectingId === item.approvalTaskId
                return (
                  <tr key={item.approvalTaskId}>
                    <td>
                      <button
                        type="button"
                        className="crumb"
                        style={{ padding: 0, background: 'none', border: 'none', cursor: 'pointer' }}
                        onClick={() => navigate(`/workflow-instances/${item.workflowInstanceId}`)}
                      >
                        {item.subjectType} — {item.subjectId.slice(0, 8)}
                      </button>
                    </td>
                    <td>{item.stageName ?? '—'}</td>
                    <td style={overdue ? { color: 'var(--danger)', fontWeight: 600 } : undefined}>
                      {formatDate(item.dueAt)}
                    </td>
                    <td>
                      {!isRejecting && (
                        <div className="flex gap-2">
                          <button
                            type="button"
                            className="btn primary"
                            disabled={decideMutation.isPending}
                            onClick={() => handleApprove(item.approvalTaskId)}
                          >
                            Approve
                          </button>
                          <button
                            type="button"
                            className="btn"
                            disabled={decideMutation.isPending}
                            onClick={() => openReject(item.approvalTaskId)}
                          >
                            Reject
                          </button>
                        </div>
                      )}
                      {isRejecting && (
                        <div className="flex flex-col gap-2" style={{ minWidth: 220 }}>
                          <input
                            className="field-input"
                            placeholder="Reason for rejection (required)"
                            value={rejectComment}
                            onChange={(e) => setRejectComment(e.target.value)}
                            autoFocus
                          />
                          {rejectError && <p className="form-error">{rejectError}</p>}
                          <div className="flex gap-2">
                            <button
                              type="button"
                              className="btn primary"
                              disabled={decideMutation.isPending}
                              onClick={() => submitReject(item.approvalTaskId)}
                            >
                              {decideMutation.isPending ? 'Submitting…' : 'Submit rejection'}
                            </button>
                            <button
                              type="button"
                              className="btn"
                              onClick={() => {
                                setRejectingId(null)
                                setRejectError(null)
                              }}
                            >
                              Cancel
                            </button>
                          </div>
                        </div>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
