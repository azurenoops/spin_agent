import MetricCard from './cards/MetricCard';
import type { DeviationSummary } from '../types/dashboard';

interface DeviationSummaryCardsProps {
  summary: DeviationSummary | null;
}

export default function DeviationSummaryCards({ summary }: DeviationSummaryCardsProps) {
  if (!summary) {
    return (
      <div className="mb-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
        Unable to load deviation summary.
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
      <MetricCard title="Total Deviations" value={summary.total} />
      <MetricCard
        title="Pending Review"
        value={summary.pending}
        severityColor={summary.pending > 0 ? '#f59e0b' : undefined}
      />
      <MetricCard
        title="Expiring ≤ 30d"
        value={summary.expiringWithin30d}
        severityColor={summary.expiringWithin30d > 0 ? '#ef4444' : undefined}
      />
      <MetricCard
        title="CAT I"
        value={summary.catI}
        severityColor={summary.catI > 0 ? '#dc2626' : undefined}
      />
    </div>
  );
}
