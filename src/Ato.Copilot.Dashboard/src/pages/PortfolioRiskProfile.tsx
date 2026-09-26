import { useState, useCallback, useMemo, useEffect, useRef } from 'react';
import { Link } from '../features/workspaces/workspaceNavigation';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import PortfolioWorkspaceLinks from '../components/layout/PortfolioWorkspaceLinks';
import PortfolioSummaryChart from '../components/charts/PortfolioSummaryChart';
import { usePolling } from '../hooks/usePolling';
import { getPortfolio } from '../api/portfolio';
import { getCoverage } from '../api/capabilities';
import type { PortfolioSystemSummary } from '../types/dashboard';

export default function PortfolioRiskProfile() {
  const [systems, setSystems] = useState<PortfolioSystemSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [coveragePct, setCoveragePct] = useState<number | null>(null);
  const [coverageFailed, setCoverageFailed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);
  const [coverageRevision, setCoverageRevision] = useState(0);
  const active = useRef(true);
  const pending = useRef(false);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);

  const fetchData = useCallback(async () => {
    if (pending.current) return;
    pending.current = true;
    try {
      const all: PortfolioSystemSummary[] = [];
      const seen = new Set<string>();
      let cursor: string | undefined;
      do {
        const result = await getPortfolio({ sortBy: 'complianceScore', sortDir: 'asc', pageSize: 100, ...(cursor ? { cursor } : {}) });
        if (!active.current) return;
        all.push(...result.items);
        cursor = result.nextCursor ?? undefined;
        if (cursor && seen.has(cursor)) throw new Error('Portfolio pagination could not complete. Please retry.');
        if (cursor) seen.add(cursor);
      } while (cursor);
      setSystems(all);
      setError(null);
      setUpdatedAt(new Date().toLocaleTimeString());
    } catch (reason) {
      if (active.current) setError(reason instanceof Error ? reason.message : 'Unable to load portfolio.');
    } finally {
      pending.current = false;
      if (active.current) setLoading(false);
    }
  }, []);

  usePolling(fetchData);

  useEffect(() => {
    let cancelled = false;
    setCoveragePct(null);
    setCoverageFailed(false);
    getCoverage(false, false).then(res => {
      if (!cancelled) setCoveragePct(res.orgWide.coveragePercent ?? null);
    }).catch(() => {
      if (!cancelled) setCoverageFailed(true);
    });
    return () => { cancelled = true; };
  }, [coverageRevision]);

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
    const atoCounts = new Map<string, number>([['Active', 0], ['Expired', 0], ['Not recorded', 0]]);
    for (const system of systems) {
      const recordedStatus = system.atoStatus?.trim();
      const label = !recordedStatus || recordedStatus === 'None' ? 'Not recorded' : recordedStatus;
      atoCounts.set(label, (atoCounts.get(label) ?? 0) + 1);
    }
    const atoSeries = [...atoCounts].map(([label, count]) => ({
      label, count,
      color: label === 'Active' ? '#10b981' : label === 'Expired' ? '#ef4444' : label === 'Not recorded' ? '#94a3b8' : '#6366f1',
    }));
    return { totalSystems, avgCompliance, totalPoams, totalOverdue, totalCatI, totalCatII, totalCatIII, expiredOrExpiring, atoSeries };
  }, [systems]);

  return <PageLayout title="Organization portfolio">
    <PageHero eyebrow="Organization overview" title="Organization portfolio"
      description="Your systems, security posture and the work that needs attention."
      actions={<button type="button" onClick={() => { void fetchData(); setCoverageRevision(v => v + 1); }}
        className="rounded-lg border border-white/30 bg-white/15 px-4 py-2 text-sm font-semibold text-white hover:bg-white/25">Refresh portfolio</button>} />
    <PortfolioWorkspaceLinks />
    {error && <div role="alert" className="mb-6 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
      <p className="font-semibold">Portfolio could not be refreshed</p><p>{error}</p>
      {updatedAt && <p>Showing the last successful update at {updatedAt}.</p>}
      <button type="button" onClick={() => void fetchData()} className="mt-2 font-semibold underline">Retry portfolio</button>
    </div>}
    {loading && <p className="text-slate-500">Loading portfolio data...</p>}
    {!loading && stats && <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 xl:grid-cols-4">
        <KpiCard label="Total Systems" value={stats.totalSystems} />
        <KpiCard label="Avg Compliance" value={`${stats.avgCompliance}%`}
          valueColor={stats.avgCompliance >= 90 ? 'text-green-600' : stats.avgCompliance >= 70 ? 'text-amber-600' : 'text-red-600'}
          title="Average recorded compliance across accessible systems; this score alone does not establish assessment history." />
        <KpiCard label="Open POA&Ms" value={stats.totalPoams} />
        <KpiCard label="Overdue" value={stats.totalOverdue} valueColor={stats.totalOverdue > 0 ? 'text-red-600' : undefined} />
        <KpiCard label="CAT I Findings" value={stats.totalCatI} valueColor={stats.totalCatI > 0 ? 'text-red-600' : undefined} />
        <KpiCard label="CAT II Findings" value={stats.totalCatII} valueColor={stats.totalCatII > 0 ? 'text-amber-600' : undefined} />
        <KpiCard label="ATO At Risk" value={stats.expiredOrExpiring} valueColor={stats.expiredOrExpiring > 0 ? 'text-red-600' : undefined} />
        <KpiCard label="Capability coverage" value={coverageFailed ? 'Unavailable' : coveragePct === null ? 'Not available' : `${coveragePct.toFixed(1)}%`}
          title="Organization-wide capability coverage, separate from system compliance and ATO decisions." />
      </div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <PortfolioSummaryChart title="ATO status across systems" category="Systems" series={stats.atoSeries}
          emptyMessage="No system authorization data" testId="org-dashboard-ato-status-chart" />
        <PortfolioSummaryChart title="Open findings by severity" category="Open findings" stacked
          summary={`${(stats.totalCatI + stats.totalCatII + stats.totalCatIII).toLocaleString()} findings · ${stats.totalPoams.toLocaleString()} open POA&Ms`}
          emptyMessage="No open findings" testId="org-dashboard-findings-by-severity-chart"
          series={[
            { label: 'CAT I', count: stats.totalCatI, color: '#ef4444' },
            { label: 'CAT II', count: stats.totalCatII, color: '#f59e0b' },
            { label: 'CAT III', count: stats.totalCatIII, color: '#818cf8' },
          ]} />
      </div>
      <section aria-labelledby="portfolio-attention" className="rounded-xl border border-indigo-200 bg-indigo-50/50 p-5 dark:border-indigo-900 dark:bg-indigo-950/30">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="portfolio-attention" className="font-semibold text-gray-900 dark:text-gray-100">Needs attention</h2>
          <span className="text-xs text-gray-500 dark:text-gray-400">{updatedAt ? `Updated ${updatedAt}` : 'Recorded system data'}</span>
        </div>
        <div className="mt-4 grid gap-4 sm:grid-cols-3">
          <div><p className="text-2xl font-semibold text-indigo-700 dark:text-indigo-300">{systems.filter(s => !s.isSetupComplete).length}</p><p className="text-sm text-gray-600 dark:text-gray-300">Systems with setup remaining</p></div>
          <div><p className="text-2xl font-semibold text-indigo-700 dark:text-indigo-300">{stats.totalOverdue}</p><p className="text-sm text-gray-600 dark:text-gray-300">Overdue POA&Ms</p></div>
          <div><p className="text-2xl font-semibold text-indigo-700 dark:text-indigo-300">{stats.expiredOrExpiring}</p><p className="text-sm text-gray-600 dark:text-gray-300">Expired or at-risk authorizations</p></div>
        </div>
        <p className="mt-4 text-xs text-gray-500 dark:text-gray-400">Use Systems to review system details and next steps. Catalog coverage does not establish an ATO.</p>
        {coverageFailed && <p className="mt-2 text-sm text-amber-800 dark:text-amber-200">Capability coverage could not be loaded. <button type="button" className="font-semibold underline" onClick={() => setCoverageRevision(v => v + 1)}>Retry coverage</button></p>}
      </section>
    </div>}
    {!loading && !error && systems.length === 0 && <div className="py-16 text-center">
      <p className="text-slate-500">No systems available in this workspace.</p>
      <Link to="/systems" className="mt-2 inline-block text-sm text-indigo-600 hover:underline">Open Systems</Link>
    </div>}
  </PageLayout>;
}

function KpiCard({ label, value, valueColor, title }: { label: string; value: string | number; valueColor?: string; title?: string }) {
  return <div className="rounded-xl border border-gray-200 bg-white px-4 py-3 dark:border-gray-700 dark:bg-gray-900" title={title}>
    <p className="text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-gray-400">{label}</p>
    <p className={`mt-1 text-2xl font-bold ${valueColor || 'text-gray-900 dark:text-gray-100'}`}>{value}</p>
  </div>;
}
