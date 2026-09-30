import { useState, useCallback, useEffect, useRef } from 'react';
import { Link, useParams } from '../features/workspaces/workspaceNavigation';
import { SystemTaskColumns, SystemTaskHeading, systemPrimaryAction, systemSecondaryAction } from '../features/systems/SystemTaskPresentation';
import SystemBoundaryInventory from '../features/systems/SystemBoundaryInventory';
import '../features/systems/systemRecordPages.css';
import SystemTaskNavigation from '../features/systems/SystemTaskNavigation';
import { useSystemContext } from '../components/layout/SystemLayout';
import SetupDialog from '../features/workspace-operations/SetupDialog';
import { BoundaryForm } from '../components/forms/BoundaryForm';
import { usePolling } from '../hooks/usePolling';
import { useSystemMutationPermission } from '../components/permissions/useSystemMutationPermission';
import {
  fetchBoundaryDefinitions,
  createBoundaryDefinition,
  updateBoundaryDefinition,
  deleteBoundaryDefinition,
  fetchBoundaryComponents,
  removeComponentFromBoundary,
  listBoundaryComponents,
  listBoundaryComponentCandidates,
  assignComponent,
  updateAssignment,
  removeAssignment as removeBoundaryAssignment,
  acquireLock,
  releaseLock,
  checkLockStatus,
} from '../api/boundaries';
import type { OrgComponentDto } from '../api/components';
import type {
  BoundaryDefinitionDto,
  CreateBoundaryDefinitionRequest,
  DeleteBoundaryDefinitionResponse,
  BoundaryComponentDto,
  BoundaryComponentCandidateDto,
  BoundaryLockStatus,
} from '../types/dashboard';

type FormMode = { kind: 'closed' } | { kind: 'create' } | { kind: 'edit'; boundary: BoundaryDefinitionDto };

export default function BoundaryManagement() {
  const { id: systemId } = useParams<{ id: string }>();
  const { detail } = useSystemContext();
  const canManage = useSystemMutationPermission(systemId, 'canManageSystem');
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [formMode, setFormMode] = useState<FormMode>({ kind: 'closed' });
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<BoundaryDefinitionDto | null>(null);
  const [deleteResult, setDeleteResult] = useState<DeleteBoundaryDefinitionResponse | null>(null);
  const reviewTrigger = useRef<HTMLButtonElement>(null);
  const drawerInvoker = useRef<HTMLElement | null>(null);
  const restoreDrawerFocus = useRef(false);
  const requireManagement = () => {
    if (canManage) return true;
    setError('You do not have permission to manage boundaries.');
    return false;
  };

  const [selectedBoundaryId, setSelectedBoundaryId] = useState('');
  const selectedBoundary = boundaries.find(item => item.id === selectedBoundaryId)
    ?? boundaries.find(item => item.isPrimary) ?? boundaries[0];
  const [expandedBoundary, setExpandedBoundary] = useState<string | null>(null);
  const expandedDefinition = boundaries.find(item => item.id === expandedBoundary);
  const [drawerBusy, setDrawerBusy] = useState(false);
  const detailRequest = useRef(0);
  const [detailError, setDetailError] = useState<string | null>(null);

  // Component management state
  const [boundaryComponents, setBoundaryComponents] = useState<OrgComponentDto[]>([]);
  const [componentsLoading, setComponentsLoading] = useState(false);

  useEffect(() => {
    if (!restoreDrawerFocus.current || expandedBoundary || formMode.kind !== 'closed' || deleteConfirm) return;
    restoreDrawerFocus.current = false;
    const invoker = drawerInvoker.current;
    if (invoker?.isConnected && invoker !== document.body) invoker.focus();
    else reviewTrigger.current?.focus();
  }, [expandedBoundary, formMode.kind, deleteConfirm]);

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

  const handleCreate = async (data: CreateBoundaryDefinitionRequest) => {
    if (!requireManagement()) return;
    if (!systemId || submitting) return;
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
    if (!requireManagement()) return;
    if (formMode.kind !== 'edit' || submitting) return;
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
    if (!requireManagement()) return;
    if (!deleteConfirm || deleting) return;
    setDeleting(true);
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
    } finally {
      setDeleting(false);
    }
  };

  const closeForm = () => {
    if (submitting) return;
    setFormMode({ kind: 'closed' });
    setFormError(null);
  };

  const closeDelete = () => {
    if (!deleting) setDeleteConfirm(null);
  };

  const closeDrawer = () => {
    if (drawerBusy) return;
    detailRequest.current++;
    restoreDrawerFocus.current = true;
    setExpandedBoundary(null);
  };

  const handleExpandBoundary = async (boundaryId: string) => {
    if (drawerBusy) return;
    if (!boundaries.some(boundary => boundary.id === boundaryId)) return;
    if (!expandedBoundary) {
      drawerInvoker.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      restoreDrawerFocus.current = false;
    }
    const request = ++detailRequest.current;
    setSelectedBoundaryId(boundaryId);
    setExpandedBoundary(boundaryId);
    setBoundaryComponents([]);
    setDetailError(null);
    setComponentsLoading(true);
    try {
      const comps = await fetchBoundaryComponents(boundaryId);
      if (request === detailRequest.current) setBoundaryComponents(comps);
    } catch {
      if (request === detailRequest.current) setDetailError('Failed to load boundary components');
    } finally {
      if (request === detailRequest.current) setComponentsLoading(false);
    }
  };

  if (loading) {
    return <p className="text-gray-500">Loading boundaries...</p>;
  }

  return (
      <div className="boundary-workspace">
        {/* Header */}
        <SystemTaskHeading eyebrow={detail.systemId === systemId ? detail.name : undefined} title="Inventory & system boundary"
          description="Confirm which components and resources are included in the documented system."
          action={<button
            ref={reviewTrigger}
            type="button"
            disabled={!selectedBoundary}
            onClick={() => { if (selectedBoundary) void handleExpandBoundary(selectedBoundary.id); }}
            className={systemPrimaryAction}
          >
            Review boundary<span aria-hidden="true"> →</span>
          </button>} />
        <SystemTaskNavigation definitionOnly />
        <div role="status" aria-label="Boundary record status"
          className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-[#dedaf5] bg-[#f1effc] px-[17px] py-3 text-xs dark:border-slate-700 dark:bg-slate-800">
          <span>{boundaries.length > 0 ? `${boundaries.length} ${boundaries.length === 1 ? 'boundary' : 'boundaries'} defined`
            : error ? 'Boundary records unavailable' : 'No boundary recorded'}</span>
          <span>Viewing does not approve scope</span>
        </div>
        {error && (
          <div role="alert" className="bg-red-50 text-red-700 p-3 rounded text-sm">{error}
            <button type="button" className="ml-3 underline" onClick={() => void fetchData()}>Retry boundaries</button>
          </div>
        )}
        {deleteResult && (
          <div className="bg-green-50 text-green-800 p-3 rounded text-sm">
            Boundary deleted. Reassigned {deleteResult.reassignedComponents} components and {deleteResult.reassignedMappings} mappings
            to Primary boundary.
            <button onClick={() => setDeleteResult(null)} className="ml-2 text-green-600 hover:underline text-xs">Dismiss</button>
          </div>
        )}
        <SystemTaskColumns support={<>
          <section className="boundary-support">
            <h2 className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px] text-slate-500">Used in your package</h2>
            <h3 className="text-sm font-semibold">SSP · Boundary description and inventory</h3>
            <p>This page contributes boundary records to the artifact above. Draft edits must not replace the approved baseline.</p>
            <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents/preview`}>Preview contribution<span aria-hidden="true"> →</span></Link>
          </section>
          <section className="boundary-support">
            <h2 className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px] text-slate-500">Review &amp; ownership</h2>
            <h3 className="text-sm font-semibold">Keep the next action clear</h3>
            <p>{canManage ? 'Review boundary to manage recorded scope and inspect source details.' : 'Review boundary to inspect recorded scope and source details. Your access is read-only.'} Opening records does not approve them.</p>
          </section>
          <section className="boundary-support">
            <h2 className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px] text-slate-500">Related work</h2>
            <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents`}>View package readiness<span aria-hidden="true"> →</span></Link>
          </section>
        </>}>
        {systemId && (!error || boundaries.length > 0) && <SystemBoundaryInventory boundaries={boundaries}
          onOpenBoundary={boundaryId => { void handleExpandBoundary(boundaryId); }}
          emptyAction={canManage && <button type="button" className={systemSecondaryAction}
            onClick={() => { if (!requireManagement()) return; setFormMode({ kind: 'create' }); setFormError(null); }}>Create boundary</button>} />}

        </SystemTaskColumns>
        <p className="boundary-workflow-note">Inputs → reviewed records → document output → ongoing change review</p>

        {/* Create/Edit Modal */}
        {formMode.kind !== 'closed' && (
          <SetupDialog busy={submitting} onClose={closeForm}
            title={formMode.kind === 'create' ? 'Create Boundary' : 'Edit Boundary'}
            description="Record the boundary name, type and description. Saving does not approve scope or authorize the system.">
              <BoundaryForm
                canSubmit={canManage}
                initial={formMode.kind === 'edit' ? formMode.boundary : undefined}
                onSubmit={formMode.kind === 'create' ? handleCreate : handleUpdate}
                onCancel={closeForm}
                isSubmitting={submitting}
                error={formError}
              />
          </SetupDialog>
        )}

        {/* Delete confirmation modal */}
        {deleteConfirm && (
          <SetupDialog busy={deleting} onClose={closeDelete} title="Delete Boundary"
            description="Confirm removal of this boundary and reassignment to the Primary boundary.">
              <p className="text-sm text-gray-600 mb-4">
                Are you sure you want to delete <strong>{deleteConfirm.name}</strong>?
                All components ({deleteConfirm.componentCount}) and mappings will be reassigned to the Primary boundary.
              </p>
              <div className="flex gap-2 justify-end">
                <button type="button" disabled={deleting} onClick={closeDelete} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50">Cancel</button>
                <button type="button" disabled={!canManage || deleting} onClick={handleDelete} className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700 disabled:opacity-50">{deleting ? 'Deleting...' : 'Delete'}</button>
              </div>
          </SetupDialog>
        )}

        {/* Resource / Component Management Dialog */}
        {expandedBoundary && systemId && (
          <SetupDialog busy={drawerBusy} onClose={closeDrawer} placement="right"
            title={`${boundaries.find(b => b.id === expandedBoundary)?.name ?? 'Boundary'} — Details`}
            description="Review component placements and their explicit included or excluded scope. Opening this view does not acquire a lock, approve records or authorize the system.">
              <div className="mb-5 space-y-3">
                <p className="text-sm text-slate-600">{expandedDefinition?.description || 'No description recorded for this boundary.'}</p>
                <p className="text-xs text-slate-500">{expandedDefinition?.boundaryType} boundary · ID: {expandedBoundary}</p>
                <div className="flex flex-wrap gap-2" aria-label="Boundary management actions">
                  <button type="button" disabled={!canManage || drawerBusy} className={systemSecondaryAction}
                    onClick={() => { if (!requireManagement() || drawerBusy) return; closeDrawer(); setFormError(null); setFormMode({ kind: 'create' }); }}>Create boundary</button>
                  <button type="button" title="Edit boundary" disabled={!canManage || drawerBusy || !expandedDefinition} className={systemSecondaryAction}
                    onClick={() => { if (!requireManagement() || drawerBusy || !expandedDefinition) return; closeDrawer(); setFormError(null); setFormMode({ kind: 'edit', boundary: expandedDefinition }); }}>Edit boundary</button>
                  {expandedDefinition && !expandedDefinition.isPrimary && <button type="button" title="Delete boundary" disabled={!canManage || drawerBusy} className={systemSecondaryAction}
                    onClick={() => { if (!requireManagement() || drawerBusy) return; closeDrawer(); setDeleteConfirm(expandedDefinition); }}>Delete boundary</button>}
                </div>
              </div>
              {detailError && <p role="alert" className="mb-3 text-sm text-red-600">{detailError}
                <button type="button" disabled={drawerBusy} className="ml-3 underline" onClick={() => void handleExpandBoundary(expandedBoundary)}>Retry boundary details</button>
              </p>}

              {/* Tab header */}
              <div className="flex border-b border-gray-200">
                <div className="py-2.5 text-sm font-medium border-b-2 -mb-px border-indigo-600 text-indigo-600">
                  Component placements
                </div>
              </div>

              {/* Tab content */}
              <div className="py-4">
                <BoundaryComponentsTab
                  key={expandedBoundary}
                  boundaryId={expandedBoundary}
                  systemId={systemId}
                  onBusyChange={setDrawerBusy}
                  components={boundaryComponents}
                  loading={componentsLoading}
                  onRefresh={async () => {
                    const request = ++detailRequest.current;
                    try {
                      const [comps] = await Promise.all([
                        fetchBoundaryComponents(expandedBoundary),
                        fetchData(),
                      ]);
                      if (request === detailRequest.current) setBoundaryComponents(comps);
                    } catch {
                      if (request === detailRequest.current) setDetailError('Failed to refresh boundary components');
                    }
                  }}
                />
              </div>
              <details className="mt-4 rounded-lg border border-slate-200 p-4 text-sm">
                <summary className="cursor-pointer font-semibold">Inventory &amp; scope guidance</summary>
                <p className="mt-3 text-slate-500">Boundary rows identify component placements, not a complete hardware/software register.</p>
                <h3 className="mt-3 font-semibold">Do cloud-native systems need an inventory?</h3>
                <p className="my-3 text-slate-500">Yes, cloud-native systems still require an inventory of in-scope virtual resources, managed services, workloads and software. Document provider-managed infrastructure as a dependency; do not invent physical hardware details.</p>
                <nav aria-label="Boundary task handoffs" className="flex flex-col items-start gap-3 text-indigo-700 dark:text-indigo-300">
                  <Link aria-disabled={drawerBusy} onClick={event => { if (drawerBusy) event.preventDefault(); }} className="underline" to={`/systems/${systemId}/security-capabilities/inventory`}>Manage component inventory</Link>
                  <Link aria-disabled={drawerBusy} onClick={event => { if (drawerBusy) event.preventDefault(); }} className="underline" to={`/systems/${systemId}/roles`}>View system team</Link>
                  <Link aria-disabled={drawerBusy} onClick={event => { if (drawerBusy) event.preventDefault(); }} className="underline" to={`/systems/${systemId}/assessments/environment`}>Review assessment scope</Link>
                  <Link aria-disabled={drawerBusy} onClick={event => { if (drawerBusy) event.preventDefault(); }} className="underline" to={`/systems/${systemId}/conmon`}>Review monitoring scope</Link>
                </nav>
              </details>
          </SetupDialog>
        )}
      </div>
  );
}

// ─── Boundary Components Tab ─────────────────────────────────────────────

const TYPE_COLORS: Record<string, string> = {
  Person: 'bg-indigo-50 text-indigo-700',
  Place: 'bg-green-50 text-green-700',
  Thing: 'bg-purple-50 text-purple-700',
};

function BoundaryComponentsTab({
  boundaryId,
  systemId,
  components,
  loading,
  onRefresh,
  onBusyChange,
}: {
  boundaryId: string;
  systemId: string;
  components: OrgComponentDto[];
  loading: boolean;
  onRefresh: () => Promise<void>;
  onBusyChange: (busy: boolean) => void;
}) {
  const canManage = useSystemMutationPermission(systemId, 'canManageSystem');
  const [showAdd, setShowAdd] = useState(false);
  const [candidates, setCandidates] = useState<BoundaryComponentCandidateDto[]>([]);
  const [search, setSearch] = useState('');
  const [addingIds, setAddingIds] = useState<Set<string>>(new Set());
  const [writing, setWriting] = useState(false);
  const writeInFlight = useRef(false);
  const assignmentsRequest = useRef(0);
  const candidatesRequest = useRef(0);

  // Feature 040 — Boundary component assignment with scope management
  const [bcAssignments, setBcAssignments] = useState<BoundaryComponentDto[]>([]);
  const [bcLoading, setBcLoading] = useState(true);
  const [lockStatus, setLockStatus] = useState<BoundaryLockStatus | null>(null);
  const [hasLock, setHasLock] = useState(false);
  const [editingScope, setEditingScope] = useState<string | null>(null);
  const [editRationale, setEditRationale] = useState('');
  const [editProvider, setEditProvider] = useState('');
  const [editInScope, setEditInScope] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const requireManagement = () => {
    if (canManage) return true;
    setLoadError('You do not have permission to manage boundary components or locks.');
    return false;
  };
  const beginWrite = () => {
    if (!requireManagement() || writeInFlight.current) return false;
    writeInFlight.current = true;
    setWriting(true);
    setLoadError(null);
    onBusyChange(true);
    return true;
  };
  const endWrite = () => {
    writeInFlight.current = false;
    setWriting(false);
    onBusyChange(false);
  };

  // Fetch boundary-component assignments (Feature 040)
  const fetchAssignments = useCallback(async () => {
    const request = ++assignmentsRequest.current;
    setBcLoading(true);
    try {
      const result = await listBoundaryComponents(systemId, boundaryId);
      const items = [...result.items];
      for (let page = 2; items.length < result.totalCount; page++) {
        const next = await listBoundaryComponents(systemId, boundaryId, { page, pageSize: result.pageSize });
        if (!next.items.length) throw new Error('Incomplete boundary assignments');
        items.push(...next.items);
      }
      if (request === assignmentsRequest.current) setBcAssignments(items);
    } catch {
      if (request === assignmentsRequest.current) setLoadError('Failed to load boundary assignments; displayed records may be incomplete.');
    } finally {
      if (request === assignmentsRequest.current) setBcLoading(false);
    }
  }, [systemId, boundaryId]);

  useEffect(() => { fetchAssignments(); }, [fetchAssignments]);

  // Check lock status on mount
  useEffect(() => {
    checkLockStatus(systemId, boundaryId).then(setLockStatus).catch(() => {
      setLoadError('Failed to check boundary lock status');
    });
  }, [systemId, boundaryId]);

  const fetchCandidates = useCallback(async () => {
    if (!showAdd) return;
    const request = ++candidatesRequest.current;
    try {
      const items = await listBoundaryComponentCandidates(systemId, boundaryId, search);
      if (request === candidatesRequest.current) setCandidates(items);
    } catch {
      if (request === candidatesRequest.current) setLoadError('Failed to load eligible components');
    }
  }, [showAdd, systemId, boundaryId, search]);

  useEffect(() => { fetchCandidates(); }, [fetchCandidates]);

  const handleAcquireLock = async () => {
    if (!requireManagement()) return false;
    try {
      const status = await acquireLock(systemId, boundaryId, 'current-user', 'Current User');
      setLockStatus(status);
      const acquired = status.locked && !status.message;
      setHasLock(acquired);
      if (!acquired) setLoadError(status.message || 'The boundary lock was not acquired.');
      return acquired;
    } catch {
      setLoadError('Failed to acquire boundary lock');
      return false;
    }
  };

  const handleReleaseLock = async () => {
    if (!beginWrite()) return;
    try {
      await releaseLock(systemId, boundaryId);
      setLockStatus(null);
      setHasLock(false);
    } catch {
      setLoadError('Failed to release boundary lock');
    } finally {
      endWrite();
    }
  };

  const handleAdd = async (candidate: BoundaryComponentCandidateDto) => {
    if (!beginWrite()) return;
    setAddingIds((prev) => new Set([...prev, candidate.id]));
    try {
      await assignComponent(systemId, boundaryId, {
        componentId: candidate.id,
        source: candidate.source,
        isInScope: true,
      });
      await fetchAssignments();
      await fetchCandidates();
      await onRefresh();
    } catch {
      setLoadError('Failed to assign component');
    } finally {
      setAddingIds((prev) => { const next = new Set(prev); next.delete(candidate.id); return next; });
      endWrite();
    }
  };

  const handleToggleScope = async (assignment: BoundaryComponentDto) => {
    if (!beginWrite()) return;
    try {
      if (!hasLock && !await handleAcquireLock()) return;
      setEditingScope(assignment.assignmentId);
      setEditInScope(assignment.isInScope);
      setEditRationale(assignment.exclusionRationale ?? '');
      setEditProvider(assignment.inheritanceProvider ?? '');
    } finally {
      endWrite();
    }
  };

  const handleSaveScope = async (assignmentId: string, isInScope: boolean) => {
    if (!requireManagement()) return;
    if (!hasLock) {
      setLoadError('Acquire a boundary lock before changing scope.');
      return;
    }
    if (!isInScope && !editRationale.trim()) {
      setLoadError('An exclusion rationale is required.');
      return;
    }
    if (!beginWrite()) return;
    try {
      await updateAssignment(systemId, boundaryId, assignmentId, {
        isInScope,
        exclusionRationale: isInScope ? null : editRationale,
        inheritanceProvider: editProvider || null,
      });
      setEditingScope(null);
      await fetchAssignments();
      await onRefresh();
    } catch {
      setLoadError('Failed to update component scope');
    } finally {
      endWrite();
    }
  };

  const handleRemoveNew = async (assignmentId: string) => {
    if (!beginWrite()) return;
    try {
      await removeBoundaryAssignment(systemId, boundaryId, assignmentId);
      await fetchAssignments();
      await onRefresh();
    } catch {
      setLoadError('Failed to remove boundary assignment');
    } finally {
      endWrite();
    }
  };

  const handleRemove = async (comp: OrgComponentDto) => {
    if (!requireManagement()) return;
    const assignment = comp.systemAssignments.find(a => a.boundaryDefinitionId === boundaryId)
      ?? comp.systemAssignments.find(a => a.registeredSystemId === systemId && !a.boundaryDefinitionId);
    if (!assignment) {
      setLoadError('No matching legacy assignment was found for this boundary.');
      return;
    }
    if (!beginWrite()) return;
    try {
      await removeComponentFromBoundary(comp.id, assignment.id);
      await onRefresh();
    } catch {
      setLoadError('Failed to remove component assignment');
    } finally {
      endWrite();
    }
  };

  // Use new boundary assignments if available, fall back to legacy
  const useNewModel = bcAssignments.length > 0 || bcLoading;

  return (
    <fieldset disabled={writing} className="min-w-0 space-y-4">
      {/* Load error banner */}
      {loadError && (
        <p role="alert" className="mb-2 text-sm text-red-600">{loadError}</p>
      )}

      {/* Lock status banner */}
      {lockStatus?.locked && !hasLock && (
        <div className="mb-3 p-2 bg-yellow-50 border border-yellow-200 rounded text-sm text-yellow-800">
          🔒 {lockStatus.message ?? `Locked by ${lockStatus.lockedBy}`}
        </div>
      )}

      {(loading || bcLoading) && <p role="status" className="text-xs text-slate-500">Loading components...</p>}
      <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
        <p className="text-sm text-gray-600">
          {(useNewModel ? bcAssignments.length : components.length)} component{(useNewModel ? bcAssignments.length : components.length) !== 1 ? 's' : ''} in this boundary
        </p>
        <div className="flex gap-2">
          {hasLock && (
            <button disabled={!canManage} onClick={handleReleaseLock} className="px-3 py-1.5 text-xs text-gray-600 border rounded hover:bg-gray-50">
              Release Lock
            </button>
          )}
          <button
            disabled={!canManage}
            onClick={() => { if (requireManagement()) setShowAdd(!showAdd); }}
            className="px-3 py-1.5 text-sm bg-indigo-600 text-white rounded hover:bg-indigo-700"
          >
            {showAdd ? 'Done adding components' : 'Add components to boundary'}
          </button>
        </div>
      </div>

      {/* Add component picker */}
      {showAdd && (
        <div className="mb-4 rounded-lg border border-indigo-200 bg-indigo-50 p-3">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search eligible components..."
            className="w-full text-sm border border-gray-300 rounded-md px-3 py-1.5 mb-2 bg-white"
          />
          <div className="max-h-48 overflow-y-auto space-y-1">
            {candidates.length === 0 ? (
              <div className="space-y-2 py-2 text-xs text-slate-600">
                <p>No eligible components found</p>
                <p>Try another search, or create, import or assign components in the inventory, then return here to add eligible components to this boundary.</p>
                <Link to={`/systems/${systemId}/security-capabilities/inventory`}
                  aria-disabled={writing || undefined} tabIndex={writing ? -1 : undefined}
                  onClick={event => { if (writing) event.preventDefault(); }}
                  className="inline-block text-indigo-700 underline underline-offset-4 dark:text-indigo-300">Manage component inventory</Link>
              </div>
            ) : (
              candidates.map((candidate) => (
                <div key={`${candidate.source}-${candidate.id}`} className="flex items-center justify-between bg-white rounded p-2 border border-gray-100">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium text-gray-800 truncate">{candidate.name}</span>
                      <span className={`text-xs px-1.5 py-0.5 rounded ${TYPE_COLORS[candidate.componentType] ?? 'bg-gray-100 text-gray-600'}`}>
                        {candidate.componentType}
                      </span>
                      <span className="text-xs px-1.5 py-0.5 rounded bg-sky-100 text-sky-700">{candidate.source}</span>
                    </div>
                    {candidate.description && <p className="text-xs text-gray-500 truncate">{candidate.description}</p>}
                  </div>
                  <button
                    onClick={() => handleAdd(candidate)}
                    disabled={!canManage || addingIds.has(candidate.id)}
                    className="ml-2 px-3 py-1 text-xs bg-indigo-600 text-white rounded hover:bg-indigo-700 disabled:opacity-50 flex-shrink-0"
                  >
                    {addingIds.has(candidate.id) ? 'Adding...' : 'Add'}
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
          !bcLoading && <p className="py-4 text-sm text-slate-500">No components assigned to this boundary yet.</p>
        ) : (
          <div className="space-y-3">
              {bcAssignments.map((a) => (
                <section key={a.assignmentId} aria-label={`Placement ${a.componentName}`}
                  className="min-w-0 rounded-lg border border-slate-200 p-3 text-sm">
                  <h3 className="break-all font-medium text-gray-800">{a.componentName}</h3>
                  <details className="my-2 min-w-0 text-xs">
                    <summary className="cursor-pointer font-medium text-indigo-700">Component details</summary>
                    <dl className="mt-2 grid min-w-0 grid-cols-1 gap-2">
                      {([
                        ['Component ID', a.componentId], ['Assignment ID', a.assignmentId],
                        ['Type', a.subType || a.componentType], ['Source', a.source],
                        ['Azure resource ID', a.azureResourceId], ['Azure resource type', a.azureResourceType],
                        ['Resource group', a.azureResourceGroup], ['Region', a.azureLocation],
                        ['Recorded by', a.createdBy], ['Recorded at', a.createdAt],
                      ] as const).map(([label, value]) => <div key={label} className="min-w-0">
                        <dt className="font-medium text-slate-500">{label}</dt>
                        <dd className="min-w-0 whitespace-pre-wrap break-all">{value || 'Not recorded'}</dd>
                      </div>)}
                    </dl>
                  </details>
                  <div className="my-2 flex flex-wrap gap-2">
                    <span className={`text-xs px-1.5 py-0.5 rounded ${TYPE_COLORS[a.componentType] ?? 'bg-gray-100 text-gray-600'}`}>
                      {a.subType || a.componentType}
                    </span>
                    <span className="text-xs text-gray-600">Source: {a.source}</span>
                  </div>
                  <div className="py-2">
                    {editingScope === a.assignmentId ? (
                      <div className="space-y-2">
                        <select
                          aria-label="Placement scope"
                          disabled={!canManage || !hasLock}
                          value={editInScope ? 'inScope' : 'excluded'}
                          onChange={(e) => setEditInScope(e.target.value === 'inScope')}
                          className="text-xs border rounded px-2 py-1"
                        >
                          <option value="inScope">In Scope</option>
                          <option value="excluded">Excluded</option>
                        </select>
                        {!editInScope && (
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
                          </>
                        )}
                        <div className="flex gap-2">
                          <button onClick={() => handleSaveScope(a.assignmentId, editInScope)}
                            disabled={!canManage || !hasLock || (!editInScope && !editRationale.trim())}
                            className="rounded bg-indigo-600 px-3 py-1 text-xs text-white disabled:opacity-50">Save</button>
                          <button onClick={() => setEditingScope(null)} className="px-2 py-1 text-xs text-gray-500">Cancel</button>
                        </div>
                      </div>
                    ) : (
                      <button
                        disabled={!canManage}
                        onClick={() => handleToggleScope(a)}
                        className={`text-xs px-2 py-0.5 rounded cursor-pointer ${a.isInScope ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}
                      >
                        {a.isInScope ? 'In Scope' : 'Excluded'}
                      </button>
                    )}
                  </div>
                  <div className="py-2 text-xs text-gray-600">
                    {a.exclusionRationale && <p>{a.exclusionRationale}</p>}
                    {a.inheritanceProvider && <div className="text-indigo-600 text-xs">↳ {a.inheritanceProvider}</div>}
                  </div>
                  <div className="text-right">
                    <button disabled={!canManage} onClick={() => handleRemoveNew(a.assignmentId)} className="text-xs text-red-600 hover:underline">Remove</button>
                  </div>
                </section>
              ))}
          </div>
        )
      ) : (
        /* Legacy components list (pre-Feature 040) */
        components.length === 0 ? (
          !loading && <p className="py-4 text-sm text-slate-500">No components assigned to this boundary yet.</p>
        ) : (
          <div className="space-y-3">
            <p className="text-xs text-slate-500">Legacy component assignments</p>
              {components.map((c) => (
                <section key={c.id} aria-label={`Legacy placement ${c.name}`} className="rounded-lg border border-slate-200 p-3 text-sm">
                  <h3 className="font-medium text-gray-800">{c.name}</h3>
                  <div className="my-2 flex flex-wrap gap-2">
                    <span className={`text-xs px-1.5 py-0.5 rounded ${TYPE_COLORS[c.componentType] ?? 'bg-gray-100 text-gray-600'}`}>
                      {c.subType || c.componentType}
                    </span>
                    <span className={`text-xs px-1.5 py-0.5 rounded ${c.status === 'Active' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600'}`}>
                      {c.status}
                    </span>
                  </div>
                  <div>
                    {c.capabilityLinks.length > 0 ? (
                      <div className="flex flex-wrap gap-1">
                        {c.capabilityLinks.map((cl) => (
                          <span key={cl.capabilityId} className="text-xs bg-indigo-50 text-indigo-700 px-1.5 py-0.5 rounded truncate max-w-[150px]" title={cl.capabilityName}>
                            {cl.capabilityName}
                          </span>
                        ))}
                      </div>
                    ) : (
                      <span className="text-xs text-gray-400">—</span>
                    )}
                  </div>
                  <div className="mt-2 text-right">
                    <button disabled={!canManage} onClick={() => handleRemove(c)} className="text-xs text-red-600 hover:underline">Remove</button>
                  </div>
                </section>
              ))}
          </div>
        )
      )}
    </fieldset>
  );
}
