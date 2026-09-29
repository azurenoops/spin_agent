import { useState, useCallback } from 'react';
import type { CreatePoamRequest } from '../../types/poam';
import ComponentPicker from './ComponentPicker';
import { useSystemMutationPermission } from '../permissions/useSystemMutationPermission';
import SetupDialog from '../../features/workspace-operations/SetupDialog';
import { systemPrimaryAction, systemSecondaryAction } from '../../features/systems/SystemTaskPresentation';
import { getPoamWorkspace } from '../../api/poamWorkspace';
import { usePoamRead } from '../../hooks/usePoamRead';
import { Link } from '../../features/workspaces/workspaceNavigation';
import { poamErrorMessage } from '../../utils/poamErrors';

interface PoamCreateFormProps {
  systemId: string;
  onClose: () => void;
  onSubmit: (req: CreatePoamRequest) => Promise<void>;
  loading: boolean;
  onOpenExisting?: (id: string) => void;
}

export default function PoamCreateForm({ systemId, onClose, onSubmit, loading, onOpenExisting }: PoamCreateFormProps) {
  const canManageRemediation = useSystemMutationPermission(systemId, 'canManageRemediation');
  const [error, setError] = useState<string | null>(null);
  const [weakness, setWeakness] = useState('');
  const [controlId, setControlId] = useState('');
  const [severity, setSeverity] = useState<'I' | 'II' | 'III'>('II');
  const [poc, setPoc] = useState('');
  const [pocEmail, setPocEmail] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [source, setSource] = useState('');
  const [resourcesRequired, setResourcesRequired] = useState('');
  const [comments, setComments] = useState('');
  const [milestones, setMilestones] = useState<{ description: string; targetDate: string }[]>([]);
  const [componentIds, setComponentIds] = useState<string[]>([]);
  const [mode, setMode] = useState<'manual' | 'finding'>('manual');
  const [findingId, setFindingId] = useState('');
  const fetchFindings = useCallback((signal: AbortSignal) => mode === 'finding' ? getPoamWorkspace(systemId, signal) : Promise.resolve(null), [systemId, mode]);
  const findings = usePoamRead(fetchFindings);
  const selectedFinding = findings.data?.findings.find(item => item.id === findingId);
  const existing = selectedFinding ? findings.data?.poams.filter(poam => poam.findingId === selectedFinding.id).map(poam => poam.id) ?? [] : [];
  const related = selectedFinding?.poamIds.filter(id => !existing.includes(id)) ?? [];

  const addMilestone = () => setMilestones(ms => [...ms, { description: '', targetDate: '' }]);
  const updateMilestone = (i: number, field: string, value: string) =>
    setMilestones(ms => ms.map((m, idx) => idx === i ? { ...m, [field]: value } : m));
  const removeMilestone = (i: number) => setMilestones(ms => ms.filter((_, idx) => idx !== i));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canManageRemediation) {
      setError('Permission denied: you cannot manage remediation for this system.');
      return;
    }
    if (mode === 'finding' && (!selectedFinding || existing.length > 0)) {
      setError(existing.length ? 'Open the existing commitment instead of creating a duplicate.' : 'Select a retained finding first.');
      return;
    }
    if (!weakness.trim() || !controlId.trim() || !poc.trim() || !dueDate || milestones.some(item => !item.description.trim() || !item.targetDate)) {
      setError('Complete the required fields and each milestone before creating the commitment.');
      return;
    }
    setError(null);
    try {
      await onSubmit({
        weakness: weakness.trim(),
        weaknessSource: source || 'Manual',
        controlId: controlId.trim(),
        catSeverity: severity,
        poc: poc.trim(),
        pocEmail: pocEmail || undefined,
        scheduledCompletionDate: dueDate,
        resourcesRequired: resourcesRequired || undefined,
        comments: comments || undefined,
        milestones: milestones.length > 0 ? milestones : undefined,
        componentIds: componentIds.length > 0 ? componentIds : undefined,
        findingId: mode === 'finding' ? findingId : undefined,
      });
    } catch (err: unknown) {
      setError(poamErrorMessage(err));
    }
  };

  return (
    <SetupDialog placement="right" busy={loading} onClose={onClose} title="Add POA&M" description="Record a remediation commitment with an owner and a completion date.">
        <div className="mb-4 flex gap-2" aria-label="Commitment source">
          <button type="button" disabled={loading} aria-pressed={mode === 'finding'} className={systemSecondaryAction} onClick={() => { setMode('finding'); setError(null); }}>Existing finding</button>
          <button type="button" disabled={loading} aria-pressed={mode === 'manual'} className={systemSecondaryAction} onClick={() => { setMode('manual'); setFindingId(''); setError(null); setSource('Manual'); }}>Manual entry</button>
        </div>
        {mode === 'finding' && <section className="mb-4 space-y-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700">
          <label htmlFor="poam-finding" className="block text-sm font-medium">Assessment finding</label>
          {findings.loading && !findings.data && <p role="status" className="text-sm">Loading retained findings…</p>}
          {findings.error && <div><p role="alert" className="text-sm text-red-700 dark:text-red-300">{findings.error.message}</p><button className={`${systemSecondaryAction} mt-2`} onClick={findings.refresh}>Retry findings</button></div>}
          {findings.data && <select id="poam-finding" disabled={loading} value={findingId} className="w-full rounded-md border p-2 text-sm dark:bg-slate-800" onChange={event => {
            setFindingId(event.target.value);
            const finding = findings.data?.findings.find(item => item.id === event.target.value);
            if (finding) { setWeakness(finding.description || finding.title); setControlId(finding.controlId); setSource('Assessment finding'); setSeverity(finding.severity === 'Critical' ? 'I' : finding.severity === 'High' ? 'II' : 'III'); }
          }}><option value="">Select a finding</option>{findings.data.findings.map(item => <option key={item.id} value={item.id}>{item.controlId} · {item.title}{findings.data?.poams.some(poam => poam.findingId === item.id) ? ' · Formal POA&M exists' : ''}</option>)}</select>}
          {findings.data?.findings.length === 0 && <p className="text-sm">No retained findings. Use Manual entry to record a new weakness.</p>}
          {existing.length > 0 && <div className="text-sm"><p>A commitment already exists for this finding. Open it instead of duplicating work.</p>{existing.map(id => onOpenExisting
            ? <button key={id} type="button" className={`${systemSecondaryAction} mt-2`} onClick={() => onOpenExisting(id)}>Open existing POA&amp;M</button>
            : <Link key={id} to={`/systems/${systemId}/poam?poam=${encodeURIComponent(id)}`} className="mt-2 block text-indigo-700 underline dark:text-indigo-300">Open existing POA&amp;M</Link>)}</div>}
          {related.length > 0 && <div className="text-sm"><p>Other commitments are connected through shared task work, not this finding as their formal source.</p>{related.map(id => onOpenExisting
            ? <button key={id} type="button" className={`${systemSecondaryAction} mt-2`} onClick={() => onOpenExisting(id)}>Open related POA&amp;M</button>
            : <Link key={id} to={`/systems/${systemId}/poam?poam=${encodeURIComponent(id)}`} className="mt-2 block text-indigo-700 underline dark:text-indigo-300">Open related POA&amp;M</Link>)}</div>}
        </section>}
        <form onSubmit={handleSubmit} className="space-y-4 [&_input]:dark:bg-slate-800 [&_select]:dark:bg-slate-800 [&_textarea]:dark:bg-slate-800 [&_label]:dark:text-slate-300">
          {(error || !canManageRemediation) && (
            <p role="alert" className="text-sm text-red-600">{error ?? 'Permission denied: you cannot manage remediation for this system.'}</p>
          )}
          <div>
            <label htmlFor="poam-weakness" className="mb-1 block text-xs font-medium text-gray-500">Weakness *</label>
            <textarea
              id="poam-weakness"
              required
              rows={2}
              placeholder="Describe the security weakness..."
              className="w-full rounded-lg border px-3 py-2 text-sm"
              value={weakness}
              onChange={e => setWeakness(e.target.value)}
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="poam-control" className="mb-1 block text-xs font-medium text-gray-500">Control ID *</label>
              <input id="poam-control" required placeholder="e.g. AC-2" className="w-full rounded-lg border px-3 py-2 text-sm" value={controlId} onChange={e => setControlId(e.target.value)} />
            </div>
            <div>
              <label htmlFor="poam-source" className="mb-1 block text-xs font-medium text-gray-500">Source</label>
              <input id="poam-source" placeholder="STIG, ACAS, Manual..." className="w-full rounded-lg border px-3 py-2 text-sm" value={source} onChange={e => setSource(e.target.value)} />
            </div>
          </div>

          <div>
            <label htmlFor="poam-severity" className="mb-1 block text-xs font-medium text-gray-500">CAT Severity *</label>
            <select id="poam-severity" className="w-full rounded-lg border px-3 py-2 text-sm" value={severity} onChange={e => setSeverity(e.target.value as 'I' | 'II' | 'III')}>
              <option value="I">CAT I — Critical</option>
              <option value="II">CAT II — High</option>
              <option value="III">CAT III — Medium</option>
            </select>
          </div>

          <ComponentPicker systemId={systemId} selectedIds={componentIds} onChange={setComponentIds} disabled={loading} />

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="poam-owner" className="mb-1 block text-xs font-medium text-gray-500">Point of Contact *</label>
              <input id="poam-owner" required placeholder="Name" className="w-full rounded-lg border px-3 py-2 text-sm" value={poc} onChange={e => setPoc(e.target.value)} />
            </div>
            <div>
              <label htmlFor="poam-email" className="mb-1 block text-xs font-medium text-gray-500">POC Email</label>
              <input id="poam-email" type="email" placeholder="email@example.com" className="w-full rounded-lg border px-3 py-2 text-sm" value={pocEmail} onChange={e => setPocEmail(e.target.value)} />
            </div>
          </div>

          <div>
            <label htmlFor="poam-date" className="mb-1 block text-xs font-medium text-gray-500">Scheduled Completion Date *</label>
            <input id="poam-date" required type="date" className="w-full rounded-lg border px-3 py-2 text-sm" value={dueDate} onChange={e => setDueDate(e.target.value)} />
          </div>

          <div>
            <label htmlFor="poam-resources" className="mb-1 block text-xs font-medium text-gray-500">Resources Required</label>
            <input id="poam-resources" placeholder="Personnel, funding, tools..." className="w-full rounded-lg border px-3 py-2 text-sm" value={resourcesRequired} onChange={e => setResourcesRequired(e.target.value)} />
          </div>

          <div>
            <label htmlFor="poam-comments" className="mb-1 block text-xs font-medium text-gray-500">Comments</label>
            <textarea id="poam-comments" rows={2} placeholder="Additional notes..." className="w-full rounded-lg border px-3 py-2 text-sm" value={comments} onChange={e => setComments(e.target.value)} />
          </div>

          {/* Milestones */}
          <div>
            <div className="mb-2 flex items-center justify-between">
              <label className="text-xs font-medium text-gray-500">Milestones</label>
              <button type="button" onClick={addMilestone} className="text-xs text-indigo-600 hover:text-indigo-700">+ Add Milestone</button>
            </div>
            {milestones.map((m, i) => (
              <div key={i} className="mb-2 flex flex-wrap items-center gap-2">
                <input
                  required aria-label={`Milestone ${i + 1} description`}
                  placeholder="Description"
                  className="flex-1 rounded-lg border px-3 py-1.5 text-sm"
                  value={m.description}
                  onChange={e => updateMilestone(i, 'description', e.target.value)}
                />
                <input
                  required aria-label={`Milestone ${i + 1} target date`}
                  type="date"
                  className="rounded-lg border px-3 py-1.5 text-sm"
                  value={m.targetDate}
                  onChange={e => updateMilestone(i, 'targetDate', e.target.value)}
                />
                <button type="button" aria-label={`Remove milestone ${i + 1}`} onClick={() => removeMilestone(i)} className="text-red-400 hover:text-red-600">&times;</button>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" disabled={loading} onClick={onClose} className={systemSecondaryAction}>Cancel</button>
            <button type="submit" disabled={!canManageRemediation || loading || (mode === 'finding' && (!selectedFinding || existing.length > 0 || !!findings.error))} className={systemPrimaryAction}>
              {loading ? 'Creating...' : 'Create POA&M'}
            </button>
          </div>
        </form>
    </SetupDialog>
  );
}
