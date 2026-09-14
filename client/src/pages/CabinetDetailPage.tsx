import { useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Models/CabinetDtos.cs CabinetDto. */
interface CabinetDto {
  id: string
  name: string
  description: string | null
}

/** Mirrors server/Domain/Archive/IndexFieldDefinition.cs IndexFieldType. */
type IndexFieldType = 'Text' | 'Number' | 'Date' | 'Boolean' | 'Picklist' | 'ErpLookup'

/** Mirrors server/Models/DocumentTypeDtos.cs IndexFieldDefinitionDto. */
interface IndexFieldDefinitionDto {
  id: string
  code: string
  label: string
  fieldType: IndexFieldType
  required: boolean
  filterable: boolean
  picklistOptionsJson: string | null
  erpLookupObjectType: number | null
  sortOrder: number
}

/** Mirrors server/Models/DocumentTypeDtos.cs DocumentTypeDto. */
interface DocumentTypeDto {
  id: string
  cabinetId: string
  name: string
  namingRule: string | null
  createdAt: string
  indexFields: IndexFieldDefinitionDto[]
}

/** Mirrors server/Models/DocumentDtos.cs DocumentVersionDto. */
interface DocumentVersionDto {
  id: string
  versionNumber: number
  originalFileName: string
  sizeBytes: number
  createdAt: string
}

/** Mirrors server/Models/DocumentDtos.cs IndexFieldValueDto. */
interface IndexFieldValueDto {
  fieldCode: string
  value: string | null
}

/** Mirrors server/Models/DocumentDtos.cs DocumentDto. */
interface DocumentDto {
  id: string
  cabinetId: string
  documentTypeId: string
  status: 'Draft' | 'Active' | 'Superseded' | 'Disposed'
  currentVersionId: string | null
  createdAt: string
  updatedAt: string
  currentVersion: DocumentVersionDto | null
  indexValues: IndexFieldValueDto[]
}

/** Mirrors server/Models/DocumentDtos.cs DocumentSearchResultDto. */
interface DocumentSearchResultDto {
  items: DocumentDto[]
  total: number
  page: number
  pageSize: number
}

async function fetchCabinet(cabinetId: string): Promise<CabinetDto> {
  const { data } = await api.get<CabinetDto>(`/api/cabinets/${cabinetId}`)
  return data
}

async function fetchDocumentTypes(cabinetId: string): Promise<DocumentTypeDto[]> {
  const { data } = await api.get<DocumentTypeDto[]>(`/api/cabinets/${cabinetId}/document-types`)
  return data
}

async function searchDocuments(params: URLSearchParams): Promise<DocumentSearchResultDto> {
  const { data } = await api.get<DocumentSearchResultDto>(`/api/documents/search?${params.toString()}`)
  return data
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

export default function CabinetDetailPage() {
  const { id: cabinetId } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [activeTypeId, setActiveTypeId] = useState<string | 'all'>('all')
  const [searchText, setSearchText] = useState('')
  const [isUploadOpen, setUploadOpen] = useState(false)

  const cabinetQuery = useQuery({
    queryKey: ['cabinet', cabinetId],
    queryFn: () => fetchCabinet(cabinetId!),
    enabled: !!cabinetId,
  })

  const documentTypesQuery = useQuery({
    queryKey: ['documentTypes', cabinetId],
    queryFn: () => fetchDocumentTypes(cabinetId!),
    enabled: !!cabinetId,
  })

  const activeType = documentTypesQuery.data?.find((t) => t.id === activeTypeId) ?? null

  const searchParams = useMemo(() => {
    const params = new URLSearchParams()
    if (cabinetId) params.set('cabinetId', cabinetId)
    if (activeTypeId !== 'all') params.set('documentTypeId', activeTypeId)

    // The search endpoint only supports exact match on individual field.{code} values (no free-text
    // "q" param) — see DocumentsController.Search. Apply the search box to every filterable Text
    // field on the selected document type.
    const trimmed = searchText.trim()
    if (trimmed && activeType) {
      for (const field of activeType.indexFields) {
        if (field.filterable && field.fieldType === 'Text') {
          params.set(`field.${field.code}`, trimmed)
        }
      }
    }
    return params
  }, [cabinetId, activeTypeId, activeType, searchText])

  const documentsQuery = useQuery({
    queryKey: ['documents', searchParams.toString()],
    queryFn: () => searchDocuments(searchParams),
    enabled: !!cabinetId,
  })

  function typeNameFor(documentTypeId: string): string {
    return documentTypesQuery.data?.find((t) => t.id === documentTypeId)?.name ?? '—'
  }

  return (
    <section>
      <div className="page-head">
        <div>
          <Link className="crumb" to="/cabinets">&larr; Cabinets</Link>
          <h1>{cabinetQuery.data?.name ?? 'Cabinet'}</h1>
          {cabinetQuery.data?.description && <div className="page-sub">{cabinetQuery.data.description}</div>}
        </div>
        <button
          type="button"
          className="btn primary"
          onClick={() => setUploadOpen(true)}
          disabled={!documentTypesQuery.data?.length}
        >
          Upload
        </button>
      </div>

      <div className="tabs">
        <button
          type="button"
          className={`tab ${activeTypeId === 'all' ? 'active' : ''}`}
          onClick={() => setActiveTypeId('all')}
        >
          All types
        </button>
        {documentTypesQuery.data?.map((type) => (
          <button
            key={type.id}
            type="button"
            className={`tab ${activeTypeId === type.id ? 'active' : ''}`}
            onClick={() => setActiveTypeId(type.id)}
          >
            {type.name}
          </button>
        ))}
      </div>

      <div className="toolbar">
        <input
          className="field-input search-input"
          placeholder={activeType ? `Search ${activeType.name}…` : 'Select a document type to search its fields'}
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
          disabled={!activeType}
        />
        <span className="spacer" />
        {documentsQuery.data && (
          <span className="page-sub">{documentsQuery.data.total} document(s)</span>
        )}
      </div>

      <div className="panel">
        {documentsQuery.isLoading && <div className="empty-state">Loading documents…</div>}
        {documentsQuery.isError && <div className="empty-state">Could not load documents.</div>}
        {documentsQuery.data && documentsQuery.data.items.length === 0 && (
          <div className="empty-state">No documents match this view yet.</div>
        )}
        {documentsQuery.data && documentsQuery.data.items.length > 0 && (
          <table className="data-table">
            <thead>
              <tr>
                <th>Name / type</th>
                <th>Status</th>
                <th>Current version</th>
                <th>Updated</th>
                <th>ERP link</th>
              </tr>
            </thead>
            <tbody>
              {documentsQuery.data.items.map((doc) => (
                <tr key={doc.id} onClick={() => navigate(`/documents/${doc.id}`)}>
                  <td>
                    <div>{doc.currentVersion?.originalFileName ?? doc.id}</div>
                    <div className="page-sub">{typeNameFor(doc.documentTypeId)}</div>
                  </td>
                  <td><span className="status">{doc.status}</span></td>
                  <td>{doc.currentVersion ? `v${doc.currentVersion.versionNumber}` : '—'}</td>
                  <td>{formatDate(doc.updatedAt)}</td>
                  <td>
                    {/* ErpObjectLink isn't included on DocumentDto yet — placeholder until Phase 2's
                        link picker exposes links here. */}
                    <span className="badge-neutral">Not linked</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {isUploadOpen && cabinetId && documentTypesQuery.data && (
        <UploadModal
          cabinetId={cabinetId}
          documentTypes={documentTypesQuery.data}
          onClose={() => setUploadOpen(false)}
          onUploaded={() => {
            setUploadOpen(false)
            queryClient.invalidateQueries({ queryKey: ['documents'] })
          }}
        />
      )}
    </section>
  )
}

interface UploadModalProps {
  cabinetId: string
  documentTypes: DocumentTypeDto[]
  onClose: () => void
  onUploaded: () => void
}

function UploadModal({ cabinetId, documentTypes, onClose, onUploaded }: UploadModalProps) {
  const [documentTypeId, setDocumentTypeId] = useState(documentTypes[0]?.id ?? '')
  const [fieldValues, setFieldValues] = useState<Record<string, string>>({})
  const [file, setFile] = useState<File | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const selectedType = documentTypes.find((t) => t.id === documentTypeId) ?? null

  const uploadMutation = useMutation({
    mutationFn: async () => {
      const formData = new FormData()
      formData.append('cabinetId', cabinetId)
      formData.append('documentTypeId', documentTypeId)
      formData.append('indexValues', JSON.stringify(fieldValues))
      if (file) formData.append('file', file)
      const { data } = await api.post('/api/documents', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      return data
    },
    onSuccess: onUploaded,
    onError: (error: unknown) => {
      const message =
        (error as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Upload failed. Please check the required fields and try again.'
      setFormError(message)
    },
  })

  function handleTypeChange(newTypeId: string) {
    setDocumentTypeId(newTypeId)
    setFieldValues({})
  }

  function setFieldValue(code: string, value: string) {
    setFieldValues((prev) => ({ ...prev, [code]: value }))
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!documentTypeId) {
      setFormError('Choose a document type.')
      return
    }
    if (!file) {
      setFormError('Choose a file to upload.')
      return
    }
    const missing = (selectedType?.indexFields ?? [])
      .filter((f) => f.required && !fieldValues[f.code]?.trim())
      .map((f) => f.label)
    if (missing.length > 0) {
      setFormError(`Missing required field(s): ${missing.join(', ')}`)
      return
    }
    setFormError(null)
    uploadMutation.mutate()
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
        <h2>Upload document</h2>
        <p className="modal-sub">Pick a document type, fill in its index fields, and choose a file.</p>
        <form className="form-grid" onSubmit={handleSubmit}>
          <div>
            <label className="field-label" htmlFor="upload-type">Document type</label>
            <select
              id="upload-type"
              className="field-input"
              value={documentTypeId}
              onChange={(e) => handleTypeChange(e.target.value)}
            >
              {documentTypes.map((type) => (
                <option key={type.id} value={type.id}>{type.name}</option>
              ))}
            </select>
          </div>

          {selectedType?.indexFields
            .slice()
            .sort((a, b) => a.sortOrder - b.sortOrder)
            .map((field) => (
              <IndexFieldInput
                key={field.id}
                field={field}
                value={fieldValues[field.code] ?? ''}
                onChange={(value) => setFieldValue(field.code, value)}
              />
            ))}

          <div>
            <label className="field-label" htmlFor="upload-file">File</label>
            <input
              id="upload-file"
              className="field-input"
              type="file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </div>

          {formError && <p className="form-error">{formError}</p>}

          <div className="modal-actions">
            <button type="button" className="btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn primary" disabled={uploadMutation.isPending}>
              {uploadMutation.isPending ? 'Uploading…' : 'Upload'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

interface IndexFieldInputProps {
  field: IndexFieldDefinitionDto
  value: string
  onChange: (value: string) => void
}

function IndexFieldInput({ field, value, onChange }: IndexFieldInputProps) {
  const label = `${field.label}${field.required ? ' *' : ''}`

  if (field.fieldType === 'Boolean') {
    return (
      <div>
        <label className="field-label" htmlFor={`field-${field.code}`}>{label}</label>
        <select
          id={`field-${field.code}`}
          className="field-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
        >
          <option value="">—</option>
          <option value="true">Yes</option>
          <option value="false">No</option>
        </select>
      </div>
    )
  }

  if (field.fieldType === 'Picklist') {
    let options: string[] = []
    try {
      options = field.picklistOptionsJson ? JSON.parse(field.picklistOptionsJson) : []
    } catch {
      options = []
    }
    return (
      <div>
        <label className="field-label" htmlFor={`field-${field.code}`}>{label}</label>
        <select
          id={`field-${field.code}`}
          className="field-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
        >
          <option value="">Select…</option>
          {options.map((option) => (
            <option key={option} value={option}>{option}</option>
          ))}
        </select>
      </div>
    )
  }

  const inputType = field.fieldType === 'Number' ? 'number' : field.fieldType === 'Date' ? 'date' : 'text'

  return (
    <div>
      <label className="field-label" htmlFor={`field-${field.code}`}>{label}</label>
      <input
        id={`field-${field.code}`}
        className="field-input"
        type={inputType}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  )
}
