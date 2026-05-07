import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getHeatmapControls } from '../../api/systemDetail';
import type { HeatmapControl } from '../../types/dashboard';

interface ControlDrillDownProps {
  systemId: string;
  familyCode: string;
  onClose: () => void;
}

const statusColors: Record<string, string> = {
  Satisfied: 'bg-green-100 text-green-800',
  OtherThanSatisfied: 'bg-red-100 text-red-800',
  NotAssessed: 'bg-gray-100 text-gray-500',
};

const severityColors: Record<string, string> = {
  CatI: 'bg-red-600 text-white',
  CatII: 'bg-amber-500 text-white',
  CatIII: 'bg-indigo-500 text-white',
};

const severityLabels: Record<string, string> = {
  CatI: 'CAT I',
  CatII: 'CAT II',
  CatIII: 'CAT III',
};

const poamColors: Record<string, string> = {
  Ongoing: 'bg-indigo-100 text-indigo-700',
  Completed: 'bg-green-100 text-green-700',
  Delayed: 'bg-red-100 text-red-700',
  RiskAccepted: 'bg-amber-100 text-amber-700',
};

type FilterMode = 'all' | 'failing' | 'passing' | 'not-assessed';

export default function ControlDrillDown({
  systemId,
  familyCode,
  onClose,
}: ControlDrillDownProps) {
  const navigate = useNavigate();
  const [controls, setControls] = useState<HeatmapControl[]>([]);
  const [familyName, setFamilyName] = useState('');
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<FilterMode>('all');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getHeatmapControls(systemId, familyCode)
      .then((result) => {
        if (!cancelled) {
          setControls(result.controls);
          setFamilyName(result.familyName);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [systemId, familyCode]);

  const satisfied = controls.filter(c => c.complianceStatus === 'Satisfied').length;
  const failing = controls.filter(c => c.complianceStatus === 'OtherThanSatisfied').length;
  const notAssessed = controls.filter(c => c.complianceStatus === 'NotAssessed').length;
  const catI = controls.filter(c => c.catSeverity === 'CatI').length;
  const catII = controls.filter(c => c.catSeverity === 'CatII').length;
  const catIII = controls.filter(c => c.catSeverity === 'CatIII').length;
  const withPoam = controls.filter(c => c.poamStatus).length;

  const filtered = controls.filter(c => {
    if (filter === 'failing') return c.complianceStatus === 'OtherThanSatisfied';
    if (filter === 'passing') return c.complianceStatus === 'Satisfied';
    if (filter === 'not-assessed') return c.complianceStatus === 'NotAssessed';
    return true;
  });

  const pct = controls.length > 0 ? Math.round((satisfied / controls.length) * 100) : 0;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="max-h-[90vh] w-full max-w-3xl rounded-xl bg-white shadow-2xl border border-gray-200 flex flex-col overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 pt-5 pb-3 flex-shrink-0">
          <div>
            <h3 className="text-lg font-semibold text-gray-900">
              {familyCode} — {familyName}
            </h3>
            <p className="text-xs text-gray-500 mt-0.5">
              {controls.length} controls · {pct}% compliant
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
            aria-label="Close"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
        <div className="border-t border-gray-100" />

        {/* Body */}
        <div className="px-6 py-4 overflow-y-auto flex-1 space-y-4">
          {loading ? (
            <p className="text-gray-500 py-8 text-center">Loading controls...</p>
          ) : (
            <>
              {/* Summary stats */}
              <div className="grid grid-cols-3 gap-3">
                <button
                  type="button"
                  onClick={() => setFilter(filter === 'passing' ? 'all' : 'passing')}
                  className={`rounded-lg border p-3 text-center transition-all ${filter === 'passing' ? 'border-green-400 ring-2 ring-green-200' : 'border-gray-200 hover:border-green-300'}`}
                >
                  <p className="text-2xl font-bold text-green-600">{satisfied}</p>
                  <p className="text-[10px] font-medium text-gray-500 uppercase">Satisfied</p>
                </button>
                <button
                  type="button"
                  onClick={() => setFilter(filter === 'failing' ? 'all' : 'failing')}
                  className={`rounded-lg border p-3 text-center transition-all ${filter === 'failing' ? 'border-red-400 ring-2 ring-red-200' : 'border-gray-200 hover:border-red-300'}`}
                >
                  <p className="text-2xl font-bold text-red-600">{failing}</p>
                  <p className="text-[10px] font-medium text-gray-500 uppercase">Failing</p>
                  {(catI > 0 || catII > 0 || catIII > 0) && (
                    <div className="flex items-center justify-center gap-1 mt-1">
                      {catI > 0 && <span className="rounded px-1.5 py-0.5 text-[9px] font-bold bg-red-600 text-white">{catI} CAT I</span>}
                      {catII > 0 && <span className="rounded px-1.5 py-0.5 text-[9px] font-bold bg-amber-500 text-white">{catII} CAT II</span>}
                      {catIII > 0 && <span className="rounded px-1.5 py-0.5 text-[9px] font-bold bg-indigo-500 text-white">{catIII} CAT III</span>}
                    </div>
                  )}
                </button>
                <button
                  type="button"
                  onClick={() => setFilter(filter === 'not-assessed' ? 'all' : 'not-assessed')}
                  className={`rounded-lg border p-3 text-center transition-all ${filter === 'not-assessed' ? 'border-gray-400 ring-2 ring-gray-200' : 'border-gray-200 hover:border-gray-300'}`}
                >
                  <p className="text-2xl font-bold text-gray-400">{notAssessed}</p>
                  <p className="text-[10px] font-medium text-gray-500 uppercase">Not Assessed</p>
                </button>
              </div>

              {/* Action banner for failing controls */}
              {failing > 0 && filter !== 'passing' && filter !== 'not-assessed' && (
                <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 flex items-center justify-between">
                  <p className="text-xs text-red-800">
                    <strong>{failing}</strong> control{failing !== 1 ? 's' : ''} need remediation
                    {withPoam > 0 && <> · <strong>{withPoam}</strong> tracked in POA&M</>}
                  </p>
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={() => { onClose(); navigate(`/systems/${systemId}/remediation`); }}
                      className="rounded-md bg-red-600 px-3 py-1 text-xs font-medium text-white hover:bg-red-700 transition-colors"
                    >
                      Go to Remediation
                    </button>
                    <button
                      type="button"
                      onClick={() => { onClose(); navigate(`/systems/${systemId}/poam`); }}
                      className="rounded-md bg-white border border-red-300 px-3 py-1 text-xs font-medium text-red-700 hover:bg-red-50 transition-colors"
                    >
                      View POA&Ms
                    </button>
                  </div>
                </div>
              )}

              {/* Filter indicator */}
              {filter !== 'all' && (
                <div className="flex items-center gap-2">
                  <span className="text-xs text-gray-500">
                    Showing {filtered.length} of {controls.length} controls
                  </span>
                  <button
                    type="button"
                    onClick={() => setFilter('all')}
                    className="text-xs text-indigo-600 hover:underline"
                  >
                    Show all
                  </button>
                </div>
              )}

              {/* Controls table */}
              <div className="overflow-hidden rounded-lg border border-gray-200">
                <table className="min-w-full divide-y divide-gray-200 text-xs">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-3 py-2 text-left font-medium text-gray-500">Control</th>
                      <th className="px-3 py-2 text-left font-medium text-gray-500">Title</th>
                      <th className="px-3 py-2 text-center font-medium text-gray-500">Status</th>
                      <th className="px-3 py-2 text-center font-medium text-gray-500">Severity</th>
                      <th className="px-3 py-2 text-center font-medium text-gray-500">Narrative</th>
                      <th className="px-3 py-2 text-center font-medium text-gray-500">POA&M</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 bg-white">
                    {filtered.map((ctrl) => (
                      <tr
                        key={ctrl.controlId}
                        className={`hover:bg-gray-50 ${ctrl.complianceStatus === 'OtherThanSatisfied' ? 'bg-red-50/30' : ''}`}
                      >
                        <td className="px-3 py-2 font-mono text-xs font-medium text-gray-800">{ctrl.controlId}</td>
                        <td className="px-3 py-2 text-gray-700">
                          {ctrl.controlTitle}
                          {ctrl.securityCapabilityName && (
                            <span className="ml-1 text-[10px] text-gray-400">({ctrl.securityCapabilityName})</span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-center">
                          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-medium ${statusColors[ctrl.complianceStatus] ?? ''}`}>
                            {ctrl.complianceStatus === 'Satisfied' ? 'Satisfied' :
                             ctrl.complianceStatus === 'OtherThanSatisfied' ? 'Other Than Satisfied' :
                             'Not Assessed'}
                          </span>
                        </td>
                        <td className="px-3 py-2 text-center">
                          {ctrl.catSeverity ? (
                            <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-bold ${severityColors[ctrl.catSeverity] ?? 'bg-gray-200 text-gray-600'}`}>
                              {severityLabels[ctrl.catSeverity] ?? ctrl.catSeverity}
                            </span>
                          ) : (
                            <span className="text-gray-300">—</span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-center">
                          {ctrl.hasNarrative ? (
                            <span className="text-green-600 font-medium">✓{ctrl.isManuallyCustomized ? '*' : ''}</span>
                          ) : (
                            <span className="text-gray-300">—</span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-center">
                          {ctrl.poamStatus ? (
                            <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-medium ${poamColors[ctrl.poamStatus] ?? 'bg-gray-100 text-gray-600'}`}>
                              {ctrl.poamStatus === 'RiskAccepted' ? 'Risk Accepted' : ctrl.poamStatus}
                            </span>
                          ) : (
                            <span className="text-gray-300">—</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex items-center justify-between flex-shrink-0">
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => { onClose(); navigate(`/systems/${systemId}/narratives`); }}
              className="text-xs text-indigo-600 hover:underline"
            >
              Edit Narratives →
            </button>
            <span className="text-gray-300">·</span>
            <button
              type="button"
              onClick={() => { onClose(); navigate(`/systems/${systemId}/assessments`); }}
              className="text-xs text-indigo-600 hover:underline"
            >
              Run Assessment →
            </button>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
