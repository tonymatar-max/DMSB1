import type { ReactNode } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import AppShell from './components/AppShell';
import LoginPage from './pages/LoginPage';
import SigningCeremonyPage from './pages/SigningCeremonyPage';
import CabinetsPage from './pages/CabinetsPage';
import CabinetDetailPage from './pages/CabinetDetailPage';
import DocumentDetailPage from './pages/DocumentDetailPage';
import ApprovalsInboxPage from './pages/ApprovalsInboxPage';
import WorkflowInstanceDetailPage from './pages/WorkflowInstanceDetailPage';
import WorkflowDefinitionsPage from './pages/WorkflowDefinitionsPage';
import RequisitionsPage from './pages/RequisitionsPage';
import ErpConnectionsPage from './pages/ErpConnectionsPage';
import IngestSourcesPage from './pages/IngestSourcesPage';
import EnvelopesPage from './pages/EnvelopesPage';
import EnvelopeDetailPage from './pages/EnvelopeDetailPage';
import IngestItemsPage from './pages/IngestItemsPage';
import IngestItemDetailPage from './pages/IngestItemDetailPage';
import DocumentTemplatesPage from './pages/DocumentTemplatesPage';
import GeneratedDocumentsPage from './pages/GeneratedDocumentsPage';

function RequireAuth({ children }: { children: ReactNode }) {
  const token = localStorage.getItem('accessToken');
  if (!token) {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/sign/:token" element={<SigningCeremonyPage />} />
        <Route
          path="/*"
          element={
            <RequireAuth>
              <AppShell>
                <Routes>
                  <Route path="/cabinets" element={<CabinetsPage />} />
                  <Route path="/cabinets/:id" element={<CabinetDetailPage />} />
                  <Route path="/documents/:id" element={<DocumentDetailPage />} />
                  <Route path="/approvals" element={<ApprovalsInboxPage />} />
                  <Route path="/workflow-instances/:id" element={<WorkflowInstanceDetailPage />} />
                  <Route path="/workflow-definitions" element={<WorkflowDefinitionsPage />} />
                  <Route path="/requisitions" element={<RequisitionsPage />} />
                  <Route path="/erp-connections" element={<ErpConnectionsPage />} />
                  <Route path="/ingest-sources" element={<IngestSourcesPage />} />
                  <Route path="/envelopes" element={<EnvelopesPage />} />
                  <Route path="/envelopes/:id" element={<EnvelopeDetailPage />} />
                  <Route path="/ingest-items" element={<IngestItemsPage />} />
                  <Route path="/ingest-items/:id" element={<IngestItemDetailPage />} />
                  <Route path="/document-templates" element={<DocumentTemplatesPage />} />
                  <Route path="/generated-documents" element={<GeneratedDocumentsPage />} />
                  <Route path="*" element={<Navigate to="/cabinets" replace />} />
                </Routes>
              </AppShell>
            </RequireAuth>
          }
        />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
