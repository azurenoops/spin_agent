import { useId } from 'react';
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

export interface PortfolioChartSeries {
  label: string;
  count: number;
  color: string;
}

interface PortfolioSummaryChartProps {
  title: string;
  category: string;
  series: PortfolioChartSeries[];
  emptyMessage: string;
  stacked?: boolean;
  summary?: string;
  testId?: string;
}

export default function PortfolioSummaryChart({
  title, category, series, emptyMessage, stacked = false, summary, testId,
}: PortfolioSummaryChartProps) {
  const titleId = useId();
  const total = series.reduce((sum, item) => sum + item.count, 0);
  const data = [{ name: category, ...Object.fromEntries(series.map((item, index) => [`value${index}`, item.count])) }];
  return <section aria-labelledby={titleId} data-testid={testId}
    className="min-w-0 rounded-xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-900">
    <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
      <h2 id={titleId} className="text-sm font-semibold text-gray-700 dark:text-gray-200">{title}</h2>
      {summary && <p className="text-xs text-gray-500 dark:text-gray-400">{summary}</p>}
    </div>
    <div role="img" aria-label={`${title}. ${series.map(item => `${item.label}: ${item.count}`).join('; ')}`} className="h-56">
      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
        <BarChart data={data} layout="vertical" accessibilityLayer>
          <CartesianGrid strokeDasharray="3 3" stroke="#cbd5e1" />
          <XAxis type="number" allowDecimals={false} domain={total === 0 ? [0, 1] : [0, 'auto']} tick={{ fontSize: 11, fill: '#64748b' }} />
          <YAxis type="category" dataKey="name" width={100} tick={{ fontSize: 11, fill: '#64748b' }} />
          <Tooltip contentStyle={{ backgroundColor: '#fff', borderColor: '#cbd5e1', color: '#0f172a' }} />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          {series.map((item, index) => <Bar key={item.label} dataKey={`value${index}`} name={item.label}
            fill={item.color} stackId={stacked ? 'total' : undefined} isAnimationActive={false} />)}
        </BarChart>
      </ResponsiveContainer>
    </div>
    {total === 0 && <p className="mt-2 text-center text-xs text-gray-500 dark:text-gray-400">{emptyMessage}</p>}
  </section>;
}
