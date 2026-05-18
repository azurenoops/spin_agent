import { useState, useCallback, useEffect } from 'react';
import { Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import { usePolling } from '../hooks/usePolling';
import {
  listComponents,
  createOrgComponent,
  updateOrgComponent,
  deleteOrgComponent,
  getComponentImpactPreview,
  type OrgComponentDto,
  type OrgComponentListResponse,
  type ComponentImpactPreview,
} from '../api/components';
import {
  discoverAzureResourcesForComponents,
  importAzureComponents,
  discoverEntraIdUsers,
  importEntraIdPeople,
} from '../api/azureDiscovery';
import type { EntraDiscoveryItem } from '../api/azureDiscovery';
import { onboarding, type AzureSubscriptionRegistrationDto } from '../features/onboarding/api/onboardingApi';
import { getCapabilities } from '../api/capabilities';
import {
  isUnavailable as isCspUnavailable,
  listCspInheritedComponents,
  type CspInheritedComponent,
} from '../features/csp-inherited-components/api';
import type { CreateComponentRequest, ComponentType, ComponentStatus, SecurityCapabilityDto, DiscoveredResource } from '../types/dashboard';

const TYPE_OPTIONS: ComponentType[] = ['Person', 'Place', 'Thing', 'Policy'];
const STATUS_OPTIONS: ComponentStatus[] = ['Active', 'Planned', 'Decommissioned'];

const TYPE_COLORS: Record<string, string> = {
  Person: 'bg-indigo-50 text-indigo-700 border-indigo-200',
  Place: 'bg-green-50 text-green-700 border-green-200',
  Thing: 'bg-purple-50 text-purple-700 border-purple-200',
  Policy: 'bg-amber-50 text-amber-700 border-amber-200',
};

export default function ComponentLibrary() {
  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [editingComp, setEditingComp] = useState<OrgComponentDto | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [impactPreview, setImpactPreview] = useState<ComponentImpactPreview | null>(null);
  const [pendingUpdate, setPendingUpdate] = useState<CreateComponentRequest | null>(null);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

  // ─── Azure Discovery State (Feature 040) ────────────────────────────────
  const [showDiscovery, setShowDiscovery] = useState(false);
  // Registered subscriptions from the onboarding wizard — the user no longer
  // has to type a sub ID; we drive discovery off the wizard's selection.
  const [registeredSubs, setRegisteredSubs] = useState<AzureSubscriptionRegistrationDto[]>([]);
  const [subsLoaded, setSubsLoaded] = useState(false);
  const [activeSubId, setActiveSubId] = useState<string>('');
  const [discoveredResources, setDiscoveredResources] = useState<DiscoveredResource[]>([]);
  const [failedGroups, setFailedGroups] = useState<string[]>([]);
  const [selectedForImport, setSelectedForImport] = useState<Set<string>>(new Set());
  const [discovering, setDiscovering] = useState(false);
  const [importing, setImporting] = useState(false);
  const [discoveryError, setDiscoveryError] = useState<string | null>(null);

  // ─── Entra ID Discovery State (Feature 040 — US9) ──────────────────────
  const [showEntraDiscovery, setShowEntraDiscovery] = useState(false);
  const [entraItems, setEntraItems] = useState<EntraDiscoveryItem[]>([]);
  const [selectedEntra, setSelectedEntra] = useState<Set<string>>(new Set());
  const [entraDiscovering, setEntraDiscovering] = useState(false);
  const [entraImporting, setEntraImporting] = useState(false);
  const [entraError, setEntraError] = useState<string | null>(null);

  const handleDiscover = useCallback(async (subId: string) => {
    if (!subId) return;
    setDiscovering(true);
    setDiscoveryError(null);
    try {
      const result = await discoverAzureResourcesForComponents({ subscriptionId: subId });
      setDiscoveredResources(result.resources);
      setFailedGroups(result.failedResourceGroups);
      setSelectedForImport(new Set(
        result.resources.filter(r => !r.alreadyImported).map(r => r.resourceId)
      ));
    } catch (err: any) {
      setDiscoveryError(err?.response?.data?.error ?? 'Discovery failed');
    } finally {
      setDiscovering(false);
    }
  }, []);

  const handleImportSelected = async () => {
    const toImport = discoveredResources.filter(r => selectedForImport.has(r.resourceId) && !r.alreadyImported);
    if (toImport.length === 0) return;
    setImporting(true);
    try {
      await importAzureComponents({
        resources: toImport.map(r => ({
          resourceId: r.resourceId, name: r.name, type: r.type,
          resourceGroup: r.resourceGroup, location: r.location,
        })),
      });
      setShowDiscovery(false);
      setDiscoveredResources([]);
      refresh();
    } catch (err: any) {
      setDiscoveryError(err?.response?.data?.error ?? 'Import failed');
    } finally {
      setImporting(false);
    }
  };

  const handleEntraDiscover = useCallback(async () => {
    setEntraDiscovering(true);
    setEntraError(null);
    try {
      const result = await discoverEntraIdUsers();
      setEntraItems(result.items);
      setSelectedEntra(new Set(
        result.items.filter(i => !i.alreadyImported).map(i => i.entraObjectId)
      ));
      if (result.partialFailure) {
        setEntraError(result.failureMessage ?? 'Some Entra ID data could not be fetched');
      }
    } catch (err: any) {
      const code = err?.response?.data?.errorCode;
      if (code === 'FEATURE_DISABLED') {
        setEntraError('Entra ID discovery is disabled. Enable it in Features configuration.');
      } else {
        setEntraError(err?.response?.data?.error ?? 'Entra ID discovery failed');
      }
    } finally {
      setEntraDiscovering(false);
    }
  }, []);

  const handleEntraImport = async () => {
    const toImport = entraItems.filter(i => selectedEntra.has(i.entraObjectId) && !i.alreadyImported);
    if (toImport.length === 0) return;
    setEntraImporting(true);
    try {
      await importEntraIdPeople({
        people: toImport.map(i => ({
          entraObjectId: i.entraObjectId,
          displayName: i.displayName,
          email: i.email,
          kind: i.kind,
        })),
      });
      setShowEntraDiscovery(false);
      setEntraItems([]);
      refresh();
    } catch (err: any) {
      setEntraError(err?.response?.data?.error ?? 'Import failed');
    } finally {
      setEntraImporting(false);
    }
  };

  // Load the wizard's registered Azure subscriptions once. The Azure-discovery
  // dialog uses these to drive the picker / auto-scan; the user no longer has
  // to type a subscription ID by hand.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const rows = await onboarding.listAzureRegistrations();
        if (cancelled) return;
        const usable = rows.filter((r) => r.status === 'Selected');
        setRegisteredSubs(usable);
        const first = usable[0];
        if (first) setActiveSubId(first.subscriptionId);
      } catch {
        if (!cancelled) setRegisteredSubs([]);
      } finally {
        if (!cancelled) setSubsLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Auto-scan the active subscription when the Azure dialog opens. If the
  // active sub changes (multi-sub picker), re-scan.
  useEffect(() => {
    if (!showDiscovery || !activeSubId) return;
    void handleDiscover(activeSubId);
  }, [showDiscovery, activeSubId, handleDiscover]);

  // Auto-scan Entra ID when its dialog opens — there's nothing for the user
  // to configure, so don't make them click an extra button.
  useEffect(() => {
    if (!showEntraDiscovery) return;
    if (entraItems.length > 0) return; // already scanned this session
    void handleEntraDiscover();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showEntraDiscovery]);

  const fetcher = useCallback(
    () => listComponents({
      search: search || undefined,
      type: typeFilter || undefined,
      status: statusFilter || undefined,
    }),
    [search, typeFilter, statusFilter],
  );
  const { data, refresh } = usePolling<OrgComponentListResponse>(fetcher, 30000);
  const components = data?.items ?? [];

  // ─── CSP-inherited components (Feature 048 / FR-104) ───────────────
  // Org users see CSP-published components alongside their own, badged
  // with a violet "CSP" chip and read-only (no edit/delete). Reference-
  // only — nothing is forked into the tenant. The endpoint silently
  // self-hides for SingleTenant deployments and pre-onboarding tenants;
  // we just render no CSP rows in that case.
  const [cspComponents, setCspComponents] = useState<CspInheritedComponent[]>([]);
  const matchesCspFilters = useCallback(
    (c: CspInheritedComponent) => {
      const term = search.trim().toLowerCase();
      if (term && !c.name.toLowerCase().includes(term) &&
          !(c.description ?? '').toLowerCase().includes(term)) {
        return false;
      }
      // CSP component types (Infrastructure/Platform/Service/Identity/
      // Network/Storage/Compute) don't map cleanly to the org's
      // Person/Place/Thing/Policy taxonomy. When the org has selected a
      // typeFilter, treat CSP rows as not matching so they don't pollute
      // a filtered org view; clear the filter to see everything.
      if (typeFilter) return false;
      // Status mapping: CSP rows are always Published when listed here,
      // so a per-row Status filter from the org form (Active/Planned/
      // Decommissioned) only applies to org rows.
      if (statusFilter) return false;
      return true;
    },
    [search, typeFilter, statusFilter],
  );
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await listCspInheritedComponents({
          status: 'Published',
          pageSize: 200,
        });
        if (cancelled) return;
        if (isCspUnavailable(result)) {
          setCspComponents([]);
          return;
        }
        setCspComponents(result.items);
      } catch {
        if (!cancelled) setCspComponents([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);
  const visibleCspComponents = cspComponents.filter(matchesCspFilters);

  const handleCreate = async (req: CreateComponentRequest) => {
    setSubmitting(true);
    setFormError(null);
    try {
      await createOrgComponent(req);
      setShowCreate(false);
      refresh();
    } catch (err: any) {
      setFormError(err?.response?.data?.error ?? 'Failed to create component');
    } finally {
      setSubmitting(false);
    }
  };

  const handleUpdate = async (req: CreateComponentRequest) => {
    if (!editingComp) return;
    // Check if name/description/owner changed — if so, check impact
    const metadataChanged = req.name !== editingComp.name ||
      req.description !== (editingComp.description ?? '') ||
      req.owner !== (editingComp.owner ?? '');
    if (metadataChanged && !impactPreview) {
      setSubmitting(true);
      try {
        const preview = await getComponentImpactPreview(editingComp.id);
        if (preview.totalNarratives > 0) {
          setImpactPreview(preview);
          setPendingUpdate(req);
          return;
        }
      } catch {
        // Preview failed — proceed anyway
      } finally {
        setSubmitting(false);
      }
    }
    await executeUpdate(req);
  };

  const executeUpdate = async (req: CreateComponentRequest) => {
    if (!editingComp) return;
    setSubmitting(true);
    setFormError(null);
    try {
      await updateOrgComponent(editingComp.id, req);
      setEditingComp(null);
      setImpactPreview(null);
      setPendingUpdate(null);
      refresh();
    } catch (err: any) {
      setFormError(err?.response?.data?.error ?? 'Failed to update component');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeleteWithPreview = async (id: string) => {
    try {
      const preview = await getComponentImpactPreview(id);
      if (preview.totalNarratives > 0) {
        setImpactPreview(preview);
        setPendingDeleteId(id);
        setDeleteConfirm(null);
        return;
      }
    } catch {
      // Preview failed — proceed anyway
    }
    await handleDelete(id);
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteOrgComponent(id);
      setDeleteConfirm(null);
      setImpactPreview(null);
      setPendingDeleteId(null);
      refresh();
    } catch {
      /* ignore */
    }
  };

  return (
    <PageLayout title="Component Library">
      <PageHero
        eyebrow="Components"
        title="Component Library"
        description="Org-wide People, Places, Things, and Policies — assign to systems with boundary scope."
      />
      <div className="space-y-6">
        {/* Filters */}
        <div className="flex flex-wrap items-center gap-3">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search components..."
            className="w-64 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
          <select
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          >
            <option value="">All Types</option>
            {TYPE_OPTIONS.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          >
            <option value="">All Statuses</option>
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
          {data && (
            <span className="self-center text-sm text-gray-500">
              {data.totalCount} component{data.totalCount !== 1 ? 's' : ''}
              {visibleCspComponents.length > 0 && (
                <>
                  {' '}
                  <span className="text-violet-700">
                    +{visibleCspComponents.length} CSP
                  </span>
                </>
              )}
            </span>
          )}
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => { setShowEntraDiscovery(true); setEntraError(null); }}
              className="inline-flex items-center rounded-md border border-indigo-600 px-4 py-1.5 text-sm font-medium text-indigo-600 hover:bg-indigo-50"
            >
              Discover from Entra ID
            </button>
            <button
              type="button"
              onClick={() => { setShowDiscovery(true); setDiscoveryError(null); }}
              className="inline-flex items-center rounded-md border border-indigo-600 px-4 py-1.5 text-sm font-medium text-indigo-600 hover:bg-indigo-50"
            >
              Discover from Azure
            </button>
            <button
              type="button"
              onClick={() => { setShowCreate(true); setFormError(null); }}
              className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
            >
              + New Component
            </button>
          </div>
        </div>

        {/* Component Cards */}
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {/* CSP-inherited components render first, badged + read-only.
              Reference-only per Feature 048 / FR-105: orgs can map their
              systems to these rows but cannot edit or delete them. */}
          {visibleCspComponents.map((csp) => (
            <div
              key={`csp-${csp.id}`}
              className="rounded-lg border border-violet-200 bg-violet-50/40 p-4 shadow-sm hover:shadow-md transition-shadow"
              data-testid={`csp-inherited-component-card-${csp.id}`}
            >
              <div className="flex items-start justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold text-gray-900 truncate">
                      {csp.name}
                    </h3>
                    <span
                      className="inline-flex items-center rounded-full border border-violet-300 bg-violet-100 px-2 py-0.5 text-xs font-semibold uppercase tracking-wide text-violet-800"
                      title="Inherited from CSP — read-only at the org scope"
                    >
                      CSP
                    </span>
                    <span className="inline-flex items-center rounded-full border border-gray-200 bg-white px-2 py-0.5 text-xs font-medium text-gray-700">
                      {csp.componentType}
                    </span>
                  </div>
                  {csp.description && (
                    <p className="mt-1 text-xs text-gray-600 line-clamp-2">
                      {csp.description}
                    </p>
                  )}
                  {csp.sourceFileName && (
                    <p className="mt-1 text-xs text-gray-500">
                      Source: {csp.sourceFileName}
                    </p>
                  )}
                </div>
              </div>
              {(csp.capabilityMappedCount ?? 0) > 0 && (
                <div className="mt-2 border-t border-violet-100 pt-2">
                  <p className="text-xs text-violet-800">
                    {csp.capabilityMappedCount} mapped capabilit
                    {csp.capabilityMappedCount === 1 ? 'y' : 'ies'}
                  </p>
                </div>
              )}
            </div>
          ))}
          {components.map((comp) => (
            <div key={comp.id} className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm hover:shadow-md transition-shadow">
              <div className="flex items-start justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold text-gray-900 truncate">{comp.name}</h3>
                    <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${TYPE_COLORS[comp.componentType] ?? 'bg-gray-50 text-gray-600'}`}>
                      {comp.componentType}
                    </span>
                  </div>
                  {comp.subType && <p className="text-xs text-gray-500 mt-0.5">{comp.subType}</p>}
                  {comp.description && <p className="mt-1 text-xs text-gray-600 line-clamp-2">{comp.description}</p>}
                  {comp.owner && <p className="mt-1 text-xs text-gray-500">Owner: {comp.owner}</p>}
                </div>
                <div className="flex gap-1 ml-2">
                  <button
                    type="button"
                    onClick={() => { setEditingComp(comp); setFormError(null); }}
                    className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
                    title="Edit"
                  >
                    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Z" />
                    </svg>
                  </button>
                  <button
                    type="button"
                    onClick={() => setDeleteConfirm(comp.id)}
                    className="rounded p-1 text-gray-400 hover:bg-red-50 hover:text-red-600"
                    title="Delete"
                  >
                    <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0" />
                    </svg>
                  </button>
                </div>
              </div>

              {/* Capability Links */}
              {comp.capabilityLinks.length > 0 && (
                <div className="mt-2 border-t border-gray-100 pt-2">
                  <p className="text-xs font-medium text-gray-500 mb-1">Linked Capabilities</p>
                  <div className="flex flex-wrap gap-1">
                    {comp.capabilityLinks.map((cl) => (
                      <span key={cl.capabilityId} className="inline-flex items-center rounded bg-indigo-50 px-1.5 py-0.5 text-xs text-indigo-700">
                        {cl.capabilityName}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ))}
        </div>

        {components.length === 0 && visibleCspComponents.length === 0 && !submitting && (
          <div className="rounded-lg border-2 border-dashed border-gray-300 p-12 text-center">
            <p className="text-sm text-gray-500">No components found. Create one to get started.</p>
          </div>
        )}

        {/* Create/Edit Modal */}
        {(showCreate || editingComp) && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
            <div className="w-full max-w-lg rounded-lg bg-white p-6 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 mb-4">
                {editingComp ? 'Edit Component' : 'Create Component'}
              </h3>
              {formError && <p className="mb-3 text-sm text-red-600">{formError}</p>}
              <ComponentFormInline
                initial={editingComp}
                onSubmit={editingComp ? handleUpdate : handleCreate}
                onCancel={() => { setShowCreate(false); setEditingComp(null); }}
                submitting={submitting}
              />
            </div>
          </div>
        )}

        {/* Delete Confirm */}
        {deleteConfirm && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
            <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Component?</h3>
              <p className="mb-4 text-sm text-gray-600">
                This will remove the component and all system assignments. Affected narratives will be regenerated.
              </p>
              <div className="flex justify-end gap-2">
                <button type="button" onClick={() => setDeleteConfirm(null)} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
                <button type="button" onClick={() => handleDeleteWithPreview(deleteConfirm)} className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700">Delete</button>
              </div>
            </div>
          </div>
        )}
        {/* Impact Preview Modal */}
        {impactPreview && (pendingUpdate || pendingDeleteId) && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
            <div className="w-full max-w-md rounded-lg bg-white p-6 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 mb-3">Narrative Impact Preview</h3>
              <p className="text-sm text-gray-600 mb-4">
                This change will regenerate narratives across {impactPreview.totalSystems} system{impactPreview.totalSystems !== 1 ? 's' : ''}.
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
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => { setImpactPreview(null); setPendingUpdate(null); setPendingDeleteId(null); }}
                  className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={() => {
                    if (pendingUpdate) executeUpdate(pendingUpdate);
                    else if (pendingDeleteId) handleDelete(pendingDeleteId);
                  }}
                  disabled={submitting}
                  className={`rounded-md px-4 py-2 text-sm text-white disabled:opacity-50 ${
                    pendingDeleteId ? 'bg-red-600 hover:bg-red-700' : 'bg-indigo-600 hover:bg-indigo-700'
                  }`}
                >
                  {submitting ? 'Processing...' : (pendingDeleteId ? 'Confirm Delete' : 'Confirm & Save')}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Azure Discovery Dialog (Feature 040) */}
        {showDiscovery && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
            <div className="max-h-[80vh] w-full max-w-3xl overflow-y-auto rounded-lg bg-white p-6 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 mb-1">Discover from Azure</h3>
              <p className="text-sm text-gray-500 mb-4">
                Resources are pulled from the Azure subscriptions you registered in the onboarding wizard.
              </p>

              {!subsLoaded ? (
                <p className="text-sm text-gray-500">Loading registered subscriptions…</p>
              ) : registeredSubs.length === 0 ? (
                <div className="rounded-md border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
                  <p className="font-medium">No Azure subscriptions are connected yet.</p>
                  <p className="mt-1">
                    Open the onboarding wizard and complete <strong>Step 3 — Add Azure subscriptions</strong>,
                    then return here to discover resources.
                  </p>
                  <Link
                    to="/onboarding?stepNav=admin"
                    className="mt-3 inline-flex items-center rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700"
                  >
                    Open onboarding wizard
                  </Link>
                </div>
              ) : (
                <>
                  {registeredSubs.length > 1 && (
                    <div className="mb-4">
                      <label className="block text-xs font-medium text-gray-700 mb-1">Subscription</label>
                      <select
                        value={activeSubId}
                        onChange={(e) => {
                          setActiveSubId(e.target.value);
                          setDiscoveredResources([]);
                          setSelectedForImport(new Set());
                          setFailedGroups([]);
                        }}
                        className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                      >
                        {registeredSubs.map((s) => (
                          <option key={s.subscriptionId} value={s.subscriptionId}>
                            {s.displayName} ({s.subscriptionId})
                          </option>
                        ))}
                      </select>
                    </div>
                  )}
                  {registeredSubs.length === 1 && registeredSubs[0] && (
                    <div className="mb-4 rounded-md bg-gray-50 px-3 py-2 text-xs text-gray-600">
                      Scanning <strong>{registeredSubs[0].displayName}</strong> ({registeredSubs[0].subscriptionId})
                    </div>
                  )}
                  {discovering && (
                    <p className="text-sm text-gray-500 mb-3">Scanning Azure resources…</p>
                  )}
                  <div className="flex justify-end mb-3">
                    <button
                      type="button"
                      onClick={() => activeSubId && void handleDiscover(activeSubId)}
                      disabled={discovering || !activeSubId}
                      className="rounded-md border border-gray-300 px-3 py-1.5 text-xs text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                    >
                      {discovering ? 'Scanning…' : 'Refresh'}
                    </button>
                  </div>
                </>
              )}

              {discoveryError && (
                <div className="mb-3 rounded-md bg-red-50 p-3 text-sm text-red-700">{discoveryError}</div>
              )}
              {failedGroups.length > 0 && (
                <div className="mb-3 rounded-md bg-yellow-50 border border-yellow-200 p-3 text-sm text-yellow-800">
                  Discovery partially failed for: {failedGroups.join(', ')}.
                  <button onClick={() => activeSubId && void handleDiscover(activeSubId)} className="ml-2 underline">Retry</button>
                </div>
              )}
              {discoveredResources.length > 0 && (
                <>
                  <div className="mb-2 text-sm text-gray-600">
                    {discoveredResources.length} resource{discoveredResources.length !== 1 ? 's' : ''} found
                    ({selectedForImport.size} selected for import)
                  </div>
                  <div className="max-h-64 overflow-y-auto rounded-md border border-gray-200">
                    <table className="min-w-full text-sm">
                      <thead className="bg-gray-50 sticky top-0">
                        <tr>
                          <th className="px-3 py-2 text-left w-8">
                            <input
                              type="checkbox"
                              checked={selectedForImport.size === discoveredResources.filter(r => !r.alreadyImported).length}
                              onChange={(e) => {
                                if (e.target.checked) {
                                  setSelectedForImport(new Set(discoveredResources.filter(r => !r.alreadyImported).map(r => r.resourceId)));
                                } else {
                                  setSelectedForImport(new Set());
                                }
                              }}
                              className="h-4 w-4 rounded border-gray-300"
                            />
                          </th>
                          <th className="px-3 py-2 text-left">Name</th>
                          <th className="px-3 py-2 text-left">Type</th>
                          <th className="px-3 py-2 text-left">Resource Group</th>
                          <th className="px-3 py-2 text-left">Status</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {discoveredResources.map((r) => (
                          <tr key={r.resourceId} className={r.alreadyImported ? 'bg-gray-50 text-gray-400' : ''}>
                            <td className="px-3 py-2">
                              {r.alreadyImported ? (
                                <span className="text-xs text-green-600">Imported</span>
                              ) : (
                                <input
                                  type="checkbox"
                                  checked={selectedForImport.has(r.resourceId)}
                                  onChange={(e) => {
                                    const next = new Set(selectedForImport);
                                    e.target.checked ? next.add(r.resourceId) : next.delete(r.resourceId);
                                    setSelectedForImport(next);
                                  }}
                                  className="h-4 w-4 rounded border-gray-300"
                                />
                              )}
                            </td>
                            <td className="px-3 py-2 font-medium">{r.name}</td>
                            <td className="px-3 py-2 text-gray-500">{r.type}</td>
                            <td className="px-3 py-2 text-gray-500">{r.resourceGroup}</td>
                            <td className="px-3 py-2">
                              {r.alreadyImported ? (
                                <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs text-green-700 border border-green-200">Already imported</span>
                              ) : (
                                <span className="inline-flex items-center rounded-full bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700 border border-indigo-200">New</span>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
              <div className="flex justify-end gap-2 mt-4">
                <button
                  onClick={() => { setShowDiscovery(false); setDiscoveredResources([]); }}
                  className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Cancel
                </button>
                {discoveredResources.length > 0 && (
                  <button
                    onClick={handleImportSelected}
                    disabled={importing || selectedForImport.size === 0}
                    className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                  >
                    {importing ? 'Importing...' : `Import ${selectedForImport.size} Component${selectedForImport.size !== 1 ? 's' : ''}`}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* Entra ID Discovery Dialog (Feature 040 — US9) */}
        {showEntraDiscovery && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
            <div className="max-h-[80vh] w-full max-w-3xl overflow-y-auto rounded-lg bg-white p-6 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 mb-4">Discover from Entra ID</h3>
              <p className="text-sm text-gray-600 mb-4">
                Discover users and security groups from Microsoft Entra ID to import as Person components.
              </p>
              {entraDiscovering && entraItems.length === 0 && (
                <p className="text-sm text-gray-500 mb-4">Scanning Entra ID…</p>
              )}
              {entraError && (
                <div className="mb-3 rounded-md bg-red-50 p-3 text-sm text-red-700">{entraError}</div>
              )}
              {entraItems.length > 0 && (
                <>
                  <div className="mb-2 text-sm text-gray-600">
                    {entraItems.length} user/group{entraItems.length !== 1 ? 's' : ''} found
                    ({selectedEntra.size} selected for import)
                  </div>
                  <div className="max-h-64 overflow-y-auto rounded-md border border-gray-200">
                    <table className="min-w-full text-sm">
                      <thead className="bg-gray-50 sticky top-0">
                        <tr>
                          <th className="px-3 py-2 text-left w-8">
                            <input
                              type="checkbox"
                              checked={selectedEntra.size === entraItems.filter(i => !i.alreadyImported).length && selectedEntra.size > 0}
                              onChange={(e) => {
                                if (e.target.checked) {
                                  setSelectedEntra(new Set(entraItems.filter(i => !i.alreadyImported).map(i => i.entraObjectId)));
                                } else {
                                  setSelectedEntra(new Set());
                                }
                              }}
                              className="h-4 w-4 rounded border-gray-300"
                            />
                          </th>
                          <th className="px-3 py-2 text-left">Name</th>
                          <th className="px-3 py-2 text-left">Email</th>
                          <th className="px-3 py-2 text-left">Kind</th>
                          <th className="px-3 py-2 text-left">Status</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {entraItems.map((i) => (
                          <tr key={i.entraObjectId} className={i.alreadyImported ? 'bg-gray-50 text-gray-400' : ''}>
                            <td className="px-3 py-2">
                              {i.alreadyImported ? (
                                <span className="text-xs text-green-600">Imported</span>
                              ) : (
                                <input
                                  type="checkbox"
                                  checked={selectedEntra.has(i.entraObjectId)}
                                  onChange={(e) => {
                                    const next = new Set(selectedEntra);
                                    e.target.checked ? next.add(i.entraObjectId) : next.delete(i.entraObjectId);
                                    setSelectedEntra(next);
                                  }}
                                  className="h-4 w-4 rounded border-gray-300"
                                />
                              )}
                            </td>
                            <td className="px-3 py-2 font-medium">{i.displayName}</td>
                            <td className="px-3 py-2 text-gray-500">{i.email ?? '—'}</td>
                            <td className="px-3 py-2 text-gray-500">{i.kind}</td>
                            <td className="px-3 py-2">
                              {i.alreadyImported ? (
                                <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs text-green-700 border border-green-200">Already imported</span>
                              ) : (
                                <span className="inline-flex items-center rounded-full bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700 border border-indigo-200">New</span>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
              <div className="flex justify-end gap-2 mt-4">
                <button
                  onClick={() => { setShowEntraDiscovery(false); setEntraItems([]); }}
                  className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Cancel
                </button>
                {entraItems.length > 0 && (
                  <button
                    onClick={handleEntraImport}
                    disabled={entraImporting || selectedEntra.size === 0}
                    className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                  >
                    {entraImporting ? 'Importing...' : `Import ${selectedEntra.size} Person${selectedEntra.size !== 1 ? 's' : ''}`}
                  </button>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </PageLayout>
  );
}

// ─── Inline Form ─────────────────────────────────────────────────────────

function ComponentFormInline({
  initial,
  onSubmit,
  onCancel,
  submitting,
}: {
  initial: OrgComponentDto | null;
  onSubmit: (req: CreateComponentRequest) => void;
  onCancel: () => void;
  submitting: boolean;
}) {
  const [name, setName] = useState(initial?.name ?? '');
  const [componentType, setComponentType] = useState<ComponentType>((initial?.componentType as ComponentType) ?? 'Thing');
  const [subType, setSubType] = useState(initial?.subType ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [owner, setOwner] = useState(initial?.owner ?? '');
  const [personName, setPersonName] = useState(initial?.personName ?? '');
  const [email, setEmail] = useState(initial?.email ?? '');
  const [status, setStatus] = useState<ComponentStatus>((initial?.status as ComponentStatus) ?? 'Active');
  const [capabilities, setCapabilities] = useState<SecurityCapabilityDto[]>([]);
  const [selectedCapIds, setSelectedCapIds] = useState<Set<string>>(
    new Set(initial?.capabilityLinks.map((cl) => cl.capabilityId) ?? []),
  );
  const [capSearch, setCapSearch] = useState('');

  useEffect(() => {
    getCapabilities({ pageSize: 200 }).then((r) => setCapabilities(r.items)).catch(() => {});
  }, []);

  const toggleCap = (id: string) => {
    setSelectedCapIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const filteredCaps = capSearch
    ? capabilities.filter((c) => c.name.toLowerCase().includes(capSearch.toLowerCase()))
    : capabilities;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSubmit({
      name,
      componentType,
      subType: subType || undefined,
      description: description || undefined,
      owner: owner || undefined,
      personName: componentType === 'Person' ? (personName || undefined) : undefined,
      email: componentType === 'Person' ? (email || undefined) : undefined,
      status,
      linkedCapabilityIds: [...selectedCapIds],
    });
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div>
        <label className="block text-sm font-medium text-gray-700">Name *</label>
        <input type="text" value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-sm font-medium text-gray-700">Type *</label>
          <select value={componentType} onChange={(e) => setComponentType(e.target.value as ComponentType)} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm">
            {TYPE_OPTIONS.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700">Status *</label>
          <select value={status} onChange={(e) => setStatus(e.target.value as ComponentStatus)} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm">
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
      </div>
      <div>
        <label className="block text-sm font-medium text-gray-700">Sub-Type</label>
        <input type="text" value={subType} onChange={(e) => setSubType(e.target.value)} maxLength={100} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
      </div>
      <div>
        <label className="block text-sm font-medium text-gray-700">Description</label>
        <textarea value={description} onChange={(e) => setDescription(e.target.value)} maxLength={2000} rows={3} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
      </div>
      <div>
        <label className="block text-sm font-medium text-gray-700">Owner</label>
        <input type="text" value={owner} onChange={(e) => setOwner(e.target.value)} maxLength={200} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
      </div>
      {componentType === 'Person' && (
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-sm font-medium text-gray-700">Person Name</label>
            <input type="text" value={personName} onChange={(e) => setPersonName(e.target.value)} maxLength={200} placeholder="e.g. John Smith" className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Email</label>
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={200} placeholder="e.g. john@example.com" className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
          </div>
        </div>
      )}
      {/* Capability Picker */}
      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">
          Linked Capabilities {selectedCapIds.size > 0 && <span className="text-gray-400">({selectedCapIds.size})</span>}
        </label>
        <input
          type="text"
          value={capSearch}
          onChange={(e) => setCapSearch(e.target.value)}
          placeholder="Search capabilities..."
          className="block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm mb-1 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
        <div className="max-h-36 overflow-y-auto rounded-md border border-gray-200 bg-gray-50 p-2 space-y-1">
          {filteredCaps.length === 0 ? (
            <p className="text-xs text-gray-400 italic py-1">
              {capabilities.length === 0 ? 'Loading...' : 'No matching capabilities'}
            </p>
          ) : (
            filteredCaps.map((cap) => (
              <label key={cap.id} className="flex items-center gap-2 text-sm cursor-pointer hover:bg-white rounded px-1 py-0.5">
                <input
                  type="checkbox"
                  checked={selectedCapIds.has(cap.id)}
                  onChange={() => toggleCap(cap.id)}
                  className="h-3.5 w-3.5 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                />
                <span className="truncate text-gray-700">{cap.name}</span>
                <span className="ml-auto text-xs text-gray-400 flex-shrink-0">{cap.category}</span>
              </label>
            ))
          )}
        </div>
      </div>
      <div className="flex justify-end gap-2 pt-2">
        <button type="button" onClick={onCancel} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
        <button type="submit" disabled={submitting || !name} className="rounded-md bg-indigo-600 px-4 py-2 text-sm text-white hover:bg-indigo-700 disabled:opacity-50">
          {submitting ? 'Saving...' : (initial ? 'Update' : 'Create')}
        </button>
      </div>
    </form>
  );
}
