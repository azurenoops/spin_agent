import { useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import RmfPhaseProgressComponent from '../components/charts/RmfPhaseProgress';
import PhaseReadinessPanel from '../components/cards/PhaseReadinessPanel';
import ComplianceHeatmap from '../components/charts/ComplianceHeatmap';
import { TrendChart } from '../components/charts/TrendChart';
import MetricCard from '../components/cards/MetricCard';
import ProfileReadinessCard from '../components/cards/ProfileReadinessCard';
import FindingsSeverityCard from '../components/cards/FindingsSeverityCard';
import AtoCountdown from '../components/cards/AtoCountdown';
import ActivityFeed from '../components/cards/ActivityFeed';
import TodoPanel from '../components/cards/TodoPanel';
import HelpTooltip from '../components/help/HelpTooltip';
import { useSettings } from '../hooks/useSettings';
import { usePolling } from '../hooks/usePolling';
import { getHeatmap } from '../api/systemDetail';
import { getProfileCompleteness } from '../api/systemProfile';
import { useSystemContext } from '../components/layout/SystemLayout';
import type { HeatmapResponse, ProfileCompletenessResponse } from '../types/dashboard';

export default function SystemDetail() {
  const { detail, refetch } = useSystemContext();
  const { settings } = useSettings();
  const [heatmapData, setHeatmapData] = useState<HeatmapResponse | null>(null);
  const [profileCompleteness, setProfileCompleteness] = useState<ProfileCompletenessResponse | null>(null);

  const fetchExtra = useCallback(async () => {
    const [h, pc] = await Promise.allSettled([
      getHeatmap(detail.systemId),
      getProfileCompleteness(detail.systemId),
    ]);
    setHeatmapData(h.status === 'fulfilled' ? h.value : null);
    setProfileCompleteness(pc.status === 'fulfilled' ? pc.value : null);
  }, [detail.systemId]);

  usePolling(fetchExtra);

  const km = detail.keyMetrics;

  return (
    <>
      {/* No-role prompt banner (FR-046) */}
      {!settings.role && (
        <div className="mb-4 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M11.25 11.25l.041-.02a.75.75 0 011.063.852l-.708 2.836a.75.75 0 001.063.853l.041-.021M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9-3.75h.008v.008H12V8.25z" />
          </svg>
          <span>
            Select a role from the <strong>DEV</strong> role switcher in the top bar to unlock role-specific features and actions.
          </span>
        </div>
      )}

      {/* Role context banner (FR-045) */}
      {settings.role === 'MissionOwner' && (
        <div className="mb-4 rounded-lg border border-indigo-200 bg-indigo-50 px-4 py-3 text-sm text-indigo-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 6a3.75 3.75 0 11-7.5 0 3.75 3.75 0 017.5 0zM4.501 20.118a7.5 7.5 0 0114.998 0A17.933 17.933 0 0112 21.75c-2.676 0-5.216-.584-7.499-1.632z" />
          </svg>
          <span>
            <strong>Mission Owner view</strong> — Complete the system profile sections under <em>Mission Profile</em> in the sidebar, then submit for ISSM review.
          </span>
        </div>
      )}
      {settings.role === 'ISSM' && (
        <div className="mb-4 rounded-lg border border-purple-200 bg-purple-50 px-4 py-3 text-sm text-purple-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" />
          </svg>
          <span>
            <strong>ISSM view</strong> — Review and approve submitted profile sections. Navigate to <em>Mission Profile</em> sections to see pending reviews.
          </span>
        </div>
      )}
      {settings.role === 'ISSO' && (
        <div className="mb-4 rounded-lg border border-teal-200 bg-teal-50 px-4 py-3 text-sm text-teal-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25" />
          </svg>
          <span>
            <strong>ISSO view</strong> — Focus on compliance posture, narratives, and technical controls. Profile sections are read-only.
          </span>
        </div>
      )}
      {settings.role === 'Engineer' && (
        <div className="mb-4 rounded-lg border border-sky-200 bg-sky-50 px-4 py-3 text-sm text-sky-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M11.42 15.17l-4.655-5.653a.75.75 0 010-.964l.903-.994a.75.75 0 011.113 0l3.64 3.938 6.64-7.193a.75.75 0 011.113 0l.903.994a.75.75 0 010 .964l-7.543 8.166a1.5 1.5 0 01-2.114 0z" />
          </svg>
          <span>
            <strong>Engineer view</strong> — Focus on remediation, findings, and component details. Profile sections are read-only.
          </span>
        </div>
      )}
      {settings.role === 'SCA' && (
        <div className="mb-4 rounded-lg border border-orange-200 bg-orange-50 px-4 py-3 text-sm text-orange-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h3.75M9 15h3.75M9 18h3.75m3 .75H18a2.25 2.25 0 002.25-2.25V6.108c0-1.135-.845-2.098-1.976-2.192a48.424 48.424 0 00-1.123-.08m-5.801 0c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 00.75-.75 2.25 2.25 0 00-.1-.664m-5.8 0A2.251 2.251 0 0113.5 2.25H15a2.25 2.25 0 012.15 1.586m-5.8 0c-.376.023-.75.05-1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m0 0H4.875c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V9.375c0-.621-.504-1.125-1.125-1.125H8.25z" />
          </svg>
          <span>
            <strong>SCA view</strong> — Focus on assessments, evidence, and gap analysis. Profile sections are read-only.
          </span>
        </div>
      )}
      {settings.role === 'AO' && (
        <div className="mb-4 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700 flex items-center gap-2">
          <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3 3v1.5M3 21v-6m0 0l2.77-.693a9 9 0 016.208.682l.108.054a9 9 0 006.086.71l3.114-.732a48.524 48.524 0 01-.005-10.499l-3.11.732a9 9 0 01-6.085-.711l-.108-.054a9 9 0 00-6.208-.682L3 4.5M3 15V4.5" />
          </svg>
          <span>
            <strong>AO view</strong> — Focus on risk posture, authorization status, and POA&amp;M oversight. Profile sections are read-only.
          </span>
        </div>
      )}

      {/* Missing Mission Owner Banner (T044) */}
      {profileCompleteness && !profileCompleteness.missionOwnerAssigned && profileCompleteness.daysSinceRegistration >= 30 && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 flex items-center justify-between">
          <div>
            <p className="font-medium">No Mission Owner Assigned</p>
            <p className="text-xs text-red-600 mt-0.5">
              This system was registered {profileCompleteness.daysSinceRegistration} days ago.
            </p>
          </div>
          {settings.role === 'ISSM' && (
            <span className="text-xs font-medium text-red-700 bg-red-100 px-2.5 py-1 rounded">
              Assign via MCP tool
            </span>
          )}
        </div>
      )}

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

      {/* Profile Incomplete Banner (T042) */}
      {profileCompleteness && !profileCompleteness.isProfileComplete && (
        <div className="mb-6 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium text-amber-800">System Profile Incomplete</p>
              <p className="text-xs text-amber-600 mt-1">
                {profileCompleteness.incompleteSections.map((s) => s.sectionType).join(', ')}
                {profileCompleteness.missionOwnerName && (
                  <> — Assigned to {profileCompleteness.missionOwnerName}</>
                )}
              </p>
            </div>
            <Link
              to={`/systems/${detail.systemId}/profile/MissionAndPurpose`}
              className="text-xs text-amber-700 font-medium hover:underline whitespace-nowrap"
            >
              View Profile →
            </Link>
          </div>
        </div>
      )}

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
        <ProfileReadinessCard completeness={profileCompleteness} />
      </div>

      {/* To Do Panel (mobile — shows below metrics when side panel is hidden) */}
      <div className="mb-6 xl:hidden">
        <TodoPanel systemId={detail.systemId} />
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
