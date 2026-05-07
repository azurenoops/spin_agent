import { useState, useEffect, useRef } from 'react';
import apiClient from '../../../api/client';
import { selectBaseline } from '../../../api/systemDetail';
import type { Sp80060InfoType } from '../../../types/dashboard';
import infoTypesData from '../../../data/sp800-60-information-types.json';

interface SelectedInfoType {
  sp80060Id: string;
  name: string;
  category: string;
  confidentialityImpact: string;
  integrityImpact: string;
  availabilityImpact: string;
  usesProvisional: boolean;
}

interface SetCategorizationProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

const IMPACT_OPTIONS = ['Low', 'Moderate', 'High'];

const IMPACT_COLORS: Record<string, string> = {
  Low: 'bg-green-100 text-green-700',
  Moderate: 'bg-amber-100 text-amber-700',
  High: 'bg-red-100 text-red-700',
};

function highWaterMark(levels: string[]): string {
  if (levels.includes('High')) return 'High';
  if (levels.includes('Moderate')) return 'Moderate';
  return 'Low';
}

export default function SetCategorization({ systemId, onNext, onErrors }: SetCategorizationProps) {
  const allTypes: Sp80060InfoType[] = (infoTypesData as { informationTypes: Sp80060InfoType[] }).informationTypes;
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<SelectedInfoType[]>([]);
  const [isNSS, setIsNSS] = useState(false);
  const [justification, setJustification] = useState('');
  const [saving, setSaving] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [filteredTypes, setFilteredTypes] = useState(allTypes);

  // Phase: 'categorization' → 'baseline'
  const [phase, setPhase] = useState<'categorization' | 'baseline'>('categorization');
  const [applyOverlay, setApplyOverlay] = useState(true);
  const [overlayName, setOverlayName] = useState('');
  const [baselineResult, setBaselineResult] = useState<{ level: string; totalControls: number } | null>(null);
  const [selectingBaseline, setSelectingBaseline] = useState(false);

  // Debounced search
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      if (!search.trim()) {
        setFilteredTypes(allTypes);
      } else {
        const q = search.toLowerCase();
        setFilteredTypes(allTypes.filter((t) => t.name.toLowerCase().includes(q) || t.category.toLowerCase().includes(q) || t.id.toLowerCase().includes(q)));
      }
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [search, allTypes]);

  const selectedIds = new Set(selected.map((s) => s.sp80060Id));

  const handleSelect = (t: Sp80060InfoType) => {
    if (selectedIds.has(t.id)) return;
    setSelected((prev) => [
      ...prev,
      {
        sp80060Id: t.id,
        name: t.name,
        category: t.category,
        confidentialityImpact: t.confidentiality,
        integrityImpact: t.integrity,
        availabilityImpact: t.availability,
        usesProvisional: true,
      },
    ]);
  };

  const handleRemove = (id: string) => {
    setSelected((prev) => prev.filter((s) => s.sp80060Id !== id));
  };

  const handleOverride = (id: string, field: keyof SelectedInfoType, value: string) => {
    setSelected((prev) =>
      prev.map((s) => (s.sp80060Id === id ? { ...s, [field]: value, usesProvisional: false } : s)),
    );
  };

  // Compute FIPS 199
  const overallC = selected.length > 0 ? highWaterMark(selected.map((s) => s.confidentialityImpact)) : 'N/A';
  const overallI = selected.length > 0 ? highWaterMark(selected.map((s) => s.integrityImpact)) : 'N/A';
  const overallA = selected.length > 0 ? highWaterMark(selected.map((s) => s.availabilityImpact)) : 'N/A';
  const overallFips = selected.length > 0 ? highWaterMark([overallC, overallI, overallA]) : 'N/A';

  const handleSaveCategorization = async () => {
    setSaving(true);
    try {
      await apiClient.post(`/systems/${systemId}/categorization`, {
        isNationalSecuritySystem: isNSS,
        justification: justification.trim() || undefined,
        informationTypes: selected.map((s) => ({
          sp80060Id: s.sp80060Id,
          name: s.name,
          category: s.category,
          confidentialityImpact: s.confidentialityImpact,
          integrityImpact: s.integrityImpact,
          availabilityImpact: s.availabilityImpact,
          usesProvisional: s.usesProvisional,
          adjustmentJustification: !s.usesProvisional
            ? (justification.trim() || 'Impact levels adjusted per system risk assessment')
            : undefined,
        })),
      });
      setPhase('baseline');
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } };
      const msg = axiosErr?.response?.data?.error ?? (err instanceof Error ? err.message : 'Failed to save categorization');
      onErrors({ _form: [msg] });
    } finally {
      setSaving(false);
    }
  };

  const handleSelectBaseline = async () => {
    setSelectingBaseline(true);
    try {
      const res = await selectBaseline(systemId, {
        applyOverlay,
        overlayName: overlayName || undefined,
      });
      setBaselineResult({ level: res.baselineLevel, totalControls: res.totalControls });
    } catch {
      onErrors({ baseline: ['Failed to select baseline. Ensure the system has a valid categorization.'] });
    } finally {
      setSelectingBaseline(false);
    }
  };

  // Group types by category
  const categories = [...new Set(filteredTypes.map((t) => t.category))];

  // ─── Phase 2: Select Baseline ────────────────────────────────────────────

  if (phase === 'baseline') {
    return (
      <div className="space-y-6">
        <div>
          <h2 className="text-xl font-semibold text-gray-900">Select Baseline</h2>
          <p className="text-sm text-gray-500 mt-1">
            The NIST 800-53 control baseline is derived from the categorization you just saved.
            Optionally apply a CNSSI 1253 overlay for DoD systems.
          </p>
        </div>

        {/* Categorization summary */}
        {selected.length > 0 && (
          <div className="rounded-xl border border-gray-200 bg-white p-5">
            <h3 className="text-sm font-semibold text-gray-900 mb-3">FIPS 199 Security Categorization</h3>
            <div className="grid grid-cols-4 gap-4">
              {[
                { label: 'Confidentiality', value: overallC },
                { label: 'Integrity', value: overallI },
                { label: 'Availability', value: overallA },
                { label: 'Overall', value: overallFips },
              ].map(dim => (
                <div key={dim.label} className="text-center">
                  <p className="text-xs font-medium uppercase tracking-wider text-gray-500 mb-1">{dim.label}</p>
                  <span className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${IMPACT_COLORS[dim.value] ?? 'bg-gray-100 text-gray-700'}`}>
                    {dim.value}
                  </span>
                </div>
              ))}
            </div>
            <p className="mt-3 text-xs text-gray-500">
              {selected.length} information type{selected.length !== 1 ? 's' : ''} selected
            </p>
          </div>
        )}

        {!baselineResult ? (
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

            <div className="flex gap-3">
              <button
                onClick={handleSelectBaseline}
                disabled={selectingBaseline}
                className="rounded-lg bg-indigo-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {selectingBaseline ? 'Selecting...' : 'Select Baseline'}
              </button>
              <button
                onClick={onNext}
                className="rounded-lg border border-gray-300 px-5 py-2.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                Skip Baseline
              </button>
            </div>
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
                    {baselineResult.level} baseline with {baselineResult.totalControls} controls
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

  // ─── Phase 1: Set Categorization ─────────────────────────────────────────

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Categorization &amp; Baseline</h2>
      <p className="text-sm text-gray-500 mb-6">Select SP 800-60 information types, review the FIPS 199 categorization, then select the control baseline.</p>

      {/* FIPS 199 Summary */}
      {selected.length > 0 && (
        <div className="mb-6 rounded-md border border-indigo-200 bg-indigo-50 p-4">
          <h3 className="text-sm font-medium text-indigo-900 mb-2">FIPS 199 Overall Categorization: <span className="font-bold">{overallFips}</span></h3>
          <div className="flex gap-6 text-sm">
            <span>Confidentiality: <strong>{overallC}</strong></span>
            <span>Integrity: <strong>{overallI}</strong></span>
            <span>Availability: <strong>{overallA}</strong></span>
          </div>
        </div>
      )}

      {/* Selected types */}
      {selected.length > 0 && (
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-700 mb-2">Selected Information Types ({selected.length})</h3>
          <div className="overflow-hidden rounded-md border border-gray-200">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-3 py-2 text-left text-xs font-medium text-gray-500">Info Type</th>
                  <th className="px-3 py-2 text-center text-xs font-medium text-gray-500">C</th>
                  <th className="px-3 py-2 text-center text-xs font-medium text-gray-500">I</th>
                  <th className="px-3 py-2 text-center text-xs font-medium text-gray-500">A</th>
                  <th className="px-3 py-2 w-8" />
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {selected.map((s) => (
                  <tr key={s.sp80060Id}>
                    <td className="px-3 py-2">
                      <span className="font-medium">{s.name}</span>
                      <span className="ml-1 text-xs text-gray-400">{s.sp80060Id}</span>
                    </td>
                    <td className="px-3 py-2 text-center">
                      <select value={s.confidentialityImpact} onChange={(e) => handleOverride(s.sp80060Id, 'confidentialityImpact', e.target.value)} className="rounded border border-gray-300 px-1 py-0.5 text-xs">
                        {IMPACT_OPTIONS.map((o) => <option key={o} value={o}>{o}</option>)}
                      </select>
                    </td>
                    <td className="px-3 py-2 text-center">
                      <select value={s.integrityImpact} onChange={(e) => handleOverride(s.sp80060Id, 'integrityImpact', e.target.value)} className="rounded border border-gray-300 px-1 py-0.5 text-xs">
                        {IMPACT_OPTIONS.map((o) => <option key={o} value={o}>{o}</option>)}
                      </select>
                    </td>
                    <td className="px-3 py-2 text-center">
                      <select value={s.availabilityImpact} onChange={(e) => handleOverride(s.sp80060Id, 'availabilityImpact', e.target.value)} className="rounded border border-gray-300 px-1 py-0.5 text-xs">
                        {IMPACT_OPTIONS.map((o) => <option key={o} value={o}>{o}</option>)}
                      </select>
                    </td>
                    <td className="px-3 py-2 text-center">
                      <button onClick={() => handleRemove(s.sp80060Id)} className="text-red-500 hover:text-red-700 text-xs">✕</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Search */}
      <div className="mb-3">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          placeholder="Search information types..."
        />
      </div>

      {/* Info type list grouped by category */}
      <div className="border border-gray-200 rounded-md max-h-56 overflow-y-auto mb-6">
        {categories.map((cat) => (
          <div key={cat}>
            <div className="sticky top-0 bg-gray-100 px-3 py-1 text-xs font-semibold text-gray-600">{cat}</div>
            {filteredTypes
              .filter((t) => t.category === cat)
              .map((t) => (
                <button
                  key={t.id}
                  onClick={() => handleSelect(t)}
                  disabled={selectedIds.has(t.id)}
                  className={`w-full text-left px-3 py-1.5 text-sm border-b border-gray-100 hover:bg-indigo-50 ${selectedIds.has(t.id) ? 'opacity-50' : ''}`}
                >
                  <span className="text-xs text-gray-400 mr-2">{t.id}</span>
                  {t.name}
                  <span className="float-right text-xs text-gray-400">{t.confidentiality}/{t.integrity}/{t.availability}</span>
                </button>
              ))}
          </div>
        ))}
      </div>

      {/* Options */}
      <div className="space-y-3 mb-6">
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={isNSS} onChange={(e) => setIsNSS(e.target.checked)} className="rounded" />
          National Security System
        </label>
        <div>
          <label className="mb-1 block text-sm font-medium text-gray-700">Justification</label>
          <textarea
            value={justification}
            onChange={(e) => setJustification(e.target.value)}
            rows={2}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            placeholder="Optional justification for categorization decisions"
          />
        </div>
      </div>

      <div className="flex justify-end gap-3 mt-6">
        <button
          onClick={onNext}
          className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
        >
          Skip
        </button>
        <button
          onClick={handleSaveCategorization}
          disabled={saving}
          className="rounded-md bg-indigo-600 px-6 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {saving ? 'Saving...' : 'Save & Select Baseline'}
        </button>
      </div>
    </div>
  );
}
