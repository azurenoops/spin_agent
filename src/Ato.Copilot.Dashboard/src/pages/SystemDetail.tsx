import { useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import RmfPhaseProgressComponent from '../components/charts/RmfPhaseProgress';
import PhaseReadinessPanel from '../components/cards/PhaseReadinessPanel';
import ComplianceHeatmap from '../components/charts/ComplianceHeatmap';
import { TrendChart } from '../components/charts/TrendChart';
import MetricCard from '../components/cards/MetricCard';
import FindingsSeverityCard from '../components/cards/FindingsSeverityCard';
import AtoCountdown from '../components/cards/AtoCountdown';
import ActivityFeed from '../components/cards/ActivityFeed';
import TodoPanel from '../components/cards/TodoPanel';
import RoleAssignmentPanel from '../components/cards/RoleAssignmentPanel';
import { BoundarySummaryCard } from '../components/cards/BoundarySummaryCard';
import HelpTooltip from '../components/help/HelpTooltip';
import { usePolling } from '../hooks/usePolling';
import { getHeatmap } from '../api/systemDetail';
import { fetchBoundaryDefinitions } from '../api/boundaries';
import { useSystemContext } from '../components/layout/SystemLayout';
import type { HeatmapResponse, BoundaryDefinitionDto } from '../types/dashboard';

export default function SystemDetail() {
  const { detail, refetch } = useSystemContext();
  const [heatmapData, setHeatmapData] = useState<HeatmapResponse | null>(null);
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);

  const fetchExtra = useCallback(async () => {
    const [h, b] = await Promise.allSettled([
      getHeatmap(detail.systemId),
      fetchBoundaryDefinitions(detail.systemId),
    ]);
    setHeatmapData(h.status === 'fulfilled' ? h.value : null);
    setBoundaries(b.status === 'fulfilled' ? b.value : []);
  }, [detail.systemId]);

  usePolling(fetchExtra);

  const km = detail.keyMetrics;

  return (
    <>
      {/* RMF Phase Progress */}
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center gap-1">
          <h2 className="mb-3 text-sm font-semibold text-gray-700">RMF Phase Progress</h2>
          <HelpTooltip helpKey="rmfProgress" />
        </div>
        <RmfPhaseProgressComponent
          phases={detail.rmfPhaseProgress}
        />
      </div>

      {/* Phase Readiness Panel */}
      <div className="mb-6">
        <PhaseReadinessPanel
          systemId={detail.systemId}
          onAdvanced={refetch}
        />
      </div>

      {/* Key Metrics */}
      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        <MetricCard
          title="Compliance Score"
          value={`${km.complianceScore.toFixed(1)}%`}
          trend={km.complianceScoreDelta}
          subtitle={`Prior: ${km.priorScore.toFixed(1)}%`}
          helpKey="complianceScore"
        />
        <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div className="flex items-center">
            <p className="text-sm font-medium text-gray-500">ATO Status</p>
            <HelpTooltip helpKey="atoStatus" />
          </div>
          <div className="mt-1">
            <AtoCountdown daysRemaining={km.atoDaysRemaining} severity={km.atoSeverity} />
          </div>
        </div>
        <MetricCard
          title="POA&Ms"
          value={km.totalOpenPoams}
          subtitle={`${km.overduePoams} overdue`}
          helpKey="poams"
        />
        <MetricCard
          title="Narrative Coverage"
          value={`${km.narrativeCoverage.toFixed(1)}%`}
          helpKey="narrativeCoverage"
        />
        <Link to={`/systems/${detail.systemId}/deviations`} className="block">
          <MetricCard
            title="Active Deviations"
            value={km.activeDeviations}
            severityColor={km.activeDeviations > 0 ? 'purple' : undefined}
          />
        </Link>
        <FindingsSeverityCard
          catI={km.catIFindings}
          catII={km.catIIFindings}
          catIII={km.catIIIFindings}
        />
      </div>

      {/* To Do Panel (mobile — shows below metrics when side panel is hidden) */}
      <div className="mb-6 xl:hidden">
        <TodoPanel systemId={detail.systemId} />
      </div>

      {/* Team & Roles */}
      <RoleAssignmentPanel systemId={detail.systemId} />

      {/* Boundary Summary (Feature 033) */}
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-gray-700">Authorization Boundaries</h2>
          <Link
            to={`/systems/${detail.systemId}/boundaries`}
            className="text-xs text-blue-600 hover:underline"
          >
            Manage Boundaries →
          </Link>
        </div>
        {boundaries.length > 0 ? (
          <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
            {boundaries.map((b) => (
              <BoundarySummaryCard key={b.id} boundary={b} />
            ))}
          </div>
        ) : (
          <p className="text-sm text-gray-500">No boundaries defined yet.</p>
        )}
      </div>

      {/* Heatmap */}
      {heatmapData && (
        <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div className="flex items-center gap-1">
            <h2 className="mb-3 text-sm font-semibold text-gray-700">
              Control Family Compliance ({heatmapData.baselineLevel} Baseline)
            </h2>
            <HelpTooltip helpKey="complianceTrends" />
          </div>
          <ComplianceHeatmap families={heatmapData.families} systemId={detail.systemId} />
        </div>
      )}

      {/* Compliance Trends */}
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center gap-1">
          <h2 className="mb-3 text-sm font-semibold text-gray-700">Compliance Trends</h2>
          <HelpTooltip helpKey="complianceTrends" />
        </div>
        <TrendChart systemId={detail.systemId} />
      </div>

      {/* Activity Feed */}
      <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center gap-1">
          <h2 className="mb-3 text-sm font-semibold text-gray-700">Recent Activity</h2>
          <HelpTooltip helpKey="recentActivity" />
        </div>
        <ActivityFeed activities={detail.recentActivity} />
      </div>
    </>
  );
}
