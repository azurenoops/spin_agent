import PortfolioSummaryChart from '../../../components/charts/PortfolioSummaryChart';
import type { FindingSeverityCounts } from '../api';

export interface FindingsBySeverityChartProps {
  counts: FindingSeverityCounts;
  openPoamCount: number;
  openDeviationCount: number;
}

export default function FindingsBySeverityChart({ counts, openPoamCount, openDeviationCount }: FindingsBySeverityChartProps) {
  const total = counts.critical + counts.high + counts.moderate + counts.low;
  return <PortfolioSummaryChart
    title="Open findings by severity"
    category="Open findings"
    testId="csp-dashboard-findings-by-severity-chart"
    emptyMessage="No open findings"
    summary={`${total.toLocaleString()} findings · ${openPoamCount.toLocaleString()} open POA&Ms · ${openDeviationCount.toLocaleString()} open deviations`}
    stacked
    series={[
      { label: 'Critical', count: counts.critical, color: '#7c3aed' },
      { label: 'High', count: counts.high, color: '#dc2626' },
      { label: 'Moderate', count: counts.moderate, color: '#f59e0b' },
      { label: 'Low', count: counts.low, color: '#10b981' },
    ]}
  />;
}
