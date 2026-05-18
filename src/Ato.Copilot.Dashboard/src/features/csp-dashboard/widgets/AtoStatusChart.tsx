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
import type { AtoStatusCounts } from '../api';

/**
 * Feature 048 / US8 / T183 — Horizontal-style ATO status chart for the
 * CSP cross-tenant dashboard. Single grouped bar with three color-coded
 * segments (Authorized / In Process / Denied) sourced from
 * `summary.atoStatusCounts`. Disabled-tenant rows are already excluded
 * server-side per FR-098.
 */
export interface AtoStatusChartProps {
  counts: AtoStatusCounts;
}

const STATUS_COLORS = {
  authorized: '#10b981', // emerald-500
  inProcess: '#6366f1', // indigo-500
  denied: '#ef4444', // red-500
};

export default function AtoStatusChart({
  counts,
}: AtoStatusChartProps): ReactElement {
  const data = [
    {
      name: 'ATO decisions',
      Authorized: counts.authorized,
      'In Process': counts.inProcess,
      Denied: counts.denied,
    },
  ];

  return (
    <div
      className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm"
      data-testid="csp-dashboard-ato-status-chart"
    >
      <h3 className="mb-3 text-sm font-semibold text-gray-700">
        ATO status across organizations
      </h3>
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
          <Bar dataKey="Authorized" fill={STATUS_COLORS.authorized} />
          <Bar dataKey="In Process" fill={STATUS_COLORS.inProcess} />
          <Bar dataKey="Denied" fill={STATUS_COLORS.denied} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
