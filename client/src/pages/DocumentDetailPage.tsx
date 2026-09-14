import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import './pages.css'

/** Mirrors server/Models/DocumentTypeDtos.cs IndexFieldDefinitionDto (fields used here only). */
interface IndexFieldDefinitionDto {
  code: string
  label: string
  sortOrder: number
}

/** Mirrors server/Models/DocumentTypeDtos.cs DocumentTypeDto (fields used here only). */
interface DocumentTypeDto {
  id: string
  cabinetId: string
  name: string
  indexFields: IndexFieldDefinitionDto[]
}

/** Mirrors server/Models/CabinetDtos.cs CabinetDto (fields used here only). */
interface CabinetDto {
  id: string
  name: string
}

/** Mirrors server/Models/DocumentDtos.cs DocumentVersionDto. */
interface DocumentVersionDto {
  id: string
  versionNumber: number
  originalFileName: string
  sizeBytes: number
  authorId: string
  comment: string | null
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

async function fetchDocument(id: string): Promise<DocumentDto> {
  const { data } = await api.get<DocumentDto>(`/api/documents/${id}`)
  return data
}

async function fetchDocumentType(id: string): Promise<DocumentTypeDto> {
  const { data } = await api.get<DocumentTypeDto>(`/api/document-types/${id}`)
  return data
}

async function fetchCabinet(id: string): Promise<CabinetDto> {
  const { data } = await api.get<CabinetDto>(`/api/cabinets/${id}`)
  return data
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export default function DocumentDetailPage() {
  const { id } = useParams<{ id: string }>()

  const documentQuery = useQuery({
    queryKey: ['document', id],
    queryFn: () => fetchDocument(id!),
    enabled: !!id,
  })

  const documentTypeQuery = useQuery({
    queryKey: ['documentType', documentQuery.data?.documentTypeId],
    queryFn: () => fetchDocumentType(documentQuery.data!.documentTypeId),
    enabled: !!documentQuery.data?.documentTypeId,
  })

  const cabinetQuery = useQuery({
    queryKey: ['cabinet', documentQuery.data?.cabinetId],
    queryFn: () => fetchCabinet(documentQuery.data!.cabinetId),
    enabled: !!documentQuery.data?.cabinetId,
  })

  if (documentQuery.isLoading) {
    return <div className="empty-state">Loading document…</div>
  }
  if (documentQuery.isError || !documentQuery.data) {
    return <div className="empty-state">Could not load this document.</div>
  }

  const doc = documentQuery.data
  const fieldLabels = new Map(
    (documentTypeQuery.data?.indexFields ?? []).map((f) => [f.code, f.label]),
  )
  const sortedValues = [...doc.indexValues].sort((a, b) => {
    const orderA = documentTypeQuery.data?.indexFields.find((f) => f.code === a.fieldCode)?.sortOrder ?? 0
    const orderB = documentTypeQuery.data?.indexFields.find((f) => f.code === b.fieldCode)?.sortOrder ?? 0
    return orderA - orderB
  })

  const downloadHref = doc.currentVersionId
    ? `/api/documents/${doc.id}/versions/${doc.currentVersionId}/content`
    : null

  return (
    <section>
      <div className="page-head">
        <div>
          {cabinetQuery.data && (
            <Link className="crumb" to={`/cabinets/${cabinetQuery.data.id}`}>&larr; {cabinetQuery.data.name}</Link>
          )}
          <h1>{doc.currentVersion?.originalFileName ?? 'Document'}</h1>
          <div className="page-sub">{documentTypeQuery.data?.name ?? 'Document type'}</div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span className="status">{doc.status}</span>
          {downloadHref && (
            <a className="btn primary" href={downloadHref} target="_blank" rel="noreferrer">
              Download
            </a>
          )}
        </div>
      </div>

      <div className="detail-grid" style={{ padding: 16 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div className="panel">
            <h2 className="panel-title">Index field values</h2>
            <div className="panel-body">
              {sortedValues.length === 0 ? (
                <div className="empty-state">No index values recorded.</div>
              ) : (
                <div className="kv-list">
                  {sortedValues.map((value) => (
                    <div className="kv-row" key={value.fieldCode}>
                      <span className="kv-key">{fieldLabels.get(value.fieldCode) ?? value.fieldCode}</span>
                      <span className="kv-value">{value.value ?? '—'}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          <div className="panel">
            <h2 className="panel-title">Version history</h2>
            <div className="panel-body">
              {/* GET /api/documents/{id} only returns the current version — there is no
                  list-all-versions endpoint yet, so this shows the current version until one is
                  added (POST /api/documents/{id}/versions already creates further versions). */}
              {doc.currentVersion ? (
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Version</th>
                      <th>File</th>
                      <th>Size</th>
                      <th>Uploaded</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>v{doc.currentVersion.versionNumber}</td>
                      <td>{doc.currentVersion.originalFileName}</td>
                      <td>{formatSize(doc.currentVersion.sizeBytes)}</td>
                      <td>{formatDate(doc.currentVersion.createdAt)}</td>
                    </tr>
                  </tbody>
                </table>
              ) : (
                <div className="empty-state">No versions uploaded yet.</div>
              )}
            </div>
          </div>
        </div>

        <div className="panel">
          <h2 className="panel-title">ERP links</h2>
          <div className="panel-body">
            {/* Phase 2's B1 link picker attaches ErpObjectLink rows here (Business Partner, A/R or
                A/P Invoice, PO, GRPO, etc. — see server/Domain/Erp/ErpObjectLink.cs). */}
            <div className="empty-state">No linked SAP Business One records yet.</div>
          </div>
        </div>
      </div>
    </section>
  )
}
