import HelpTooltip from '../help/HelpTooltip';
import type { ProfileCompletenessResponse } from '../../types/dashboard';
import { Link } from '../../features/workspaces/workspaceNavigation';

interface ProfileReadinessCardProps {
  completeness: ProfileCompletenessResponse | null;
}

export default function ProfileReadinessCard({ completeness }: ProfileReadinessCardProps) {
  if (!completeness) return null;

  const approved = completeness.statusCounts['Approved'] ?? 0;
  const total = completeness.totalSections;

  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <div className="flex items-center">
        <p className="text-sm font-medium text-gray-500 dark:text-gray-300">Profile Completeness</p>
        <HelpTooltip helpKey="profile-readiness" />
      </div>
      <div className="mt-1 flex items-baseline gap-2">
        <span className="text-2xl font-bold text-gray-900">
          {approved}/{total}
        </span>
        <span className="text-sm text-gray-500">approved</span>
      </div>
      <p className="mt-1 text-xs text-gray-400">{completeness.approvedPercentage}%</p>
      {completeness.designStatus && <div className="mt-3 border-t pt-3 text-xs">
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${encodeURIComponent(completeness.systemId)}/profile/SystemDesign`}>System design</Link>
        <p className="mt-1">{completeness.designStatus.replace(/([a-z])([A-Z])/g, '$1 $2')} · Revision {completeness.designRevision ?? 0}</p>
        <p>Approved design: {completeness.approvedDesignRevision ? `v${completeness.approvedDesignRevision}` : 'Not recorded'}. Source freshness and output readiness are checked in System design.</p>
      </div>}
    </div>
  );
}
