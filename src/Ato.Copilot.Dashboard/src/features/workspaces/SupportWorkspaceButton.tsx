import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { startImpersonation } from '../tenancy/api';
import { buildWorkspaceUrl } from './workspaceRoutes';
import { workspaceErrorMessage } from './api';

export default function SupportWorkspaceButton({ tenantId, tenantName, route = '/', disabled = false }: {
  tenantId: string; tenantName: string; route?: string; disabled?: boolean;
}) {
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      await startImpersonation(tenantId, tenantName);
      navigate(buildWorkspaceUrl({ kind: 'organization', tenantId, mode: 'support' }, route));
    } catch (reason) {
      setError(workspaceErrorMessage(reason));
    } finally {
      setBusy(false);
    }
  };
  return (
    <span onClick={event => event.stopPropagation()}>
      <button type="button" disabled={disabled} onClick={() => setConfirm(true)}
        className="rounded border border-amber-400 px-2 py-1 text-amber-900">Audited support</button>
      {confirm && <span role="dialog" aria-modal="true" aria-label={`Audited support for ${tenantName}`}
        className="fixed inset-0 z-[100] flex items-center justify-center whitespace-normal bg-black/50 p-4">
        <span className="block max-w-md space-y-4 rounded bg-white p-6 text-gray-900">
          <strong className="block">Enter audited support for {tenantName}?</strong>
          <span className="block">This starts a time-limited, audited impersonation session, not ordinary membership. Save your work first; continuing discards unsaved changes in this tab.</span>
          {error && <span role="alert" className="block text-red-700">{error}</span>}
          <span className="flex gap-4">
            <button type="button" autoFocus disabled={busy} onClick={() => setConfirm(false)}>Cancel</button>
            <button type="button" disabled={busy} onClick={() => { void start(); }}>Start audited support</button>
          </span>
        </span>
      </span>}
    </span>
  );
}
