import { useState, useCallback, useMemo } from 'react';
import { Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import { usePolling } from '../hooks/usePolling';
import { getPortfolio } from '../api/portfolio';
import type { PortfolioSystemSummary, AtoSeverity } from '../types/dashboard';

// ─── Helpers ────────────────────────────────────────────────────────────────────

function severityColor(severity: AtoSeverity) {
  switch (severity) {
    case 'green': return 'bg-green-100 text-green-700';
    case 'yellow': return 'bg-amber-100 text-amber-700';
    case 'red': return 'bg-red-100 text-red-700';
    case 'expired': return 'bg-red-200 text-red-900';
    default: return 'bg-gray-100 text-gray-500';
  }
}

function complianceColor(score: number) {
  if (score >= 90) return 'bg-green-500';
  if (score >= 70) return 'bg-amber-500';
  return 'bg-red-500';
}

function complianceBadge(score: number) {
  if (score >= 90) return 'bg-green-100 text-green-700';
  if (score >= 70) return 'bg-amber-100 text-amber-700';
  return 'bg-red-100 text-red-700';
}

// ─── Component ──────────────────────────────────────────────────────────────────

export default function PortfolioRiskProfile() {
  const [systems, setSystems] = useState<PortfolioSystemSummary[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchData = useCallback(async () => {
    try {
      const result = await getPortfolio({ sortBy: 'complianceScore', sortDir: 'asc' });
      setSystems(result.items);
    } finally {
      setLoading(false);
    }
  }, []);

  usePolling(fetchData);

  // ─── Aggregations ───────────────────────────────────────────────────────────
  const stats = useMemo(() => {
    if (systems.length === 0) return null;

    const totalSystems = systems.length;
    const avgCompliance = Math.round(systems.reduce((sum, s) => sum + s.complianceScore, 0) / totalSystems * 10) / 10;
    const totalPoams = systems.reduce((sum, s) => sum + s.openPoamCount, 0);
    const totalOverdue = systems.reduce((sum, s) => sum + s.overduePoamCount, 0);
    const totalCatI = systems.reduce((sum, s) => sum + s.catICounts, 0);
    const totalCatII = systems.reduce((sum, s) => sum + s.catIICounts, 0);
    const totalCatIII = systems.reduce((sum, s) => sum + s.catIIICounts, 0);
    const expiredOrExpiring = systems.filter(s => s.atoSeverity === 'expired' || s.atoSeverity === 'red').length;

    return { totalSystems, avgCompliance, totalPoams, totalOverdue, totalCatI, totalCatII, totalCatIII, expiredOrExpiring };
  }, [systems]);

  // ─── Render ─────────────────────────────────────────────────────────────────
  return (
    <PageLayout title="Portfolio Risk Profile">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Portfolio Risk Profile</h1>
        <p className="text-sm text-gray-500 mt-1">Aggregate risk posture across all registered systems</p>
      </div>

      {loading && <p className="text-gray-500">Loading portfolio data...</p>}

      {!loading && stats && (
        <div className="space-y-6">
          {/* KPI Cards */}
          <div className="grid grid-cols-2 md:grid-cols-4 xl:grid-cols-7 gap-4">
            <KpiCard label="Total Systems" value={stats.totalSystems} />
            <KpiCard label="Avg Compliance" value={`${stats.avgCompliance}%`} valueColor={stats.avgCompliance >= 90 ? 'text-green-600' : stats.avgCompliance >= 70 ? 'text-amber-600' : 'text-red-600'} />
            <KpiCard label="Open POA&Ms" value={stats.totalPoams} />
            <KpiCard label="Overdue" value={stats.totalOverdue} valueColor={stats.totalOverdue > 0 ? 'text-red-600' : undefined} />
            <KpiCard label="CAT I Findings" value={stats.totalCatI} valueColor={stats.totalCatI > 0 ? 'text-red-600' : undefined} />
            <KpiCard label="CAT II Findings" value={stats.totalCatII} valueColor={stats.totalCatII > 0 ? 'text-amber-600' : undefined} />
            <KpiCard label="ATO At Risk" value={stats.expiredOrExpiring} valueColor={stats.expiredOrExpiring > 0 ? 'text-red-600' : undefined} />
          </div>

          {/* Compliance by System */}
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
            <div className="rounded-xl border border-gray-200 bg-white p-5">
              <h2 className="text-base font-semibold text-gray-900 mb-4">Compliance by System</h2>
              <div className="space-y-3">
                {[...systems].sort((a, b) => a.complianceScore - b.complianceScore).map(s => (
                  <div key={s.systemId} className="flex items-center gap-3">
                    <Link to={`/systems/${s.systemId}`} className="w-32 text-sm text-blue-600 hover:underline truncate" title={s.name}>
                      {s.acronym || s.name}
                    </Link>
                    <div className="flex-1 h-5 bg-gray-100 rounded-full overflow-hidden">
                      <div className={`h-5 rounded-full ${complianceColor(s.complianceScore)} flex items-center justify-end pr-2`} style={{ width: `${Math.max(s.complianceScore, 5)}%` }}>
                        <span className="text-[11px] font-semibold text-white">{s.complianceScore}%</span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Findings by Severity */}
            <div className="rounded-xl border border-gray-200 bg-white p-5">
              <h2 className="text-base font-semibold text-gray-900 mb-4">Findings by Severity</h2>
              <div className="space-y-3">
                {systems.filter(s => s.catICounts + s.catIICounts + s.catIIICounts > 0).sort((a, b) => (b.catICounts * 100 + b.catIICounts * 10 + b.catIIICounts) - (a.catICounts * 100 + a.catIICounts * 10 + a.catIIICounts)).map(s => {
                  const total = s.catICounts + s.catIICounts + s.catIIICounts;
                  return (
                    <div key={s.systemId} className="flex items-center gap-3">
                      <Link to={`/systems/${s.systemId}`} className="w-32 text-sm text-blue-600 hover:underline truncate" title={s.name}>
                        {s.acronym || s.name}
                      </Link>
                      <div className="flex-1 flex gap-0.5 h-5 rounded-full overflow-hidden">
                        {s.catICounts > 0 && <div className="bg-red-500 h-5 flex items-center justify-center" style={{ width: `${s.catICounts / total * 100}%`, minWidth: '24px' }}><span className="text-[10px] font-bold text-white">{s.catICounts}</span></div>}
                        {s.catIICounts > 0 && <div className="bg-amber-500 h-5 flex items-center justify-center" style={{ width: `${s.catIICounts / total * 100}%`, minWidth: '24px' }}><span className="text-[10px] font-bold text-white">{s.catIICounts}</span></div>}
                        {s.catIIICounts > 0 && <div className="bg-blue-400 h-5 flex items-center justify-center" style={{ width: `${s.catIIICounts / total * 100}%`, minWidth: '24px' }}><span className="text-[10px] font-bold text-white">{s.catIIICounts}</span></div>}
                      </div>
                      <span className="text-xs text-gray-500 w-8 text-right">{total}</span>
                    </div>
                  );
                })}
                {systems.every(s => s.catICounts + s.catIICounts + s.catIIICounts === 0) && (
                  <p className="text-sm text-gray-400 text-center py-4">No open findings</p>
                )}
                <div className="flex gap-4 pt-2 text-xs text-gray-500 border-t border-gray-100 mt-2">
                  <span className="flex items-center gap-1"><span className="w-3 h-3 rounded-sm bg-red-500" /> CAT I</span>
                  <span className="flex items-center gap-1"><span className="w-3 h-3 rounded-sm bg-amber-500" /> CAT II</span>
                  <span className="flex items-center gap-1"><span className="w-3 h-3 rounded-sm bg-blue-400" /> CAT III</span>
                </div>
              </div>
            </div>
          </div>

          {/* POA&M Summary + ATO Status */}
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
            {/* POA&M by System */}
            <div className="rounded-xl border border-gray-200 bg-white p-5">
              <h2 className="text-base font-semibold text-gray-900 mb-4">Open POA&Ms by System</h2>
              <div className="space-y-3">
                {[...systems].filter(s => s.openPoamCount > 0).sort((a, b) => b.openPoamCount - a.openPoamCount).map(s => (
                  <div key={s.systemId} className="flex items-center gap-3">
                    <Link to={`/systems/${s.systemId}/remediation`} className="w-32 text-sm text-blue-600 hover:underline truncate" title={s.name}>
                      {s.acronym || s.name}
                    </Link>
                    <div className="flex-1 flex items-center gap-2">
                      <span className="text-sm font-semibold text-gray-900 w-8">{s.openPoamCount}</span>
                      {s.overduePoamCount > 0 && (
                        <span className="rounded-full bg-red-100 text-red-700 px-2 py-0.5 text-xs font-medium">{s.overduePoamCount} overdue</span>
                      )}
                    </div>
                  </div>
                ))}
                {systems.every(s => s.openPoamCount === 0) && (
                  <p className="text-sm text-gray-400 text-center py-4">No open POA&Ms</p>
                )}
              </div>
            </div>

            {/* ATO Status */}
            <div className="rounded-xl border border-gray-200 bg-white p-5">
              <h2 className="text-base font-semibold text-gray-900 mb-4">ATO Status</h2>
              <div className="space-y-2">
                {systems.map(s => (
                  <div key={s.systemId} className="flex items-center justify-between py-1.5">
                    <Link to={`/systems/${s.systemId}`} className="text-sm text-blue-600 hover:underline truncate max-w-[200px]" title={s.name}>
                      {s.acronym || s.name}
                    </Link>
                    <div className="flex items-center gap-3">
                      <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${severityColor(s.atoSeverity)}`}>
                        {s.atoStatus || 'Not Set'}
                      </span>
                      {s.atoDaysRemaining !== null && (
                        <span className="text-xs text-gray-500 w-20 text-right">
                          {s.atoDaysRemaining > 0 ? `${s.atoDaysRemaining}d remaining` : 'Expired'}
                        </span>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* System Risk Table */}
          <div className="rounded-xl border border-gray-200 bg-white overflow-hidden">
            <div className="px-5 py-4 border-b border-gray-100">
              <h2 className="text-base font-semibold text-gray-900">System Risk Summary</h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-100 bg-gray-50 text-left text-xs font-semibold uppercase tracking-wider text-gray-500">
                    <th className="px-5 py-3">System</th>
                    <th className="px-5 py-3">Impact</th>
                    <th className="px-5 py-3">RMF Phase</th>
                    <th className="px-5 py-3">Compliance</th>
                    <th className="px-5 py-3">POA&Ms</th>
                    <th className="px-5 py-3">CAT I</th>
                    <th className="px-5 py-3">CAT II</th>
                    <th className="px-5 py-3">CAT III</th>
                    <th className="px-5 py-3">ATO</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-50">
                  {systems.map(s => (
                    <tr key={s.systemId} className="hover:bg-gray-50">
                      <td className="px-5 py-3">
                        <Link to={`/systems/${s.systemId}`} className="font-medium text-blue-600 hover:underline">{s.name}</Link>
                        {s.acronym && <span className="ml-1 text-xs text-gray-400">({s.acronym})</span>}
                      </td>
                      <td className="px-5 py-3 text-gray-700">{s.impactLevel || '—'}</td>
                      <td className="px-5 py-3 text-gray-700">{s.currentRmfPhase}</td>
                      <td className="px-5 py-3">
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${complianceBadge(s.complianceScore)}`}>{s.complianceScore}%</span>
                      </td>
                      <td className="px-5 py-3">
                        <span className="text-gray-900">{s.openPoamCount}</span>
                        {s.overduePoamCount > 0 && <span className="ml-1 text-xs text-red-600">({s.overduePoamCount} overdue)</span>}
                      </td>
                      <td className="px-5 py-3"><span className={s.catICounts > 0 ? 'font-semibold text-red-600' : 'text-gray-400'}>{s.catICounts}</span></td>
                      <td className="px-5 py-3"><span className={s.catIICounts > 0 ? 'font-semibold text-amber-600' : 'text-gray-400'}>{s.catIICounts}</span></td>
                      <td className="px-5 py-3"><span className={s.catIIICounts > 0 ? 'font-medium text-blue-500' : 'text-gray-400'}>{s.catIIICounts}</span></td>
                      <td className="px-5 py-3">
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${severityColor(s.atoSeverity)}`}>{s.atoStatus || '—'}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {!loading && systems.length === 0 && (
        <div className="text-center py-16">
          <p className="text-gray-500">No systems registered yet.</p>
          <Link to="/systems" className="text-sm text-blue-600 hover:underline mt-2 inline-block">Go to Systems to add one</Link>
        </div>
      )}
    </PageLayout>
  );
}

// ─── Sub-components ─────────────────────────────────────────────────────────────

function KpiCard({ label, value, valueColor }: { label: string; value: string | number; valueColor?: string }) {
  return (
    <div className="rounded-xl border border-gray-200 bg-white px-4 py-3">
      <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">{label}</p>
      <p className={`text-2xl font-bold mt-1 ${valueColor || 'text-gray-900'}`}>{value}</p>
    </div>
  );
}
