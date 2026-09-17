import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Models/GenDtos.cs DocumentTemplateListItemDto. */
interface DocumentTemplateListItemDto {
  id: string
  name: string
  description: string | null
  sourceObjectType: number | null
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

interface CreateTemplateForm {
  name: string
  description: string
  templateHtml: string
  sourceObjectType: string
  sampleDataJson: string
}

async function fetchTemplates(): Promise<DocumentTemplateListItemDto[]> {
  const { data } = await api.get<DocumentTemplateListItemDto[]>('/api/document-templates')
  return data
}

async function createTemplate(form: CreateTemplateForm): Promise<DocumentTemplateListItemDto> {
  const { data } = await api.post<DocumentTemplateListItemDto>('/api/document-templates', {
    name: form.name,
    description: form.description || null,
    templateHtml: form.templateHtml,
    sourceObjectType: form.sourceObjectType ? Number(form.sourceObjectType) : null,
    sampleDataJson: form.sampleDataJson || null,
  })
  return data
}

async function deleteTemplate(id: string): Promise<void> {
  await api.delete(`/api/document-templates/${id}`)
}

async function previewTemplate(id: string): Promise<Blob> {
  const { data } = await api.post(`/api/document-templates/${id}/preview`, {}, { responseType: 'blob' })
  return data as Blob
}

export default function DocumentTemplatesPage() {
  const queryClient = useQueryClient()
  const [isCreateOpen, setCreateOpen] = useState(false)

  const templatesQuery = useQuery({ queryKey: ['documentTemplates'], queryFn: fetchTemplates })

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [templateHtml, setTemplateHtml] = useState('')
  const [sourceObjectType, setSourceObjectType] = useState('')
  const [sampleDataJson, setSampleDataJson] = useState('')
  const [formError, setFormError] = useState<string | null>(null)

  const createMutation = useMutation({
    mutationFn: createTemplate,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['documentTemplates'] })
      closeCreateModal()
    },
    onError: () => setFormError('Could not save the template. Please try again.'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteTemplate,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['documentTemplates'] }),
  })

  const [previewError, setPreviewError] = useState<Record<string, string>>({})
  const previewMutation = useMutation({
    mutationFn: previewTemplate,
    onSuccess: (blob) => {
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank', 'noopener')
    },
    onError: (_error, id) =>
      setPreviewError((prev) => ({ ...prev, [id]: 'Could not render a preview. Check the template and sample data.' })),
  })

  function closeCreateModal() {
    setCreateOpen(false)
    setName('')
    setDescription('')
    setTemplateHtml('')
    setSourceObjectType('')
    setSampleDataJson('')
    setFormError(null)
  }

  function handleCreateSubmit(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || !templateHtml.trim()) {
      setFormError('Name and template source are required.')
      return
    }
    if (sampleDataJson.trim()) {
      try {
        JSON.parse(sampleDataJson)
      } catch {
        setFormError('Sample data is not valid JSON.')
        return
      }
    }
    setFormError(null)
    createMutation.mutate({
      name: name.trim(),
      description: description.trim(),
      templateHtml,
      sourceObjectType: sourceObjectType.trim(),
      sampleDataJson: sampleDataJson.trim(),
    })
  }

  function handlePreview(id: string) {
    setPreviewError((prev) => ({ ...prev, [id]: '' }))
    previewMutation.mutate(id)
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <h1>Document Templates</h1>
          <div className="page-sub">
            HTML/Scriban templates rendered to PDF from ERP or free-form data (module GEN).
          </div>
        </div>
        <button type="button" className="btn primary" onClick={() => setCreateOpen(true)}>
          New template
        </button>
      </div>

      {templatesQuery.isLoading && <div className="empty-state">Loading templates…</div>}
      {templatesQuery.isError && <div className="empty-state">Could not load templates.</div>}

      {templatesQuery.data && (
        <div className="panel">
          {templatesQuery.data.length === 0 && (
            <div className="empty-state">
              No document templates yet. Create the first one to start generating PDFs from data.
            </div>
          )}
          {templatesQuery.data.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Description</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {templatesQuery.data.map((tpl) => (
                  <tr key={tpl.id}>
                    <td>{tpl.name}</td>
                    <td>{tpl.description || '—'}</td>
                    <td>
                      <span className={`status${tpl.isActive ? ' ok' : ''}`}>
                        {tpl.isActive ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                        <button
                          type="button"
                          className="btn sm"
                          onClick={() => handlePreview(tpl.id)}
                          disabled={previewMutation.isPending && previewMutation.variables === tpl.id}
                        >
                          {previewMutation.isPending && previewMutation.variables === tpl.id
                            ? 'Rendering…'
                            : 'Preview'}
                        </button>
                        <button
                          type="button"
                          className="btn sm"
                          onClick={() => deleteMutation.mutate(tpl.id)}
                          disabled={deleteMutation.isPending}
                        >
                          Remove
                        </button>
                      </div>
                      {previewError[tpl.id] && (
                        <div className="page-sub" style={{ color: 'var(--danger)', marginTop: 4, textAlign: 'right' }}>
                          {previewError[tpl.id]}
                        </div>
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
          <div className="modal-panel wide" onClick={(e) => e.stopPropagation()}>
            <h2>New document template</h2>
            <p className="modal-sub">
              The template source is Scriban (Handlebars-like) syntax rendered to HTML, then to PDF.
              Sample data lets you preview it without a live ERP connection.
            </p>
            <form className="form-grid" onSubmit={handleCreateSubmit}>
              <div className="field-row">
                <div>
                  <label className="field-label" htmlFor="tpl-name">Name</label>
                  <input
                    id="tpl-name"
                    className="field-input"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. Purchase Order"
                    autoFocus
                  />
                </div>
                <div>
                  <label className="field-label" htmlFor="tpl-source-object">B1 object type (optional)</label>
                  <input
                    id="tpl-source-object"
                    className="field-input"
                    type="number"
                    value={sourceObjectType}
                    onChange={(e) => setSourceObjectType(e.target.value)}
                    placeholder="e.g. 22"
                  />
                </div>
              </div>
              <div>
                <label className="field-label" htmlFor="tpl-description">Description</label>
                <input
                  id="tpl-description"
                  className="field-input"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Optional"
                />
              </div>
              <div>
                <label className="field-label" htmlFor="tpl-html">Template source (HTML/Scriban)</label>
                <textarea
                  id="tpl-html"
                  className="code-textarea"
                  value={templateHtml}
                  onChange={(e) => setTemplateHtml(e.target.value)}
                  placeholder={'<h1>{{ order.doc_num }}</h1>\n<p>{{ order.card_name }}</p>'}
                  spellCheck={false}
                />
              </div>
              <div>
                <label className="field-label" htmlFor="tpl-sample-data">Sample data (JSON, optional)</label>
                <textarea
                  id="tpl-sample-data"
                  className="code-textarea sm"
                  value={sampleDataJson}
                  onChange={(e) => setSampleDataJson(e.target.value)}
                  placeholder={'{ "order": { "doc_num": 1001, "card_name": "Acme LLC" } }'}
                  spellCheck={false}
                />
              </div>
              {formError && <p className="form-error">{formError}</p>}
              <div className="modal-actions">
                <button type="button" className="btn" onClick={closeCreateModal}>Cancel</button>
                <button type="submit" className="btn primary" disabled={createMutation.isPending}>
                  {createMutation.isPending ? 'Saving…' : 'Save template'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </section>
  )
}
