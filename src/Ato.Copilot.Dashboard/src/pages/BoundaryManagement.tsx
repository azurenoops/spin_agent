import { useState, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import { BoundarySummaryCard } from '../components/cards/BoundarySummaryCard';
import { BoundaryForm } from '../components/forms/BoundaryForm';
import { usePolling } from '../hooks/usePolling';
import {
  fetchBoundaryDefinitions,
  createBoundaryDefinition,
  updateBoundaryDefinition,
  deleteBoundaryDefinition,
  fetchBoundaryResources,
  addBoundaryResource,
  deleteBoundaryResource,
} from '../api/boundaries';
import type { BoundaryResourceDto, AddBoundaryResourceBody } from '../api/boundaries';
import { discoverAzureResources } from '../api/azureDiscovery';
import type {
  BoundaryDefinitionDto,
  CreateBoundaryDefinitionRequest,
  DeleteBoundaryDefinitionResponse,
  AzureDiscoveryResponse,
  AzureSuggestedBoundaryDto,
} from '../types/dashboard';

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

  // Azure discovery state
  const [discoveryLoading, setDiscoveryLoading] = useState(false);
  const [discoveryError, setDiscoveryError] = useState<string | null>(null);
  const [discoveryData, setDiscoveryData] = useState<AzureDiscoveryResponse | null>(null);
  const [discoverySearch, setDiscoverySearch] = useState('');
  const [applyResult, setApplyResult] = useState<string | null>(null);

  // Resource management state
  const [expandedBoundary, setExpandedBoundary] = useState<string | null>(null);
  const [resources, setResources] = useState<BoundaryResourceDto[]>([]);
  const [resourcesLoading, setResourcesLoading] = useState(false);
  const [addResourceForm, setAddResourceForm] = useState<AddBoundaryResourceBody>({
    resourceId: '',
    resourceType: '',
    resourceName: '',
  });
  const [addingResource, setAddingResource] = useState(false);
  const [resourceDialogTab, setResourceDialogTab] = useState<'resources' | 'manual' | 'discover'>('resources');
  const [selectedDiscoveryResources, setSelectedDiscoveryResources] = useState<Set<string>>(new Set());

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

  const handleDiscover = async () => {
    if (!systemId) return;
    setDiscoveryLoading(true);
    setDiscoveryError(null);
    try {
      const result = await discoverAzureResources(systemId, {
        search: discoverySearch || undefined,
      });
      setDiscoveryData(result);
    } catch (err: unknown) {
      const detail = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { error?: string } } }).response?.data?.error
        : undefined;
      setDiscoveryError(detail ?? 'Failed to discover Azure resources. Check Azure credentials and RBAC configuration.');
    } finally {
      setDiscoveryLoading(false);
    }
  };

  const handleExpandBoundary = async (boundaryId: string) => {
    if (expandedBoundary === boundaryId) {
      setExpandedBoundary(null);
      return;
    }
    setExpandedBoundary(boundaryId);
    setResourceDialogTab('resources');
    setResourcesLoading(true);
    setDiscoveryData(null);
    setDiscoveryError(null);
    setSelectedDiscoveryResources(new Set());
    try {
      const items = await fetchBoundaryResources(boundaryId);
      setResources(items);
    } catch {
      setResources([]);
    } finally {
      setResourcesLoading(false);
    }
  };

  const handleAddResource = async () => {
    if (!expandedBoundary || !addResourceForm.resourceId.trim() || !addResourceForm.resourceType.trim()) return;
    setAddingResource(true);
    try {
      await addBoundaryResource(expandedBoundary, addResourceForm);
      setAddResourceForm({ resourceId: '', resourceType: '', resourceName: '' });
      setResourceDialogTab('resources');
      const items = await fetchBoundaryResources(expandedBoundary);
      setResources(items);
      await fetchData();
    } catch {
      setError('Failed to add resource');
    } finally {
      setAddingResource(false);
    }
  };

  const handleAddDiscoveredResource = async (resourceId: string, resourceType: string, resourceName: string) => {
    if (!expandedBoundary) return;
    try {
      await addBoundaryResource(expandedBoundary, { resourceId, resourceType, resourceName });
      setSelectedDiscoveryResources((prev) => new Set([...prev, resourceId]));
      const items = await fetchBoundaryResources(expandedBoundary);
      setResources(items);
      await fetchData();
    } catch {
      setError('Failed to add resource');
    }
  };

  const handleDeleteResource = async (resourceEntryId: string) => {
    if (!expandedBoundary) return;
    try {
      await deleteBoundaryResource(expandedBoundary, resourceEntryId);
      const items = await fetchBoundaryResources(expandedBoundary);
      setResources(items);
      await fetchData();
    } catch {
      setError('Failed to remove resource');
    }
  };

  if (loading) {
    return (
      <PageLayout title="Boundary Management">
        <p className="text-gray-500">Loading boundaries...</p>
      </PageLayout>
    );
  }

  return (
    <PageLayout title="Boundary Management">
      {/* Breadcrumb */}
      <div className="mb-4 text-sm">
        <Link to="/" className="text-blue-600 hover:underline">Portfolio</Link>
        <span className="mx-2 text-gray-400">/</span>
        <Link to={`/systems/${systemId}`} className="text-blue-600 hover:underline">System</Link>
        <span className="mx-2 text-gray-400">/</span>
        <span className="text-gray-700">Boundaries</span>
      </div>

      {error && (
        <div className="mb-4 bg-red-50 text-red-700 p-3 rounded text-sm">{error}</div>
      )}

      {/* Delete result banner */}
      {deleteResult && (
        <div className="mb-4 bg-green-50 text-green-800 p-3 rounded text-sm">
          Boundary deleted. Reassigned {deleteResult.reassignedResources} resources,{' '}
          {deleteResult.reassignedComponents} components, and {deleteResult.reassignedMappings} mappings
          to Primary boundary.
          <button
            onClick={() => setDeleteResult(null)}
            className="ml-2 text-green-600 hover:underline text-xs"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Add boundary button */}
      {formMode.kind === 'closed' && (
        <div className="mb-4 flex gap-2">
          <button
            onClick={() => setFormMode({ kind: 'create' })}
            className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700"
          >
            + Add Boundary
          </button>
        </div>
      )}

      {/* Apply result banner */}
      {applyResult && (
        <div className="mb-4 bg-green-50 text-green-800 p-3 rounded text-sm">
          {applyResult}
          <button onClick={() => setApplyResult(null)} className="ml-2 text-green-600 hover:underline text-xs">
            Dismiss
          </button>
        </div>
      )}

      {/* Form */}
      {formMode.kind !== 'closed' && (
        <div className="mb-6 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <h2 className="text-sm font-semibold text-gray-700 mb-3">
            {formMode.kind === 'create' ? 'Create Boundary' : 'Edit Boundary'}
          </h2>
          <BoundaryForm
            initial={formMode.kind === 'edit' ? formMode.boundary : undefined}
            onSubmit={formMode.kind === 'create' ? handleCreate : handleUpdate}
            onCancel={() => { setFormMode({ kind: 'closed' }); setFormError(null); }}
            isSubmitting={submitting}
            error={formError}
          />
        </div>
      )}

      {/* Delete confirmation modal */}
      {deleteConfirm && (
        <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-lg p-6 max-w-md w-full mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Boundary</h3>
            <p className="text-sm text-gray-600 mb-4">
              Are you sure you want to delete <strong>{deleteConfirm.name}</strong>?
              All resources ({deleteConfirm.resourceCount}), components ({deleteConfirm.componentCount}),
              and mappings will be reassigned to the Primary boundary.
            </p>
            <div className="flex gap-2 justify-end">
              <button
                onClick={() => setDeleteConfirm(null)}
                className="px-4 py-2 text-sm bg-gray-100 text-gray-700 rounded hover:bg-gray-200"
              >
                Cancel
              </button>
              <button
                onClick={handleDelete}
                className="px-4 py-2 text-sm bg-red-600 text-white rounded hover:bg-red-700"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Boundary list */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {boundaries.map((b) => (
          <BoundarySummaryCard
            key={b.id}
            boundary={b}
            onEdit={() => setFormMode({ kind: 'edit', boundary: b })}
            onDelete={() => setDeleteConfirm(b)}
            onExpand={() => handleExpandBoundary(b.id)}
            expanded={expandedBoundary === b.id}
          />
        ))}
      </div>

      {/* Resource Management Dialog */}
      {expandedBoundary && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-3xl max-h-[85vh] flex flex-col rounded-lg bg-white shadow-xl mx-4">
            {/* Dialog header */}
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
              <h2 className="text-lg font-semibold text-gray-900">
                Resources — {boundaries.find((b) => b.id === expandedBoundary)?.name}
              </h2>
              <button
                onClick={() => setExpandedBoundary(null)}
                className="text-gray-400 hover:text-gray-600 text-xl leading-none"
              >
                &times;
              </button>
            </div>

            {/* Tabs */}
            <div className="flex border-b border-gray-200 px-6">
              <button
                onClick={() => setResourceDialogTab('resources')}
                className={`px-4 py-2.5 text-sm font-medium border-b-2 -mb-px ${
                  resourceDialogTab === 'resources'
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                Resources ({resources.length})
              </button>
              <button
                onClick={() => setResourceDialogTab('manual')}
                className={`px-4 py-2.5 text-sm font-medium border-b-2 -mb-px ${
                  resourceDialogTab === 'manual'
                    ? 'border-blue-600 text-blue-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                + Add Manually
              </button>
              <button
                onClick={() => {
                  setResourceDialogTab('discover');
                  if (!discoveryData && !discoveryLoading) handleDiscover();
                }}
                className={`px-4 py-2.5 text-sm font-medium border-b-2 -mb-px ${
                  resourceDialogTab === 'discover'
                    ? 'border-emerald-600 text-emerald-600'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
              >
                Discover from Azure
              </button>
            </div>

            {/* Tab content */}
            <div className="flex-1 overflow-y-auto px-6 py-4">
              {/* Resources tab */}
              {resourceDialogTab === 'resources' && (
                <>
                  {resourcesLoading ? (
                    <p className="text-sm text-gray-500">Loading resources...</p>
                  ) : resources.length === 0 ? (
                    <div className="text-center py-8">
                      <p className="text-gray-500 mb-3">No resources in this boundary yet.</p>
                      <div className="flex gap-2 justify-center">
                        <button
                          onClick={() => setResourceDialogTab('manual')}
                          className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700"
                        >
                          Add Manually
                        </button>
                        <button
                          onClick={() => {
                            setResourceDialogTab('discover');
                            if (!discoveryData && !discoveryLoading) handleDiscover();
                          }}
                          className="px-4 py-2 text-sm bg-emerald-600 text-white rounded hover:bg-emerald-700"
                        >
                          Discover from Azure
                        </button>
                      </div>
                    </div>
                  ) : (
                    <table className="min-w-full text-sm">
                      <thead>
                        <tr className="border-b border-gray-200 text-left text-xs font-medium uppercase text-gray-500">
                          <th className="py-2 pr-3">Name</th>
                          <th className="py-2 pr-3">Resource ID</th>
                          <th className="py-2 pr-3">Type</th>
                          <th className="py-2 pr-3">Status</th>
                          <th className="py-2"></th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {resources.map((r) => (
                          <tr key={r.id}>
                            <td className="py-2 pr-3 font-medium text-gray-800">{r.resourceName || '—'}</td>
                            <td className="py-2 pr-3 text-gray-600 max-w-[200px] truncate" title={r.resourceId}>
                              {r.resourceId}
                            </td>
                            <td className="py-2 pr-3 text-gray-600">{r.resourceType.split('/').pop()}</td>
                            <td className="py-2 pr-3">
                              {r.isInBoundary ? (
                                <span className="text-xs bg-green-100 text-green-700 px-1.5 py-0.5 rounded">Active</span>
                              ) : (
                                <span className="text-xs bg-gray-100 text-gray-600 px-1.5 py-0.5 rounded">Excluded</span>
                              )}
                            </td>
                            <td className="py-2 text-right">
                              <button onClick={() => handleDeleteResource(r.id)} className="text-xs text-red-600 hover:underline">
                                Remove
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </>
              )}

              {/* Manual add tab */}
              {resourceDialogTab === 'manual' && (
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Resource ID *</label>
                    <input
                      type="text"
                      value={addResourceForm.resourceId}
                      onChange={(e) => setAddResourceForm({ ...addResourceForm, resourceId: e.target.value })}
                      placeholder="/subscriptions/xxx/resourceGroups/myRG/providers/Microsoft.Compute/virtualMachines/myVM"
                      className="w-full text-sm border border-gray-300 rounded-md px-3 py-2"
                    />
                  </div>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">Resource Type *</label>
                      <input
                        type="text"
                        value={addResourceForm.resourceType}
                        onChange={(e) => setAddResourceForm({ ...addResourceForm, resourceType: e.target.value })}
                        placeholder="Microsoft.Compute/virtualMachines"
                        className="w-full text-sm border border-gray-300 rounded-md px-3 py-2"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">Display Name</label>
                      <input
                        type="text"
                        value={addResourceForm.resourceName ?? ''}
                        onChange={(e) => setAddResourceForm({ ...addResourceForm, resourceName: e.target.value })}
                        placeholder="e.g. My Web Server"
                        className="w-full text-sm border border-gray-300 rounded-md px-3 py-2"
                      />
                    </div>
                  </div>
                  <div className="flex gap-2 pt-2">
                    <button
                      onClick={handleAddResource}
                      disabled={addingResource || !addResourceForm.resourceId.trim() || !addResourceForm.resourceType.trim()}
                      className="px-4 py-2 text-sm bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
                    >
                      {addingResource ? 'Adding...' : 'Add Resource'}
                    </button>
                    <button
                      onClick={() => setResourceDialogTab('resources')}
                      className="px-4 py-2 text-sm text-gray-600 hover:text-gray-800"
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              )}

              {/* Discover tab */}
              {resourceDialogTab === 'discover' && (
                <div>
                  {/* Search */}
                  <div className="mb-4 flex gap-2">
                    <input
                      type="text"
                      value={discoverySearch}
                      onChange={(e) => setDiscoverySearch(e.target.value)}
                      placeholder="Filter by resource name..."
                      className="flex-1 text-sm border border-gray-300 rounded-md px-3 py-2"
                    />
                    <button
                      onClick={handleDiscover}
                      disabled={discoveryLoading}
                      className="px-4 py-2 text-sm bg-emerald-600 text-white rounded-md hover:bg-emerald-700 disabled:opacity-50"
                    >
                      {discoveryLoading ? 'Searching...' : 'Search'}
                    </button>
                  </div>

                  {discoveryError && (
                    <div className="mb-3 rounded bg-amber-50 p-3 text-sm text-amber-800">
                      {discoveryError}
                    </div>
                  )}

                  {discoveryLoading && !discoveryData && (
                    <p className="text-sm text-gray-500 text-center py-8">Discovering Azure resources...</p>
                  )}

                  {discoveryData && (
                    <>
                      <p className="text-xs text-gray-500 mb-3">
                        {discoveryData.totalResourceCount} resources found. Click "Add" to add individual resources to this boundary.
                      </p>
                      <div className="space-y-2 max-h-[400px] overflow-y-auto">
                        {discoveryData.suggestedBoundaries.flatMap((sb: AzureSuggestedBoundaryDto) =>
                          sb.resources.map((r) => {
                            const alreadyAdded = r.alreadyInBoundary || selectedDiscoveryResources.has(r.resourceId);
                            return (
                              <div
                                key={r.resourceId}
                                className={`flex items-center justify-between border rounded-md p-3 ${
                                  alreadyAdded ? 'bg-gray-50 border-gray-200' : 'bg-white border-gray-200 hover:border-emerald-300'
                                }`}
                              >
                                <div className="min-w-0 flex-1">
                                  <div className="flex items-center gap-2">
                                    <span className="text-sm font-medium text-gray-800 truncate">{r.name}</span>
                                    <span className="text-xs text-gray-400">({r.type.split('/').pop()})</span>
                                  </div>
                                  <p className="text-xs text-gray-500 truncate mt-0.5" title={r.resourceId}>
                                    {r.resourceId}
                                  </p>
                                  <p className="text-xs text-gray-400 mt-0.5">
                                    {sb.resourceGroupName} &middot; {r.location}
                                  </p>
                                </div>
                                <div className="ml-3 flex-shrink-0">
                                  {alreadyAdded ? (
                                    <span className="text-xs bg-green-100 text-green-700 px-2 py-1 rounded">Added</span>
                                  ) : (
                                    <button
                                      onClick={() => handleAddDiscoveredResource(r.resourceId, r.type, r.name)}
                                      className="px-3 py-1 text-xs bg-emerald-600 text-white rounded hover:bg-emerald-700"
                                    >
                                      Add
                                    </button>
                                  )}
                                </div>
                              </div>
                            );
                          }),
                        )}
                      </div>
                    </>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {boundaries.length === 0 && !loading && (
        <p className="text-gray-500 text-sm mt-4">No boundary definitions found.</p>
      )}
    </PageLayout>
  );
}
