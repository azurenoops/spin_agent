import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import InheritanceSummaryBar from '../components/inheritance/InheritanceSummaryBar';
import InheritanceTable from '../components/inheritance/InheritanceTable';
import BulkUpdateToolbar from '../components/inheritance/BulkUpdateToolbar';
import AuditHistoryPanel from '../components/inheritance/AuditHistoryPanel';
import CrmView from '../components/inheritance/CrmView';
import CspProfileDialog from '../components/inheritance/CspProfileDialog';
import CrmImportDialog from '../components/inheritance/CrmImportDialog';
import { listInheritance, setInheritance, getAudit, getCrm, exportCrm, getProfiles, applyProfile, importPreview, importApply } from '../api/inheritance';
import type { ImportPreview, ImportApplyResult } from '../types/inheritance';
import type {
  InheritanceSummary,
  InheritanceDesignation,
  InheritanceListQuery,
  InheritanceType,
  CrmResult,
  CrmExportFormat,
  CrmExportLayout,
  CspProfile,
  ApplyProfilePreview,
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

  // CSP profile dialog
  const [showProfileDialog, setShowProfileDialog] = useState(false);
  const [profiles, setProfiles] = useState<CspProfile[]>([]);
  const [profilesLoading, setProfilesLoading] = useState(false);

  // CRM import dialog
  const [showImportDialog, setShowImportDialog] = useState(false);

  // Error state
  const [noBaseline, setNoBaseline] = useState(false);

  // Narrative auto-update banner
  const [narrativeBanner, setNarrativeBanner] = useState<string | null>(null);

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

  const handleOpenProfileDialog = async () => {
    if (!systemId) return;
    setShowProfileDialog(true);
    setProfilesLoading(true);
    try {
      const data = await getProfiles(systemId);
      setProfiles(data.profiles);
    } catch {
      setProfiles([]);
    } finally {
      setProfilesLoading(false);
    }
  };

  const handlePreviewProfile = async (profileId: string, conflictResolution: string): Promise<ApplyProfilePreview | null> => {
    if (!systemId) return null;
    try {
      const result = await applyProfile(systemId, {
        profileId,
        conflictResolution: conflictResolution as 'skip' | 'overwrite',
        preview: true,
      });
      return result as ApplyProfilePreview;
    } catch {
      return null;
    }
  };

  const handleApplyProfile = async (profileId: string, conflictResolution: string) => {
    if (!systemId) return;
    const result = await applyProfile(systemId, {
      profileId,
      conflictResolution: conflictResolution as 'skip' | 'overwrite',
      preview: false,
    });
    setShowProfileDialog(false);
    if ('narrativesAutoUpdated' in result && result.narrativesAutoUpdated > 0) {
      setNarrativeBanner(`${result.narrativesAutoUpdated} narrative${result.narrativesAutoUpdated !== 1 ? 's' : ''} auto-updated: Inherited → Implemented, Shared → Partially Implemented`);
    }
    await fetchData();
  };

  // ─── CRM Import Actions ────────────────────────────────────────────────────

  const handleImportPreview = async (file: File): Promise<ImportPreview | null> => {
    if (!systemId) return null;
    try {
      return await importPreview(systemId, file);
    } catch {
      return null;
    }
  };

  const handleImportApply = async (
    previewToken: string,
    columnMapping: Record<string, string>,
    conflictResolution: 'skip' | 'overwrite',
  ): Promise<ImportApplyResult | null> => {
    if (!systemId) return null;
    try {
      const result = await importApply(systemId, {
        previewToken,
        columnMapping: columnMapping as {
          controlId: string; inheritanceType: string; provider: string; customerResponsibility: string;
        },
        conflictResolution,
      });
      await fetchData();
      return result;
    } catch {
      return null;
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
          <strong>No baseline configured.</strong> This system does not have a control baseline yet. Apply a baseline before managing control inheritance, importing a CRM, or applying a CSP profile.
        </div>
      )}
      {narrativeBanner && (
        <div className="flex items-center justify-between rounded-lg border border-blue-300 bg-blue-50 px-4 py-3 text-sm text-blue-800">
          <span>{narrativeBanner}</span>
          <button onClick={() => setNarrativeBanner(null)} className="ml-4 text-blue-600 hover:text-blue-800 font-medium">&times;</button>
        </div>
      )}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Control Inheritance</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage inheritance designations, apply CSP profiles, and generate Customer Responsibility Matrices for the active baseline.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleOpenProfileDialog}
            className="rounded-lg border border-indigo-600 px-4 py-2 text-sm font-medium text-indigo-600 hover:bg-indigo-50"
          >
            Apply CSP Profile
          </button>
          <button
            onClick={() => setShowImportDialog(true)}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Import CRM
          </button>
          <button
            onClick={handleGenerateCrm}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
          >
            Generate CRM
          </button>
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

      <BulkUpdateToolbar
        selectedCount={selectedIds.size}
        onApply={handleBulkApply}
        onClearSelection={() => setSelectedIds(new Set())}
      />

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

      <CspProfileDialog
        open={showProfileDialog}
        profiles={profiles}
        loading={profilesLoading}
        onPreview={handlePreviewProfile}
        onApply={handleApplyProfile}
        onClose={() => setShowProfileDialog(false)}
      />

      <CrmImportDialog
        open={showImportDialog}
        onPreview={handleImportPreview}
        onApply={handleImportApply}
        onClose={() => setShowImportDialog(false)}
      />
    </div>
  );
}
