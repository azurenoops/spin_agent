import { useState, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import { CapabilityCard } from '../components/cards/CapabilityCard';
import { CapabilityForm } from '../components/forms/CapabilityForm';
import { MappingPanel } from '../components/cards/MappingPanel';
import { usePolling } from '../hooks/usePolling';
import {
  getCapabilities,
  createCapability,
  updateCapability,
  deleteCapability,
  getCapabilityImpactPreview,
} from '../api/capabilities';
import type { CapabilityImpactPreview } from '../api/capabilities';
import type {
  SecurityCapabilityDto,
  CreateCapabilityRequest,
  PaginatedResponse,
} from '../types/dashboard';

const NIST_FAMILIES: Record<string, string> = {
  AC: 'Access Control', AT: 'Awareness and Training', AU: 'Audit and Accountability',
  CA: 'Assessment, Authorization, and Monitoring', CM: 'Configuration Management',
  CP: 'Contingency Planning', IA: 'Identification and Authentication',
  IR: 'Incident Response', MA: 'Maintenance', MP: 'Media Protection',
  PE: 'Physical and Environmental Protection', PL: 'Planning',
  PM: 'Program Management', PS: 'Personnel Security',
  PT: 'PII Processing and Transparency', RA: 'Risk Assessment',
  SA: 'System and Services Acquisition', SC: 'System and Communications Protection',
  SI: 'System and Information Integrity', SR: 'Supply Chain Risk Management',
};

const STATUS_OPTIONS = ['Planned', 'InProgress', 'Implemented', 'Deprecated'];

export default function CapabilityLibrary() {
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [editingCap, setEditingCap] = useState<SecurityCapabilityDto | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [impactPreview, setImpactPreview] = useState<CapabilityImpactPreview | null>(null);
  const [pendingUpdate, setPendingUpdate] = useState<CreateCapabilityRequest | null>(null);

  const fetcher = useCallback(
    () => getCapabilities({ search: search || undefined, category: categoryFilter || undefined, status: statusFilter || undefined }),
    [search, categoryFilter, statusFilter],
  );
  const { data, refresh } = usePolling<PaginatedResponse<SecurityCapabilityDto>>(fetcher, 30000);
  const capabilities = data?.items ?? [];

  const handleCreate = async (req: CreateCapabilityRequest) => {
    setSubmitting(true);
    setFormError(null);
    try {
      await createCapability(req);
      setShowCreate(false);
      refresh();
    } catch (err: any) {
      setFormError(err?.response?.data?.error ?? 'Failed to create capability');
    } finally {
      setSubmitting(false);
    }
  };

  const handleUpdate = async (req: CreateCapabilityRequest) => {
    if (!editingCap) return;
    // Check if description/provider changed — if so, show impact preview first
    const descChanged = req.description !== editingCap.description || req.provider !== editingCap.provider;
    if (descChanged && !impactPreview) {
      setSubmitting(true);
      setFormError(null);
      try {
        const preview = await getCapabilityImpactPreview(editingCap.id);
        if (preview.totalNarratives > 0) {
          setImpactPreview(preview);
          setPendingUpdate(req);
          return;
        }
        // No affected narratives — proceed directly
      } catch {
        // Preview failed — proceed with update anyway
      } finally {
        setSubmitting(false);
      }
    }
    await executeUpdate(req);
  };

  const executeUpdate = async (req: CreateCapabilityRequest) => {
    if (!editingCap) return;
    setSubmitting(true);
    setFormError(null);
    try {
      await updateCapability(editingCap.id, req);
      setEditingCap(null);
      setImpactPreview(null);
      setPendingUpdate(null);
      refresh();
    } catch (err: any) {
      setFormError(err?.response?.data?.error ?? 'Failed to update capability');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteCapability(id);
      setDeleteConfirm(null);
      setExpandedId(null);
      refresh();
    } catch {
      // silently handled
    }
  };

  return (
    <PageLayout title="Security Capabilities">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Security Capabilities</h1>
        <p className="text-sm text-gray-500 mt-1">Define and manage organizational security capabilities mapped to controls</p>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3 mb-6">
        <input
          type="text"
          placeholder="Search capabilities..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="border rounded px-3 py-2 text-sm w-64 focus:ring-2 focus:ring-blue-300 focus:outline-none"
        />
        <select
          value={categoryFilter}
          onChange={(e) => setCategoryFilter(e.target.value)}
          className="border rounded px-3 py-2 text-sm"
        >
          <option value="">All Categories</option>
          {Object.entries(NIST_FAMILIES).map(([code, label]) => (
            <option key={code} value={code}>{code} — {label}</option>
          ))}
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="border rounded px-3 py-2 text-sm"
        >
          <option value="">All Statuses</option>
          {STATUS_OPTIONS.map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </select>
        <div className="flex-1" />
        <button
          onClick={() => { setShowCreate(true); setFormError(null); }}
          className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700"
        >
          + New Capability
        </button>
      </div>

      {/* Create modal */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-lg p-6">
            <h2 className="text-lg font-semibold mb-4">Create Security Capability</h2>
            <CapabilityForm
              onSubmit={handleCreate}
              onCancel={() => setShowCreate(false)}
              isSubmitting={submitting}
              error={formError}
            />
          </div>
        </div>
      )}

      {/* Edit modal */}
      {editingCap && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-lg p-6">
            <h2 className="text-lg font-semibold mb-4">Edit Capability</h2>
            <CapabilityForm
              initial={editingCap}
              onSubmit={handleUpdate}
              onCancel={() => setEditingCap(null)}
              isSubmitting={submitting}
              error={formError}
            />
          </div>
        </div>
      )}

      {/* Impact preview modal */}
      {impactPreview && pendingUpdate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-md p-6">
            <h2 className="text-lg font-semibold mb-3">Narrative Impact Preview</h2>
            <p className="text-sm text-gray-600 mb-4">
              Changing this capability will regenerate narratives across {impactPreview.totalSystems} system{impactPreview.totalSystems !== 1 ? 's' : ''}.
            </p>
            <div className="bg-gray-50 rounded p-3 mb-4 text-sm space-y-1">
              <div className="flex justify-between"><span>Narratives to regenerate:</span><span className="font-medium">{impactPreview.totalNarratives}</span></div>
              <div className="flex justify-between"><span>Custom narratives (skipped):</span><span className="font-medium">{impactPreview.customSkipped}</span></div>
            </div>
            {impactPreview.bySystem.length > 0 && (
              <table className="w-full text-sm mb-4">
                <thead>
                  <tr className="border-b text-left text-gray-500">
                    <th className="pb-1">System</th>
                    <th className="pb-1 text-right">Regenerate</th>
                    <th className="pb-1 text-right">Skipped</th>
                  </tr>
                </thead>
                <tbody>
                  {impactPreview.bySystem.map((s) => (
                    <tr key={s.systemId} className="border-b last:border-0">
                      <td className="py-1">{s.systemName ?? s.systemId}</td>
                      <td className="py-1 text-right">{s.narrativeCount}</td>
                      <td className="py-1 text-right">{s.customSkipped}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <div className="flex justify-end gap-3">
              <button
                onClick={() => { setImpactPreview(null); setPendingUpdate(null); }}
                className="px-4 py-2 text-sm border rounded hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={() => executeUpdate(pendingUpdate)}
                disabled={submitting}
                className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
              >
                {submitting ? 'Saving...' : 'Confirm & Save'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete confirmation */}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-sm p-6 text-center">
            <h3 className="text-lg font-semibold mb-2">Delete Capability?</h3>
            <p className="text-sm text-gray-500 mb-4">
              This will unlink all control narratives and create review tasks.
            </p>
            <div className="flex justify-center gap-3">
              <button
                onClick={() => setDeleteConfirm(null)}
                className="px-4 py-2 text-sm border rounded hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(deleteConfirm)}
                className="px-4 py-2 text-sm bg-red-600 text-white rounded hover:bg-red-700"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Capability list */}
      {capabilities.length === 0 ? (
        <div className="text-center py-16">
          <p className="text-lg text-gray-400 mb-2">No security capabilities found</p>
          <p className="text-sm text-gray-400">
            Create your first Security Capability to start mapping controls.
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          <p className="text-sm text-gray-500 mb-2">{data?.totalCount ?? 0} capabilities</p>
          {capabilities.map((cap) => (
            <div key={cap.id}>
              <CapabilityCard
                capability={cap}
                isExpanded={expandedId === cap.id}
                onToggle={() => setExpandedId(expandedId === cap.id ? null : cap.id)}
                onEdit={() => { setEditingCap(cap); setFormError(null); }}
                onDelete={() => setDeleteConfirm(cap.id)}
              />
              {expandedId === cap.id && (
                <div className="ml-4 mt-2 pl-4 border-l-2 border-indigo-200">
                  <MappingPanel capabilityId={cap.id} />
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </PageLayout>
  );
}
