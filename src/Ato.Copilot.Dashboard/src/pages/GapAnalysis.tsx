/**
 * Wave 6 — GAP-006: Gap Analysis page.
 *
 * Mounted at /systems/:id/gap-analysis. Surfaces control gaps
 * and activates 'gap-analysis' AI suggestions (Prioritise gaps, Remediation plan).
 */
import { useParams } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import apiClient from '../api/client';
import { useCallback } from 'react';

interface GapItem {
  controlId: string;
  controlTitle: string;
  gapType: 'NoNarrative' | 'NoEvidence' | 'FailingFinding' | 'NotImplemented';
  severity: 'Critical' | 'High' | 'Moderate' | 'Low';
  detail: string | null;
}

interface GapAnalysisSummary {
  totalGaps: number;
  criticalCount: number;
  highCount: number;
  moderateCount: number;
  lowCount: number;
  items: GapItem[];
}

const GAP_TYPE_LABELS: Record<GapItem['gapType'], string> = {
  NoNarrative: 'No narrative',
  NoEvidence: 'No evidence',
  FailingFinding: 'Failing finding',
  NotImplemented: 'Not implemented',
};

const SEVERITY_BADGE: Record<GapItem['severity'], string> = {
  Critical: 'bg-red-100 text-red-800',
  High: 'bg-orange-100 text-orange-800',
  Moderate: 'bg-amber-100 text-amber-800',
  Low: 'bg-gray-100 text-gray-600',
};

export default function GapAnalysis() {
  const { id: systemId } = useParams<{ id: string }>();

  const fetchGaps = useCallback(async (): Promise<GapAnalysisSummary | null> => {
    if (!systemId) return null;
    try {
      const { data } = await apiClient.get<GapAnalysisSummary>(
        `/systems/${systemId}/gap-analysis`,
      );
      return data;
    } catch {
      return null;
    }
  }, [systemId]);

  const { data: summary, refresh } = usePolling(fetchGaps, 60000);

  if (!systemId) return null;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Gap Analysis</h1>
          <p className="mt-1 text-sm text-gray-500">
            Controls with missing narratives, evidence gaps, or failing findings.
            Open the AI chat panel (Ctrl+Shift+C) for prioritization guidance.
          </p>
        </div>
        <button
          onClick={() => void refresh()}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm text-gray-700 hover:bg-gray-50"
        >
          ↻ Refresh
        </button>
      </div>

      {summary && (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
          {[
            { label: 'Total Gaps', value: summary.totalGaps, color: 'text-gray-900' },
            { label: 'Critical', value: summary.criticalCount, color: 'text-red-700' },
            { label: 'High', value: summary.highCount, color: 'text-orange-700' },
            { label: 'Moderate', value: summary.moderateCount, color: 'text-amber-700' },
          ].map(({ label, value, color }) => (
            <div key={label} className="rounded-lg border border-gray-200 bg-white px-4 py-3">
              <p className="text-xs font-medium uppercase tracking-wider text-gray-500">{label}</p>
              <p className={`mt-1 text-2xl font-bold ${color}`}>{value}</p>
            </div>
          ))}
        </div>
      )}

      {!summary ? (
        <div className="rounded-lg border border-gray-200 bg-white p-12 text-center text-gray-400">
          <p className="text-sm">Loading gap analysis…</p>
        </div>
      ) : summary.items.length === 0 ? (
        <div className="rounded-lg border border-green-200 bg-green-50 p-8 text-center">
          <p className="text-lg font-medium text-green-800">✓ No control gaps found</p>
          <p className="mt-1 text-sm text-green-600">All controls have narratives, evidence, and no failing findings.</p>
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
          <table className="min-w-full text-sm">
            <thead className="bg-gray-50 text-xs font-medium uppercase tracking-wider text-gray-500">
              <tr>
                <th className="px-4 py-3 text-left">Control</th>
                <th className="px-4 py-3 text-left">Gap Type</th>
                <th className="px-4 py-3 text-left">Severity</th>
                <th className="px-4 py-3 text-left">Detail</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {summary.items.map((gap) => (
                <tr key={`${gap.controlId}-${gap.gapType}`} className="hover:bg-gray-50">
                  <td className="whitespace-nowrap px-4 py-3">
                    <p className="font-mono font-medium text-gray-900">{gap.controlId}</p>
                    <p className="text-xs text-gray-400 mt-0.5 max-w-[200px] truncate">{gap.controlTitle}</p>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-gray-600">
                    {GAP_TYPE_LABELS[gap.gapType]}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3">
                    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${SEVERITY_BADGE[gap.severity]}`}>
                      {gap.severity}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-xs text-gray-500 max-w-[300px] truncate">
                    {gap.detail ?? '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
