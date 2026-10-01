import { useEffect, useState } from 'react';
import { Link, useWorkspaceTarget } from '../workspaces/workspaceNavigation';
import {
  backfillCatalogSources, captureCatalogSource, getCatalogSourceManagement,
  type CatalogSourceManagementStatus,
} from '../../api/catalogSources';

function message(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === 'object') {
    if ('error' in error && typeof error.error === 'string') return error.error;
    if ('failures' in error && Array.isArray(error.failures))
      return error.failures.map(value => value && typeof value === 'object' && 'identifier' in value
        ? `${String(value.identifier)}: source was not loaded.` : 'Source was not loaded.').join(' ');
  }
  return 'Catalog sources could not be loaded. Verify your platform permissions and retry.';
}

export default function CatalogSourceManagement({ onImportRequested, onPermissionsChanged, refreshToken = '' }: {
  onImportRequested: () => void; onPermissionsChanged: (allowed: boolean) => void; refreshToken?: string;
}) {
  const workspaceKey = JSON.stringify(useWorkspaceTarget());
  const [data, setData] = useState<CatalogSourceManagementStatus | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setData(null); setError(''); onPermissionsChanged(false);
    getCatalogSourceManagement(controller.signal).then(value => {
      if (!controller.signal.aborted) { setData(value); onPermissionsChanged(value.canManageSources); }
    }).catch(reason => { if (!controller.signal.aborted) setError(message(reason)); });
    return () => controller.abort();
  }, [workspaceKey, revision, refreshToken, onPermissionsChanged]);

  const run = async (operation: () => Promise<string>) => {
    setBusy(true); setError(''); setNotice('');
    try {
      const result = await operation();
      const current = await getCatalogSourceManagement();
      setData(current); onPermissionsChanged(current.canManageSources); setNotice(result);
    } catch (reason) { setError(message(reason)); }
    finally { setBusy(false); }
  };

  return <section aria-label="Reference catalog sources" className="rounded-lg border border-gray-200 bg-white p-4 space-y-3 dark:border-slate-700 dark:bg-slate-900">
    <h2 className="font-semibold">Reference catalog sources</h2>
    <p className="text-sm text-gray-600 dark:text-slate-300">
      Platform administrators load official reference sources here. SPIN automatically associates them with
      matching system baselines. Loading a source does not select controls or approve statements.
    </p>
    {error && <div role="alert" className="text-sm text-red-700 dark:text-red-300">{error}
      <button type="button" className="ml-2 underline" onClick={() => setRevision(value => value + 1)}>Retry catalog status</button></div>}
    {notice && <p role="status" className="text-sm">{notice}</p>}
    {!data && !error && <p role="status">Loading catalog source status...</p>}
    {data && <>
      {!data.canManageSources && <p className="text-sm">{data.reason}
        {data.isPlatformAdministrator && <Link className="ml-2 underline" to={data.managementPath}>
          Open platform catalog administration
        </Link>}</p>}
      {data.canManageSources && <div className="flex flex-wrap gap-3">
        <button type="button" disabled={busy} className="rounded border px-3 py-2" onClick={onImportRequested}>
          Import/refresh framework definitions
        </button>
        <button type="button" disabled={busy || data.sources.every(source => source.sourceAvailable)}
          className="rounded border px-3 py-2" onClick={() => run(async () => {
            const result = await backfillCatalogSources();
            return `${result.captured.filter(item => item.changed).length} source(s) loaded. ${
              result.failures.length ? result.failures.map(item => `${item.identifier}: ${item.message}`).join(' ') : ''
            } Control selections and reviewed content were not changed.`;
          })}>{busy ? 'Loading sources...' : 'Load missing sources'}</button>
      </div>}
      <ul className="space-y-3">{data.sources.map(source => <li key={source.identifier} className="border-t pt-3 dark:border-slate-700">
        <strong>{source.name}</strong>
        <p className="text-sm">Catalog definitions: {source.definitionVersion}. Source: {
          source.sourceAvailable ? source.sourceVersion ?? 'version not recorded' : 'administrator refresh needed'}.</p>
        {source.sourceUri && <p className="break-all text-xs">{source.sourceUri}</p>}
        {source.capturedAt && <p className="text-xs">Captured: {source.capturedAt}</p>}
        {data.canManageSources && <button type="button" disabled={busy} className="mt-1 rounded border px-3 py-1 text-sm"
          aria-label={`${source.sourceAvailable ? 'Refresh' : 'Load'} source for ${source.name}`}
          onClick={() => run(async () => {
            await captureCatalogSource(source.identifier);
            return `Source loaded for ${source.name}. Control selections and reviewed content were not changed.`;
          })}>{source.sourceAvailable ? 'Refresh source' : 'Load source'}</button>}
      </li>)}</ul>
    </>}
  </section>;
}
