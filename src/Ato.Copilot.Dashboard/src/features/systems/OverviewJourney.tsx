import { useState } from 'react';
import type { PackageReadinessWorkspace } from '../../api/packageReadiness';
import { confirmOverviewPhase, overviewError } from '../../api/systemOverview';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { inputClass } from '../workspace-operations/workspaceUi';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import { overviewDate, rmfJourney, type OverviewPhase } from './overviewPresentation';

export default function OverviewJourney({ systemId, rmf, viewing, onView, onConfirmed }: {
  systemId: string; rmf: PackageReadinessWorkspace['rmf'] | undefined;
  viewing: OverviewPhase; onView: (phase: OverviewPhase) => void; onConfirmed: () => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const [phase, setPhase] = useState<OverviewPhase>(viewing);
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const info = rmfJourney.find(item => item.phase === viewing)!;
  const current = rmf?.confirmed ? rmf.phase : 'Not confirmed';
  async function confirm() {
    if (!rmf?.canConfirm || busy || !notes.trim()) return;
    setBusy(true); setError('');
    try {
      await confirmOverviewPhase(systemId, { phase, expectedPhase: rmf.phase, notes: notes.trim() });
      setConfirming(false); onConfirmed();
    } catch (reason) { setError(overviewError(reason)); }
    finally { setBusy(false); }
  }
  return <section aria-label="RMF journey" className={`${systemPanel} space-y-4`}>
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 className="text-lg font-semibold">Current RMF phase: {current}</h2>
        <p className="mt-1 text-xs text-slate-600 dark:text-slate-300">{rmf?.confirmed
          ? `${rmf.source} · ${overviewDate(rmf.recordedAt)} · ${rmf.actor}`
          : 'An initial or legacy phase value is not an explicitly confirmed phase.'}</p></div>
      {rmf?.canConfirm ? <button type="button" className={systemSecondaryAction} onClick={() => {
        setPhase(viewing); setError(''); setConfirming(true);
      }}>Confirm recorded phase</button> : <p className="text-xs text-slate-600 dark:text-slate-300">An authorized system manager must confirm the phase.</p>}
    </div>
    <nav aria-label="Explore RMF phases" className="grid grid-cols-2 gap-2 min-[400px]:grid-cols-4 min-[900px]:grid-cols-7"
      onKeyDown={event => {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('button'));
        const current = Math.max(0, buttons.indexOf(document.activeElement as HTMLButtonElement));
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1
          : (current + (event.key === 'ArrowRight' ? 1 : -1) + buttons.length) % buttons.length;
        event.preventDefault(); buttons[next]?.focus(); buttons[next]?.click();
      }}>
      {rmfJourney.map((item, index) => <button type="button" key={item.phase} aria-label={`View ${item.phase} phase`}
        tabIndex={viewing === item.phase ? 0 : -1}
        aria-pressed={viewing === item.phase} onClick={() => onView(item.phase)}
        className={`min-h-16 rounded-md border px-2 py-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-600 ${
          viewing === item.phase ? 'border-indigo-300 bg-indigo-50 text-indigo-800 dark:bg-indigo-950 dark:text-indigo-200'
            : 'border-slate-200 bg-slate-50 dark:border-slate-700 dark:bg-slate-800'}`}>
        <span className="block text-xs">{index + 1}</span>{item.phase}
        {rmf?.confirmed && rmf.phase === item.phase && <span className="block text-xs font-semibold">Current phase</span>}
      </button>)}
    </nav>
    <div className="border-t border-slate-200 pt-4 dark:border-slate-700">
      <h3 className="font-semibold">Viewing phase: {viewing}</h3>
      <p className="mt-1 text-sm">{info.purpose}</p>
      <p className="mt-2 text-xs text-slate-600 dark:text-slate-300">{info.work} Supports {info.documents}.</p>
      <Link className={`${systemSecondaryAction} mt-3`} to={`/systems/${encodeURIComponent(systemId)}/${info.path}`}>Explore {viewing} work</Link>
      <p className="mt-3 text-xs text-slate-600 dark:text-slate-300">Browsing does not change the recorded phase. Work may overlap, and earlier gaps may still need attention.</p>
    </div>
    {confirming && <SetupDialog title="Confirm recorded RMF phase" busy={busy} onClose={() => setConfirming(false)}
      description="This explicit action records the phase through the existing audited lifecycle and gate checks. It does not approve documents or issue authorization.">
      <form className="space-y-4" onSubmit={event => { event.preventDefault(); void confirm(); }}>
        <label className="grid gap-1 text-sm">Phase to record<select value={phase} disabled={busy} className={inputClass}
          onChange={event => setPhase(event.target.value as OverviewPhase)}>
          {rmfJourney.map(item => <option key={item.phase} value={item.phase}>{item.phase}</option>)}</select></label>
        <label className="grid gap-1 text-sm">Basis for this recorded phase<textarea required maxLength={2000}
          value={notes} disabled={busy} className={`${inputClass} min-h-24`} onChange={event => setNotes(event.target.value)} /></label>
        {error && <p role="alert" className="text-sm text-red-800 dark:text-red-200">{error}</p>}
        <button type="submit" className={systemPrimaryAction} disabled={busy || !notes.trim()}>{busy ? 'Confirming…' : 'Confirm phase'}</button>
      </form>
    </SetupDialog>}
  </section>;
}
