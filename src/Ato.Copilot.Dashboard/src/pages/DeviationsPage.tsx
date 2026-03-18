import { useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import DeviationSummaryCards from '../components/DeviationSummaryCards';
import DeviationTable from '../components/DeviationTable';
import DeviationDetailDrawer from '../components/DeviationDetailDrawer';
import { getDeviations, getDeviationSummary } from '../api/deviations';
import { usePolling } from '../hooks/usePolling';
import type { DeviationListItem, DeviationSummary } from '../types/dashboard';

export default function DeviationsPage() {
  const { id: systemId } = useParams<{ id: string }>();
  const [items, setItems] = useState<DeviationListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [summary, setSummary] = useState<DeviationSummary | null>(null);
  const [loading, setLoading] = useState(true);

  // Filters
  const [typeFilter, setTypeFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [severityFilter, setSeverityFilter] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  // Drawer
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    if (!systemId) return;
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
      {loading ? (
        <p className="text-sm text-gray-400">Loading...</p>
      ) : (
        <>
          <DeviationSummaryCards summary={summary} />
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
      <DeviationDetailDrawer
        deviationId={selectedId}
        onClose={() => setSelectedId(null)}
        onActionComplete={handleActionComplete}
      />
    </>
  );
}
