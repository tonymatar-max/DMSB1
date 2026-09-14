import type { ReactNode } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import AppShell from './components/AppShell';
import LoginPage from './pages/LoginPage';
import CabinetsPage from './pages/CabinetsPage';
import CabinetDetailPage from './pages/CabinetDetailPage';
import DocumentDetailPage from './pages/DocumentDetailPage';

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
        <Route
          path="/*"
          element={
            <RequireAuth>
              <AppShell>
                <Routes>
                  <Route path="/cabinets" element={<CabinetsPage />} />
                  <Route path="/cabinets/:id" element={<CabinetDetailPage />} />
                  <Route path="/documents/:id" element={<DocumentDetailPage />} />
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
