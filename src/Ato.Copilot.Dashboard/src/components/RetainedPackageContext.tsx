import { useState } from 'react';
import { getPackageContextOptions, type RetainedPackageSelection } from '../api/package';
import { Status, useRemote } from '../features/workspace-operations/workspaceUi';

export default function RetainedPackageContext({ systemId, purpose, disabled = false, onChange }: {
  systemId: string;
  purpose: 'AuthorizedBaselineArchive' | 'ChangeSubmission';
  disabled?: boolean;
  onChange: (selection: RetainedPackageSelection | null) => void;
}) {
  const options = useRemote(signal => getPackageContextOptions(systemId, signal), [systemId]);
  const [ids, setIds] = useState({ baseline: '', decision: '', preview: '' });
  const select = (update: Partial<typeof ids>) => {
    const next = { ...ids, ...update };
    setIds(next);
    const baseline = options.data?.baselines.find(item => item.id === next.baseline);
    const decision = options.data?.decisions.find(item => item.id === next.decision);
    const preview = options.data?.previews.find(item => item.id === next.preview);
    onChange(baseline && decision && (purpose !== 'ChangeSubmission' || preview)
      ? { baselinePackageId: baseline.id, baselineContentHash: baseline.contentHash,
        authorizationDecisionId: decision.id, expectedDecisionSnapshotHash: decision.snapshotHash,
        ...(purpose === 'ChangeSubmission' && preview ? { changePreviewId: preview.id, changeContentHash: preview.contentHash } : {}) }
      : null);
  };
  const field = 'mt-1 block w-full rounded-md border border-slate-300 bg-white p-2 text-sm text-slate-900 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100';
  return <section aria-label="Retained package context" className="my-4 space-y-3 rounded-lg border border-slate-200 p-4 dark:border-slate-700">
    <h3 className="font-semibold">Select retained source versions</h3>
    <p className="text-xs text-slate-500">No source is selected automatically. Changing a source requires fresh validation.</p>
    <Status loading={options.loading} error={options.error} retry={() => {
      setIds({ baseline: '', decision: '', preview: '' }); onChange(null); options.retry();
    }} />
    {options.data && <fieldset disabled={disabled} className="space-y-3">
      <label className="block text-sm">Retained baseline package
        <select className={field} value={ids.baseline} onChange={event => select({ baseline: event.target.value })}>
          <option value="">Select a completed package</option>
          {options.data.baselines.map(item => <option key={item.id} value={item.id}>{item.purpose} · {item.generatedAt} · {item.id}</option>)}
        </select>
      </label>
      {!options.data.baselines.length && <p className="text-xs text-amber-800">No completed, readable baseline package is available.</p>}
      <label className="block text-sm">Recorded authorization decision
        <select className={field} value={ids.decision} onChange={event => select({ decision: event.target.value })}>
          <option value="">Select a recorded decision</option>
          {options.data.decisions.map(item => <option key={item.id} value={item.id}>{item.decisionType} · {item.decisionDate} · {item.issuer}</option>)}
        </select>
      </label>
      {!options.data.decisions.length && <p className="text-xs text-amber-800">No applicable recorded decision is available. This flow does not issue one.</p>}
      {purpose === 'ChangeSubmission' && <label className="block text-sm">Retained SSP change preview
        <select className={field} value={ids.preview} onChange={event => select({ preview: event.target.value })}>
          <option value="">Select the reviewed retained preview</option>
          {options.data.previews.map(item => <option key={item.id} value={item.id}>{item.generatedAt} · {item.id}</option>)}
        </select>
      </label>}
      {purpose === 'ChangeSubmission' && <p className="text-xs text-slate-500">This bundle retains the selected baseline and SSP change, not a regenerated assessment package. Review applicability before submission.</p>}
    </fieldset>}
  </section>;
}
