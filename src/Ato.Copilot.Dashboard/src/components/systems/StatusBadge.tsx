import type { GovernanceStatus } from '../../types/dashboard';
import { formatGovernanceStatusLabel } from '../../utils/profileSections';

// ─── StatusBadge — named export so SystemLayout sidebar can import it ─────────

function badgeClasses(status: GovernanceStatus): string {
  switch (status) {
    case 'NotStarted':    return 'bg-gray-100 text-gray-600';
    case 'Draft':         return 'bg-amber-100 text-amber-700';
    case 'UnderReview':   return 'bg-indigo-100 text-indigo-700';
    case 'Approved':      return 'bg-green-100 text-green-700';
    case 'NeedsRevision': return 'bg-red-100 text-red-700';
    default:              return 'bg-gray-100 text-gray-600';
  }
}

interface StatusBadgeProps {
  status: GovernanceStatus;
  className?: string;
}

export function StatusBadge({ status, className = '' }: StatusBadgeProps) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${badgeClasses(status)} ${className}`}
    >
      {formatGovernanceStatusLabel(status)}
    </span>
  );
}
