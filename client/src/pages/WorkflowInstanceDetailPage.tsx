import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Domain/Flow/Decision.cs DecisionOutcome. */
type DecisionOutcome = 'Approved' | 'Rejected'

/** Mirrors server/Domain/Flow/ApprovalTask.cs ApprovalTaskStatus. */
type ApprovalTaskStatus = 'Pending' | 'Approved' | 'Rejected' | 'Reassigned'

/** Mirrors server/Domain/Flow/StageInstance.cs StageInstanceStatus. */
type StageInstanceStatus = 'Pending' | 'Active' | 'Approved' | 'Rejected' | 'Skipped'

/** Mirrors server/Domain/Flow/WorkflowInstance.cs WorkflowInstanceStatus. */
type WorkflowInstanceStatus = 'Running' | 'Approved' | 'Rejected' | 'Cancelled'

/** Mirrors server/Models/FlowDtos.cs DecisionDto. */
interface DecisionDto {
  id: string
  approvalTaskId: string
  actorId: string
  outcome: DecisionOutcome
  comment: string | null
  decidedAt: string
}

/** Mirrors server/Models/FlowDtos.cs ApprovalTaskDto. */
interface ApprovalTaskDto {
  id: string
  assigneeId: string
  originalAssigneeId: string | null
  status: ApprovalTaskStatus
  createdAt: string
  completedAt: string | null
  decisions: DecisionDto[]
}

/** Mirrors server/Models/FlowDtos.cs StageInstanceDto. */
interface StageInstanceDto {
  id: string
  stageDefinitionId: string
  stageName: string | null
  status: StageInstanceStatus
  startedAt: string | null
  dueAt: string | null
  completedAt: string | null
  tasks: ApprovalTaskDto[]
}

/** Mirrors server/Models/FlowDtos.cs CommentDto. */
interface CommentDto {
  id: string
  authorId: string
  body: string
  createdAt: string
}

/** Mirrors server/Models/FlowDtos.cs WorkflowInstanceDetailDto. */
interface WorkflowInstanceDetailDto {
  id: string
  workflowDefinitionId: string
  subjectType: string
  subjectId: string
  initiatorId: string
  status: WorkflowInstanceStatus
  currentStageDefinitionId: string | null
  startedAt: string
  completedAt: string | null
  stages: StageInstanceDto[]
  comments: CommentDto[]
}

async function fetchInstance(id: string): Promise<WorkflowInstanceDetailDto> {
  const { data } = await api.get<WorkflowInstanceDetailDto>(`/api/workflow-instances/${id}`)
  return data
}

async function addComment(id: string, body: string): Promise<CommentDto> {
  const { data } = await api.post<CommentDto>(`/api/workflow-instances/${id}/comments`, { body })
  return data
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

export default function WorkflowInstanceDetailPage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()
  const [commentBody, setCommentBody] = useState('')
  const [commentError, setCommentError] = useState<string | null>(null)

  const instanceQuery = useQuery({
    queryKey: ['workflowInstance', id],
    queryFn: () => fetchInstance(id!),
    enabled: !!id,
  })

  const commentMutation = useMutation({
    mutationFn: (body: string) => addComment(id!, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflowInstance', id] })
      setCommentBody('')
      setCommentError(null)
    },
    onError: () => setCommentError('Could not post comment. Please try again.'),
  })

  function handleCommentSubmit(event: FormEvent) {
    event.preventDefault()
    if (!commentBody.trim()) {
      setCommentError('Comment cannot be empty.')
      return
    }
    setCommentError(null)
    commentMutation.mutate(commentBody.trim())
  }

  const instance = instanceQuery.data

  return (
    <section>
      <div className="page-head">
        <div>
          <Link className="crumb" to="/approvals">&larr; Approvals</Link>
          <h1>{instance ? `${instance.subjectType} approval` : 'Workflow instance'}</h1>
          {instance && <div className="page-sub">Started {formatDate(instance.startedAt)}</div>}
        </div>
        {instance && <span className="status">{instance.status}</span>}
      </div>

      {instanceQuery.isLoading && <div className="empty-state">Loading workflow…</div>}
      {instanceQuery.isError && <div className="empty-state">Could not load this workflow instance.</div>}

      {instance && (
        <>
          <div className="panel" style={{ padding: 16 }}>
            <h2 style={{ marginTop: 0 }}>Stages</h2>
            {instance.stages.length === 0 && <div className="empty-state">No stages recorded.</div>}
            <div className="flex flex-col gap-3">
              {instance.stages.map((stage) => (
                <div
                  key={stage.id}
                  className="tile"
                  style={{
                    borderColor: stage.stageDefinitionId === instance.currentStageDefinitionId ? 'var(--accent)' : undefined,
                  }}
                >
                  <div className="flex items-center gap-2" style={{ justifyContent: 'space-between' }}>
                    <strong>{stage.stageName ?? 'Stage'}</strong>
                    <span className="status">{stage.status}</span>
                  </div>
                  <div className="page-sub">
                    Started {formatDate(stage.startedAt)} · Due {formatDate(stage.dueAt)} · Completed {formatDate(stage.completedAt)}
                  </div>

                  {stage.tasks.length > 0 && (
                    <table className="data-table" style={{ marginTop: 10 }}>
                      <thead>
                        <tr>
                          <th>Assignee</th>
                          <th>Status</th>
                          <th>Decision</th>
                        </tr>
                      </thead>
                      <tbody>
                        {stage.tasks.map((task) => (
                          <tr key={task.id}>
                            <td>{task.assigneeId.slice(0, 8)}</td>
                            <td><span className="status">{task.status}</span></td>
                            <td>
                              {task.decisions.length === 0 && '—'}
                              {task.decisions.map((d) => (
                                <div key={d.id}>
                                  {d.outcome} — {formatDate(d.decidedAt)}
                                  {d.comment && <div className="page-sub">"{d.comment}"</div>}
                                </div>
                              ))}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>
              ))}
            </div>
          </div>

          <div className="panel" style={{ padding: 16 }}>
            <h2 style={{ marginTop: 0 }}>Comments</h2>
            {instance.comments.length === 0 && <div className="empty-state">No comments yet.</div>}
            <div className="flex flex-col gap-2">
              {instance.comments.map((comment) => (
                <div key={comment.id} className="tile">
                  <div className="page-sub">{comment.authorId.slice(0, 8)} · {formatDate(comment.createdAt)}</div>
                  <div>{comment.body}</div>
                </div>
              ))}
            </div>

            <form className="form-grid" onSubmit={handleCommentSubmit} style={{ marginTop: 12 }}>
              <div>
                <label className="field-label" htmlFor="comment-body">Add a comment</label>
                <input
                  id="comment-body"
                  className="field-input"
                  value={commentBody}
                  onChange={(e) => setCommentBody(e.target.value)}
                  placeholder="Write a comment…"
                />
              </div>
              {commentError && <p className="form-error">{commentError}</p>}
              <div className="modal-actions">
                <button type="submit" className="btn primary" disabled={commentMutation.isPending}>
                  {commentMutation.isPending ? 'Posting…' : 'Post comment'}
                </button>
              </div>
            </form>
          </div>
        </>
      )}
    </section>
  )
}
