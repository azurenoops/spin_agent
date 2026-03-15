import type { AtoSeverity } from '../../types/dashboard';

interface AtoCountdownProps {
  daysRemaining: number | null;
  severity: AtoSeverity;
}

const severityStyles: Record<AtoSeverity, string> = {
  green: 'bg-green-100 text-green-800',
  yellow: 'bg-yellow-100 text-yellow-800',
  red: 'bg-red-100 text-red-800',
  expired: 'bg-gray-900 text-white',
  none: 'bg-gray-100 text-gray-500',
};

export default function AtoCountdown({ daysRemaining, severity }: AtoCountdownProps) {
  const label =
    severity === 'none'
      ? '--'
      : severity === 'expired'
        ? 'Expired'
        : `${daysRemaining}d`;

  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ${severityStyles[severity]}`}
    >
      {label}
    </span>
  );
}
