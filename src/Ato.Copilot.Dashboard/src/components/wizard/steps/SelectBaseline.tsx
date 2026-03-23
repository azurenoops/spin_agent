import { useState, useEffect } from 'react';
import { selectBaseline, getSystemDetail } from '../../../api/systemDetail';
import type { CategorizationInfo } from '../../../types/dashboard';

const IMPACT_COLORS: Record<string, string> = {
  Low: 'bg-green-100 text-green-700',
  Moderate: 'bg-amber-100 text-amber-700',
  High: 'bg-red-100 text-red-700',
};

interface SelectBaselineProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

export default function SelectBaseline({ systemId, onNext, onErrors }: SelectBaselineProps) {
  const [applyOverlay, setApplyOverlay] = useState(true);
  const [overlayName, setOverlayName] = useState('');
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<{ level: string; totalControls: number } | null>(null);
  const [categorization, setCategorization] = useState<CategorizationInfo | null>(null);
  const [catLoading, setCatLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const detail = await getSystemDetail(systemId);
        setCategorization(detail.categorization ?? null);
      } catch {
        // Categorization may not be set yet — that's OK
      } finally {
        setCatLoading(false);
      }
    })();
  }, [systemId]);

  const handleSelect = async () => {
    setSaving(true);
    try {
      const res = await selectBaseline(systemId, {
        applyOverlay,
        overlayName: overlayName || undefined,
      });
      setResult({ level: res.baselineLevel, totalControls: res.totalControls });
    } catch {
      onErrors({ baseline: ['Failed to select baseline. Ensure the system has a valid categorization.'] });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-bold text-gray-900">Select Baseline</h2>
        <p className="mt-1 text-sm text-gray-500">
          The NIST 800-53 control baseline is derived from the system&apos;s FIPS 199 security
          categorization set in the previous step. Optionally apply a CNSSI 1253 overlay for
          DoD systems.
        </p>
      </div>

      {/* Categorization Summary */}
      {catLoading ? (
        <div className="h-24 animate-pulse rounded-xl border border-gray-200 bg-gray-50" />
      ) : categorization ? (
        <div className="rounded-xl border border-gray-200 bg-white p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-3">FIPS 199 Security Categorization</h3>
          <div className="grid grid-cols-4 gap-4">
            {(['confidentiality', 'integrity', 'availability'] as const).map(dim => (
              <div key={dim} className="text-center">
                <p className="text-xs font-medium uppercase tracking-wider text-gray-500 mb-1">
                  {dim}
                </p>
                <span className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${IMPACT_COLORS[categorization[dim]] ?? 'bg-gray-100 text-gray-700'}`}>
                  {categorization[dim]}
                </span>
              </div>
            ))}
            <div className="text-center">
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500 mb-1">Overall</p>
              <span className={`inline-flex rounded-full px-3 py-1 text-xs font-bold ${IMPACT_COLORS[categorization.overall] ?? 'bg-gray-100 text-gray-700'}`}>
                {categorization.overall}
              </span>
            </div>
          </div>
          {categorization.dodImpactLevel && (
            <p className="mt-3 text-xs text-gray-500">
              DoD Impact Level: <span className="font-medium text-gray-700">{categorization.dodImpactLevel}</span>
              {' · '}{categorization.informationTypes.length} information type{categorization.informationTypes.length !== 1 ? 's' : ''}
            </p>
          )}
        </div>
      ) : (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-5 py-4">
          <p className="text-sm text-amber-800">
            <strong>No categorization found.</strong> Go back to the Set Categorization step to define
            information types and impact levels before selecting a baseline.
          </p>
        </div>
      )}

      {!result ? (
        <div className="space-y-4 rounded-xl border border-gray-200 bg-white p-6">
          <label className="flex items-center gap-3 text-sm">
            <input
              type="checkbox"
              checked={applyOverlay}
              onChange={e => setApplyOverlay(e.target.checked)}
              className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
            />
            <span className="text-gray-700">Apply CNSSI 1253 overlay (recommended for DoD systems)</span>
          </label>

          {applyOverlay && (
            <div>
              <label className="block text-xs font-medium text-gray-700">Overlay Name (optional)</label>
              <input
                type="text"
                value={overlayName}
                onChange={e => setOverlayName(e.target.value)}
                placeholder="Auto-detected from DoD IL (e.g., CNSSI 1253 IL5)"
                className="mt-1 block w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}

          <button
            onClick={handleSelect}
            disabled={saving || !categorization}
            className="rounded-lg bg-indigo-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {saving ? 'Selecting...' : 'Select Baseline'}
          </button>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="rounded-xl border border-green-200 bg-green-50 p-6">
            <div className="flex items-center gap-3">
              <svg className="h-6 w-6 text-green-600" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <div>
                <p className="text-sm font-semibold text-green-800">Baseline Selected</p>
                <p className="text-sm text-green-700">
                  {result.level} baseline with {result.totalControls} controls
                  {applyOverlay ? ` (overlay: ${overlayName || 'CNSSI 1253'})` : ''}
                </p>
              </div>
            </div>
          </div>

          <button
            onClick={onNext}
            className="rounded-lg bg-indigo-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-indigo-700"
          >
            Continue
          </button>
        </div>
      )}
    </div>
  );
}
