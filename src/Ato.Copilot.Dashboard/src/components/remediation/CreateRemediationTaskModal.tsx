import { useState } from 'react';
import { createTask } from '../../api/remediation';

interface Props {
  systemId: string;
  /** When launched from a finding, pre-populates title/severity/findingId.
   *  When launched standalone from the Remediation page, these are omitted
   *  and the user fills them in manually. fix(#441) */
  findingTitle?: string;
  findingId?: string;
  findingSeverity?: string;
  onClose: () => void;
  onCreated?: () => void;
}

const SEVERITY_OPTIONS = ['Critical', 'High', 'Medium', 'Low'];

export default function CreateRemediationTaskModal({
  systemId,
  findingTitle,
  findingId,
  findingSeverity,
  onClose,
  onCreated,
}: Props) {
  const isStandalone = !findingId;

  const [title, setTitle] = useState(findingTitle ?? '');
  const [description, setDescription] = useState('');
  const [controlId, setControlId] = useState('');
  const [severity, setSeverity] = useState(findingSeverity ?? 'Medium');
  const [dueDate, setDueDate] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isValid = title.trim().length > 0;

  const handleSubmit = async () => {
    if (!isValid) return;
    setSaving(true);
    setError(null);
    try {
      await createTask(systemId, {
        title: title.trim(),
        description: description.trim() || undefined,
        findingId: findingId || undefined,
        severity,
        controlId: controlId.trim() || undefined,
        dueDate: dueDate || undefined,
      });
      onCreated?.();
      onClose();
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'response' in err) {
        const resp = (err as { response?: { data?: { error?: string; details?: string } } }).response;
        setError(resp?.data?.details || resp?.data?.error || 'Failed to create task');
      } else {
        setError(err instanceof Error ? err.message : 'Failed to create task');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center" role="dialog" aria-modal="true" aria-label="Create Remediation Task">
      <div className="fixed inset-0 bg-black/40" onClick={onClose} />
      <div className="relative w-full max-w-lg rounded-lg bg-white shadow-xl mx-4">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h3 className="text-lg font-semibold text-gray-900">Create Remediation Task</h3>
            <p className="text-sm text-gray-500">
              {isStandalone ? 'Create a new remediation task for this system' : 'Create a task from this finding'}
            </p>
          </div>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-500">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="px-6 py-4 space-y-4">
          {error && (
            <div className="rounded-md bg-red-50 border border-red-200 p-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}

          {/* Severity — select when standalone, badge when from finding */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Severity</label>
            {isStandalone ? (
              <select
                value={severity}
                onChange={(e) => setSeverity(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              >
                {SEVERITY_OPTIONS.map(s => <option key={s} value={s}>{s}</option>)}
              </select>
            ) : (
              <span className="inline-flex items-center rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800 capitalize">
                {findingSeverity}
              </span>
            )}
          </div>

          {/* Control ID — only shown in standalone mode */}
          {isStandalone && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Control ID</label>
              <input
                type="text"
                value={controlId}
                onChange={(e) => setControlId(e.target.value)}
                placeholder="e.g. AC-2"
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}

          {/* Title */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Task Title *</label>
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Describe the remediation task"
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            />
          </div>

          {/* Description */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              placeholder="Describe the remediation steps..."
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            />
          </div>

          {/* Due Date — only shown in standalone mode */}
          {isStandalone && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Due Date</label>
              <input
                type="date"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}

          {/* Finding ID (informational) — only when launched from finding */}
          {findingId && (
            <p className="text-xs text-gray-400">Finding ID: <span className="font-mono">{findingId}</span></p>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={() => void handleSubmit()}
            disabled={!isValid || saving}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {saving ? 'Creating...' : 'Create Task'}
          </button>
        </div>
      </div>
    </div>
  );
}
