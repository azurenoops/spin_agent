import { useState, useEffect, useCallback } from 'react';
import {
  LineChart, Line, BarChart, Bar, XAxis, YAxis, CartesianGrid,
  Tooltip, Legend, ResponsiveContainer,
} from 'recharts';
import { getPoamTrend, exportTrendPdf } from '../../api/poam';
import type { PoamTrendResponse } from '../../types/poam';

interface PoamTrendChartsProps {
  systemId: string;
}

export default function PoamTrendCharts({ systemId }: PoamTrendChartsProps) {
  const [trend, setTrend] = useState<PoamTrendResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [period, setPeriod] = useState('monthly');
  const [dateRange, setDateRange] = useState<{ start: string; end: string }>({
    start: '',
    end: '',
  });
  const [exporting, setExporting] = useState(false);

  const fetchTrend = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getPoamTrend(
        systemId,
        period,
        dateRange.start || undefined,
        dateRange.end || undefined,
      );
      setTrend(data);
    } catch {
      // Silent
    } finally {
      setLoading(false);
    }
  }, [systemId, period, dateRange]);

  useEffect(() => {
    void fetchTrend();
  }, [fetchTrend]);

  const handleExportPdf = async () => {
    setExporting(true);
    try {
      await exportTrendPdf(systemId, period, dateRange.start || undefined, dateRange.end || undefined);
    } catch {
      // Silent
    } finally {
      setExporting(false);
    }
  };

  if (loading && !trend) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="h-6 w-6 animate-spin rounded-full border-4 border-indigo-500 border-t-transparent" />
      </div>
    );
  }

  if (!trend) return null;

  const openOverTimeData = trend.openOverTime.map(d => ({
    name: d.date,
    open: d.count,
  }));

  const closureData = trend.closureRate.map(d => ({
    name: d.period,
    closed: d.closed,
    opened: d.opened,
  }));

  const agingData = trend.agingBreakdown.map(d => ({
    name: d.bucket,
    'CAT I': d.catI,
    'CAT II': d.catII,
    'CAT III': d.catIII,
  }));

  const timeToCloseData = trend.timeToClose.map(d => ({
    name: d.bucket,
    count: d.count,
  }));

  return (
    <div className="space-y-6">
      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <select
          value={period}
          onChange={(e) => setPeriod(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
        >
          <option value="daily">Daily</option>
          <option value="weekly">Weekly</option>
          <option value="monthly">Monthly</option>
        </select>
        <input
          type="date"
          value={dateRange.start}
          onChange={(e) => setDateRange(prev => ({ ...prev, start: e.target.value }))}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
          placeholder="Start date"
        />
        <input
          type="date"
          value={dateRange.end}
          onChange={(e) => setDateRange(prev => ({ ...prev, end: e.target.value }))}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
          placeholder="End date"
        />
        <button
          onClick={() => void handleExportPdf()}
          disabled={exporting}
          className="rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {exporting ? 'Exporting...' : 'Export PDF'}
        </button>
      </div>

      {/* Charts Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Open Over Time */}
        <div className="rounded-lg border border-gray-200 bg-white p-4">
          <h3 className="text-sm font-semibold text-gray-700 mb-3">Open POA&Ms Over Time</h3>
          <ResponsiveContainer width="100%" height={250}>
            <LineChart data={openOverTimeData}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="name" tick={{ fontSize: 10 }} />
              <YAxis tick={{ fontSize: 10 }} />
              <Tooltip />
              <Line type="monotone" dataKey="open" stroke="#3b82f6" strokeWidth={2} dot={{ r: 3 }} />
            </LineChart>
          </ResponsiveContainer>
        </div>

        {/* Closure Rate */}
        <div className="rounded-lg border border-gray-200 bg-white p-4">
          <h3 className="text-sm font-semibold text-gray-700 mb-3">Monthly Closure Rate</h3>
          <ResponsiveContainer width="100%" height={250}>
            <BarChart data={closureData}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="name" tick={{ fontSize: 10 }} />
              <YAxis tick={{ fontSize: 10 }} />
              <Tooltip />
              <Legend />
              <Bar dataKey="closed" fill="#22c55e" name="Closed" />
              <Bar dataKey="opened" fill="#ef4444" name="Opened" />
            </BarChart>
          </ResponsiveContainer>
        </div>

        {/* Aging Breakdown */}
        <div className="rounded-lg border border-gray-200 bg-white p-4">
          <h3 className="text-sm font-semibold text-gray-700 mb-3">Aging by Severity</h3>
          <ResponsiveContainer width="100%" height={250}>
            <BarChart data={agingData}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="name" tick={{ fontSize: 10 }} />
              <YAxis tick={{ fontSize: 10 }} />
              <Tooltip />
              <Legend />
              <Bar dataKey="CAT I" stackId="a" fill="#7c3aed" />
              <Bar dataKey="CAT II" stackId="a" fill="#ef4444" />
              <Bar dataKey="CAT III" stackId="a" fill="#f59e0b" />
            </BarChart>
          </ResponsiveContainer>
        </div>

        {/* Time to Close Distribution */}
        <div className="rounded-lg border border-gray-200 bg-white p-4">
          <h3 className="text-sm font-semibold text-gray-700 mb-3">Time to Close Distribution</h3>
          <ResponsiveContainer width="100%" height={250}>
            <BarChart data={timeToCloseData}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="name" tick={{ fontSize: 10 }} />
              <YAxis tick={{ fontSize: 10 }} />
              <Tooltip />
              <Bar dataKey="count" fill="#6366f1" />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
}
