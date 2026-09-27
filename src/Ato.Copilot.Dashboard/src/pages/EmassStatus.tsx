import { useEffect, useState } from 'react';
import {
  getEmassConflicts,
  getEmassReadiness,
  getEmassStatus,
  resolveConflict,
  uploadEmassSync,
} from '../api/emass-status';
import type {
  ConflictStatus,
  EmassConflict,
  EmassExportReadinessResult,
  EmassWorkflowStatus,
} from '../api/emass-status';
import SystemEmassConflictReview from '../features/systems/SystemEmassConflictReview';
import EmassExchangeHistory from '../features/systems/EmassExchangeHistory';
import { useSystemContext } from '../components/layout/SystemLayout';
import { Link } from '../features/workspaces/workspaceNavigation';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport } from '../features/systems/SystemTaskPresentation';
import SystemPackageValidation from '../features/systems/SystemPackageValidation';

function formatStatus(value: string): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .replace(/^./, (letter) => letter.toUpperCase());
}

function formatDate(value: string | null): string {
  return value ? new Date(value).toLocaleString() : 'Never';
}

function errorMessage(error: unknown): string {
  if (typeof error === 'object' && error && 'errors' in error) {
    const errors = (error as { errors?: Array<{ message?: string }> }).errors;
    if (errors?.[0]?.message) return errors[0].message;
  }
  return error instanceof Error ? error.message : 'The eMASS workflow request failed.';
}

export default function EmassStatusPage() {
  const { detail } = useSystemContext();
  return <SystemEmassTask key={detail.systemId} systemId={detail.systemId} />;
}

function SystemEmassTask({ systemId }: { systemId: string }) {
  const [status, setStatus] = useState<EmassWorkflowStatus | null>(null);
  const [readiness, setReadiness] = useState<EmassExportReadinessResult | null>(null);
  const [conflicts, setConflicts] = useState<EmassConflict[]>([]);
  const [conflictOffset, setConflictOffset] = useState(0);
  const [conflictTotal, setConflictTotal] = useState(0);
  const [file, setFile] = useState<File | null>(null);
  const [acknowledge, setAcknowledge] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      const [nextStatus, nextReadiness, conflictPage] = await Promise.all([
        getEmassStatus(systemId),
        getEmassReadiness(systemId),
        getEmassConflicts(systemId, { status: 'Unresolved', limit: 50, offset: conflictOffset }),
      ]);
      setStatus(nextStatus);
      setReadiness(nextReadiness);
      setConflicts(conflictPage.items);
      setConflictTotal(conflictPage.total);
      setError(null);
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [systemId, conflictOffset]);

  async function syncWorkbook() {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      await uploadEmassSync(systemId, file, acknowledge);
      setFile(null);
      await load();
    } catch (requestError) {
      setError(errorMessage(requestError));
    } finally {
      setBusy(false);
    }
  }

  async function handleResolve(conflictId: string, resolution: Exclude<ConflictStatus, 'Unresolved'>, rationale?: string) {
    setError(null);
    try {
      await resolveConflict(systemId, conflictId, resolution, rationale);
      await load();
    } catch (requestError) {
      setError(errorMessage(requestError));
      throw requestError;
    }
  }

  if (loading) return <p className="p-6 text-sm text-gray-500">Loading eMASS workflow...</p>;

  return (
    <div className="space-y-6">
      <SystemTaskHeading title="eMASS round-trip review"
        description="Compare returned records with your export and resolve differences explicitly."
        status={status ? (
          <span className={`rounded-full px-3 py-1 text-sm font-medium ${
            status.overallStatus === 'UpToDate' ? 'bg-green-100 text-green-700'
              : status.overallStatus === 'HasConflicts' ? 'bg-red-100 text-red-700'
                : 'bg-amber-100 text-amber-800'
          }`}>{formatStatus(status.overallStatus)}</span>
        ) : null} />
      <SystemTaskColumns support={<>
        <SystemTaskSupport title="Contributes to"><p>Reconciled system records / Exchange history</p></SystemTaskSupport>
        <SystemTaskSupport title="Review before accepting">
          <p>Keep the exported and returned values distinct until a permitted reviewer records a resolution. A successful local export does not prove eMASS receipt.</p>
          <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/documents?tab=exports`}>Export packages</Link>
        </SystemTaskSupport>
      </>}>

      {error ? <div role="alert" className="border-l-4 border-red-500 bg-red-50 px-4 py-3 text-sm text-red-800">{error}
        <button type="button" className="ml-3 underline" onClick={() => void load()}>Retry eMASS data</button>
      </div> : null}
      {status && !error && <section className="overflow-hidden rounded-lg border border-slate-200 bg-white dark:border-slate-700 dark:bg-slate-900">
        <div className="border-b border-slate-200 px-5 py-4 dark:border-slate-700"><h2 className="text-lg font-semibold">Field difference review</h2></div>
        <SystemEmassConflictReview conflicts={conflicts} onResolve={handleResolve} />
        {conflictTotal > 50 && <nav aria-label="Conflict pages" className="flex flex-wrap items-center justify-between gap-3 border-t p-4 text-sm">
          <button type="button" disabled={conflictOffset === 0} onClick={() => setConflictOffset(value => Math.max(0, value - 50))}>Previous conflicts</button>
          <span>{conflictOffset + 1}–{Math.min(conflictOffset + 50, conflictTotal)} of {conflictTotal}</span>
          <button type="button" disabled={conflictOffset + 50 >= conflictTotal} onClick={() => setConflictOffset(value => value + 50)}>Next conflicts</button>
        </nav>}
      </section>}

      {readiness && !readiness.isReady ? (
        <section className="border-l-4 border-amber-500 bg-amber-50 px-5 py-4">
          <h2 className="text-sm font-semibold text-amber-900">eMASS identifier and exchange advisories</h2>
          <p className="mt-2 text-sm text-amber-900">These eMASS-specific fields are separate from purpose-specific package validation below.</p>
          <ul className="mt-2 space-y-1 text-sm text-amber-900">
            {readiness.gaps.map((gap) => (
              <li key={gap.fieldName} className={gap.severity === 'Blocking' ? 'font-semibold' : undefined}>
                {gap.description}
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      <SystemPackageValidation systemId={systemId} />

      {status ? (
        <section className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
          <div className="grid divide-y divide-gray-200 sm:grid-cols-3 sm:divide-x sm:divide-y-0">
            <div className="p-5"><p className="text-xs uppercase text-gray-500">Last exported</p><p className="mt-2 text-sm font-medium text-gray-900">{formatDate(status.lastExportedAt)}</p></div>
            <div className="p-5"><p className="text-xs uppercase text-gray-500">Last synced</p><p className="mt-2 text-sm font-medium text-gray-900">{formatDate(status.lastSyncedAt)}</p></div>
            <div className="p-5"><p className="text-xs uppercase text-gray-500">Unresolved conflicts</p><p className="mt-2 text-2xl font-semibold text-gray-900">{status.unresolvedConflictCount}</p></div>
          </div>
          <div className="overflow-x-auto border-t border-gray-200">
            <table className="min-w-full divide-y divide-gray-200 text-left text-sm">
              <thead className="bg-gray-50 text-xs uppercase text-gray-500"><tr><th className="px-5 py-3 font-medium">Category</th><th className="px-5 py-3 text-right font-medium">Exported</th><th className="px-5 py-3 text-right font-medium">Pending</th><th className="px-5 py-3 font-medium">Last exported</th></tr></thead>
              <tbody className="divide-y divide-gray-100">{status.exportSummary.map((item) => <tr key={item.category}><td className="px-5 py-3 font-medium text-gray-900">{item.category}</td><td className="px-5 py-3 text-right text-gray-700">{item.exportedCount}</td><td className="px-5 py-3 text-right text-gray-700">{item.pendingCount}</td><td className="px-5 py-3 text-gray-500">{formatDate(item.lastExportedAt)}</td></tr>)}</tbody>
            </table>
          </div>
        </section>
      ) : null}

      <section className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <h2 className="text-sm font-semibold text-gray-900">Sync eMASS workbook</h2>
        <div className="mt-4 flex flex-wrap items-end gap-4">
          <label className="block min-w-64 flex-1 text-sm font-medium text-gray-700">
            eMASS workbook
            <input type="file" accept=".xlsx" onChange={(event) => setFile(event.target.files?.[0] ?? null)} className="mt-1 block w-full text-sm text-gray-600 file:mr-3 file:rounded-md file:border-0 file:bg-gray-100 file:px-3 file:py-2 file:text-sm file:font-medium file:text-gray-700 hover:file:bg-gray-200" />
          </label>
          <label className="flex items-center gap-2 pb-2 text-sm text-gray-600"><input type="checkbox" checked={acknowledge} onChange={(event) => setAcknowledge(event.target.checked)} className="h-4 w-4 rounded border-gray-300 text-indigo-600" />Acknowledge unresolved conflicts</label>
          <button type="button" onClick={syncWorkbook} disabled={!file || busy} className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50">{busy ? 'Syncing...' : 'Sync workbook'}</button>
        </div>
      </section>

      <EmassExchangeHistory systemId={systemId} />

      </SystemTaskColumns>
    </div>
  );
}