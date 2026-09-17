import type { ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import BrandMark from './BrandMark';
import { logout } from '../api/client';

interface AppShellProps {
  children: ReactNode;
}

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `nav-item${isActive ? ' active' : ''}`;

export default function AppShell({ children }: AppShellProps) {
  return (
    <div className="flex min-h-screen flex-col" style={{ background: 'var(--page)' }}>
      <header
        className="flex h-12 flex-none items-center gap-3 px-4"
        style={{ background: 'var(--panel)', borderBottom: '1px solid var(--line)' }}
      >
        <BrandMark size="sm" />
        <div className="product-title">
          <strong>Nexus</strong>
          <span className="product-sub">Docs</span>
        </div>
        <div className="flex-1" />
        <button type="button" className="btn" onClick={logout}>
          Sign out
        </button>
      </header>

      <div className="flex min-h-0 flex-1">
        <nav
          className="flex w-56 flex-none flex-col py-3"
          style={{ background: 'var(--panel)', borderRight: '1px solid var(--line)' }}
        >
          <NavLink to="/cabinets" className={navLinkClass}>
            Cabinets
          </NavLink>
          <NavLink to="/search" className={navLinkClass}>
            Search
          </NavLink>
          <NavLink to="/requisitions" className={navLinkClass}>
            Requisitions
          </NavLink>
          <NavLink to="/workflow-definitions" className={navLinkClass}>
            Workflow Designer
          </NavLink>
          <NavLink to="/approvals" className={navLinkClass}>
            Approvals
          </NavLink>
          <NavLink to="/ingest-sources" className={navLinkClass}>
            Capture
          </NavLink>
          <NavLink to="/erp-connections" className={navLinkClass}>
            ERP Connections
          </NavLink>
          <NavLink to="/envelopes" className={navLinkClass}>
            Sign
          </NavLink>
        </nav>

        <main className="min-w-0 flex-1 overflow-auto">{children}</main>
      </div>
    </div>
  );
}
