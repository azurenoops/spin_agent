import { useState, useEffect, useCallback } from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
} from 'recharts';
import { getTrends, type TrendDataPoint } from '../../api/trends';

type Granularity = 'Daily' | 'Weekly' | 'Monthly' | 'Quarterly';

interface TrendChartProps {
  systemId: string;
}

export function TrendChart({ systemId }: TrendChartProps) {
  const [data, setData] = useState<TrendDataPoint[]>([]);
  const [granularity, setGranularity] = useState<Granularity>('Daily');
  const [days, setDays] = useState(90);
  const [loading, setLoading] = useState(true);

  const fetchTrends = useCallback(async () => {
    setLoading(true);
    try {
      const endDate = new Date().toISOString();
      const startDate = new Date(Date.now() - days * 86400000).toISOString();
      const result = await getTrends(systemId, { startDate, endDate, granularity });
      setData(result.dataPoints);
    } catch {
      setData([]);
    } finally {
      setLoading(false);
    }
  }, [systemId, granularity, days]);

  useEffect(() => { fetchTrends(); }, [fetchTrends]);

  if (loading) {
    return <div className="text-gray-400 text-sm py-8 text-center">Loading trend data...</div>;
  }

  if (data.length === 0) {
    return (
      <div className="text-center py-8 text-gray-400">
        <p className="text-sm">No trend data available</p>
        <p className="text-xs mt-1">Trend data is captured daily and after each assessment.</p>
      </div>
    );
  }

  const formatDate = (dateStr: string) => {
    const d = new Date(dateStr);
    return `${d.getMonth() + 1}/${d.getDate()}`;
  };

  const chartData = data.map((pt) => ({
    ...pt,
    dateLabel: formatDate(pt.date),
  }));

  return (
    <div className="space-y-3">
      {/* Controls */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex gap-1">
          {(['Daily', 'Weekly', 'Monthly', 'Quarterly'] as Granularity[]).map((g) => (
            <button
              key={g}
              onClick={() => setGranularity(g)}
              className={`px-3 py-1 text-xs rounded ${
                granularity === g
                  ? 'bg-indigo-600 text-white'
                  : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
              }`}
            >
              {g}
            </button>
          ))}
        </div>
        <div className="flex gap-1">
          {[30, 60, 90, 180, 365].map((d) => (
            <button
              key={d}
              onClick={() => setDays(d)}
              className={`px-3 py-1 text-xs rounded ${
                days === d
                  ? 'bg-indigo-600 text-white'
                  : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
              }`}
            >
              {d}d
            </button>
          ))}
        </div>
      </div>

      {/* Chart */}
      <div className="h-64">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={chartData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
            <XAxis dataKey="dateLabel" tick={{ fontSize: 11 }} />
            <YAxis domain={[0, 100]} tick={{ fontSize: 11 }} />
            <Tooltip
              contentStyle={{ fontSize: 12 }}
              formatter={(value: number, name: string) => {
                const labels: Record<string, string> = {
                  complianceScore: 'Compliance Score',
                  narrativeCoverage: 'Narrative Coverage',
                  catICount: 'CAT I',
                  catIICount: 'CAT II',
                  catIIICount: 'CAT III',
                  openPoamCount: 'Open POA&Ms',
                  overduePoamCount: 'Overdue POA&Ms',
                };
                return [value, labels[name] ?? name];
              }}
            />
            <ReferenceLine y={80} stroke="#22c55e" strokeDasharray="4 4" label={{ value: 'Target', fontSize: 10 }} />
            <Line
              type="monotone"
              dataKey="complianceScore"
              stroke="#3b82f6"
              strokeWidth={2}
              dot={(props: Record<string, unknown>) => {
                const { cx, cy, payload } = props as { cx: number; cy: number; payload: TrendDataPoint };
                if (payload?.isSignificantDecline) {
                  return (
                    <circle
                      key={`decline-${cx}-${cy}`}
                      cx={cx}
                      cy={cy}
                      r={5}
                      fill="#ef4444"
                      stroke="#ef4444"
                      strokeWidth={2}
                    />
                  );
                }
                return <circle key={`dot-${cx}-${cy}`} cx={cx} cy={cy} r={3} fill="#3b82f6" stroke="#3b82f6" />;
              }}
            />
            <Line
              type="monotone"
              dataKey="narrativeCoverage"
              stroke="#8b5cf6"
              strokeWidth={1}
              strokeDasharray="4 2"
              dot={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>

      {/* Legend */}
      <div className="flex items-center gap-4 text-xs text-gray-500">
        <span className="flex items-center gap-1">
          <span className="w-3 h-0.5 bg-indigo-500 inline-block" /> Compliance Score
        </span>
        <span className="flex items-center gap-1">
          <span className="w-3 h-0.5 bg-purple-500 inline-block border-dashed" /> Narrative Coverage
        </span>
        <span className="flex items-center gap-1">
          <span className="w-2 h-2 bg-red-500 rounded-full inline-block" /> Significant Decline (&gt;5%)
        </span>
      </div>
    </div>
  );
}
