import { useState } from 'react';
import type { CreatePoamRequest } from '../../types/poam';
import ComponentPicker from './ComponentPicker';

interface PoamCreateFormProps {
  onClose: () => void;
  onSubmit: (req: CreatePoamRequest) => Promise<void>;
  loading: boolean;
}

export default function PoamCreateForm({ onClose, onSubmit, loading }: PoamCreateFormProps) {
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

  const addMilestone = () => setMilestones(ms => [...ms, { description: '', targetDate: '' }]);
  const updateMilestone = (i: number, field: string, value: string) =>
    setMilestones(ms => ms.map((m, idx) => idx === i ? { ...m, [field]: value } : m));
  const removeMilestone = (i: number) => setMilestones(ms => ms.filter((_, idx) => idx !== i));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await onSubmit({
      weakness,
      weaknessSource: source || 'Manual',
      controlId,
      catSeverity: severity,
      poc,
      pocEmail: pocEmail || undefined,
      scheduledCompletionDate: dueDate,
      resourcesRequired: resourcesRequired || undefined,
      comments: comments || undefined,
      milestones: milestones.filter(m => m.description && m.targetDate).length > 0
        ? milestones.filter(m => m.description && m.targetDate)
        : undefined,
      componentIds: componentIds.length > 0 ? componentIds : undefined,
    });
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30" onClick={onClose}>
      <div className="w-full max-w-lg rounded-xl bg-white p-6 shadow-xl" onClick={e => e.stopPropagation()}>
        <h2 className="mb-4 text-lg font-bold text-gray-900">New POA&amp;M Item</h2>
        <form onSubmit={handleSubmit} className="max-h-[70vh] space-y-3 overflow-y-auto pr-1">
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500">Weakness *</label>
            <textarea
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
              <label className="mb-1 block text-xs font-medium text-gray-500">Control ID *</label>
              <input required placeholder="e.g. AC-2" className="w-full rounded-lg border px-3 py-2 text-sm" value={controlId} onChange={e => setControlId(e.target.value)} />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-500">Source</label>
              <input placeholder="STIG, ACAS, Manual..." className="w-full rounded-lg border px-3 py-2 text-sm" value={source} onChange={e => setSource(e.target.value)} />
            </div>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500">CAT Severity *</label>
            <select className="w-full rounded-lg border px-3 py-2 text-sm" value={severity} onChange={e => setSeverity(e.target.value as 'I' | 'II' | 'III')}>
              <option value="I">CAT I — Critical</option>
              <option value="II">CAT II — High</option>
              <option value="III">CAT III — Medium</option>
            </select>
          </div>

          <ComponentPicker selectedIds={componentIds} onChange={setComponentIds} />

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-500">Point of Contact *</label>
              <input required placeholder="Name" className="w-full rounded-lg border px-3 py-2 text-sm" value={poc} onChange={e => setPoc(e.target.value)} />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-500">POC Email</label>
              <input type="email" placeholder="email@example.com" className="w-full rounded-lg border px-3 py-2 text-sm" value={pocEmail} onChange={e => setPocEmail(e.target.value)} />
            </div>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500">Scheduled Completion Date *</label>
            <input required type="date" className="w-full rounded-lg border px-3 py-2 text-sm" value={dueDate} onChange={e => setDueDate(e.target.value)} />
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500">Resources Required</label>
            <input placeholder="Personnel, funding, tools..." className="w-full rounded-lg border px-3 py-2 text-sm" value={resourcesRequired} onChange={e => setResourcesRequired(e.target.value)} />
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500">Comments</label>
            <textarea rows={2} placeholder="Additional notes..." className="w-full rounded-lg border px-3 py-2 text-sm" value={comments} onChange={e => setComments(e.target.value)} />
          </div>

          {/* Milestones */}
          <div>
            <div className="mb-2 flex items-center justify-between">
              <label className="text-xs font-medium text-gray-500">Milestones</label>
              <button type="button" onClick={addMilestone} className="text-xs text-indigo-600 hover:text-indigo-700">+ Add Milestone</button>
            </div>
            {milestones.map((m, i) => (
              <div key={i} className="mb-2 flex items-center gap-2">
                <input
                  placeholder="Description"
                  className="flex-1 rounded-lg border px-3 py-1.5 text-sm"
                  value={m.description}
                  onChange={e => updateMilestone(i, 'description', e.target.value)}
                />
                <input
                  type="date"
                  className="rounded-lg border px-3 py-1.5 text-sm"
                  value={m.targetDate}
                  onChange={e => updateMilestone(i, 'targetDate', e.target.value)}
                />
                <button type="button" onClick={() => removeMilestone(i)} className="text-red-400 hover:text-red-600">&times;</button>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-sm">Cancel</button>
            <button type="submit" disabled={loading} className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50">
              {loading ? 'Creating...' : 'Create POA&M'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
