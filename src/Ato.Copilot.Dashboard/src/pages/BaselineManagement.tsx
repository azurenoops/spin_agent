import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import {
  getBaselineDetail,
  getSystemDetail,
  selectBaseline,
  setCategorization as apiSetCategorization,
  type BaselineDetailResponse,
  type InfoTypeInput,
} from '../api/systemDetail';
import type { CategorizationInfo, Sp80060InfoType } from '../types/dashboard';
import { useSettings } from '../hooks/useSettings';
import infoTypesData from '../data/sp800-60-information-types.json';

// ─── Summary Card ────────────────────────────────────────────────────────────

function Card({ label, value, color }: { label: string; value: number | string; color: string }) {
  const colorMap: Record<string, string> = {
    blue: 'bg-blue-50 text-blue-700 border-blue-200',
    green: 'bg-green-50 text-green-700 border-green-200',
    indigo: 'bg-indigo-50 text-indigo-700 border-indigo-200',
    amber: 'bg-amber-50 text-amber-700 border-amber-200',
    gray: 'bg-gray-50 text-gray-600 border-gray-200',
    red: 'bg-red-50 text-red-700 border-red-200',
  };
  return (
    <div className={`rounded-xl border p-4 ${colorMap[color] ?? 'bg-gray-50 text-gray-700 border-gray-200'}`}>
      <p className="text-xs font-medium uppercase tracking-wider opacity-75">{label}</p>
      <p className="mt-1 text-2xl font-bold">{value}</p>
    </div>
  );
}

// ─── Level Badge ─────────────────────────────────────────────────────────────

function LevelBadge({ level }: { level: string }) {
  const map: Record<string, string> = {
    High: 'bg-red-100 text-red-700 ring-red-200',
    Moderate: 'bg-amber-100 text-amber-700 ring-amber-200',
    Low: 'bg-green-100 text-green-700 ring-green-200',
  };
  return (
    <span className={`inline-flex items-center rounded-full px-3 py-1 text-sm font-semibold ring-1 ring-inset ${map[level] ?? 'bg-gray-100 text-gray-700 ring-gray-200'}`}>
      {level}
    </span>
  );
}

// ─── Main Page ───────────────────────────────────────────────────────────────

export default function BaselineManagement() {
  const { id: systemId } = useParams<{ id: string }>();
  const { settings } = useSettings();

  const [baseline, setBaseline] = useState<BaselineDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [noBaseline, setNoBaseline] = useState(false);
  const [categorization, setCategorization] = useState<CategorizationInfo | null>(null);

  // Select dialog
  const [showSelectDialog, setShowSelectDialog] = useState(false);
  const [showRecategorizeDialog, setShowRecategorizeDialog] = useState(false);
  const [applyOverlay, setApplyOverlay] = useState(true);
  const [overlayName, setOverlayName] = useState('');
  const [selecting, setSelecting] = useState(false);

  // Family table filter
  const [familySearch, setFamilySearch] = useState('');

  // Cascade banner
  const [cascadeBanner, setCascadeBanner] = useState<string | null>(null);

  const fetchBaseline = useCallback(async () => {
    if (!systemId) return;
    setLoading(true);
    setNoBaseline(false);
    try {
      const [data, detail] = await Promise.all([
        getBaselineDetail(systemId).catch((err: unknown) => {
          const status = (err as { response?: { status?: number } })?.response?.status;
          if (status === 404) setNoBaseline(true);
          return null;
        }),
        getSystemDetail(systemId).catch(() => null),
      ]);
      setBaseline(data);
      setCategorization(detail?.categorization ?? null);
    } finally {
      setLoading(false);
    }
  }, [systemId]);

  useEffect(() => { fetchBaseline(); }, [fetchBaseline]);

  const handleCategorizationSaved = async (cascade?: { baselineReselected: string; baselineControls: number; inheritancesReapplied: number } | null) => {
    if (cascade) {
      setCascadeBanner(
        `Baseline auto-updated to ${cascade.baselineReselected} (${cascade.baselineControls} controls). ${cascade.inheritancesReapplied} inheritance designation${cascade.inheritancesReapplied !== 1 ? 's' : ''} reapplied.`
      );
    }
    await fetchBaseline();
  };

  const handleSelectBaseline = async () => {
    if (!systemId) return;
    setSelecting(true);
    try {
      await selectBaseline(systemId, {
        applyOverlay,
        overlayName: overlayName || undefined,
      });
      setShowSelectDialog(false);
      await fetchBaseline();
    } catch {
      // Error handling
    } finally {
      setSelecting(false);
    }
  };

  if (!systemId) return <div className="p-6 text-gray-500">No system selected.</div>;

  // ─── Loading ───────────────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="p-6 space-y-6">
        <h1 className="text-2xl font-bold text-gray-900">Categorization & Baseline</h1>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-20 animate-pulse rounded-xl border border-gray-200 bg-gray-100" />
          ))}
        </div>
      </div>
    );
  }

  // ─── No Baseline State ─────────────────────────────────────────────────────

  if (noBaseline || !baseline) {
    return (
      <div className="p-6 space-y-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Categorization & Baseline</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage the FIPS 199 security categorization and NIST 800-53 control baseline for this system.
          </p>
        </div>

        {/* No Categorization */}
        {!categorization && (
          <div className="flex flex-col items-center justify-center rounded-xl border-2 border-dashed border-gray-300 py-12">
            <svg className="h-10 w-10 text-gray-400 mb-3" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" />
            </svg>
            <h2 className="text-lg font-semibold text-gray-700">No Categorization</h2>
            <p className="mt-1 text-sm text-gray-500 max-w-md text-center">
              This system has not been categorized yet. Select SP 800-60 information types to derive the FIPS 199 security categorization.
            </p>
            <button
              onClick={() => setShowRecategorizeDialog(true)}
              className="mt-5 rounded-lg bg-indigo-600 px-6 py-2.5 text-sm font-medium text-white hover:bg-indigo-700"
            >
              Select Categorization
            </button>
          </div>
        )}

        {/* No Baseline */}
        <div className="flex flex-col items-center justify-center rounded-xl border-2 border-dashed border-gray-300 py-12">
          <svg className="h-10 w-10 text-gray-400 mb-3" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <h2 className="text-lg font-semibold text-gray-700">No Baseline Configured</h2>
          <p className="mt-1 text-sm text-gray-500 max-w-md text-center">
            This system does not have a NIST 800-53 control baseline yet. The baseline level
            is automatically derived from the system&apos;s FIPS 199 categorization.
          </p>
          <button
            onClick={() => setShowSelectDialog(true)}
            disabled={!categorization}
            className="mt-5 rounded-lg bg-indigo-600 px-6 py-2.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Select Baseline
          </button>
          {!categorization && (
            <p className="mt-2 text-xs text-amber-600">Complete categorization first to enable baseline selection.</p>
          )}
        </div>

        {/* Select Dialog */}
        {showSelectDialog && (
          <SelectBaselineDialog
            applyOverlay={applyOverlay}
            overlayName={overlayName}
            selecting={selecting}
            onApplyOverlayChange={setApplyOverlay}
            onOverlayNameChange={setOverlayName}
            onSelect={handleSelectBaseline}
            onClose={() => setShowSelectDialog(false)}
          />
        )}

        {/* Recategorize Dialog */}
        {showRecategorizeDialog && (
          <RecategorizeDialog
            systemId={systemId}
            currentCategorization={categorization}
            onClose={() => setShowRecategorizeDialog(false)}
            onSaved={handleCategorizationSaved}
          />
        )}
      </div>
    );
  }

  // ─── Baseline Detail View ──────────────────────────────────────────────────

  const undesignated = baseline.totalControls - baseline.inheritedControls - baseline.sharedControls - baseline.customerControls;
  const filteredFamilies = baseline.familyBreakdown.filter(f =>
    !familySearch || f.family.toLowerCase().includes(familySearch.toLowerCase()),
  );

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div>
        <div className="flex items-center gap-4">
          <h1 className="text-2xl font-bold text-gray-900">Categorization & Baseline</h1>
          <LevelBadge level={baseline.baselineLevel} />
          {baseline.overlayApplied && (
            <span className="inline-flex items-center rounded-full bg-purple-100 px-3 py-1 text-xs font-medium text-purple-700">
              {baseline.overlayApplied}
            </span>
          )}
        </div>
        <p className="mt-1 text-sm text-gray-500">
          Manage the FIPS 199 security categorization and NIST 800-53 control baseline, including family breakdown and tailoring history.
        </p>
      </div>

      {/* Organization Framework Indicator */}
      <div className="flex items-center gap-2 rounded-lg bg-blue-50 border border-blue-200 px-4 py-2">
        <svg className="h-4 w-4 text-blue-600 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
        </svg>
        <span className="text-sm text-blue-800">
          Organization framework: <strong>{settings.activeFramework}</strong>
        </span>
        <span className="text-xs text-blue-600">• This system&apos;s impact level: <strong>{baseline.baselineLevel}</strong></span>
      </div>

      {/* Cascade Banner */}
      {cascadeBanner && (
        <div className="flex items-center justify-between rounded-lg border border-blue-300 bg-blue-50 px-4 py-3 text-sm text-blue-800">
          <span>{cascadeBanner}</span>
          <button onClick={() => setCascadeBanner(null)} className="ml-4 text-blue-600 hover:text-blue-800 font-medium">&times;</button>
        </div>
      )}

      {/* Summary Cards */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
        <Card label="Total Controls" value={baseline.totalControls} color="blue" />
        <Card label="Inherited" value={baseline.inheritedControls} color="green" />
        <Card label="Shared" value={baseline.sharedControls} color="indigo" />
        <Card label="Customer" value={baseline.customerControls} color="amber" />
        <Card label="Undesignated" value={undesignated} color="gray" />
      </div>

      {/* FIPS 199 Categorization */}
      {!categorization && (
        <div className="rounded-xl border-2 border-dashed border-gray-300 bg-white">
          <div className="flex flex-col items-center justify-center py-10">
            <svg className="h-8 w-8 text-gray-400 mb-2" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" />
            </svg>
            <h3 className="text-sm font-semibold text-gray-700">No Categorization</h3>
            <p className="mt-1 text-xs text-gray-500">This system has not been categorized yet.</p>
            <button
              onClick={() => setShowRecategorizeDialog(true)}
              className="mt-4 rounded-lg bg-indigo-600 px-5 py-2 text-sm font-medium text-white hover:bg-indigo-700"
            >
              Select Categorization
            </button>
          </div>
        </div>
      )}
      {categorization && (
        <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
          <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
            <h3 className="text-sm font-semibold text-gray-900">FIPS 199 Security Categorization</h3>
            <button
              onClick={() => setShowRecategorizeDialog(true)}
              className="rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
            >
              Re-categorize
            </button>
          </div>
          <div className="px-6 py-4">
            <div className="grid grid-cols-4 gap-6">
              {(['confidentiality', 'integrity', 'availability'] as const).map(dim => (
                <div key={dim} className="text-center">
                  <p className="text-xs font-medium uppercase tracking-wider text-gray-500 mb-2">{dim}</p>
                  <span className={`inline-flex rounded-full px-3 py-1 text-sm font-semibold ${
                    categorization[dim] === 'High' ? 'bg-red-100 text-red-700' :
                    categorization[dim] === 'Moderate' ? 'bg-amber-100 text-amber-700' :
                    'bg-green-100 text-green-700'
                  }`}>
                    {categorization[dim]}
                  </span>
                </div>
              ))}
              <div className="text-center">
                <p className="text-xs font-medium uppercase tracking-wider text-gray-500 mb-2">Overall</p>
                <span className={`inline-flex rounded-full px-3 py-1 text-sm font-bold ${
                  categorization.overall === 'High' ? 'bg-red-100 text-red-700' :
                  categorization.overall === 'Moderate' ? 'bg-amber-100 text-amber-700' :
                  'bg-green-100 text-green-700'
                }`}>
                  {categorization.overall}
                </span>
              </div>
            </div>
            {(categorization.dodImpactLevel || categorization.informationTypes.length > 0) && (
              <div className="mt-4 flex items-center gap-4 text-xs text-gray-500">
                {categorization.dodImpactLevel && (
                  <span>DoD Impact Level: <span className="font-medium text-gray-700">{categorization.dodImpactLevel}</span></span>
                )}
                {categorization.informationTypes.length > 0 && (
                  <span>{categorization.informationTypes.length} information type{categorization.informationTypes.length !== 1 ? 's' : ''}</span>
                )}
                {categorization.formalNotation && (
                  <span>SC: <span className="font-mono font-medium text-gray-700">{categorization.formalNotation}</span></span>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Metadata */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h3 className="text-sm font-semibold text-gray-900">Baseline Details</h3>
          <button
            onClick={() => setShowSelectDialog(true)}
            className="rounded-lg border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
          >
            Re-select Baseline
          </button>
        </div>
        <div className="grid grid-cols-2 gap-4 px-6 py-4 sm:grid-cols-4">
          <div>
            <p className="text-xs font-medium text-gray-500">Level</p>
            <p className="mt-1 text-sm font-semibold text-gray-900">{baseline.baselineLevel}</p>
          </div>
          <div>
            <p className="text-xs font-medium text-gray-500">Overlay</p>
            <p className="mt-1 text-sm text-gray-900">{baseline.overlayApplied ?? 'None'}</p>
          </div>
          <div>
            <p className="text-xs font-medium text-gray-500">Selected By</p>
            <p className="mt-1 text-sm text-gray-900">{baseline.createdBy}</p>
          </div>
          <div>
            <p className="text-xs font-medium text-gray-500">Selected At</p>
            <p className="mt-1 text-sm text-gray-900">{new Date(baseline.createdAt).toLocaleDateString()}</p>
          </div>
          <div>
            <p className="text-xs font-medium text-gray-500">Controls Added (Tailored In)</p>
            <p className="mt-1 text-sm text-gray-900">{baseline.tailoredInControls}</p>
          </div>
          <div>
            <p className="text-xs font-medium text-gray-500">Controls Removed (Tailored Out)</p>
            <p className="mt-1 text-sm text-gray-900">{baseline.tailoredOutControls}</p>
          </div>
          {baseline.modifiedAt && (
            <div>
              <p className="text-xs font-medium text-gray-500">Last Modified</p>
              <p className="mt-1 text-sm text-gray-900">{new Date(baseline.modifiedAt).toLocaleDateString()}</p>
            </div>
          )}
        </div>
      </div>

      {/* Family Breakdown */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h3 className="text-sm font-semibold text-gray-900">
            Control Families ({baseline.familyBreakdown.length} families, {baseline.totalControls} controls)
          </h3>
          <input
            type="text"
            placeholder="Filter families..."
            value={familySearch}
            onChange={e => setFamilySearch(e.target.value)}
            className="w-48 rounded-lg border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>
        <div className="max-h-[400px] overflow-y-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Family</th>
                <th className="px-6 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Controls</th>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Distribution</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 bg-white">
              {filteredFamilies.map(f => (
                <tr key={f.family} className="hover:bg-gray-50">
                  <td className="whitespace-nowrap px-6 py-3 text-sm font-medium text-gray-900">{f.family}</td>
                  <td className="whitespace-nowrap px-6 py-3 text-right text-sm text-gray-700">{f.count}</td>
                  <td className="px-6 py-3">
                    <div className="h-2 w-full max-w-xs rounded-full bg-gray-200">
                      <div
                        className="h-2 rounded-full bg-indigo-500"
                        style={{ width: `${(f.count / baseline.totalControls) * 100}%` }}
                      />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Tailoring History */}
      {baseline.tailorings.length > 0 && (
        <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
          <div className="border-b border-gray-200 px-6 py-4">
            <h3 className="text-sm font-semibold text-gray-900">
              Tailoring History ({baseline.tailorings.length} actions)
            </h3>
          </div>
          <div className="max-h-[300px] overflow-y-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Control</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Action</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Rationale</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Overlay Required</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">By</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Date</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 bg-white">
                {baseline.tailorings.map(t => (
                  <tr key={t.id} className="hover:bg-gray-50">
                    <td className="whitespace-nowrap px-6 py-3 text-sm font-medium text-gray-900">{t.controlId}</td>
                    <td className="whitespace-nowrap px-6 py-3 text-sm">
                      <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                        t.action === 'Added' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
                      }`}>
                        {t.action}
                      </span>
                    </td>
                    <td className="max-w-xs truncate px-6 py-3 text-sm text-gray-600">{t.rationale}</td>
                    <td className="whitespace-nowrap px-6 py-3 text-sm text-gray-500">{t.isOverlayRequired ? 'Yes' : 'No'}</td>
                    <td className="whitespace-nowrap px-6 py-3 text-sm text-gray-500">{t.tailoredBy}</td>
                    <td className="whitespace-nowrap px-6 py-3 text-sm text-gray-500">{new Date(t.tailoredAt).toLocaleDateString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Select Dialog */}
      {showSelectDialog && (
        <SelectBaselineDialog
          applyOverlay={applyOverlay}
          overlayName={overlayName}
          selecting={selecting}
          onApplyOverlayChange={setApplyOverlay}
          onOverlayNameChange={setOverlayName}
          onSelect={handleSelectBaseline}
          onClose={() => setShowSelectDialog(false)}
        />
      )}

      {/* Recategorize Dialog */}
      {showRecategorizeDialog && (
        <RecategorizeDialog
          systemId={systemId}
          currentCategorization={categorization}
          onClose={() => setShowRecategorizeDialog(false)}
          onSaved={handleCategorizationSaved}
        />
      )}
    </div>
  );
}

// ─── Recategorize Dialog ─────────────────────────────────────────────────────

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

interface SelectedInfoType {
  sp80060Id: string;
  name: string;
  category: string;
  confidentialityImpact: string;
  integrityImpact: string;
  availabilityImpact: string;
  usesProvisional: boolean;
}

function RecategorizeDialog({
  systemId, currentCategorization, onClose, onSaved,
}: {
  systemId: string;
  currentCategorization: CategorizationInfo | null;
  onClose: () => void;
  onSaved: (cascade?: { baselineReselected: string; baselineControls: number; inheritancesReapplied: number } | null) => void;
}) {
  const allTypes: Sp80060InfoType[] = (infoTypesData as { informationTypes: Sp80060InfoType[] }).informationTypes;
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<SelectedInfoType[]>(() => {
    if (!currentCategorization?.informationTypes?.length) return [];
    return currentCategorization.informationTypes.map(it => {
      const match = allTypes.find(t => t.name === it.name);
      return {
        sp80060Id: match?.id ?? it.name,
        name: it.name,
        category: match?.category ?? '',
        confidentialityImpact: it.confidentiality,
        integrityImpact: it.integrity,
        availabilityImpact: it.availability,
        usesProvisional: false,
      };
    });
  });
  const [isNSS, setIsNSS] = useState(false);
  const [justification, setJustification] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const filtered = search.trim()
    ? allTypes.filter(t => t.name.toLowerCase().includes(search.toLowerCase()) || t.category.toLowerCase().includes(search.toLowerCase()) || t.id.toLowerCase().includes(search.toLowerCase()))
    : allTypes;

  const selectedIds = new Set(selected.map(s => s.sp80060Id));

  const handleSelect = (t: Sp80060InfoType) => {
    if (selectedIds.has(t.id)) return;
    setSelected(prev => [...prev, {
      sp80060Id: t.id, name: t.name, category: t.category,
      confidentialityImpact: t.confidentiality, integrityImpact: t.integrity,
      availabilityImpact: t.availability, usesProvisional: true,
    }]);
  };

  const handleRemove = (id: string) => setSelected(prev => prev.filter(s => s.sp80060Id !== id));

  const handleOverride = (id: string, field: keyof SelectedInfoType, value: string) => {
    setSelected(prev => prev.map(s => s.sp80060Id === id ? { ...s, [field]: value, usesProvisional: false } : s));
  };

  const overallC = selected.length > 0 ? highWaterMark(selected.map(s => s.confidentialityImpact)) : 'N/A';
  const overallI = selected.length > 0 ? highWaterMark(selected.map(s => s.integrityImpact)) : 'N/A';
  const overallA = selected.length > 0 ? highWaterMark(selected.map(s => s.availabilityImpact)) : 'N/A';
  const overallFips = selected.length > 0 ? highWaterMark([overallC, overallI, overallA]) : 'N/A';

  const categories = [...new Set(filtered.map(t => t.category))];

  const handleSave = async () => {
    if (selected.length === 0) { setError('Select at least one information type.'); return; }
    setSaving(true);
    setError('');
    try {
      const body = {
        isNationalSecuritySystem: isNSS,
        justification: justification.trim() || undefined,
        informationTypes: selected.map<InfoTypeInput>(s => ({
          sp80060Id: s.sp80060Id, name: s.name, category: s.category,
          confidentialityImpact: s.confidentialityImpact, integrityImpact: s.integrityImpact,
          availabilityImpact: s.availabilityImpact, usesProvisional: s.usesProvisional,
          adjustmentJustification: !s.usesProvisional
            ? (justification.trim() || 'Impact levels adjusted per system risk assessment')
            : undefined,
        })),
      };
      const result = await apiSetCategorization(systemId, body);
      onClose();
      if (result.baselineReselected) {
        onSaved({
          baselineReselected: result.baselineReselected,
          baselineControls: result.baselineControls ?? 0,
          inheritancesReapplied: result.inheritancesReapplied ?? 0,
        });
      } else {
        onSaved();
      }
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } };
      setError(axiosErr?.response?.data?.error ?? (err instanceof Error ? err.message : 'Failed to save categorization'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-3xl max-h-[90vh] flex flex-col rounded-xl bg-white shadow-2xl">
        {/* Header */}
        <div className="border-b border-gray-200 px-6 py-4">
          <h3 className="text-lg font-semibold text-gray-900">Re-categorize System</h3>
          <p className="mt-1 text-sm text-gray-500">Update SP 800-60 information types and review the FIPS 199 categorization.</p>
        </div>

        {/* Body (scrollable) */}
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {/* FIPS 199 Summary */}
          {selected.length > 0 && (
            <div className="rounded-lg border border-blue-200 bg-blue-50 p-4">
              <h4 className="text-sm font-medium text-blue-900 mb-2">
                FIPS 199 Overall Categorization: <span className="font-bold">{overallFips}</span>
              </h4>
              <div className="flex gap-6 text-sm">
                {[{ label: 'Confidentiality', value: overallC }, { label: 'Integrity', value: overallI }, { label: 'Availability', value: overallA }].map(d => (
                  <span key={d.label}>{d.label}: <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-semibold ${IMPACT_COLORS[d.value] ?? 'bg-gray-100 text-gray-700'}`}>{d.value}</span></span>
                ))}
              </div>
            </div>
          )}

          {/* Selected types table */}
          {selected.length > 0 && (
            <div>
              <h4 className="text-sm font-medium text-gray-700 mb-2">Selected Information Types ({selected.length})</h4>
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
                    {selected.map(s => (
                      <tr key={s.sp80060Id}>
                        <td className="px-3 py-2"><span className="font-medium">{s.name}</span> <span className="text-xs text-gray-400">{s.sp80060Id}</span></td>
                        <td className="px-3 py-2 text-center">
                          <select value={s.confidentialityImpact} onChange={e => handleOverride(s.sp80060Id, 'confidentialityImpact', e.target.value)} className="rounded border border-gray-300 px-1 py-0.5 text-xs">
                            {IMPACT_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                          </select>
                        </td>
                        <td className="px-3 py-2 text-center">
                          <select value={s.integrityImpact} onChange={e => handleOverride(s.sp80060Id, 'integrityImpact', e.target.value)} className="rounded border border-gray-300 px-1 py-0.5 text-xs">
                            {IMPACT_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                          </select>
                        </td>
                        <td className="px-3 py-2 text-center">
                          <select value={s.availabilityImpact} onChange={e => handleOverride(s.sp80060Id, 'availabilityImpact', e.target.value)} className="rounded border border-gray-300 px-1 py-0.5 text-xs">
                            {IMPACT_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
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

          {/* Search + Info type list */}
          <div>
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              placeholder="Search information types..."
            />
          </div>
          <div className="border border-gray-200 rounded-md max-h-48 overflow-y-auto">
            {categories.map(cat => (
              <div key={cat}>
                <div className="sticky top-0 bg-gray-100 px-3 py-1 text-xs font-semibold text-gray-600">{cat}</div>
                {filtered.filter(t => t.category === cat).map(t => (
                  <button
                    key={t.id}
                    onClick={() => handleSelect(t)}
                    disabled={selectedIds.has(t.id)}
                    className={`w-full text-left px-3 py-1.5 text-sm border-b border-gray-100 hover:bg-blue-50 ${selectedIds.has(t.id) ? 'opacity-50' : ''}`}
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
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={isNSS} onChange={e => setIsNSS(e.target.checked)} className="rounded" />
            National Security System
          </label>
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">Justification</label>
            <textarea
              value={justification}
              onChange={e => setJustification(e.target.value)}
              rows={2}
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              placeholder="Optional justification for categorization changes"
            />
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button onClick={onClose} className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-600 hover:bg-gray-100">Cancel</button>
          <button
            onClick={handleSave}
            disabled={saving || selected.length === 0}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {saving ? 'Saving...' : 'Save Categorization'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Select Baseline Dialog ──────────────────────────────────────────────────

function SelectBaselineDialog({
  applyOverlay, overlayName, selecting,
  onApplyOverlayChange, onOverlayNameChange, onSelect, onClose,
}: {
  applyOverlay: boolean;
  overlayName: string;
  selecting: boolean;
  onApplyOverlayChange: (v: boolean) => void;
  onOverlayNameChange: (v: string) => void;
  onSelect: () => void;
  onClose: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-md rounded-xl bg-white shadow-2xl">
        <div className="border-b border-gray-200 px-6 py-4">
          <h3 className="text-lg font-semibold text-gray-900">Select Baseline</h3>
          <p className="mt-1 text-sm text-gray-500">
            The baseline level (Low / Moderate / High) is automatically derived from the
            system&apos;s FIPS 199 security categorization.
          </p>
        </div>
        <div className="space-y-4 px-6 py-4">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={applyOverlay}
              onChange={e => onApplyOverlayChange(e.target.checked)}
              className="rounded border-gray-300"
            />
            Apply CNSSI 1253 overlay (recommended for DoD systems)
          </label>
          {applyOverlay && (
            <div>
              <label className="text-xs font-medium text-gray-700">Overlay Name (optional)</label>
              <input
                type="text"
                value={overlayName}
                onChange={e => onOverlayNameChange(e.target.value)}
                placeholder="Auto-detected from DoD IL (e.g., CNSSI 1253 IL5)"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          )}
        </div>
        <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
          <button
            onClick={onClose}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-600 hover:bg-gray-100"
          >
            Cancel
          </button>
          <button
            onClick={onSelect}
            disabled={selecting}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {selecting ? 'Selecting...' : 'Select Baseline'}
          </button>
        </div>
      </div>
    </div>
  );
}
