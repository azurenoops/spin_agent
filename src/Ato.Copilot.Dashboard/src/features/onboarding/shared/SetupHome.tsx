import { Building2, Layers, Monitor } from 'lucide-react';
import { Link } from '../../workspaces/workspaceNavigation';
import SetupFrame, { SetupGuidance, SetupPanel } from './SetupFrame';

export type SetupKind = 'provider' | 'organization' | 'system';
export interface SetupOption { kind: SetupKind; destination: string }
export interface SavedSetupRecord {
  id: string;
  kind: SetupKind;
  name: string;
  detail: string;
  state: string;
  destination: string;
}

const paths = {
  provider: { title: 'A provider workspace', description: 'Define the provider, first service offering, and optional source package. Review and publish later.', action: 'Set up provider', icon: Layers },
  organization: { title: 'An organization', description: 'Create or reuse the organization and establish explicit administrator access.', action: 'Set up organization', icon: Building2 },
  system: { title: 'A mission system', description: 'Register a draft system, choose its preparation path, and identify hosting. Finish documentation in Systems.', action: 'Set up system', icon: Monitor },
};
const steps = [{ id: 'start', label: 'Choose a path' }, { id: 'resume', label: 'Resume' }];

export default function SetupHome({
  mode, workspaceName, options, records, loading, error, onRetry, onChangeMode, onLoadMore,
}: {
  mode: 'start' | 'resume';
  workspaceName: string;
  options: readonly SetupOption[];
  records: readonly SavedSetupRecord[];
  loading: boolean;
  error?: string | null;
  onRetry?: () => void;
  onLoadMore?: () => void;
  onChangeMode: (mode: 'start' | 'resume') => void;
}) {
  return <SetupFrame journey="Start" title={mode === 'start' ? 'What are you setting up?' : 'Continue your setup'}
    description={mode === 'start' ? 'Take the shortest path to the workspace you need. Detailed review happens after setup.'
      : 'Saved records and unfinished tasks are shown separately. Resume without starting over.'}
    currentStep={mode} steps={steps} onStepChange={id => onChangeMode(id === 'resume' ? 'resume' : 'start')}
    error={error} stepLabel={mode === 'start' ? 'Choose a path' : 'Saved setup'}
    onBack={mode === 'resume' ? () => onChangeMode('start') : undefined}
    primaryAction={mode === 'start' ? { label: 'View saved setup', onClick: () => onChangeMode('resume') } : undefined}
    guidance={mode === 'resume' ? <>
      <SetupGuidance title="Creation is not repeated">Continue uses the existing provider, organization, or system identity. Failed follow-up work does not require another record.</SetupGuidance>
      <SetupGuidance title="Saved facts, not browser progress">These records come from the server. Selected files that were not uploaded must be selected again; an uncertain receipt must be reconciled before retry.</SetupGuidance>
    </> : undefined}>
    {mode === 'start' && <>
      <div className="rounded-lg border border-indigo-200 bg-indigo-50 px-4 py-3 text-sm dark:border-indigo-800 dark:bg-indigo-950">
        <p className="font-semibold">{workspaceName}</p>
        <p className="mt-1 text-xs text-slate-600 dark:text-gray-300">Available actions follow your current workspace permissions. Choosing a path does not grant access.</p>
      </div>
      <div className="grid min-w-0 gap-4 xl:grid-cols-3">
        {options.map(option => {
          const definition = paths[option.kind];
          const Icon = definition.icon;
          return <section key={option.kind} className="flex min-w-0 flex-col items-start rounded-xl border border-slate-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-900">
            <Icon aria-hidden="true" size={28} className="mb-5 text-indigo-600 dark:text-indigo-300" />
            <h2 className="text-base font-semibold">{definition.title}</h2>
            <p className="mb-6 mt-3 text-sm text-slate-500 dark:text-gray-400">{definition.description}</p>
            <Link to={option.destination} className="mt-auto rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-500">
              {definition.action}
            </Link>
          </section>;
        })}
      </div>
      {!options.length && !loading && <p className="text-sm text-slate-600 dark:text-gray-300">
        {error ? 'Setup actions could not be verified.' : 'No setup actions are available in this workspace.'}
      </p>}
    </>}
    <SetupPanel title={mode === 'resume' ? 'Setup in progress' : 'Already have a workspace?'}>
      {loading && <p role="status" className="text-sm text-slate-500 dark:text-gray-400">Loading saved setup...</p>}
      {error && onRetry && <button type="button" onClick={onRetry}
        className="mb-4 text-sm text-indigo-700 underline dark:text-indigo-300">Retry loading setup</button>}
      {!loading && !error && !records.length && <p className="text-sm text-slate-500 dark:text-gray-400">No saved setup records.</p>}
      <ul className="divide-y divide-slate-200 dark:divide-gray-700">
        {records.map(record => <li key={`${record.kind}:${record.id}`} className="flex min-w-0 flex-wrap items-center justify-between gap-4 py-4 first:pt-0 last:pb-0">
          <div className="min-w-0 flex-1">
            <h3 className="break-words text-sm font-semibold">{record.name}</h3>
            <p className="mt-1 text-xs text-slate-500 dark:text-gray-400">{record.detail}</p>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <span className="rounded bg-slate-100 px-2 py-1 text-xs text-slate-600 dark:bg-gray-800 dark:text-gray-300">{record.state}</span>
            <Link to={record.destination} aria-label={`Continue ${record.name}`}
              className="rounded-lg border border-slate-300 px-3 py-2 text-sm font-medium hover:bg-slate-50 dark:border-gray-600 dark:hover:bg-gray-800">Continue</Link>
          </div>
        </li>)}
      </ul>
      {onLoadMore && <button type="button" disabled={loading} onClick={onLoadMore}
        className="mt-4 rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium disabled:opacity-50 dark:border-gray-600">
        Load more saved setup
      </button>}
    </SetupPanel>
  </SetupFrame>;
}
