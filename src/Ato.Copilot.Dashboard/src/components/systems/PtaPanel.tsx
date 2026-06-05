import { useState, useCallback } from 'react';
import {
  createPta,
  type CreatePtaResponse,
  type CreatePtaRequest,
} from '../../api/systemDetail';

// ─── Props ────────────────────────────────────────────────────────────────────

interface PtaPanelProps {
  systemId: string;
  /** True once at least one profile section has been saved (status >= Draft). */
  sectionsSaved: boolean;
  /** Called when a PTA result is received so parent can unlock PIA. */
  onPtaComplete?: (completed: boolean) => void;
}

// ─── PTA Panel ────────────────────────────────────────────────────────────────

/**
 * PTA (Privacy Threshold Analysis) initiation panel.
 * Blocked until `sectionsSaved === true` — profile sections must exist before
 * we can run a PTA determination.
 *
 * Closes #253.
 */
export default function PtaPanel({ systemId, sectionsSaved, onPtaComplete }: PtaPanelProps) {
  const [pta, setPta] = useState<CreatePtaResponse | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // PTA form state
  const [collectsPii, setCollectsPii] = useState(false);
  const [maintainsPii, setMaintainsPii] = useState(false);
  const [disseminatesPii, setDisseminatesPii] = useState(false);
  const [piiCategories, setPiiCategories] = useState('');
  const [purpose, setPurpose] = useState('');
  const [showForm, setShowForm] = useState(false);

  const handleInitiate = useCallback(async () => {
    setSubmitting(true);
    setError(null);
    try {
      const categories = piiCategories
        .split(',')
        .map((c) => c.trim())
        .filter(Boolean);

      const body: CreatePtaRequest = {
        collectsPii,
        maintainsPii,
        disseminatesPii,
        ...(categories.length > 0 ? { piiCategories: categories } : {}),
        ...(purpose.trim() ? { purpose: purpose.trim() } : {}),
      };

      const result = await createPta(systemId, body);
      setPta(result);
      setShowForm(false);
      // Notify parent so PIA panel can unlock
      onPtaComplete?.(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'PTA initiation failed');
    } finally {
      setSubmitting(false);
    }
  }, [systemId, collectsPii, maintainsPii, disseminatesPii, piiCategories, purpose]);

  return (
    <div className="rounded-xl border border-gray-200 bg-white p-5 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-gray-900">Privacy Threshold Analysis (PTA)</h2>
          <p className="text-sm text-gray-500 mt-0.5">
            Determine whether this system collects, maintains, or disseminates PII.
          </p>
        </div>
        {pta && (
          <span className="rounded-full bg-green-100 text-green-700 px-2.5 py-0.5 text-xs font-medium">
            Complete
          </span>
        )}
      </div>

      {/* Existing PTA result */}
      {pta && (
        <div className="rounded-lg border border-gray-100 bg-gray-50 divide-y divide-gray-100 text-sm">
          <div className="flex justify-between px-4 py-2.5">
            <span className="text-gray-500">PTA ID</span>
            <span className="font-mono text-xs text-gray-700">{pta.ptaId}</span>
          </div>
          <div className="flex justify-between px-4 py-2.5">
            <span className="text-gray-500">Determination</span>
            <span
              className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
                pta.determination === 'PIA Required'
                  ? 'bg-red-100 text-red-700'
                  : pta.determination === 'PTA Only'
                  ? 'bg-amber-100 text-amber-700'
                  : 'bg-green-100 text-green-700'
              }`}
            >
              {pta.determination}
            </span>
          </div>
          <div className="flex justify-between px-4 py-2.5">
            <span className="text-gray-500">Collects PII</span>
            <span className={pta.collectsPii ? 'text-red-600 font-medium' : 'text-gray-600'}>
              {pta.collectsPii ? 'Yes' : 'No'}
            </span>
          </div>
          {pta.piiCategories && pta.piiCategories.length > 0 && (
            <div className="px-4 py-2.5">
              <span className="text-gray-500 block mb-1.5">PII Categories</span>
              <div className="flex flex-wrap gap-1.5">
                {pta.piiCategories.map((cat) => (
                  <span key={cat} className="rounded-full bg-indigo-50 text-indigo-700 px-2 py-0.5 text-xs font-medium">
                    {cat}
                  </span>
                ))}
              </div>
            </div>
          )}
          <div className="px-4 py-2.5">
            <span className="text-gray-500 block mb-1">Rationale</span>
            <p className="text-xs text-gray-700 leading-relaxed">{pta.rationale}</p>
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Gate: profile sections must be saved */}
      {!sectionsSaved && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-700">
          Complete and save at least one profile section before initiating the PTA.
        </div>
      )}

      {/* Initiation form */}
      {sectionsSaved && showForm && (
        <div className="rounded-lg border border-gray-200 p-4 space-y-4">
          <h3 className="text-sm font-semibold text-gray-800">PTA Details</h3>

          <div className="space-y-2">
            {(
              [
                { key: 'collectsPii',     label: 'System collects PII',      value: collectsPii,     set: setCollectsPii },
                { key: 'maintainsPii',    label: 'System maintains PII',     value: maintainsPii,    set: setMaintainsPii },
                { key: 'disseminatesPii', label: 'System disseminates PII',  value: disseminatesPii, set: setDisseminatesPii },
              ] as const
            ).map(({ key, label, value, set }) => (
              <label key={key} className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border-gray-300 text-indigo-600"
                  checked={value}
                  onChange={(e) => (set as (v: boolean) => void)(e.target.checked)}
                />
                {label}
              </label>
            ))}
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              PII Categories <span className="text-gray-400 font-normal">(comma-separated)</span>
            </label>
            <input
              type="text"
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              placeholder="e.g. Name, SSN, Email, Phone Number"
              value={piiCategories}
              onChange={(e) => setPiiCategories(e.target.value)}
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Purpose</label>
            <textarea
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              rows={3}
              placeholder="Describe why PII is collected/used…"
              value={purpose}
              onChange={(e) => setPurpose(e.target.value)}
            />
          </div>

          <div className="flex gap-2 justify-end">
            <button
              type="button"
              onClick={() => setShowForm(false)}
              className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 transition-colors"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleInitiate}
              disabled={submitting}
              className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {submitting ? (
                <>
                  <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                  </svg>
                  Initiating…
                </>
              ) : (
                'Submit PTA'
              )}
            </button>
          </div>
        </div>
      )}

      {/* Open form / re-run button */}
      {sectionsSaved && !showForm && (
        <div className="flex justify-end">
          <button
            type="button"
            onClick={() => setShowForm(true)}
            className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 transition-colors"
          >
            {pta ? 'Re-run PTA' : 'Initiate PTA'}
          </button>
        </div>
      )}
    </div>
  );
}
