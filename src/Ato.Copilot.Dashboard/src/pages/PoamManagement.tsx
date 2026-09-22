import { useState, useCallback } from 'react';
import { useSystemContext } from '../components/layout/SystemLayout';
import { usePoamList, usePoamMetrics, useCreatePoam } from '../hooks/usePoam';
import PoamSummaryCards from '../components/poam/PoamSummaryCards';
import PoamSeverityHeatbar from '../components/poam/PoamSeverityHeatbar';
import PoamTable from '../components/poam/PoamTable';
import PoamDetailDrawer from '../components/poam/PoamDetailDrawer';
import PoamCreateForm from '../components/poam/PoamCreateForm';
import PoamTrendCharts from '../components/poam/PoamTrendCharts';
import TicketingConfig from '../components/poam/TicketingConfig';
import PoamExportDialog from '../components/poam/PoamExportDialog';
import type { PoamListItem, PoamListQuery, CreatePoamRequest } from '../types/poam';
import AsyncErrorState from '../components/AsyncErrorState';
import { useSystemMutationPermission } from '../components/permissions/useSystemMutationPermission';

type ViewTab = 'overview' | 'trends' | 'ticketing';

export default function PoamManagement() {
  const { detail } = useSystemContext();
  const systemId = detail.systemId;
  const canManageRemediation = useSystemMutationPermission(systemId, 'canManageRemediation');
  const [permissionError, setPermissionError] = useState<string | null>(null);

  const [activeTab, setActiveTab] = useState<ViewTab>('overview');
  const [query, setQuery] = useState<PoamListQuery>({ page: 1, pageSize: 25, sortBy: 'scheduledCompletionDate', sortDirection: 'asc' });
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [showExportDialog, setShowExportDialog] = useState(false);
  const [selectedPoam, setSelectedPoam] = useState<PoamListItem | null>(null);

  const {
    data: poamData,
    loading: listLoading,
    error: listError,
    refresh: refreshList,
  } = usePoamList(systemId, query);
  const {
    data: metrics,
    loading: metricsLoading,
    error: metricsError,
    refresh: refreshMetrics,
  } = usePoamMetrics(systemId);
  const { create, loading: creating } = useCreatePoam();

  const items = poamData?.items ?? [];
  const totalItems = poamData?.totalCount ?? 0;

  const handleCreate = useCallback(async (req: CreatePoamRequest) => {
    if (!canManageRemediation) throw new Error('Permission denied: you cannot manage remediation for this system.');
    await create(systemId, req);
    setShowCreateForm(false);
  }, [systemId, create, canManageRemediation]);

  return (
    <div className="space-y-6">
      {permissionError && <p role="alert" className="text-sm text-red-600">{permissionError}</p>}
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">POA&amp;M Management</h1>
          <p className="mt-1 text-sm text-gray-500">
            Plan of Action &amp; Milestones — track, prioritize, and resolve security findings
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setShowExportDialog(true)}
            className="inline-flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5M16.5 12L12 16.5m0 0L7.5 12m4.5 4.5V3" />
            </svg>
            Export
          </button>
          <button
            disabled={!canManageRemediation}
            onClick={() => {
              if (!canManageRemediation) {
                setPermissionError('Permission denied: you cannot manage remediation for this system.');
                return;
              }
              setShowCreateForm(true);
            }}
            className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
            </svg>
            Add POA&amp;M
          </button>
        </div>
      </div>

      {/* Tab Switcher */}
      <div className="border-b border-gray-200">
        <nav className="-mb-px flex gap-6">
          {[
            { key: 'overview' as ViewTab, label: 'Overview' },
            { key: 'trends' as ViewTab, label: 'Trends & Analytics' },
            { key: 'ticketing' as ViewTab, label: 'Ticketing' },
          ].map(tab => (
            <button
              key={tab.key}
              onClick={() => setActiveTab(tab.key)}
              className={`pb-3 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.key
                  ? 'border-indigo-600 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'overview' ? (
        <>
          {/* Summary Cards */}
          {metrics && <PoamSummaryCards metrics={metrics} />}

          {metricsLoading && !metrics && (
            <p className="py-4 text-center text-sm text-gray-500" role="status">Loading POA&amp;M summary...</p>
          )}

          {metricsError && (
            <AsyncErrorState
              title={metrics ? 'Unable to refresh POA&M summary.' : 'Unable to load POA&M summary.'}
              onRetry={refreshMetrics}
            />
          )}

          {/* Severity Heatbar */}
          {metrics && metrics.totalOpen > 0 && (
            <PoamSeverityHeatbar catI={metrics.catICount} catII={metrics.catIICount} catIII={metrics.catIIICount} />
          )}

          {/* Table */}
          {listError && poamData && (
            <AsyncErrorState title="Unable to refresh POA&amp;M items." onRetry={refreshList} />
          )}

          {listError && !poamData ? (
            <AsyncErrorState title="Unable to load POA&amp;M items." onRetry={refreshList} />
          ) : (
            <PoamTable
              items={items}
              totalItems={totalItems}
              query={query}
              loading={listLoading}
              onQueryChange={setQuery}
              onRowClick={setSelectedPoam}
            />
          )}
        </>
      ) : activeTab === 'trends' ? (
        <PoamTrendCharts systemId={systemId} />
      ) : (
        <TicketingConfig systemId={systemId} />
      )}

      {/* Detail Drawer */}
      {selectedPoam && (
        <PoamDetailDrawer poamId={selectedPoam.id} onClose={() => setSelectedPoam(null)} />
      )}

      {/* Create Form */}
      {showCreateForm && (
        <PoamCreateForm
          systemId={systemId}
          onClose={() => setShowCreateForm(false)}
          onSubmit={handleCreate}
          loading={creating}
        />
      )}

      {/* Export Dialog */}
      {showExportDialog && (
        <PoamExportDialog
          systemId={systemId}
          currentStatus={query.status}
          currentSeverity={query.catSeverity}
          onClose={() => setShowExportDialog(false)}
        />
      )}
    </div>
  );
}
