import { useState, useCallback } from 'react';
import { Link, useParams } from '../features/workspaces/workspaceNavigation';
import DeviationSummaryCards from '../components/DeviationSummaryCards';
import DeviationTable from '../components/DeviationTable';
import DeviationDetailDrawer from '../components/DeviationDetailDrawer';
import AddDeviationDialog from '../components/AddDeviationDialog';
import { getDeviations, getDeviationSummary } from '../api/deviations';
import { usePolling } from '../hooks/usePolling';
import type { DeviationListItem, DeviationSummary } from '../types/dashboard';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport } from '../features/systems/SystemTaskPresentation';

export default function DeviationsPage() {
  const { id: systemId } = useParams<{ id: string }>();
  const [items, setItems] = useState<DeviationListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [summary, setSummary] = useState<DeviationSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Filters
  const [typeFilter, setTypeFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [severityFilter, setSeverityFilter] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  // Drawer
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showAddDialog, setShowAddDialog] = useState(false);

  const fetchData = useCallback(async () => {
    if (!systemId) return;
    setLoadError(null);
    try {
      const [listResult, summaryResult] = await Promise.all([
        getDeviations(systemId, {
          type: typeFilter || undefined,
          status: statusFilter || undefined,
          severity: severityFilter || undefined,
          search: search || undefined,
          page,
          pageSize: 50,
        }),
        getDeviationSummary(systemId),
      ]);
      setItems(listResult.items);
      setTotalCount(listResult.totalCount);
      setSummary(summaryResult);
    } catch (reason) {
      setLoadError(reason instanceof Error ? reason.message : 'Unable to load system exceptions.');
    } finally {
      setLoading(false);
    }
  }, [systemId, typeFilter, statusFilter, severityFilter, search, page]);

  usePolling(fetchData);

  const handleActionComplete = () => {
    setSelectedId(null);
    fetchData();
  };

  if (!systemId) return null;

  return (
    <>
      {/* Header */}
      <SystemTaskHeading title="Risk decisions & exceptions"
        description="Review each exception with its justification, evidence, authority and expiration."
        action={<button
          type="button"
          onClick={() => setShowAddDialog(true)}
          className="inline-flex items-center gap-1.5 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          Request exception
        </button>} />

      <SystemTaskColumns support={<>
        <SystemTaskSupport title="Contributes to"><p>Risk register / Package supporting decisions</p></SystemTaskSupport>
        <SystemTaskSupport title="Distinct records and authority">
          <p>Waivers, risk acceptance requests and false-positive claims remain separate records. Submission does not approve a claim or change an authorization decision.</p>
          <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/poam`}>Linked POA&amp;M items</Link>
          <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/authorize`}>Recorded decisions</Link>
        </SystemTaskSupport>
      </>}>
      {loadError && <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
        <p role="alert">{loadError}</p>
        {items.length > 0 && <p className="mt-2">Showing previously loaded records. Open a record to recheck its current state before acting.</p>}
        <button type="button" className="mt-3 rounded border px-3 py-2" onClick={() => void fetchData()}>Retry exceptions</button>
      </div>}
      {loading ? (
        <p className="text-sm text-gray-400">Loading...</p>
      ) : (!loadError || items.length > 0) && (
        <>
          <details className="rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900">
            <summary className="cursor-pointer text-sm font-semibold">Exception summary</summary>
            <div className="mt-4"><DeviationSummaryCards summary={summary} /></div>
          </details>
          <DeviationTable
            items={items}
            totalCount={totalCount}
            page={page}
            pageSize={50}
            typeFilter={typeFilter}
            statusFilter={statusFilter}
            severityFilter={severityFilter}
            search={search}
            onTypeChange={(t) => { setTypeFilter(t); setPage(1); }}
            onStatusChange={(s) => { setStatusFilter(s); setPage(1); }}
            onSeverityChange={(s) => { setSeverityFilter(s); setPage(1); }}
            onSearchChange={(s) => { setSearch(s); setPage(1); }}
            onPageChange={setPage}
            onRowClick={setSelectedId}
          />
        </>
      )}
      </SystemTaskColumns>
      <DeviationDetailDrawer
        deviationId={selectedId}
        onClose={() => setSelectedId(null)}
        onActionComplete={handleActionComplete}
      />

      {showAddDialog && (
        <AddDeviationDialog
          systemId={systemId}
          onClose={() => setShowAddDialog(false)}
          onCreated={() => {
            setShowAddDialog(false);
            fetchData();
          }}
        />
      )}
    </>
  );
}
