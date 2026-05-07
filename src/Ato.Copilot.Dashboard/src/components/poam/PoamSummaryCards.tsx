import type { PoamMetrics } from '../../types/poam';

function SummaryCard({ label, value, color }: { label: string; value: number; color: string }) {
  const colorMap: Record<string, string> = {
    blue: 'bg-indigo-50 text-indigo-700 border-indigo-200',
    red: 'bg-red-50 text-red-700 border-red-200',
    rose: 'bg-rose-50 text-rose-700 border-rose-200',
    amber: 'bg-amber-50 text-amber-700 border-amber-200',
    green: 'bg-green-50 text-green-700 border-green-200',
  };
  return (
    <div className={`rounded-xl border p-4 ${colorMap[color] ?? 'bg-gray-50 text-gray-700 border-gray-200'}`}>
      <p className="text-xs font-medium uppercase tracking-wider opacity-75">{label}</p>
      <p className="mt-1 text-2xl font-bold">{typeof value === 'number' && !Number.isInteger(value) ? value.toFixed(1) : value}</p>
    </div>
  );
}

export default function PoamSummaryCards({ metrics }: { metrics: PoamMetrics }) {
  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
      <SummaryCard label="Total Open" value={metrics.totalOpen} color="blue" />
      <SummaryCard label="Overdue" value={metrics.overdue} color="red" />
      <SummaryCard label="CAT I" value={metrics.catICount} color="rose" />
      <SummaryCard label="Expiring (30d)" value={metrics.expiringWithin30Days} color="amber" />
      <SummaryCard label="Avg Days to Close" value={metrics.avgDaysToClose} color="green" />
    </div>
  );
}
