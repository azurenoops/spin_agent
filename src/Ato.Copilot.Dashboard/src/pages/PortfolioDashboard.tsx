import { useState, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import SystemSummaryRow from '../components/cards/SystemSummaryRow';
import { usePolling } from '../hooks/usePolling';
import { getPortfolio } from '../api/portfolio';
import type { PortfolioSystemSummary } from '../types/dashboard';

const SORT_COLUMNS = [
  { key: 'name', label: 'System Name' },
  { key: 'impactLevel', label: 'Impact Level' },
  { key: 'rmfPhase', label: 'RMF Phase' },
  { key: 'complianceScore', label: 'Compliance' },
  { key: 'atoExpiration', label: 'ATO' },
  { key: 'openPoamCount', label: 'POA&Ms' },
] as const;

export default function PortfolioDashboard() {
  const [systems, setSystems] = useState<PortfolioSystemSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [sortBy, setSortBy] = useState('name');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [impactFilter, setImpactFilter] = useState('');
  const [rmfFilter, setRmfFilter] = useState('');

  const fetchPortfolio = useCallback(async () => {
    try {
      const result = await getPortfolio({
        sortBy,
        sortDir,
        impactLevel: impactFilter || undefined,
        rmfPhase: rmfFilter || undefined,
      });
      setSystems(result.items);
      setTotalCount(result.totalCount);
    } finally {
      setLoading(false);
    }
  }, [sortBy, sortDir, impactFilter, rmfFilter]);

  usePolling(fetchPortfolio);

  const toggleSort = (column: string) => {
    if (sortBy === column) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(column);
      setSortDir('asc');
    }
  };

  return (
    <PageLayout title="Portfolio Dashboard">
      {/* Filters */}
      <div className="mb-4 flex gap-3">
        <select
          value={impactFilter}
          onChange={(e) => setImpactFilter(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
        >
          <option value="">All Impact Levels</option>
          <option value="Low">Low</option>
          <option value="Moderate">Moderate</option>
          <option value="High">High</option>
        </select>
        <select
          value={rmfFilter}
          onChange={(e) => setRmfFilter(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
        >
          <option value="">All RMF Phases</option>
          <option value="Prepare">Prepare</option>
          <option value="Categorize">Categorize</option>
          <option value="Select">Select</option>
          <option value="Implement">Implement</option>
          <option value="Assess">Assess</option>
          <option value="Authorize">Authorize</option>
          <option value="Monitor">Monitor</option>
        </select>
        <span className="ml-auto self-center text-sm text-gray-500">
          {totalCount} system{totalCount !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Table */}
      {loading ? (
        <p className="text-gray-500">Loading portfolio...</p>
      ) : systems.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 p-12 text-center">
          <p className="text-gray-500">No systems registered</p>
        </div>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {SORT_COLUMNS.map((col) => (
                  <th
                    key={col.key}
                    onClick={() => toggleSort(col.key)}
                    className="cursor-pointer px-3 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700"
                  >
                    {col.label}
                    {sortBy === col.key && (
                      <span className="ml-1">{sortDir === 'asc' ? '↑' : '↓'}</span>
                    )}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {systems.map((system) => (
                <SystemSummaryRow key={system.systemId} system={system} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </PageLayout>
  );
}
