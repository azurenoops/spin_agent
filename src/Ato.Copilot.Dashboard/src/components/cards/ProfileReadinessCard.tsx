import HelpTooltip from '../help/HelpTooltip';
import type { ProfileCompletenessResponse } from '../../types/dashboard';

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
        <p className="text-sm font-medium text-gray-500">Profile Readiness</p>
        <HelpTooltip helpKey="profile-readiness" />
      </div>
      <div className="mt-1 flex items-baseline gap-2">
        <span className="text-2xl font-bold text-gray-900">
          {approved}/{total}
        </span>
        <span className="text-sm text-gray-500">approved</span>
      </div>
      <p className="mt-1 text-xs text-gray-400">{completeness.approvedPercentage}%</p>
    </div>
  );
}
