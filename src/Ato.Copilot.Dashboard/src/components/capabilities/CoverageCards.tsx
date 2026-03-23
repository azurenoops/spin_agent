import type { OrgWideCoverage } from '../../types/capabilities';

interface CoverageCardsProps {
  coverage: OrgWideCoverage | null;
  loading?: boolean;
}

interface CardProps {
  label: string;
  value: string | number;
  color?: string;
  subtitle?: string;
}

function Card({ label, value, color = 'text-gray-900', subtitle }: CardProps) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wide text-gray-500">{label}</p>
      <p className={`mt-1 text-2xl font-bold ${color}`}>{value}</p>
      {subtitle && <p className="mt-0.5 text-xs text-gray-400">{subtitle}</p>}
    </div>
  );
}

export default function CoverageCards({ coverage, loading }: CoverageCardsProps) {
  if (loading) {
    return (
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {[1, 2, 3, 4].map(i => (
          <div key={i} className="h-24 animate-pulse rounded-lg border border-gray-200 bg-gray-100" />
        ))}
      </div>
    );
  }

  if (!coverage) return null;

  const gapControls = coverage.unmappedControls;
  const coveragePct = coverage.coveragePercent;

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
      <Card
        label="Total Capabilities"
        value={coverage.totalCapabilities}
      />
      <Card
        label="Mapped Controls"
        value={coverage.mappedControls}
        color="text-green-700"
      />
      <Card
        label="Gap Controls"
        value={gapControls ?? 'N/A'}
        color={gapControls != null && gapControls > 0 ? 'text-amber-600' : 'text-gray-900'}
        subtitle={coverage.baselineLevel ? `${coverage.baselineLevel} baseline` : undefined}
      />
      <Card
        label="Coverage %"
        value={coveragePct != null ? `${coveragePct.toFixed(1)}%` : 'N/A'}
        color={coveragePct != null && coveragePct >= 80 ? 'text-green-700' : coveragePct != null ? 'text-amber-600' : 'text-gray-400'}
        subtitle={coverage.baselineControlCount != null ? `of ${coverage.baselineControlCount} baseline controls` : undefined}
      />
    </div>
  );
}
