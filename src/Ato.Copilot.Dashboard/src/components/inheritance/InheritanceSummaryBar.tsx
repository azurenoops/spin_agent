import type { InheritanceSummary } from '../../types/inheritance';

function SummaryCard({ label, value, color }: { label: string; value: number | string; color: string }) {
  const colorMap: Record<string, string> = {
    blue: 'bg-indigo-50 text-indigo-700 border-indigo-200',
    green: 'bg-green-50 text-green-700 border-green-200',
    indigo: 'bg-indigo-50 text-indigo-700 border-indigo-200',
    amber: 'bg-amber-50 text-amber-700 border-amber-200',
    gray: 'bg-gray-50 text-gray-600 border-gray-200',
  };
  return (
    <div className={`rounded-xl border p-4 ${colorMap[color] ?? 'bg-gray-50 text-gray-700 border-gray-200'}`}>
      <p className="text-xs font-medium uppercase tracking-wider opacity-75">{label}</p>
      <p className="mt-1 text-2xl font-bold">{value}</p>
    </div>
  );
}

interface InheritanceSummaryBarProps {
  summary: InheritanceSummary | null;
  loading: boolean;
}

export default function InheritanceSummaryBar({ summary, loading }: InheritanceSummaryBarProps) {
  if (loading || !summary) {
    return (
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="h-20 animate-pulse rounded-xl border border-gray-200 bg-gray-100" />
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
      <SummaryCard label="Total Controls" value={summary.totalControls} color="blue" />
      <SummaryCard label="Inherited" value={summary.inheritedCount} color="green" />
      <SummaryCard label="Shared" value={summary.sharedCount} color="indigo" />
      <SummaryCard label="Customer" value={summary.customerCount} color="amber" />
      <SummaryCard label="Undesignated" value={summary.undesignatedCount} color="gray" />
      <SummaryCard label="Inheritance %" value={`${summary.inheritancePercentage}%`} color="blue" />
      {summary.orgDefaultCount != null && (
        <>
          <SummaryCard label="Org Defaults" value={summary.orgDefaultCount} color="green" />
          <SummaryCard label="Overrides" value={summary.systemOverrideCount} color="amber" />
        </>
      )}
    </div>
  );
}
