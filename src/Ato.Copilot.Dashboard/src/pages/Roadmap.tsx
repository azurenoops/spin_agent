import { useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import RoadmapTimeline from '../components/charts/RoadmapTimeline';
import RiskReductionCurve from '../components/charts/RiskReductionCurve';
import MetricCard from '../components/cards/MetricCard';
import { usePolling } from '../hooks/usePolling';
import { fetchRoadmap, fetchRoadmapProgress } from '../api/roadmap';
import type { Roadmap as RoadmapType, RoadmapProgress, RoadmapPhase } from '../types/dashboard';

export default function Roadmap() {
  const { id } = useParams<{ id: string }>();
  const [roadmap, setRoadmap] = useState<RoadmapType | null>(null);
  const [progress, setProgress] = useState<RoadmapProgress | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedPhase, setExpandedPhase] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    if (!id) return;
    try {
      const [r, p] = await Promise.allSettled([
        fetchRoadmap(id),
        fetchRoadmapProgress(id),
      ]);
      if (r.status === 'fulfilled') {
        setRoadmap(r.value);
        setError(null);
      } else {
        setError('No active roadmap found');
      }
      setProgress(p.status === 'fulfilled' ? p.value : null);
    } finally {
      setLoading(false);
    }
  }, [id]);

  usePolling(fetchData);

  const header = (
    <div className="mb-6">
      <h2 className="text-2xl font-bold text-gray-900">Roadmap</h2>
      <p className="mt-1 text-sm text-gray-500">
        Phased implementation timeline with effort estimates, risk reduction targets, and milestone tracking.
      </p>
    </div>
  );

  if (loading) {
    return <>{header}<p className="text-gray-500">Loading roadmap...</p></>;
  }

  if (error || !roadmap) {
    return (
      <>
        {header}
        <div>
          <p className="text-red-500">{error ?? 'No active roadmap found'}</p>
          <p className="mt-2 text-sm text-gray-500">
            Generate a roadmap using the ISSM chat command: &quot;Generate an implementation roadmap for this system&quot;
          </p>
        </div>
      </>
    );
  }

  const togglePhase = (phaseId: string) => {
    setExpandedPhase(expandedPhase === phaseId ? null : phaseId);
  };

  const maxWeek = Math.max(...roadmap.phases.map((p) => p.targetEndWeek ?? 0), 12);

  return (
    <>
      {header}

      {/* Summary Metrics */}
      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-4">
        <MetricCard title="Total Gaps" value={roadmap.totalGaps} />
        <MetricCard
          title="Total Effort"
          value={`${roadmap.totalEstimatedEffortDays.toFixed(0)}d`}
        />
        <MetricCard
          title="Risk Reduction"
          value={`${roadmap.overallCompletionPercent.toFixed(1)}%`}
        />
        <MetricCard
          title="Timeline"
          value={`${maxWeek} wks`}
          subtitle={`${roadmap.phases.length} phases`}
        />
      </div>

      {/* Timeline */}
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold text-gray-700">Phase Timeline</h2>
        <RoadmapTimeline phases={roadmap.phases} totalWeeks={maxWeek} />
      </div>

      {/* Risk Reduction Curve */}
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold text-gray-700">Risk Reduction Curve</h2>
        <RiskReductionCurve
          projected={progress?.riskCurve ?? []}
          actual={
            progress?.phaseProgress?.some((pp) => pp.completionPercent > 0)
              ? progress.phaseProgress.map((pp) => ({
                  week: pp.displayOrder,
                  riskPoints: 0,
                  riskReductionPercent: pp.actualRiskReductionPercent,
                }))
              : undefined
          }
        />
      </div>

      {/* Progress (if available) */}
      {progress && (
        <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-gray-700">Phase Progress</h2>
            <span className="text-xs text-gray-500">
              {progress.itemsCompleted} / {progress.itemsTotal} items · {progress.overallCompletionPercent.toFixed(0)}% overall
            </span>
          </div>
          <div className="space-y-3">
            {progress.phaseProgress.map((pp) => (
              <div key={pp.displayOrder} className="flex items-center gap-4">
                <span className="w-40 truncate text-sm font-medium text-gray-700">{pp.name}</span>
                <div className="flex-1">
                  <div className="h-3 rounded-full bg-gray-200">
                    <div
                      className={`h-3 rounded-full ${pp.isOverdue ? 'bg-red-500' : 'bg-blue-500'}`}
                      style={{ width: `${Math.min(pp.completionPercent, 100)}%` }}
                    />
                  </div>
                </div>
                <span className="w-16 text-right text-sm text-gray-600">
                  {pp.completionPercent.toFixed(0)}%
                </span>
                {pp.isOverdue && (
                  <span className="rounded bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800">
                    {pp.daysOverdue}d overdue
                  </span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Phase Detail Tables */}
      <div className="space-y-4">
        {roadmap.phases.map((phase: RoadmapPhase) => (
          <div key={phase.phaseId} className="rounded-lg border border-gray-200 bg-white shadow-sm">
            <button
              onClick={() => togglePhase(phase.phaseId)}
              className="flex w-full items-center justify-between p-4 text-left hover:bg-gray-50"
            >
              <div>
                <h3 className="text-sm font-semibold text-gray-800">
                  Phase {phase.displayOrder}: {phase.name}
                </h3>
                <p className="text-xs text-gray-500">
                  {phase.totalItemCount} items · {phase.estimatedEffortDays.toFixed(0)}d ·{' '}
                  {phase.riskReductionPercent.toFixed(1)}% risk reduction
                </p>
              </div>
              <span className="text-gray-400">{expandedPhase === phase.phaseId ? '▼' : '▶'}</span>
            </button>

            {expandedPhase === phase.phaseId && phase.items && (
              <div className="border-t border-gray-200 overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-gray-50 text-xs uppercase text-gray-500">
                    <tr>
                      <th className="px-4 py-2">Control ID</th>
                      <th className="px-4 py-2">Gap Type</th>
                      <th className="px-4 py-2">Severity</th>
                      <th className="px-4 py-2">Effort</th>
                      <th className="px-4 py-2">Role</th>
                      <th className="px-4 py-2">Dependencies</th>
                      <th className="px-4 py-2">Status</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {phase.items.map((item) => (
                      <tr key={item.itemId} className="hover:bg-gray-50">
                        <td className="px-4 py-2 font-medium">{item.controlId}</td>
                        <td className="px-4 py-2">{item.gapType}</td>
                        <td className="px-4 py-2">
                          <SeverityBadge severity={item.severity} />
                        </td>
                        <td className="px-4 py-2">{item.estimatedEffortDays}d</td>
                        <td className="px-4 py-2">{item.assignedRole}</td>
                        <td className="px-4 py-2 text-gray-500">
                          {item.dependsOn?.join(', ') ?? '—'}
                        </td>
                        <td className="px-4 py-2">
                          <StatusBadge status={item.status} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        ))}
      </div>
    </>
  );
}

function SeverityBadge({ severity }: { severity: string }) {
  const colors: Record<string, string> = {
    Critical: 'bg-red-100 text-red-800',
    High: 'bg-orange-100 text-orange-800',
    Medium: 'bg-yellow-100 text-yellow-800',
  };
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${colors[severity] ?? 'bg-gray-100 text-gray-600'}`}>
      {severity}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Complete: 'bg-green-100 text-green-800',
    InProgress: 'bg-blue-100 text-blue-800',
    NotStarted: 'bg-gray-100 text-gray-600',
  };
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${colors[status] ?? 'bg-gray-100 text-gray-600'}`}>
      {status}
    </span>
  );
}
