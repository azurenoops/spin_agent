import HelpTooltip from '../help/HelpTooltip';

interface MetricCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  trend?: number;
  severityColor?: string;
  helpKey?: string;
}

export default function MetricCard({
  title,
  value,
  subtitle,
  trend,
  severityColor,
  helpKey,
}: MetricCardProps) {
  const trendArrow =
    trend !== undefined
      ? trend > 0
        ? '▲'
        : trend < 0
          ? '▼'
          : '—'
      : null;

  const trendColor =
    trend !== undefined
      ? trend > 0
        ? 'text-green-600'
        : trend < 0
          ? 'text-red-600'
          : 'text-gray-400'
      : '';

  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <div className="flex items-center">
        <p className="text-sm font-medium text-gray-500">{title}</p>
        {helpKey && <HelpTooltip helpKey={helpKey} />}
      </div>
      <div className="mt-1 flex items-baseline gap-2">
        <span
          className="text-2xl font-bold"
          style={severityColor ? { color: severityColor } : undefined}
        >
          {value}
        </span>
        {trendArrow && (
          <span className={`text-sm font-medium ${trendColor}`}>
            {trendArrow} {trend !== undefined ? Math.abs(trend).toFixed(1) : ''}
          </span>
        )}
      </div>
      {subtitle && <p className="mt-1 text-xs text-gray-400">{subtitle}</p>}
    </div>
  );
}
