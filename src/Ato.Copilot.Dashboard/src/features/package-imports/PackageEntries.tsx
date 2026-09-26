import { useRef, useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { errorClass, inputClass, message, Pager, secondaryButtonClass, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { excludePackageEntry, getPackageEntries, packageArtifactUrl } from './api';
import type { PackageEntry } from './types';

export function PackageEntries({ packageId, revision, disabled, onChanged }: {
  packageId: string; revision: number; disabled: boolean; onChanged: () => void;
}) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => getPackageEntries(packageId, page, signal), [packageId, page, revision]);
  return <section aria-label="Source entries" className="space-y-3">
    <h2 className="text-lg font-semibold">Source documents and coverage</h2>
    <p className="text-sm text-gray-600">Every source entry remains accounted for. Excluding an entry and its children makes affected candidates ineligible; it does not mean they were analyzed.</p>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      {!remote.data.items.length && <p className={`${surfaceClass} p-4`}>No source entries returned.</p>}
      {remote.data.items.map(entry => <EntryRow key={`${entry.entryId}:${entry.revision}`} packageId={packageId} entry={entry} disabled={disabled} onChanged={onChanged} />)}
      <Pager {...remote.data} onPage={setPage} />
    </>}
  </section>;
}

function EntryRow({ packageId, entry, disabled, onChanged }: {
  packageId: string; entry: PackageEntry; disabled: boolean; onChanged: () => void;
}) {
  const [rationale, setRationale] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const submitting = useRef(false);
  const exclusionReason = entry.exclusionReason ?? (entry.status === 'Excluded' ? entry.reason : null);
  const exclude = async () => {
    if (submitting.current || disabled) return;
    if (!rationale.trim()) { setError('An explicit exclusion rationale is required.'); return; }
    submitting.current = true; setBusy(true); setError(null);
    try { await excludePackageEntry(packageId, entry.entryId, entry.revision, rationale.trim()); onChanged(); }
    catch (reason) { setError(message(reason)); }
    finally { submitting.current = false; setBusy(false); }
  };
  return <article className={`${surfaceClass} min-w-0 space-y-3 p-4`} aria-label={entry.fileName}>
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0"><h3 className="break-all font-semibold">{entry.fileName}</h3><p className="break-all text-xs text-gray-500">{entry.archivePath} · {entry.mediaType}</p></div>
      <span className="rounded bg-gray-100 px-2 py-1 text-xs text-gray-700">{entry.status}</span>
    </div>
    <p className="text-xs text-gray-600">{entry.byteLength.toLocaleString()} bytes · {entry.candidateCount} candidates · Revision {entry.revision}</p>
    {entry.familyCoverage && <section className="space-y-2 rounded border border-slate-200 p-3 text-sm" aria-label="Semantic family coverage">
      <p>Successful extraction does not establish complete family coverage.</p>
      {entry.familyCoverage.length ? <ul className="space-y-2">{entry.familyCoverage.map(family => <li key={family.family}
        className={['Analyzed', 'NoDeclarations'].includes(family.status) ? '' : warningClass}>
        <p>{family.family} · {family.status}</p>{family.reason && <p className="text-xs">{family.reason}</p>}
      </li>)}</ul> : <p>No semantic family coverage reported. This is not a complete-analysis assertion.</p>}
    </section>}
    {entry.reason && entry.reason !== exclusionReason && <p className={warningClass}>{entry.reason}</p>}
    {exclusionReason && <p className={entry.status === 'Excluded' ? `${surfaceClass} p-3 text-sm` : warningClass}>Excluded from analysis: {exclusionReason}</p>}
    <AuthenticatedDownload className="text-sm font-medium text-indigo-700 underline" url={packageArtifactUrl(packageId, entry.artifactId)} fileName={entry.fileName}>Download source</AuthenticatedDownload>
    {entry.status !== 'Excluded' && <details>
      <summary className="cursor-pointer text-sm font-medium text-gray-700">Exclude source entry</summary>
      <form className="mt-3 space-y-2" onSubmit={event => { event.preventDefault(); void exclude(); }}>
        <label className="block text-sm">Exclusion rationale for {entry.fileName}<textarea className={`${inputClass} mt-1 w-full`} maxLength={2000} disabled={disabled || busy} value={rationale} onChange={event => setRationale(event.target.value)} /></label>
        <button className={secondaryButtonClass} disabled={disabled || busy} type="submit">Exclude entry</button>
      </form>
    </details>}
    {error && <p role="alert" className={errorClass}>{error}</p>}
  </article>;
}
