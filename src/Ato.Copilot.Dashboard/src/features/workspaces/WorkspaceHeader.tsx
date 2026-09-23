import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Link } from './workspaceNavigation';
import { useWorkspaceSession } from './WorkspaceBoundary';
import { useSystemContext } from '../../hooks/useSystemContext';

export default function WorkspaceHeader() {
  const session = useWorkspaceSession();
  const system = useSystemContext();
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState(false);
  if (!session) return null;
  const label = session.target.kind === 'csp' ? 'Provider workspace'
    : session.target.mode === 'support' ? 'Audited support workspace' : 'Organization workspace';
  return (
    <section aria-label="Active workspace" className="flex shrink-0 flex-wrap items-center justify-between gap-3 border-b bg-indigo-950 px-4 py-3 text-sm text-white">
      <div className="min-w-0 space-y-1">
        <p className="font-semibold">{label} · {session.workspace.displayName}</p>
        {session.target.kind === 'organization' && <p>Active organization: {session.workspace.displayName}</p>}
        {session.systemAccess && <p>Selected system: {system?.name ?? session.systemAccess.systemId}</p>}
        <p>Effective roles: {session.roles.length ? session.roles.join(', ') : 'No assigned roles'}</p>
      </div>
      <div className="flex flex-wrap items-center gap-4">
        <Link to="/narrative-library" className="underline">
          {session.target.kind === 'csp' ? 'Provider' : 'Organization'} Narrative Library
        </Link>
        {session.target.kind === 'organization' && session.workspace.permissions.canManageMemberships && (
          <Link to="/settings/memberships" className="underline">Manage memberships</Link>
        )}
        {session.target.kind === 'organization' && session.workspace.permissions.canManageOrganization && (
          <Link to="/settings/org" className="underline">Organization settings</Link>
        )}
        <button type="button" onClick={() => setConfirm(true)} className="rounded border px-3 py-2">Switch workspace</button>
      </div>
      {confirm && (
        <div role="dialog" aria-modal="true" aria-labelledby="workspace-switch-title"
          className="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 p-4"
          onKeyDown={event => { if (event.key === 'Escape') setConfirm(false); }}>
          <div className="max-w-md space-y-4 rounded bg-white p-6 text-gray-900">
            <h2 id="workspace-switch-title" className="text-lg font-semibold">Switch workspace?</h2>
            <p>Save your work first. Continuing leaves this workspace and discards any unsaved changes in this tab.</p>
            <div className="flex flex-wrap justify-end gap-4">
              <button type="button" autoFocus onClick={() => setConfirm(false)}>Stay here</button>
              <button type="button" className="rounded bg-indigo-700 px-3 py-2 text-white"
                onClick={() => navigate('/login/select-tenant')}>Discard and choose workspace</button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
