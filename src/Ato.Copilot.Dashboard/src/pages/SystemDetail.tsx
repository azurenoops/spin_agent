import { useState, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
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
import { getSystemDetail, getHeatmap } from '../api/systemDetail';
import { fetchBoundaryDefinitions } from '../api/boundaries';
import type { SystemDetailResponse, HeatmapResponse, BoundaryDefinitionDto } from '../types/dashboard';

export default function SystemDetail() {
  const { id } = useParams<{ id: string }>();
  const [detail, setDetail] = useState<SystemDetailResponse | null>(null);
  const [heatmapData, setHeatmapData] = useState<HeatmapResponse | null>(null);
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    if (!id) return;
    try {
      const [d, h, b] = await Promise.allSettled([
        getSystemDetail(id),
        getHeatmap(id),
        fetchBoundaryDefinitions(id),
      ]);
      if (d.status === 'fulfilled') {
        setDetail(d.value);
        setError(null);
      } else {
        setError('Failed to load system detail');
      }
      setHeatmapData(h.status === 'fulfilled' ? h.value : null);
      setBoundaries(b.status === 'fulfilled' ? b.value : []);
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

  const sidePanel = (
    <div className="space-y-4">
      {/* System Summary */}
      <div className="rounded-xl border border-gray-200 bg-white">
        <div className="px-5 pt-5 pb-1">
          <h2 className="text-lg font-semibold text-gray-900">System Details</h2>
        </div>
        <div className="divide-y divide-gray-100 text-sm">
          <div className="flex items-center justify-between px-5 py-2.5">
            <span className="text-gray-500">Name</span>
            <span className="font-medium text-gray-900">{detail.name}</span>
          </div>
          <div className="flex items-center justify-between px-5 py-2.5">
            <span className="text-gray-500">Acronym</span>
            <span className="font-medium text-gray-900">{detail.acronym || '—'}</span>
          </div>
          <div className="flex items-center justify-between px-5 py-2.5">
            <span className="text-gray-500">System Type</span>
            <span className="font-medium text-gray-900">{detail.systemType}</span>
          </div>
          <div className="flex items-center justify-between px-5 py-2.5">
            <span className="text-gray-500">Mission Criticality</span>
            <span className="font-medium text-gray-900">{detail.missionCriticality}</span>
          </div>
          <div className="flex items-center justify-between px-5 py-2.5">
            <span className="text-gray-500">Hosting</span>
            <span className="font-medium text-gray-900">{detail.hostingEnvironment}</span>
          </div>
        </div>
      </div>

      {/* To Do */}
      <TodoPanel systemId={detail.systemId} />

      {/* Security Categorization */}
      {detail.categorization && (
        <div className="rounded-xl border border-gray-200 bg-white">
          <div className="px-5 pt-5 pb-1">
            <h2 className="text-lg font-semibold text-gray-900">Security Categorization</h2>
            <p className="text-xs text-gray-400 mt-0.5">{detail.categorization.formalNotation}</p>
          </div>
          <div className="divide-y divide-gray-100 text-sm">
            {(['confidentiality', 'integrity', 'availability'] as const).map((dim) => {
              const val = detail.categorization![dim];
              const color = val === 'High' ? 'bg-red-100 text-red-700' : val === 'Moderate' ? 'bg-amber-100 text-amber-700' : 'bg-green-100 text-green-700';
              return (
                <div key={dim} className="flex items-center justify-between px-5 py-2.5">
                  <span className="text-gray-500 capitalize">{dim}</span>
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${color}`}>{val}</span>
                </div>
              );
            })}
            <div className="flex items-center justify-between px-5 py-2.5">
              <span className="text-gray-500">Overall</span>
              <span className="font-medium text-gray-900">{detail.categorization.overall}</span>
            </div>
            <div className="flex items-center justify-between px-5 py-2.5">
              <span className="text-gray-500">DoD IL</span>
              <span className="font-medium text-gray-900">{detail.categorization.dodImpactLevel}</span>
            </div>
          </div>
        </div>
      )}

      {/* Navigation */}
      <div className="rounded-xl border border-gray-200 bg-white">
        <div className="px-5 pt-5 pb-1">
          <h2 className="text-lg font-semibold text-gray-900">Navigate</h2>
        </div>
        <div className="divide-y divide-gray-100">
          {[
            { to: `/systems/${detail.systemId}/documents`, label: 'Documents', desc: 'ATO package, privacy, scans & exports' },
            { to: `/systems/${detail.systemId}/narratives`, label: 'Narratives', desc: 'View and manage control narratives' },
            { to: `/systems/${detail.systemId}/gaps`, label: 'Gap Analysis', desc: 'View control gaps and coverage' },
            { to: `/systems/${detail.systemId}/components`, label: 'Component Inventory', desc: 'Hardware & software assets' },
            { to: `/systems/${detail.systemId}/boundaries`, label: 'Manage Boundaries', desc: 'Authorization boundary definitions' },
            { to: `/systems/${detail.systemId}/roadmap`, label: 'Implementation Roadmap', desc: 'Milestones and timeline' },
          ].map((item) => (
            <Link
              key={item.to}
              to={item.to}
              className="flex w-full items-center justify-between gap-4 px-5 py-4 hover:bg-gray-50 transition-colors text-left"
            >
              <div className="min-w-0">
                <p className="text-sm font-medium text-gray-900">{item.label}</p>
                <p className="text-sm text-gray-500 mt-0.5">{item.desc}</p>
              </div>
              <svg className="h-4 w-4 flex-shrink-0 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
              </svg>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );

  return (
    <PageLayout
      title={detail.name}
      sidePanel={sidePanel}
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
        <RmfPhaseProgressComponent
          phases={detail.rmfPhaseProgress}
        />
      </div>

      {/* Phase Readiness Panel */}
      <div className="mb-6">
        <PhaseReadinessPanel
          systemId={detail.systemId}
          onAdvanced={fetchData}
        />
      </div>

      {/* Key Metrics */}
      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-5">
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
    </PageLayout>
  );
}
