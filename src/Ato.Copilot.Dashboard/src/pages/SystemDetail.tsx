import { useState, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import RmfPhaseProgressComponent from '../components/charts/RmfPhaseProgress';
import ComplianceHeatmap from '../components/charts/ComplianceHeatmap';
import { TrendChart } from '../components/charts/TrendChart';
import MetricCard from '../components/cards/MetricCard';
import FindingsSeverityCard from '../components/cards/FindingsSeverityCard';
import AtoCountdown from '../components/cards/AtoCountdown';
import ActivityFeed from '../components/cards/ActivityFeed';
import TodoPanel from '../components/cards/TodoPanel';
import HelpTooltip from '../components/help/HelpTooltip';
import { usePolling } from '../hooks/usePolling';
import { getSystemDetail, getHeatmap } from '../api/systemDetail';
import type { SystemDetailResponse, HeatmapResponse } from '../types/dashboard';

export default function SystemDetail() {
  const { id } = useParams<{ id: string }>();
  const [detail, setDetail] = useState<SystemDetailResponse | null>(null);
  const [heatmapData, setHeatmapData] = useState<HeatmapResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    if (!id) return;
    try {
      const [d, h] = await Promise.allSettled([getSystemDetail(id), getHeatmap(id)]);
      if (d.status === 'fulfilled') {
        setDetail(d.value);
        setError(null);
      } else {
        setError('Failed to load system detail');
      }
      setHeatmapData(h.status === 'fulfilled' ? h.value : null);
    } finally {
      setLoading(false);
    }
  }, [id]);

  usePolling(fetchData);

  if (loading) {
    return (
      <PageLayout title="System Detail">
        <p className="text-gray-500">Loading system detail...</p>
      </PageLayout>
    );
  }

  if (error || !detail) {
    return (
      <PageLayout title="System Detail">
        <p className="text-red-500">{error ?? 'System not found'}</p>
      </PageLayout>
    );
  }

  const km = detail.keyMetrics;

  return (
    <PageLayout
      title={detail.name}
      sidePanel={<TodoPanel systemId={detail.systemId} />}
    >
      {/* Breadcrumb */}
      <div className="mb-4 text-sm">
        <Link to="/" className="text-blue-600 hover:underline">
          Portfolio
        </Link>
        <span className="mx-2 text-gray-400">/</span>
        <span className="text-gray-700">{detail.name}</span>
      </div>

      {/* RMF Phase Progress */}
      <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="flex items-center gap-1">
          <h2 className="mb-3 text-sm font-semibold text-gray-700">RMF Phase Progress</h2>
          <HelpTooltip helpKey="rmfProgress" />
        </div>
        <RmfPhaseProgressComponent phases={detail.rmfPhaseProgress} />
      </div>

      {/* Key Metrics */}
      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-4">
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
      </div>

      {/* To Do Panel (mobile — shows below metrics when side panel is hidden) */}
      <div className="mb-6 xl:hidden">
        <TodoPanel systemId={detail.systemId} />
      </div>

      {/* Findings */}
      <div className="mb-6">
        <FindingsSeverityCard
          catI={km.catIFindings}
          catII={km.catIIFindings}
          catIII={km.catIIIFindings}
        />
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

      {/* Navigation Links */}
      <div className="mt-4 flex gap-3">
        <Link
          to={`/systems/${detail.systemId}/gaps`}
          className="rounded-md bg-blue-50 px-3 py-1.5 text-sm text-blue-700 hover:bg-blue-100"
        >
          Gap Analysis
        </Link>
        <Link
          to={`/systems/${detail.systemId}/components`}
          className="rounded-md bg-blue-50 px-3 py-1.5 text-sm text-blue-700 hover:bg-blue-100"
        >
          Component Inventory
        </Link>
        <Link
          to={`/systems/${detail.systemId}/roadmap`}
          className="rounded-md bg-blue-50 px-3 py-1.5 text-sm text-blue-700 hover:bg-blue-100"
        >
          Implementation Roadmap
        </Link>
      </div>
    </PageLayout>
  );
}
