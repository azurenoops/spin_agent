import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import InheritanceSummaryBar from '../components/inheritance/InheritanceSummaryBar';
import InheritanceTable from '../components/inheritance/InheritanceTable';
import BulkUpdateToolbar from '../components/inheritance/BulkUpdateToolbar';
import AuditHistoryPanel from '../components/inheritance/AuditHistoryPanel';
import CrmView from '../components/inheritance/CrmView';
import { listInheritance, setInheritance, getAudit, getCrm, exportCrm, getOrgDefaults, deriveOrgDefaults, revertToOrgDefaults } from '../api/inheritance';
import type { OrgInheritanceDefault, OrgDefaultsListResult } from '../types/inheritance';
import type {
  InheritanceSummary,
  InheritanceDesignation,
  InheritanceListQuery,
  InheritanceType,
  CrmResult,
  CrmExportFormat,
  CrmExportLayout,
  AuditEntry,
} from '../types/inheritance';

export default function ControlInheritance() {
  const { id: systemId } = useParams<{ id: string }>();

  // ─── State ────────────────────────────────────────────────────────────────

  const [items, setItems] = useState<InheritanceDesignation[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [summary, setSummary] = useState<InheritanceSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState<InheritanceListQuery>({ page: 1, pageSize: 50 });
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  // Audit panel
  const [auditControlId, setAuditControlId] = useState<string | null>(null);
  const [auditEntries, setAuditEntries] = useState<AuditEntry[]>([]);
  const [auditLoading, setAuditLoading] = useState(false);

  // CRM view
  const [crmData, setCrmData] = useState<CrmResult | null>(null);
  const [crmLoading, setCrmLoading] = useState(false);
  const [showCrm, setShowCrm] = useState(false);

  // Error state
  const [noBaseline, setNoBaseline] = useState(false);

  // Narrative auto-update banner
  const [narrativeBanner, setNarrativeBanner] = useState<string | null>(null);

  // Org defaults modal
  const [showOrgDefaults, setShowOrgDefaults] = useState(false);
  const [orgDefaults, setOrgDefaults] = useState<OrgInheritanceDefault[]>([]);
  const [orgDefaultsTotal, setOrgDefaultsTotal] = useState(0);
  const [orgDefaultsLoading, setOrgDefaultsLoading] = useState(false);
  const [deriving, setDeriving] = useState(false);
  const [showMoreActions, setShowMoreActions] = useState(false);

  const hasOrgDefaults = (summary?.orgDefaultCount ?? 0) > 0;

  // ─── Data Fetching ────────────────────────────────────────────────────────

  const fetchData = useCallback(async () => {
    if (!systemId) return;
    setLoading(true);
    setNoBaseline(false);
    try {
      const data = await listInheritance(systemId, query);
      setItems(data.items);
      setTotalItems(data.totalItems);
      setSummary(data.summary);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 404) setNoBaseline(true);
    } finally {
      setLoading(false);
    }
  }, [systemId, query]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const fetchAudit = useCallback(async (controlId: string) => {
    if (!systemId) return;
    setAuditControlId(controlId);
    setAuditLoading(true);
    try {
      const data = await getAudit(systemId, controlId);
      setAuditEntries(data.entries);
    } catch {
      setAuditEntries([]);
    } finally {
      setAuditLoading(false);
    }
  }, [systemId]);

  // ─── CRM Actions ──────────────────────────────────────────────────────────

  const handleGenerateCrm = useCallback(async () => {
    if (!systemId) return;
    setCrmLoading(true);
    setShowCrm(true);
    try {
      const data = await getCrm(systemId);
      setCrmData(data);
    } catch {
      setCrmData(null);
    } finally {
      setCrmLoading(false);
    }
  }, [systemId]);

  const handleExportCrm = async (format: CrmExportFormat, layout: CrmExportLayout) => {
    if (!systemId) return;
    const blob = await exportCrm(systemId, format, layout);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `crm-${systemId}-${new Date().toISOString().slice(0, 10)}.${format === 'csv' ? 'csv' : 'xlsx'}`;
    a.click();
    URL.revokeObjectURL(url);
  };

  // ─── CSP Profile Actions ─────────────────────────────────────────────────

  // ─── Org Defaults Actions ────────────────────────────────────────────────

  const handleViewOrgDefaults = async () => {
    setShowOrgDefaults(true);
    setOrgDefaultsLoading(true);
    try {
      const data: OrgDefaultsListResult = await getOrgDefaults({ pageSize: 200 });
      setOrgDefaults(data.items);
      setOrgDefaultsTotal(data.totalCount);
    } catch {
      setOrgDefaults([]);
    } finally {
      setOrgDefaultsLoading(false);
    }
  };

  const handleDeriveOrgDefaults = async () => {
    setDeriving(true);
    try {
      const result = await deriveOrgDefaults();
      setNarrativeBanner(`Org defaults derived: ${result.derivedCount} derived, ${result.removedCount} removed`);
      await fetchData();
      // Refresh CRM if currently open so it reflects new org defaults
      if (showCrm && systemId) {
        setCrmLoading(true);
        try {
          const data = await getCrm(systemId);
          setCrmData(data);
        } catch {
          setCrmData(null);
        } finally {
          setCrmLoading(false);
        }
      }
    } catch {
      setNarrativeBanner('Failed to derive org defaults. Check that capability mappings exist.');
    } finally {
      setDeriving(false);
    }
  };

  const handleRevertSelected = async () => {
    if (!systemId || selectedIds.size === 0) return;
    try {
      const result = await revertToOrgDefaults(systemId, Array.from(selectedIds));
      setNarrativeBanner(`Reverted ${result.revertedCount} control(s) to org defaults.`);
      setSelectedIds(new Set());
      await fetchData();
    } catch {
      setNarrativeBanner('Failed to revert selected controls to org defaults.');
    }
  };

  // ─── Actions ──────────────────────────────────────────────────────────────

  const handleSave = async (edit: { controlId: string; inheritanceType: string; provider?: string; customerResponsibility?: string }) => {
    if (!systemId) return;
    const result = await setInheritance(systemId, {
      designations: [{
        controlId: edit.controlId,
        inheritanceType: edit.inheritanceType as InheritanceType,
        provider: edit.provider,
        customerResponsibility: edit.customerResponsibility,
      }],
      changeSource: 'Manual',
    });
    if (result.narrativesAutoUpdated > 0) {
      setNarrativeBanner(`${result.narrativesAutoUpdated} narrative${result.narrativesAutoUpdated !== 1 ? 's' : ''} auto-updated: Inherited → Implemented, Shared → Partially Implemented`);
    }
    await fetchData();
  };

  const handleBulkApply = async (inheritanceType: string, provider?: string, customerResponsibility?: string) => {
    if (!systemId || selectedIds.size === 0) return;
    const result = await setInheritance(systemId, {
      designations: Array.from(selectedIds).map(controlId => ({
        controlId,
        inheritanceType: inheritanceType as InheritanceType,
        provider,
        customerResponsibility,
      })),
      changeSource: 'BulkUpdate',
    });
    if (result.narrativesAutoUpdated > 0) {
      setNarrativeBanner(`${result.narrativesAutoUpdated} narrative${result.narrativesAutoUpdated !== 1 ? 's' : ''} auto-updated: Inherited → Implemented, Shared → Partially Implemented`);
    }
    setSelectedIds(new Set());
    await fetchData();
  };

  const toggleSelect = (controlId: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(controlId)) next.delete(controlId);
      else next.add(controlId);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (items.every(i => selectedIds.has(i.controlId))) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(items.map(i => i.controlId)));
    }
  };

  // ─── Render ───────────────────────────────────────────────────────────────

  if (!systemId) {
    return <div className="p-6 text-gray-500">No system selected.</div>;
  }

  return (
    <div className="p-6 space-y-6">
      {noBaseline && (
        <div className="rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <strong>No baseline configured.</strong> This system does not have a control baseline yet. Apply a baseline before managing control inheritance.
        </div>
      )}
      {narrativeBanner && (
        <div className="flex items-center justify-between rounded-lg border border-indigo-300 bg-indigo-50 px-4 py-3 text-sm text-indigo-800">
          <span>{narrativeBanner}</span>
          <button onClick={() => setNarrativeBanner(null)} className="ml-4 text-indigo-600 hover:text-indigo-800 font-medium">&times;</button>
        </div>
      )}
      {hasOrgDefaults && summary && (
        <div className="rounded-lg border border-teal-200 bg-teal-50 px-4 py-3 text-sm text-teal-800">
          <strong>{summary.orgDefaultCount}</strong> of {summary.totalControls} controls have org-level defaults.
          {summary.undesignatedCount > 0 && ' Apply a CSP profile to fill remaining gaps (optional).'}
        </div>
      )}
      <div className="rounded-lg border border-indigo-200 bg-indigo-50 px-4 py-3 text-sm text-indigo-800">
        Designations are derived from Security Capabilities.{' '}
        <Link to="/capabilities" className="font-medium underline hover:text-indigo-900">
          Manage Capabilities &rarr;
        </Link>
      </div>
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Control Inheritance</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage inheritance designations, apply CSP profiles, and generate Customer Responsibility Matrices for the active baseline.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleViewOrgDefaults}
            className="rounded-lg border border-teal-600 px-4 py-2 text-sm font-medium text-teal-600 hover:bg-teal-50"
          >
            View Org Defaults
          </button>
          <button
            onClick={handleDeriveOrgDefaults}
            disabled={deriving}
            className="rounded-lg border border-teal-600 bg-teal-600 px-4 py-2 text-sm font-medium text-white hover:bg-teal-700 disabled:opacity-50"
          >
            {deriving ? 'Deriving...' : 'Derive Org Defaults'}
          </button>
          <button
            onClick={handleGenerateCrm}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
          >
            Generate CRM
          </button>
          {hasOrgDefaults && (
            <div className="relative">
              <button
                onClick={() => setShowMoreActions(v => !v)}
                className="rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                &#8943;
              </button>
              {showMoreActions && (
                <div className="absolute right-0 z-10 mt-1 w-48 rounded-lg border border-gray-200 bg-white py-1 shadow-lg">
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      <InheritanceSummaryBar summary={summary} loading={loading} />

      {showCrm && (
        <CrmView
          crm={crmData}
          loading={crmLoading}
          onExport={handleExportCrm}
          onClose={() => setShowCrm(false)}
        />
      )}

      <div className="flex items-center gap-4">
        <BulkUpdateToolbar
          selectedCount={selectedIds.size}
          onApply={handleBulkApply}
          onClearSelection={() => setSelectedIds(new Set())}
        />
        {selectedIds.size > 0 && (
          <button
            onClick={handleRevertSelected}
            className="rounded-lg border border-teal-600 px-3 py-1.5 text-sm font-medium text-teal-600 hover:bg-teal-50"
          >
            Revert to Org Defaults ({selectedIds.size})
          </button>
        )}
      </div>

      <div className={auditControlId ? 'grid grid-cols-1 gap-6 lg:grid-cols-3' : ''}>
        <div className={auditControlId ? 'lg:col-span-2' : ''}>
          <InheritanceTable
            items={items}
            totalItems={totalItems}
            query={query}
            loading={loading}
            selectedIds={selectedIds}
            onQueryChange={setQuery}
            onRowClick={item => fetchAudit(item.controlId)}
            onToggleSelect={toggleSelect}
            onToggleSelectAll={toggleSelectAll}
            onSave={handleSave}
          />
        </div>
        {auditControlId && (
          <div>
            <AuditHistoryPanel
              controlId={auditControlId}
              entries={auditEntries}
              loading={auditLoading}
              onClose={() => setAuditControlId(null)}
            />
          </div>
        )}
      </div>

      {/* Org Defaults Modal */}
      {showOrgDefaults && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="max-h-[80vh] w-full max-w-3xl overflow-hidden rounded-xl bg-white shadow-xl">
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
              <div>
                <h2 className="text-lg font-semibold text-gray-900">Org-Level Inheritance Defaults</h2>
                <p className="text-sm text-gray-500">{orgDefaultsTotal} defaults derived from capability mappings</p>
              </div>
              <button onClick={() => setShowOrgDefaults(false)} className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>
            <div className="max-h-[60vh] overflow-y-auto px-6 py-4">
              {orgDefaultsLoading ? (
                <div className="py-10 text-center text-gray-400">Loading org defaults...</div>
              ) : orgDefaults.length === 0 ? (
                <div className="py-10 text-center text-gray-400">
                  <p className="font-medium">No org defaults found</p>
                  <p className="mt-1 text-xs">Click "Derive Org Defaults" to generate them from capability mappings.</p>
                </div>
              ) : (
                <table className="min-w-full divide-y divide-gray-200">
                  <thead className="bg-gray-50">
                    <tr>
                      {['Control ID', 'Type', 'Provider', 'Source Capability', 'Derived At'].map(h => (
                        <th key={h} className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wider text-gray-500">{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-200">
                    {orgDefaults.map(d => (
                      <tr key={d.id} className="hover:bg-gray-50">
                        <td className="whitespace-nowrap px-4 py-2 text-sm font-medium text-gray-900">{d.controlId}</td>
                        <td className="whitespace-nowrap px-4 py-2 text-sm">
                          <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                            d.inheritanceType === 'Inherited' ? 'bg-green-100 text-green-700' :
                            d.inheritanceType === 'Shared' ? 'bg-indigo-100 text-indigo-700' :
                            'bg-gray-100 text-gray-600'
                          }`}>{d.inheritanceType}</span>
                        </td>
                        <td className="max-w-xs truncate px-4 py-2 text-sm text-gray-600">{d.provider ?? '—'}</td>
                        <td className="max-w-xs px-4 py-2 text-sm text-gray-600 group relative">
                          <span className="truncate block cursor-help" title={d.sourceCapabilityNames ?? '—'}>
                            {d.sourceCapabilityNames ?? '—'}
                          </span>
                          {d.sourceCapabilityNames && (
                            <div className="absolute bottom-full left-0 z-20 mb-1 hidden group-hover:block w-64 rounded-lg border border-gray-200 bg-white p-3 shadow-lg text-xs text-gray-700">
                              <p className="font-medium text-gray-900 mb-1">Source Capabilities</p>
                              {d.sourceCapabilityNames.split(',').map((name, i) => (
                                <p key={i} className="truncate">{name.trim()}</p>
                              ))}
                              <p className="mt-1 text-gray-400">
                                <Link to="/capabilities" className="underline hover:text-indigo-600">View components &rarr;</Link>
                              </p>
                            </div>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-4 py-2 text-sm text-gray-500">
                          {d.derivedAt ? new Date(d.derivedAt).toLocaleDateString() : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
            <div className="flex justify-end border-t border-gray-200 px-6 py-3">
              <button
                onClick={() => setShowOrgDefaults(false)}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
