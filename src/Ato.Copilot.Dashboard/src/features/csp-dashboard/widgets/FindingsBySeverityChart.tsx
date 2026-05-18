import type { ReactElement } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { FindingSeverityCounts } from '../api';

/**
 * Feature 048 / US8 / T183 — Stacked-bar chart showing open findings by
 * severity across every tenant in the deployment. Disabled-tenant rows
 * are excluded server-side per FR-098.
 */
export interface FindingsBySeverityChartProps {
  counts: FindingSeverityCounts;
  openPoamCount: number;
  openDeviationCount: number;
}

const SEVERITY_COLORS = {
  critical: '#7c3aed', // violet-600
  high: '#dc2626', // red-600
  moderate: '#f59e0b', // amber-500
  low: '#10b981', // emerald-500
};

export default function FindingsBySeverityChart({
  counts,
  openPoamCount,
  openDeviationCount,
}: FindingsBySeverityChartProps): ReactElement {
  const totalFindings =
    counts.critical + counts.high + counts.moderate + counts.low;

  const data = [
    {
      name: 'Open findings',
      Critical: counts.critical,
      High: counts.high,
      Moderate: counts.moderate,
      Low: counts.low,
    },
  ];

  return (
    <div
      className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm"
      data-testid="csp-dashboard-findings-by-severity-chart"
    >
      <div className="mb-3 flex items-baseline justify-between">
        <h3 className="text-sm font-semibold text-gray-700">
          Open findings by severity
        </h3>
        <div className="text-xs text-gray-500">
          {totalFindings.toLocaleString()} findings · {openPoamCount.toLocaleString()} open POA&amp;Ms ·{' '}
          {openDeviationCount.toLocaleString()} open deviations
        </div>
      </div>
      <ResponsiveContainer width="100%" height={220}>
        <BarChart data={data} layout="vertical">
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis type="number" allowDecimals={false} tick={{ fontSize: 11 }} />
          <YAxis
            type="category"
            dataKey="name"
            tick={{ fontSize: 11 }}
            width={120}
          />
          <Tooltip />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          <Bar dataKey="Critical" stackId="a" fill={SEVERITY_COLORS.critical} />
          <Bar dataKey="High" stackId="a" fill={SEVERITY_COLORS.high} />
          <Bar dataKey="Moderate" stackId="a" fill={SEVERITY_COLORS.moderate} />
          <Bar dataKey="Low" stackId="a" fill={SEVERITY_COLORS.low} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
