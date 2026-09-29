import { useEffect, useRef, useState } from 'react';
import { createDeviation } from '../api/deviations';
import { listPoamItems } from '../api/poam';
import type { CreateDeviationRequest } from '../types/dashboard';
import SetupDialog from '../features/workspace-operations/SetupDialog';
import { useSystemMutationPermission } from './permissions/useSystemMutationPermission';
import { poamErrorMessage } from '../utils/poamErrors';

interface Props {
  systemId: string;
  onClose: () => void;
  onCreated: () => void;
  /** Pre-fill finding ID from assessment context — field becomes read-only. */
  initialFindingId?: string;
  /** Pre-fill control reference from assessment context. */
  initialControlId?: string;
  /** Pre-fill title/description from assessment context. */
  initialTitle?: string;
  initialPoamEntryId?: string;
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

export default function AddDeviationDialog({ systemId, onClose, onCreated, initialFindingId, initialControlId, initialPoamEntryId }: Props) {
  const canManage = useSystemMutationPermission(systemId, 'canManageRemediation');
  const [deviationType, setDeviationType] = useState('');
  const [controlId, setControlId] = useState(initialControlId ?? '');
  const [catSeverity, setCatSeverity] = useState('');
  const [justification, setJustification] = useState('');
  const [compensatingControls, setCompensatingControls] = useState('');
  const [expirationDate, setExpirationDate] = useState(defaultExpiration());
  const [reviewCycle, setReviewCycle] = useState('90');
  const [findingId, setFindingId] = useState(initialFindingId ?? '');
  const [poamEntryId, setPoamEntryId] = useState(initialPoamEntryId ?? '');
  const [poamSearch, setPoamSearch] = useState('');
  const [poamResults, setPoamResults] = useState<{ id: string; controlId: string; weakness: string }[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchError, setSearchError] = useState<string | null>(null);
  const searchRequest = useRef<AbortController | null>(null);
  useEffect(() => () => searchRequest.current?.abort(), [systemId]);

  const isValid = deviationType && controlId.trim() && catSeverity && justification.trim() && expirationDate;

  const handlePoamSearch = async (query: string) => {
    setPoamSearch(query);
    searchRequest.current?.abort();
    setPoamResults([]); setSearchError(null);
    if (query.length < 2) return;
    const controller = new AbortController();
    searchRequest.current = controller;
    try {
      const resp = await listPoamItems(systemId, { search: query, pageSize: 8 }, controller.signal);
      if (!controller.signal.aborted) setPoamResults(resp.items.map(p => ({ id: p.id, controlId: p.controlId, weakness: p.weakness })));
    } catch (failure) {
      if (!controller.signal.aborted) setSearchError(poamErrorMessage(failure));
    }
  };

  const handleSubmit = async () => {
    if (!canManage) { setError('Permission denied: exception requests require remediation management permission.'); return; }
    if (!isValid || saving) return;
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
      setError(poamErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <SetupDialog placement="right" busy={saving} onClose={onClose} title="Request exception" description="A request requires a separate authorized decision; it does not accept risk or change deadlines.">
      <div className="[&_input]:dark:bg-slate-800 [&_textarea]:dark:bg-slate-800 [&_select]:dark:bg-slate-800 [&_label]:dark:text-slate-200">
        {/* Body */}
        <div className="space-y-5">
          {(error || !canManage) && (
            <div className="rounded-md bg-red-50 border border-red-200 p-3">
              <p role="alert" className="text-sm text-red-700">{error ?? 'Permission denied: exception requests require remediation management permission.'}</p>
            </div>
          )}

          {/* Deviation Type */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Deviation Type *</label>
            <div className="grid gap-3 sm:grid-cols-3">
              {DEVIATION_TYPES.map((t) => (
                <button
                  key={t.value}
                  type="button"
                  disabled={saving || !canManage}
                  aria-pressed={deviationType === t.value}
                  onClick={() => setDeviationType(t.value)}
                  className={`rounded-md border px-3 py-2.5 text-left transition ${
                    deviationType === t.value
                      ? 'border-indigo-500 bg-indigo-50 text-indigo-700 ring-1 ring-indigo-500 dark:bg-indigo-950 dark:text-indigo-200'
                      : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200'
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
              <label htmlFor="exception-control" className="block text-sm font-medium text-gray-700 mb-1">Control ID *</label>
              <input
                            id="exception-control"
                            type="text"
                            value={controlId}
                            onChange={(e) => setControlId(e.target.value)}
                            disabled={!!initialControlId}
                placeholder="e.g. AC-2, IA-5(1)"
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            </div>
            <div>
              <label htmlFor="exception-severity" className="block text-sm font-medium text-gray-700 mb-1">Severity *</label>
              <select
                id="exception-severity"
                value={catSeverity}
                onChange={(e) => setCatSeverity(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
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
            <label htmlFor="exception-justification" className="block text-sm font-medium text-gray-700 mb-1">Justification *</label>
            <textarea
              id="exception-justification"
              value={justification}
              onChange={(e) => setJustification(e.target.value)}
              rows={3}
              placeholder="Explain why this deviation is necessary..."
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            />
          </div>

          {/* Compensating Controls */}
          {(deviationType === 'RiskAcceptance' || deviationType === 'Waiver') && (
            <div>
              <label htmlFor="exception-compensating" className="block text-sm font-medium text-gray-700 mb-1">Compensating Controls</label>
              <textarea
                id="exception-compensating"
                value={compensatingControls}
                onChange={(e) => setCompensatingControls(e.target.value)}
                rows={2}
                placeholder="Describe any compensating controls in place..."
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}

          {/* Expiration + Review Cycle */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label htmlFor="exception-expiration" className="block text-sm font-medium text-gray-700 mb-1">Expiration Date *</label>
              <input
                id="exception-expiration"
                type="date"
                value={expirationDate}
                onChange={(e) => setExpirationDate(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            </div>
            <div>
              <label htmlFor="exception-review-cycle" className="block text-sm font-medium text-gray-700 mb-1">Review Cycle</label>
              <select
                id="exception-review-cycle"
                value={reviewCycle}
                onChange={(e) => setReviewCycle(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
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
                <span className="inline-flex items-center gap-1 rounded-full bg-indigo-100 px-3 py-1 text-xs font-medium text-indigo-700">
                  {poamResults.find(p => p.id === poamEntryId)?.controlId ?? poamEntryId.slice(0, 8)}
                  {!initialPoamEntryId && <button type="button" aria-label="Remove POA&M link" onClick={() => { setPoamEntryId(''); setPoamSearch(''); setPoamResults([]); }} className="text-indigo-400 hover:text-indigo-600">&times;</button>}
                </span>
              </div>
            ) : (
              <div className="relative">
                <input
                  type="text"
                  aria-label="Search POA&M entries"
                  value={poamSearch}
                  onChange={(e) => void handlePoamSearch(e.target.value)}
                  placeholder="Search POA&M by control or weakness..."
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
                {searchError && <p role="alert" className="mt-2 text-xs text-red-700 dark:text-red-300">{searchError}</p>}
                {poamResults.length > 0 && (
                  <div className="absolute z-10 mt-1 w-full max-h-40 overflow-y-auto rounded-md border border-gray-200 bg-white shadow-lg">
                    {poamResults.map(p => (
                      <button
                        key={p.id}
                        type="button"
                        onClick={() => { setPoamEntryId(p.id); setPoamSearch(''); setPoamResults([]); }}
                        className="block w-full px-3 py-2 text-left text-sm hover:bg-indigo-50"
                      >
                        <span className="font-mono font-medium text-indigo-700">{p.controlId}</span>
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
            <label htmlFor="exception-finding" className="block text-sm font-medium text-gray-700 mb-1">Finding ID</label>
            <input
                          id="exception-finding"
                          type="text"
                          value={findingId}
                          onChange={(e) => setFindingId(e.target.value)}
                          disabled={!!initialFindingId}
              placeholder="e.g. finding UUID or scan reference"
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
            />
            <p className="mt-1 text-xs text-gray-400">Optional — link to a specific scan finding</p>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button
            type="button"
            disabled={saving}
            onClick={onClose}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-800"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!canManage || !isValid || saving}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {saving ? 'Submitting...' : 'Submit Deviation'}
          </button>
        </div>
      </div>
    </SetupDialog>
  );
}
