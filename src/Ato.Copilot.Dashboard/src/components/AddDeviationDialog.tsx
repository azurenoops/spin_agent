import { useState } from 'react';
import { createDeviation } from '../api/deviations';
import { listPoamItems } from '../api/poam';
import type { CreateDeviationRequest } from '../types/dashboard';

interface Props {
  systemId: string;
  onClose: () => void;
  onCreated: () => void;
}

const DEVIATION_TYPES = [
  { value: 'FalsePositive', label: 'False Positive', description: 'Finding is incorrect or does not apply' },
  { value: 'RiskAcceptance', label: 'Risk Acceptance', description: 'Known risk accepted with compensating controls' },
  { value: 'Waiver', label: 'Waiver', description: 'Temporary exception approved by AO/ISSM' },
];

const SEVERITIES = [
  { value: 'CatI', label: 'CAT I — Critical', color: 'text-red-600' },
  { value: 'CatII', label: 'CAT II — High', color: 'text-amber-600' },
  { value: 'CatIII', label: 'CAT III — Medium', color: 'text-yellow-600' },
];

const REVIEW_CYCLES = [
  { value: '90', label: 'Every 90 days' },
  { value: '180', label: 'Every 180 days' },
  { value: '365', label: 'Annual' },
];

function defaultExpiration(): string {
  const d = new Date();
  d.setFullYear(d.getFullYear() + 1);
  return d.toISOString().split('T')[0] ?? '';
}

export default function AddDeviationDialog({ systemId, onClose, onCreated }: Props) {
  const [deviationType, setDeviationType] = useState('');
  const [controlId, setControlId] = useState('');
  const [catSeverity, setCatSeverity] = useState('');
  const [justification, setJustification] = useState('');
  const [compensatingControls, setCompensatingControls] = useState('');
  const [expirationDate, setExpirationDate] = useState(defaultExpiration());
  const [reviewCycle, setReviewCycle] = useState('90');
  const [findingId, setFindingId] = useState('');
  const [poamEntryId, setPoamEntryId] = useState('');
  const [poamSearch, setPoamSearch] = useState('');
  const [poamResults, setPoamResults] = useState<{ id: string; controlId: string; weakness: string }[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isValid = deviationType && controlId.trim() && catSeverity && justification.trim() && expirationDate;

  const handlePoamSearch = async (query: string) => {
    setPoamSearch(query);
    if (query.length < 2) { setPoamResults([]); return; }
    try {
      const resp = await listPoamItems(systemId, { search: query, pageSize: 8 });
      setPoamResults(resp.items.map(p => ({ id: p.id, controlId: p.controlId, weakness: p.weakness })));
    } catch {
      setPoamResults([]);
    }
  };

  const handleSubmit = async () => {
    if (!isValid) return;
    setSaving(true);
    setError(null);
    try {
      const request: CreateDeviationRequest = {
        deviationType,
        controlId: controlId.trim().toUpperCase(),
        catSeverity,
        justification: justification.trim(),
        compensatingControls: compensatingControls.trim() || undefined,
        expirationDate,
        reviewCycle,
        findingId: findingId.trim() || undefined,
        poamEntryId: poamEntryId || undefined,
      };
      await createDeviation(systemId, request);
      onCreated();
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'response' in err) {
        const resp = (err as { response?: { data?: { error?: string; details?: string } } }).response;
        setError(resp?.data?.details || resp?.data?.error || 'Failed to create deviation');
      } else {
        setError(err instanceof Error ? err.message : 'Failed to create deviation');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/40" onClick={onClose} />
      <div className="relative w-full max-w-xl rounded-lg bg-white shadow-xl mx-4 max-h-[85vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h3 className="text-lg font-semibold text-gray-900">Add Deviation</h3>
            <p className="text-sm text-gray-500">Request a false positive, risk acceptance, or waiver</p>
          </div>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-500">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && (
            <div className="rounded-md bg-red-50 border border-red-200 p-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}

          {/* Deviation Type */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Deviation Type *</label>
            <div className="grid grid-cols-3 gap-3">
              {DEVIATION_TYPES.map((t) => (
                <button
                  key={t.value}
                  type="button"
                  onClick={() => setDeviationType(t.value)}
                  className={`rounded-md border px-3 py-2.5 text-left transition ${
                    deviationType === t.value
                      ? 'border-blue-500 bg-blue-50 text-blue-700 ring-1 ring-blue-500'
                      : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300'
                  }`}
                >
                  <div className="text-sm font-medium">{t.label}</div>
                  <div className="text-xs opacity-70 mt-0.5">{t.description}</div>
                </button>
              ))}
            </div>
          </div>

          {/* Control ID + Severity (side by side) */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Control ID *</label>
              <input
                type="text"
                value={controlId}
                onChange={(e) => setControlId(e.target.value)}
                placeholder="e.g. AC-2, IA-5(1)"
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Severity *</label>
              <select
                value={catSeverity}
                onChange={(e) => setCatSeverity(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
              >
                <option value="">Select severity...</option>
                {SEVERITIES.map((s) => (
                  <option key={s.value} value={s.value}>{s.label}</option>
                ))}
              </select>
            </div>
          </div>

          {/* Justification */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Justification *</label>
            <textarea
              value={justification}
              onChange={(e) => setJustification(e.target.value)}
              rows={3}
              placeholder="Explain why this deviation is necessary..."
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
            />
          </div>

          {/* Compensating Controls */}
          {(deviationType === 'RiskAcceptance' || deviationType === 'Waiver') && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Compensating Controls</label>
              <textarea
                value={compensatingControls}
                onChange={(e) => setCompensatingControls(e.target.value)}
                rows={2}
                placeholder="Describe any compensating controls in place..."
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
              />
            </div>
          )}

          {/* Expiration + Review Cycle */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Expiration Date *</label>
              <input
                type="date"
                value={expirationDate}
                onChange={(e) => setExpirationDate(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Review Cycle</label>
              <select
                value={reviewCycle}
                onChange={(e) => setReviewCycle(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
              >
                {REVIEW_CYCLES.map((r) => (
                  <option key={r.value} value={r.value}>{r.label}</option>
                ))}
              </select>
            </div>
          </div>

          {/* Link to POA&M (optional) */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Link to POA&M Entry</label>
            {poamEntryId ? (
              <div className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1 rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700">
                  {poamResults.find(p => p.id === poamEntryId)?.controlId ?? poamEntryId.slice(0, 8)}
                  <button type="button" onClick={() => { setPoamEntryId(''); setPoamSearch(''); setPoamResults([]); }} className="text-blue-400 hover:text-blue-600">&times;</button>
                </span>
              </div>
            ) : (
              <div className="relative">
                <input
                  type="text"
                  value={poamSearch}
                  onChange={(e) => void handlePoamSearch(e.target.value)}
                  placeholder="Search POA&M by control or weakness..."
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
                />
                {poamResults.length > 0 && (
                  <div className="absolute z-10 mt-1 w-full max-h-40 overflow-y-auto rounded-md border border-gray-200 bg-white shadow-lg">
                    {poamResults.map(p => (
                      <button
                        key={p.id}
                        type="button"
                        onClick={() => { setPoamEntryId(p.id); setPoamSearch(''); setPoamResults([]); }}
                        className="block w-full px-3 py-2 text-left text-sm hover:bg-blue-50"
                      >
                        <span className="font-mono font-medium text-blue-700">{p.controlId}</span>
                        <span className="ml-2 text-gray-500 truncate">{p.weakness}</span>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}
            <p className="mt-1 text-xs text-gray-400">Optional — link this deviation to an existing POA&M entry</p>
          </div>

          {/* Finding ID (optional) */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Finding ID</label>
            <input
              type="text"
              value={findingId}
              onChange={(e) => setFindingId(e.target.value)}
              placeholder="e.g. finding UUID or scan reference"
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
            />
            <p className="mt-1 text-xs text-gray-400">Optional — link to a specific scan finding</p>
          </div>
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
            onClick={handleSubmit}
            disabled={!isValid || saving}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {saving ? 'Submitting...' : 'Submit Deviation'}
          </button>
        </div>
      </div>
    </div>
  );
}
