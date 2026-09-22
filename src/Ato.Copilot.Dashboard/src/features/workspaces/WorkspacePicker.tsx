import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { getWorkspaceOptions, workspaceErrorMessage } from './api';
import { WorkspaceStatus } from './WorkspaceBoundary';
import { buildWorkspaceUrl, parseWorkspaceUrl } from './workspaceRoutes';
import { optionTarget, safeDeepLink } from './WorkspaceEntry';
import type { WorkspaceOption } from './types';

export default function WorkspacePicker() {
  const navigate = useNavigate();
  const location = useLocation();
  const [choices, setChoices] = useState<WorkspaceOption[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    getWorkspaceOptions(page).then(result => {
      if (!active) return;
      if (!Array.isArray(result.items) || !Number.isInteger(result.total) || result.total < result.items.length
        || result.total < 0 || (result.total > (page - 1) * 50 && result.items.length === 0)) {
        throw new Error('The workspace API returned incomplete choices.');
      }
      result.items.forEach(optionTarget);
      setChoices(result.items);
      setTotal(result.total);
    }).catch(reason => {
      if (active) setError(workspaceErrorMessage(reason));
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [page, revision]);

  const select = (option: WorkspaceOption) => {
    const intended = safeDeepLink((location.state as { deepLink?: unknown } | null)?.deepLink);
    const parsed = parseWorkspaceUrl(intended);
    // Selection is an ordinary URL context only: never start/end support or
    // write a browser-wide selection cookie on this path.
    navigate(buildWorkspaceUrl(optionTarget(option), parsed?.route ?? intended), { replace: true });
  };
  return (
    <main className="mx-auto max-w-xl space-y-5 px-6 py-12">
      <h1 className="text-2xl font-semibold">Choose a workspace</h1>
      <p>Choose an authorized provider or organization workspace for this tab.</p>
      {error ? <WorkspaceStatus message={error} onRetry={() => setRevision(value => value + 1)} /> : loading
        ? <p role="status">Loading authorized workspaces…</p>
        : choices.length === 0 ? <p role="status">No authorized workspaces are available. Contact your administrator.</p>
        : <ul className="space-y-3">{choices.map(option => (
          <li key={`${option.kind}:${option.tenantId ?? ''}`}>
            <button type="button" disabled={option.status === 'Disabled'} onClick={() => select(option)}
              className="w-full rounded border p-4 text-left hover:bg-indigo-50 disabled:opacity-50">
              <strong>{option.displayName}</strong>
              <span className="block text-sm">{option.kind === 'csp' ? 'Provider workspace' : 'Organization workspace'} · {option.status}</span>
            </button>
          </li>
        ))}</ul>}
      <nav aria-label="Workspace pages" className="flex gap-4">
        <button disabled={loading || page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <span>Page {page}</span>
        <button disabled={loading || page * 50 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
      </nav>
    </main>
  );
}
