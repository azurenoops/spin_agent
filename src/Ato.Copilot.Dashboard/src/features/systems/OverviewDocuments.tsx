import type { PackageReadinessWorkspace } from '../../api/packageReadiness';
import { Link } from '../workspaces/workspaceNavigation';
import { systemPanel, systemSecondaryAction } from './SystemTaskPresentation';
import { overviewDate } from './overviewPresentation';
import { packageSourceHref } from './packageReadinessNavigation';

const titles: Record<string, string> = {
  ssp: 'System Security Plan', boundary: 'Boundary records', inventory: 'Boundary and inventory records',
  sap: 'Assessment plan', sar: 'Assessment report', evidence: 'Evidence index', poam: 'POA&M',
  conmon: 'Continuous monitoring documentation',
};
const milestoneTitles: Record<string, string> = {
  prepare: 'Preparation', export: 'Export', emass: 'Actual eMASS submission', decision: 'Authorization decision',
};
export default function OverviewDocuments({ systemId, workspace, search }: {
  systemId: string; workspace: PackageReadinessWorkspace | null; search: string;
}) {
  return <aside className="min-w-0 space-y-5">
    <section aria-label="Your ATO package" className={`${systemPanel} space-y-3`}>
      <h2 className="text-lg font-semibold">Your ATO package</h2>
      <p className="text-xs text-slate-600 dark:text-slate-300">Gaps, document presence and recorded reviews are separate facts. A readiness finding is not document approval.</p>
      {!workspace && <p className="text-sm">Document records are unavailable until the authorized workspace loads.</p>}
      {workspace?.documents.map(document => {
        const gaps = document.validationOutcome === 'Blocking' || document.validationOutcome === 'FollowUp' || document.validationOutcome === 'Unavailable';
        const href = document.action.canView && document.action.path ? packageSourceHref(systemId, document.action.path, search, 'overview') : null;
        return <article key={document.kind} className="space-y-2 border-b border-slate-200 py-3 text-sm dark:border-slate-700">
          <h3 className="font-semibold">{titles[document.kind] ?? document.title}</h3>
          {gaps && <p className="text-amber-900 dark:text-amber-200">Gaps</p>}
          <p>{document.presence === 'Present' ? `${document.recordCount ?? document.records.length} recorded ${document.recordCount === 1 ? 'record' : 'records'}`
            : document.presence === 'Missing' ? 'Not recorded' : 'Records unavailable'}</p>
          {document.status && <p>{document.status}</p>}
          {document.reviewState && <p>{document.reviewState}</p>}
          {!!document.records.length && <details><summary className="cursor-pointer text-indigo-700 dark:text-indigo-300">Recorded status and sources</summary>
            <ul className="mt-2 space-y-2 text-xs">{document.records.map(record => <li key={`${record.kind}:${record.id}`} className="break-words">
              {record.status ?? 'Review status not provided'} · {overviewDate(record.recordedAt)} · {record.sourceRelationship}
              <details><summary className="cursor-pointer">Record identity</summary><p className="break-all">{record.id}</p></details>
            </li>)}</ul>
          </details>}
          {href && <Link className="inline-flex min-h-11 items-center font-medium text-indigo-700 dark:text-indigo-300" to={href}>Open {titles[document.kind] ?? document.title} records</Link>}
          {document.kind !== 'ssp' && <p className="text-xs text-slate-600 dark:text-slate-300">Preview is not provided on this overview. Use the existing workflow for available outputs.</p>}
        </article>;
      })}
      <Link className={systemSecondaryAction} to={`/systems/${encodeURIComponent(systemId)}/documents/preview`}>Preview working SSP</Link>
      <p className="text-xs text-slate-600 dark:text-slate-300">Working preview is not an approved export. Its source availability and permissions are checked by the existing preview workflow.</p>
    </section>
    <section aria-label="Package milestones" className={`${systemPanel} space-y-3`}>
      <h2 className="font-semibold">Package milestones</h2>
      {workspace?.progress.filter(milestone => milestone.id !== 'validate').map(milestone => <div key={milestone.id}
        className="space-y-1 border-b border-slate-200 py-3 text-sm dark:border-slate-700">
        <h3 className="font-medium">{milestoneTitles[milestone.id]}</h3>
        <p>{milestone.state === 'NotRecorded' ? 'Not recorded' : milestone.state === 'NotChecked' ? 'Not checked'
          : milestone.id === 'export' && milestone.state === 'Recorded' && !milestone.records.some(record => record.status === 'Completed')
            ? 'Jobs recorded; no completed export shown'
            : milestone.id === 'emass' && milestone.state === 'Recorded'
              ? 'Receiving observations recorded; submission not established by this summary' : milestone.state}</p>
        <p className="text-xs text-slate-600 dark:text-slate-300">{milestone.description}</p>
        {milestone.records.map(record => <p key={`${record.kind}:${record.id}`} className="text-xs break-words">
          {record.status ?? 'Status not recorded'} · Recorded {overviewDate(record.recordedAt)} · {record.sourceRelationship}
        </p>)}
      </div>)}
      {!workspace && <p className="text-sm">Milestone records unavailable.</p>}
      <Link className={systemSecondaryAction} to={`/systems/${encodeURIComponent(systemId)}/documents?purpose=InitialSubmission`}>Open package workspace</Link>
      <p className="text-xs text-slate-600 dark:text-slate-300">Readiness is not export. Export is not submission. A recorded decision may be expired, inactive or a denial.</p>
    </section>
  </aside>;
}
