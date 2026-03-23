import { useState, useCallback, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { BoundaryForm } from '../components/forms/BoundaryForm';
import { usePolling } from '../hooks/usePolling';
import {
  fetchBoundaryDefinitions,
  createBoundaryDefinition,
  updateBoundaryDefinition,
  deleteBoundaryDefinition,
  fetchBoundaryResources,
  fetchBoundaryComponents,
  assignComponentToBoundary,
  removeComponentFromBoundary,
  listBoundaryComponents,
  assignComponent,
  updateAssignment,
  removeAssignment as removeBoundaryAssignment,
  acquireLock,
  releaseLock,
  checkLockStatus,
} from '../api/boundaries';
import { listComponents } from '../api/components';
import type { OrgComponentDto } from '../api/components';
import type { BoundaryResourceDto } from '../api/boundaries';
import type {
  BoundaryDefinitionDto,
  BoundaryDefinitionType,
  CreateBoundaryDefinitionRequest,
  DeleteBoundaryDefinitionResponse,
  BoundaryComponentDto,
  BoundaryLockStatus,
} from '../types/dashboard';

const TYPE_FILTER_OPTIONS: BoundaryDefinitionType[] = ['Physical', 'Logical', 'Hybrid'];

const TYPE_BADGE: Record<string, string> = {
  Physical: 'bg-orange-100 text-orange-800',
  Logical: 'bg-blue-100 text-blue-800',
  Hybrid: 'bg-purple-100 text-purple-800',
};

type FormMode = { kind: 'closed' } | { kind: 'create' } | { kind: 'edit'; boundary: BoundaryDefinitionDto };

export default function BoundaryManagement() {
  const { id: systemId } = useParams<{ id: string }>();
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [formMode, setFormMode] = useState<FormMode>({ kind: 'closed' });
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<BoundaryDefinitionDto | null>(null);
  const [deleteResult, setDeleteResult] = useState<DeleteBoundaryDefinitionResponse | null>(null);

  // Filters
  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState('');

  // Resource management state
  const [expandedBoundary, setExpandedBoundary] = useState<string | null>(null);
  const [, setResources] = useState<BoundaryResourceDto[]>([]);
  const [, setResourcesLoading] = useState(false);

  // Component management state
  const [boundaryComponents, setBoundaryComponents] = useState<OrgComponentDto[]>([]);
  const [componentsLoading, setComponentsLoading] = useState(false);

  // P-16/P-17 workflow guidance (Feature 040 US7)
  const [systemComponentCount, setSystemComponentCount] = useState<number | null>(null);

  useEffect(() => {
    if (!systemId) return;
    listComponents({ page: 1, pageSize: 1 })
      .then(res => setSystemComponentCount(res.totalCount))
      .catch(() => setSystemComponentCount(null));
  }, [systemId]);

  const fetchData = useCallback(async () => {
    if (!systemId) return;
    try {
      const items = await fetchBoundaryDefinitions(systemId);
      setBoundaries(items);
      setError(null);
    } catch {
      setError('Failed to load boundary definitions');
    } finally {
      setLoading(false);
    }
  }, [systemId]);

  usePolling(fetchData);

  // Client-side filtering
  const filtered = boundaries.filter((b) => {
    if (search && !b.name.toLowerCase().includes(search.toLowerCase()) &&
      !(b.description ?? '').toLowerCase().includes(search.toLowerCase())) return false;
    if (typeFilter && b.boundaryType !== typeFilter) return false;
    return true;
  });

  const handleCreate = async (data: CreateBoundaryDefinitionRequest) => {
    if (!systemId) return;
    setSubmitting(true);
    setFormError(null);
    try {
      await createBoundaryDefinition(systemId, data);
      setFormMode({ kind: 'closed' });
      await fetchData();
    } catch (err: unknown) {
      const msg = err && typeof err === 'object' && 'error' in err
        ? (err as { error: string }).error
        : 'Failed to create boundary';
      setFormError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleUpdate = async (data: CreateBoundaryDefinitionRequest) => {
    if (formMode.kind !== 'edit') return;
    setSubmitting(true);
    setFormError(null);
    try {
      await updateBoundaryDefinition(formMode.boundary.id, data);
      setFormMode({ kind: 'closed' });
      await fetchData();
    } catch (err: unknown) {
      const msg = err && typeof err === 'object' && 'error' in err
        ? (err as { error: string }).error
        : 'Failed to update boundary';
      setFormError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteConfirm) return;
    try {
      const result = await deleteBoundaryDefinition(deleteConfirm.id);
      setDeleteResult(result);
      setDeleteConfirm(null);
      await fetchData();
    } catch (err: unknown) {
      const msg = err && typeof err === 'object' && 'error' in err
        ? (err as { error: string }).error
        : 'Failed to delete boundary';
      setError(msg);
      setDeleteConfirm(null);
    }
  };

  const handleExpandBoundary = async (boundaryId: string) => {
    if (expandedBoundary === boundaryId) {
      setExpandedBoundary(null);
      return;
    }
    setExpandedBoundary(boundaryId);
    setResourcesLoading(true);
    setComponentsLoading(true);
    try {
      const [res, comps] = await Promise.all([
        fetchBoundaryResources(boundaryId),
        fetchBoundaryComponents(boundaryId),
      ]);
      setResources(res);
      setBoundaryComponents(comps);
    } catch {
      setResources([]);
      setBoundaryComponents([]);
    } finally {
      setResourcesLoading(false);
      setComponentsLoading(false);
    }
  };

  if (loading) {
    return <p className="text-gray-500">Loading boundaries...</p>;
  }

  return (
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-2xl font-bold text-gray-900">Authorization Boundaries</h2>
            <p className="mt-1 text-sm text-gray-500">
              Define and manage system boundaries — assign components and track coverage
            </p>
          </div>
          <button
            type="button"
            onClick={() => { setFormMode({ kind: 'create' }); setFormError(null); }}
            className="inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            + Add Boundary
          </button>
        </div>

        {/* P-16 Guidance: Component Library first (Feature 040 US7) */}
        {systemComponentCount !== null && systemComponentCount === 0 && (
          <div className="rounded-lg border border-blue-200 bg-blue-50 p-4">
            <div className="flex items-start gap-3">
              <svg className="h-5 w-5 text-blue-600 mt-0.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M12 2a10 10 0 100 20 10 10 0 000-20z" />
              </svg>
              <div>
                <h3 className="text-sm font-semibold text-blue-800">NIST RMF Step P-16: Complete Asset Identification First</h3>
                <p className="mt-1 text-sm text-blue-700">
                  Before defining authorization boundaries (P-17), populate your Component Library with the system's assets.
                  Navigate to <strong>Component Inventory</strong> to discover Azure resources and import them as components.
                </p>
              </div>
            </div>
          </div>
        )}

        {/* Filters */}
        <div className="flex flex-wrap gap-3">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search boundaries..."
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          <select
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm"
          >
            <option value="">All Types</option>
            {TYPE_FILTER_OPTIONS.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
          <span className="self-center text-sm text-gray-500">
            {filtered.length} boundar{filtered.length !== 1 ? 'ies' : 'y'}
          </span>
        </div>

        {/* Banners */}
        {error && (
          <div className="bg-red-50 text-red-700 p-3 rounded text-sm">{error}</div>
        )}
        {deleteResult && (
          <div className="bg-green-50 text-green-800 p-3 rounded text-sm">
            Boundary deleted. Reassigned {deleteResult.reassignedComponents} components and {deleteResult.reassignedMappings} mappings
            to Primary boundary.
            <button onClick={() => setDeleteResult(null)} className="ml-2 text-green-600 hover:underline text-xs">Dismiss</button>
          </div>
        )}

        {/* Table */}
        {filtered.length === 0 ? (
          <div className="rounded-lg border-2 border-dashed border-gray-300 p-12 text-center">
            <p className="text-sm text-gray-500">
              {boundaries.length === 0
                ? 'No boundaries defined yet. Create one to get started.'
                : 'No boundaries match your filters.'}
            </p>
          </div>
        ) : (
          <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Name</th>
                  <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Type</th>
                  <th className="px-4 py-3 text-center text-xs font-medium uppercase text-gray-500">Components</th>
                  <th className="px-4 py-3 text-center text-xs font-medium uppercase text-gray-500">Coverage</th>
                  <th className="px-4 py-3 text-right text-xs font-medium uppercase text-gray-500">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {filtered.map((b) => (
                  <tr
                    key={b.id}
                    className="hover:bg-gray-50 cursor-pointer transition-colors"
                    onClick={() => handleExpandBoundary(b.id)}
                  >
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-medium text-gray-900">{b.name}</span>
                        {b.isPrimary && (
                          <span className="text-xs bg-green-100 text-green-800 px-1.5 py-0.5 rounded font-medium">Primary</span>
                        )}
                      </div>
                      {b.description && (
                        <p className="text-xs text-gray-500 mt-0.5 line-clamp-1">{b.description}</p>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${TYPE_BADGE[b.boundaryType] ?? 'bg-gray-100 text-gray-700'}`}>
                        {b.boundaryType}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-center text-sm text-gray-700">{b.componentCount}</td>
                    <td className="px-4 py-3 text-center">
                      <span className={`text-sm font-medium ${b.coveragePercent >= 80 ? 'text-green-700' : b.coveragePercent >= 50 ? 'text-yellow-700' : 'text-gray-700'}`}>
                        {b.coveragePercent.toFixed(1)}%
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex items-center justify-end gap-1">
                        <button
                          type="button"
                          onClick={(e) => { e.stopPropagation(); setFormMode({ kind: 'edit', boundary: b }); setFormError(null); }}
                          className="rounded p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
                          title="Edit boundary"
                        >
                          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Z" />
                          </svg>
                        </button>
                        {!b.isPrimary && (
                          <button
                            type="button"
                            onClick={(e) => { e.stopPropagation(); setDeleteConfirm(b); }}
                            className="rounded p-1.5 text-gray-400 hover:bg-red-50 hover:text-red-600"
                            title="Delete boundary"
                          >
                            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0" />
                            </svg>
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Create/Edit Modal */}
        {formMode.kind !== 'closed' && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
            <div className="w-full max-w-lg rounded-lg bg-white p-6 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 mb-4">
                {formMode.kind === 'create' ? 'Create Boundary' : 'Edit Boundary'}
              </h3>
              <BoundaryForm
                initial={formMode.kind === 'edit' ? formMode.boundary : undefined}
                onSubmit={formMode.kind === 'create' ? handleCreate : handleUpdate}
                onCancel={() => { setFormMode({ kind: 'closed' }); setFormError(null); }}
                isSubmitting={submitting}
                error={formError}
              />
            </div>
          </div>
        )}

        {/* Delete confirmation modal */}
        {deleteConfirm && (
          <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl p-6 max-w-md w-full mx-4">
              <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Boundary</h3>
              <p className="text-sm text-gray-600 mb-4">
                Are you sure you want to delete <strong>{deleteConfirm.name}</strong>?
                All components ({deleteConfirm.componentCount}) and mappings will be reassigned to the Primary boundary.
              </p>
              <div className="flex gap-2 justify-end">
                <button onClick={() => setDeleteConfirm(null)} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
                <button onClick={handleDelete} className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700">Delete</button>
              </div>
            </div>
          </div>
        )}

        {/* Resource / Component Management Dialog */}
        {expandedBoundary && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
            <div className="w-full max-w-3xl max-h-[85vh] flex flex-col rounded-lg bg-white shadow-xl mx-4">
              {/* Dialog header */}
              <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
                <h2 className="text-lg font-semibold text-gray-900">
                  {boundaries.find((b) => b.id === expandedBoundary)?.name} — Details
                </h2>
                <button
                  onClick={() => setExpandedBoundary(null)}
                  className="text-gray-400 hover:text-gray-600 text-xl leading-none"
                >
                  &times;
                </button>
              </div>

              {/* Tab header */}
              <div className="flex border-b border-gray-200 px-6">
                <div className="px-4 py-2.5 text-sm font-medium border-b-2 -mb-px border-blue-600 text-blue-600">
                  Components ({boundaryComponents.length})
                </div>
              </div>

              {/* Tab content */}
              <div className="flex-1 overflow-y-auto px-6 py-4">
                <BoundaryComponentsTab
                  boundaryId={expandedBoundary}
                  systemId={systemId!}
                  components={boundaryComponents}
                  loading={componentsLoading}
                  onRefresh={async () => {
                    const [comps] = await Promise.all([
                      fetchBoundaryComponents(expandedBoundary),
                      fetchData(),
                    ]);
                    setBoundaryComponents(comps);
                  }}
                />
              </div>
            </div>
          </div>
        )}
      </div>
  );
}

// ─── Boundary Components Tab ─────────────────────────────────────────────

const TYPE_COLORS: Record<string, string> = {
  Person: 'bg-blue-50 text-blue-700',
  Place: 'bg-green-50 text-green-700',
  Thing: 'bg-purple-50 text-purple-700',
};

function BoundaryComponentsTab({
  boundaryId,
  systemId,
  components,
  loading,
  onRefresh,
}: {
  boundaryId: string;
  systemId: string;
  components: OrgComponentDto[];
  loading: boolean;
  onRefresh: () => void;
}) {
  const [showAdd, setShowAdd] = useState(false);
  const [allComponents, setAllComponents] = useState<OrgComponentDto[]>([]);
  const [search, setSearch] = useState('');
  const [addingIds, setAddingIds] = useState<Set<string>>(new Set());

  // Feature 040 — Boundary component assignment with scope management
  const [bcAssignments, setBcAssignments] = useState<BoundaryComponentDto[]>([]);
  const [bcLoading, setBcLoading] = useState(false);
  const [lockStatus, setLockStatus] = useState<BoundaryLockStatus | null>(null);
  const [hasLock, setHasLock] = useState(false);
  const [editingScope, setEditingScope] = useState<string | null>(null);
  const [editRationale, setEditRationale] = useState('');
  const [editProvider, setEditProvider] = useState('');

  // Fetch boundary-component assignments (Feature 040)
  const fetchAssignments = useCallback(async () => {
    setBcLoading(true);
    try {
      const result = await listBoundaryComponents(systemId, boundaryId);
      setBcAssignments(result.items);
    } catch {
      // Fall back to legacy components
    } finally {
      setBcLoading(false);
    }
  }, [systemId, boundaryId]);

  useEffect(() => { fetchAssignments(); }, [fetchAssignments]);

  // Check lock status on mount
  useEffect(() => {
    checkLockStatus(systemId, boundaryId).then(setLockStatus).catch(() => {});
  }, [systemId, boundaryId]);

  useEffect(() => {
    if (showAdd && allComponents.length === 0) {
      listComponents({ pageSize: 200 }).then((r) => setAllComponents(r.items)).catch(() => {});
    }
  }, [showAdd, allComponents.length]);

  const assignedIds = new Set([...components.map((c) => c.id), ...bcAssignments.map((a) => a.componentId)]);
  const available = allComponents.filter(
    (c) => !assignedIds.has(c.id) && (!search || c.name.toLowerCase().includes(search.toLowerCase())),
  );

  const handleAcquireLock = async () => {
    try {
      const status = await acquireLock(systemId, boundaryId, 'current-user', 'Current User');
      setLockStatus(status);
      setHasLock(!status.message);
    } catch {
      // conflict
    }
  };

  const handleReleaseLock = async () => {
    try {
      await releaseLock(systemId, boundaryId);
      setLockStatus(null);
      setHasLock(false);
    } catch {
      // ignore
    }
  };

  const handleAdd = async (comp: OrgComponentDto) => {
    setAddingIds((prev) => new Set([...prev, comp.id]));
    try {
      await assignComponent(systemId, boundaryId, { componentId: comp.id, isInScope: true });
      await fetchAssignments();
      onRefresh();
    } catch {
      // fall back to legacy assign
      try {
        await assignComponentToBoundary(comp.id, systemId, boundaryId);
        onRefresh();
      } catch { /* ignore */ }
    } finally {
      setAddingIds((prev) => { const next = new Set(prev); next.delete(comp.id); return next; });
    }
  };

  const handleToggleScope = async (assignment: BoundaryComponentDto) => {
    if (!hasLock) {
      await handleAcquireLock();
    }
    setEditingScope(assignment.assignmentId);
    setEditRationale(assignment.exclusionRationale ?? '');
    setEditProvider(assignment.inheritanceProvider ?? '');
  };

  const handleSaveScope = async (assignmentId: string, isInScope: boolean) => {
    if (!isInScope && !editRationale.trim()) return;
    try {
      await updateAssignment(systemId, boundaryId, assignmentId, {
        isInScope,
        exclusionRationale: isInScope ? null : editRationale,
        inheritanceProvider: editProvider || null,
      });
      setEditingScope(null);
      await fetchAssignments();
    } catch {
      // ignore
    }
  };

  const handleRemoveNew = async (assignmentId: string) => {
    try {
      await removeBoundaryAssignment(systemId, boundaryId, assignmentId);
      await fetchAssignments();
      onRefresh();
    } catch {
      // ignore
    }
  };

  const handleRemove = async (comp: OrgComponentDto) => {
    const assignment = comp.systemAssignments.find(
      (a) => a.boundaryDefinitionId === boundaryId
        || (a.registeredSystemId === systemId && !a.boundaryDefinitionId),
    );
    if (!assignment) return;
    try {
      await removeComponentFromBoundary(comp.id, assignment.id);
      onRefresh();
    } catch {
      // ignore
    }
  };

  if (loading && bcLoading) return <p className="text-sm text-gray-500">Loading components...</p>;

  // Use new boundary assignments if available, fall back to legacy
  const useNewModel = bcAssignments.length > 0 || bcLoading;

  return (
    <div>
      {/* Lock status banner */}
      {lockStatus?.locked && !hasLock && (
        <div className="mb-3 p-2 bg-yellow-50 border border-yellow-200 rounded text-sm text-yellow-800">
          🔒 {lockStatus.message ?? `Locked by ${lockStatus.lockedBy}`}
        </div>
      )}

      <div className="flex items-center justify-between mb-3">
        <p className="text-sm text-gray-600">
          {(useNewModel ? bcAssignments.length : components.length)} component{(useNewModel ? bcAssignments.length : components.length) !== 1 ? 's' : ''} in this boundary
        </p>
        <div className="flex gap-2">
          {hasLock && (
            <button onClick={handleReleaseLock} className="px-3 py-1.5 text-xs text-gray-600 border rounded hover:bg-gray-50">
              Release Lock
            </button>
          )}
          <button
            onClick={() => setShowAdd(!showAdd)}
            className="px-3 py-1.5 text-sm bg-blue-600 text-white rounded hover:bg-blue-700"
          >
            {showAdd ? 'Done' : '+ Assign Component'}
          </button>
        </div>
      </div>

      {/* Add component picker */}
      {showAdd && (
        <div className="mb-4 rounded-lg border border-blue-200 bg-blue-50 p-3">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search org components..."
            className="w-full text-sm border border-gray-300 rounded-md px-3 py-1.5 mb-2 bg-white"
          />
          <div className="max-h-48 overflow-y-auto space-y-1">
            {available.length === 0 ? (
              <p className="text-xs text-gray-500 italic py-2 text-center">
                {allComponents.length === 0 ? 'Loading...' : 'All components already assigned'}
              </p>
            ) : (
              available.map((c) => (
                <div key={c.id} className="flex items-center justify-between bg-white rounded p-2 border border-gray-100">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium text-gray-800 truncate">{c.name}</span>
                      <span className={`text-xs px-1.5 py-0.5 rounded ${TYPE_COLORS[c.componentType] ?? 'bg-gray-100 text-gray-600'}`}>
                        {c.componentType}
                      </span>
                    </div>
                    {c.subType && <p className="text-xs text-gray-500">{c.subType}</p>}
                  </div>
                  <button
                    onClick={() => handleAdd(c)}
                    disabled={addingIds.has(c.id)}
                    className="ml-2 px-3 py-1 text-xs bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 flex-shrink-0"
                  >
                    {addingIds.has(c.id) ? 'Adding...' : 'Add'}
                  </button>
                </div>
              ))
            )}
          </div>
        </div>
      )}

      {/* Component assignments list (Feature 040 — scope-aware) */}
      {useNewModel ? (
        bcAssignments.length === 0 ? (
          <div className="text-center py-8">
            <p className="text-gray-500 mb-3">No components assigned to this boundary yet.</p>
            {!showAdd && (
              <button onClick={() => setShowAdd(true)} className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700">
                Assign Components
              </button>
            )}
          </div>
        ) : (
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 text-left text-xs font-medium uppercase text-gray-500">
                <th className="py-2 pr-3">Name</th>
                <th className="py-2 pr-3">Type</th>
                <th className="py-2 pr-3">Scope</th>
                <th className="py-2 pr-3">Rationale / Provider</th>
                <th className="py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {bcAssignments.map((a) => (
                <tr key={a.assignmentId}>
                  <td className="py-2 pr-3 font-medium text-gray-800">{a.componentName}</td>
                  <td className="py-2 pr-3">
                    <span className={`text-xs px-1.5 py-0.5 rounded ${TYPE_COLORS[a.componentType] ?? 'bg-gray-100 text-gray-600'}`}>
                      {a.componentType}
                    </span>
                  </td>
                  <td className="py-2 pr-3">
                    {editingScope === a.assignmentId ? (
                      <div className="space-y-1">
                        <select
                          defaultValue={a.isInScope ? 'inScope' : 'excluded'}
                          onChange={(e) => {
                            const inScope = e.target.value === 'inScope';
                            if (inScope) handleSaveScope(a.assignmentId, true);
                          }}
                          className="text-xs border rounded px-2 py-1"
                        >
                          <option value="inScope">In Scope</option>
                          <option value="excluded">Excluded</option>
                        </select>
                        {!a.isInScope && (
                          <>
                            <input
                              type="text"
                              value={editRationale}
                              onChange={(e) => setEditRationale(e.target.value)}
                              placeholder="Exclusion rationale (required)"
                              className="w-full text-xs border rounded px-2 py-1"
                            />
                            <input
                              type="text"
                              value={editProvider}
                              onChange={(e) => setEditProvider(e.target.value)}
                              placeholder="Inheritance provider (optional)"
                              className="w-full text-xs border rounded px-2 py-1"
                            />
                            <div className="flex gap-1">
                              <button
                                onClick={() => handleSaveScope(a.assignmentId, false)}
                                disabled={!editRationale.trim()}
                                className="text-xs bg-blue-600 text-white px-2 py-0.5 rounded disabled:opacity-50"
                              >
                                Save
                              </button>
                              <button onClick={() => setEditingScope(null)} className="text-xs text-gray-500 px-2 py-0.5">
                                Cancel
                              </button>
                            </div>
                          </>
                        )}
                      </div>
                    ) : (
                      <button
                        onClick={() => handleToggleScope(a)}
                        className={`text-xs px-2 py-0.5 rounded cursor-pointer ${a.isInScope ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}
                      >
                        {a.isInScope ? 'In Scope' : 'Excluded'}
                      </button>
                    )}
                  </td>
                  <td className="py-2 pr-3 text-xs text-gray-600">
                    {a.exclusionRationale && <div title={a.exclusionRationale} className="truncate max-w-[200px]">{a.exclusionRationale}</div>}
                    {a.inheritanceProvider && <div className="text-blue-600 text-xs">↳ {a.inheritanceProvider}</div>}
                  </td>
                  <td className="py-2 text-right">
                    <button onClick={() => handleRemoveNew(a.assignmentId)} className="text-xs text-red-600 hover:underline">Remove</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      ) : (
        /* Legacy components list (pre-Feature 040) */
        components.length === 0 ? (
          <div className="text-center py-8">
            <p className="text-gray-500 mb-3">No components assigned to this boundary yet.</p>
            {!showAdd && (
              <button onClick={() => setShowAdd(true)} className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700">
                Add Components
              </button>
            )}
          </div>
        ) : (
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 text-left text-xs font-medium uppercase text-gray-500">
                <th className="py-2 pr-3">Name</th>
                <th className="py-2 pr-3">Type</th>
                <th className="py-2 pr-3">Sub-Type</th>
                <th className="py-2 pr-3">Status</th>
                <th className="py-2 pr-3">Capabilities</th>
                <th className="py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {components.map((c) => (
                <tr key={c.id}>
                  <td className="py-2 pr-3 font-medium text-gray-800">{c.name}</td>
                  <td className="py-2 pr-3">
                    <span className={`text-xs px-1.5 py-0.5 rounded ${TYPE_COLORS[c.componentType] ?? 'bg-gray-100 text-gray-600'}`}>
                      {c.componentType}
                    </span>
                  </td>
                  <td className="py-2 pr-3 text-gray-600">{c.subType ?? '—'}</td>
                  <td className="py-2 pr-3">
                    <span className={`text-xs px-1.5 py-0.5 rounded ${c.status === 'Active' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600'}`}>
                      {c.status}
                    </span>
                  </td>
                  <td className="py-2 pr-3">
                    {c.capabilityLinks.length > 0 ? (
                      <div className="flex flex-wrap gap-1">
                        {c.capabilityLinks.map((cl) => (
                          <span key={cl.capabilityId} className="text-xs bg-blue-50 text-blue-700 px-1.5 py-0.5 rounded truncate max-w-[150px]" title={cl.capabilityName}>
                            {cl.capabilityName}
                          </span>
                        ))}
                      </div>
                    ) : (
                      <span className="text-xs text-gray-400">—</span>
                    )}
                  </td>
                  <td className="py-2 text-right">
                    <button onClick={() => handleRemove(c)} className="text-xs text-red-600 hover:underline">Remove</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      )}
    </div>
  );
}
