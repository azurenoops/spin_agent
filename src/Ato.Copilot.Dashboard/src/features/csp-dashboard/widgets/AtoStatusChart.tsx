import PortfolioSummaryChart from '../../../components/charts/PortfolioSummaryChart';
import type { AtoStatusCounts } from '../api';

export interface AtoStatusChartProps {
  counts: AtoStatusCounts;
}

export default function AtoStatusChart({ counts }: AtoStatusChartProps) {
  return <PortfolioSummaryChart
    title="ATO status across organizations"
    category="ATO decisions"
    testId="csp-dashboard-ato-status-chart"
    emptyMessage="No ATO decisions recorded"
    series={[
      { label: 'Authorized', count: counts.authorized, color: '#10b981' },
      { label: 'In Process', count: counts.inProcess, color: '#6366f1' },
      { label: 'Denied', count: counts.denied, color: '#ef4444' },
    ]}
  />;
}
