import { useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { ComponentSection } from '../components/cards/ComponentSection';
import { ComponentForm } from '../components/forms/ComponentForm';
import MetricCard from '../components/cards/MetricCard';
import { usePolling } from '../hooks/usePolling';
import { getComponents, createComponent, updateComponent, deleteComponent, discoverSystemAzureResources, importSystemAzureComponents, relinkComponentFindings } from '../api/components';
import { fetchBoundaryDefinitions } from '../api/boundaries';
import apiClient from '../api/client';
import type { SystemComponentDto, CreateComponentRequest, ComponentType, BoundaryDefinitionDto, DiscoveredResource } from '../types/dashboard';

const SECTIONS: { title: string; type: ComponentType }[] = [
  { title: 'People', type: 'Person' },
  { title: 'Places', type: 'Place' },
  { title: 'Things', type: 'Thing' },
];

export default function ComponentInventory() {
  const { id: systemId } = useParams<{ id: string }>();
  const [components, setComponents] = useState<SystemComponentDto[]>([]);
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [summary, setSummary] = useState({ personCount: 0, placeCount: 0, thingCount: 0, totalCount: 0 });
  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<SystemComponentDto | undefined>();
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [riskMap, setRiskMap] = useState<Record<string, { openCount: number; overdueCount: number; highestSeverity: string | null }>>({});

  // Azure discovery state (Feature 040 — US2)
  const [showDiscover, setShowDiscover] = useState(false);
  const [discoverSubscription, setDiscoverSubscription] = useState('');
  const [discovered, setDiscovered] = useState<DiscoveredResource[]>([]);
  const [discoverLoading, setDiscoverLoading] = useState(false);
  const [discoverError, setDiscoverError] = useState<string | null>(null);
  const [selectedResources, setSelectedResources] = useState<Set<string>>(new Set());
  const [importLoading, setImportLoading] = useState(false);
  const [failedGroups, setFailedGroups] = useState<string[]>([]);

  const fetchData = useCallback(async () => {
    if (!systemId) return;
    const [result, boundaryItems] = await Promise.all([
      getComponents(systemId, {
        search: search || undefined,
        type: typeFilter || undefined,
        status: statusFilter || undefined,
        pageSize: 200,
      }),
      fetchBoundaryDefinitions(systemId).catch(() => [] as BoundaryDefinitionDto[]),
    ]);
    setComponents(result.items);
    setSummary(result.summary);
    setBoundaries(boundaryItems);

    // Fetch POA&M risk summaries for visible components
    const riskEntries: Record<string, { openCount: number; overdueCount: number; highestSeverity: string | null }> = {};
    await Promise.all(
      result.items.map(async (comp) => {
        try {
          const { data } = await apiClient.get<{ openCount: number; overdueCount: number; highestSeverity: string | null }>(
            `/components/${comp.id}/poam`,
          );
          if (data.openCount > 0) {
            riskEntries[comp.id] = data;
          }
        } catch {
          // non-fatal — component has no POA&M data
        }
      }),
    );
    setRiskMap(riskEntries);
  }, [systemId, search, typeFilter, statusFilter]);

  usePolling(fetchData, 15000);

  const handleSubmit = async (data: CreateComponentRequest) => {
    if (!systemId) return;
    setSubmitting(true);
    setFormError(null);
    try {
      if (editing) {
        await updateComponent(editing.id, data);
      } else {
        await createComponent(systemId, data);
      }
      setShowForm(false);
      setEditing(undefined);
      await fetchData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'An error occurred';
      setFormError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleEdit = (comp: SystemComponentDto) => {
    setEditing(comp);
    setShowForm(true);
    setFormError(null);
  };

  const handleDelete = async (id: string) => {
    try {
      const result = await deleteComponent(id);
      if (result.flaggedCapabilities.length > 0) {
        alert(
          `Deleted. The following capabilities were flagged for review:\n${result.flaggedCapabilities
            .map((f) => `• ${f.capabilityName}: ${f.message}`)
            .join('\n')}`,
        );
      }
      setDeleteConfirm(null);
      await fetchData();
    } catch {
      alert('Failed to delete component');
    }
  };

  const handleCancel = () => {
    setShowForm(false);
    setEditing(undefined);
    setFormError(null);
  };

  // Re-link findings handler (Feature 040 — FR-027)
  const handleRelink = async (comp: SystemComponentDto) => {
    if (!systemId) return;
    try {
      const result = await relinkComponentFindings(systemId, comp.id);
      alert(`Re-linked ${result.linkedCount} finding(s) for ${comp.name}`);
      await fetchData();
    } catch {
      alert('Failed to re-link findings');
    }
  };

  // Azure discovery handlers (Feature 040 — US2)
  const handleDiscover = async () => {
    if (!systemId || !discoverSubscription.trim()) return;
    setDiscoverLoading(true);
    setDiscoverError(null);
    setFailedGroups([]);
    try {
      const result = await discoverSystemAzureResources(systemId, {
        subscriptionId: discoverSubscription.trim(),
      });
      setDiscovered(result.resources);
      setSelectedResources(new Set(
        result.resources.filter((r) => !r.alreadyImported).map((r) => r.resourceId),
      ));
      if (result.failedResourceGroups?.length) {
        setFailedGroups(result.failedResourceGroups);
      }
    } catch (err) {
      setDiscoverError(err instanceof Error ? err.message : 'Discovery failed');
    } finally {
      setDiscoverLoading(false);
    }
  };

  const handleImportSelected = async () => {
    if (!systemId || selectedResources.size === 0) return;
    setImportLoading(true);
    try {
      const toImport = discovered
        .filter((r) => selectedResources.has(r.resourceId) && !r.alreadyImported)
        .map((r) => ({
          resourceId: r.resourceId,
          name: r.name,
          type: r.type,
          resourceGroup: r.resourceGroup,
          location: r.location,
        }));

      // Check for org-library resources to assign instead of re-create
      const orgAssign = discovered
        .filter((r) => selectedResources.has(r.resourceId) && r.existsInOrgLibrary && r.orgLibraryComponentId)
        .map((r) => r.orgLibraryComponentId!);

      await importSystemAzureComponents(systemId, {
        resources: toImport,
        assignExistingOrgComponents: orgAssign.length > 0 ? orgAssign : undefined,
      });
      setShowDiscover(false);
      setDiscovered([]);
      setSelectedResources(new Set());
      await fetchData();
    } catch (err) {
      setDiscoverError(err instanceof Error ? err.message : 'Import failed');
    } finally {
      setImportLoading(false);
    }
  };

  if (!systemId) return null;

  return (
    <>
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">System Components</h2>
          <p className="mt-1 text-sm text-gray-500">
            People, Places, and Things that make up your system.
          </p>
        </div>
      </div>

      {/* Summary metrics */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
        <MetricCard title="Total" value={summary.totalCount} />
        <MetricCard title="People" value={summary.personCount} />
        <MetricCard title="Places" value={summary.placeCount} />
        <MetricCard title="Things" value={summary.thingCount} />
      </div>

      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-3 mb-4">
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search components..."
          className="border rounded px-3 py-1.5 text-sm flex-1 min-w-[200px] focus:ring-2 focus:ring-blue-300 focus:outline-none"
        />
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value)}
          className="border rounded px-3 py-1.5 text-sm focus:outline-none"
        >
          <option value="">All Types</option>
          <option value="Person">Person</option>
          <option value="Place">Place</option>
          <option value="Thing">Thing</option>
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="border rounded px-3 py-1.5 text-sm focus:outline-none"
        >
          <option value="">All Statuses</option>
          <option value="Active">Active</option>
          <option value="Planned">Planned</option>
          <option value="Decommissioned">Decommissioned</option>
        </select>
        <button
          onClick={() => { setShowForm(true); setEditing(undefined); setFormError(null); }}
          className="px-4 py-1.5 text-sm bg-blue-600 text-white rounded hover:bg-blue-700"
        >
          + Add Component
        </button>
        <button
          onClick={() => { setShowDiscover(true); setDiscoverError(null); }}
          className="px-4 py-1.5 text-sm bg-green-600 text-white rounded hover:bg-green-700"
        >
          Discover from Azure
        </button>
      </div>

      {/* Form modal */}
      {showForm && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-lg max-h-[90vh] overflow-y-auto">
            <h2 className="text-lg font-semibold mb-4">{editing ? 'Edit Component' : 'Add Component'}</h2>
            <ComponentForm
              initial={editing}
              systemId={systemId}
              onSubmit={handleSubmit}
              onCancel={handleCancel}
              isSubmitting={submitting}
              error={formError}
            />
          </div>
        </div>
      )}

      {/* Delete confirmation */}
      {deleteConfirm && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-sm">
            <h3 className="font-semibold text-gray-900 mb-2">Delete Component?</h3>
            <p className="text-sm text-gray-600 mb-4">
              Linked capabilities will be flagged for review. This action cannot be undone.
            </p>
            <div className="flex justify-end gap-3">
              <button onClick={() => setDeleteConfirm(null)} className="text-sm text-gray-600 hover:text-gray-800 px-3 py-1.5">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm)} className="text-sm bg-red-600 text-white rounded px-3 py-1.5 hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {/* Azure Discovery dialog (Feature 040 — US2) */}
      {showDiscover && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-3xl max-h-[85vh] overflow-y-auto">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold">Discover Azure Resources</h2>
              <button onClick={() => { setShowDiscover(false); setDiscovered([]); }} className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
            </div>

            <div className="flex gap-2 mb-4">
              <input
                type="text"
                value={discoverSubscription}
                onChange={(e) => setDiscoverSubscription(e.target.value)}
                placeholder="Azure Subscription ID"
                className="border rounded px-3 py-1.5 text-sm flex-1 focus:ring-2 focus:ring-green-300 focus:outline-none"
              />
              <button
                onClick={handleDiscover}
                disabled={discoverLoading || !discoverSubscription.trim()}
                className="px-4 py-1.5 text-sm bg-green-600 text-white rounded hover:bg-green-700 disabled:opacity-50"
              >
                {discoverLoading ? 'Scanning...' : 'Scan'}
              </button>
            </div>

            {discoverError && (
              <div className="bg-red-50 border border-red-200 rounded p-3 mb-3 text-sm text-red-700">{discoverError}</div>
            )}

            {failedGroups.length > 0 && (
              <div className="bg-yellow-50 border border-yellow-200 rounded p-3 mb-3 text-sm text-yellow-800">
                <strong>Partial failure:</strong> Could not scan resource groups: {failedGroups.join(', ')}
                <button
                  onClick={handleDiscover}
                  className="ml-2 text-yellow-900 underline text-xs"
                >
                  Retry
                </button>
              </div>
            )}

            {discovered.length > 0 && (
              <>
                <div className="border rounded overflow-hidden mb-4">
                  <table className="w-full text-sm">
                    <thead className="bg-gray-50">
                      <tr>
                        <th className="px-3 py-2 text-left w-8">
                          <input
                            type="checkbox"
                            checked={selectedResources.size === discovered.filter((r) => !r.alreadyImported).length && discovered.some((r) => !r.alreadyImported)}
                            onChange={(e) => {
                              if (e.target.checked) {
                                setSelectedResources(new Set(discovered.filter((r) => !r.alreadyImported).map((r) => r.resourceId)));
                              } else {
                                setSelectedResources(new Set());
                              }
                            }}
                          />
                        </th>
                        <th className="px-3 py-2 text-left">Name</th>
                        <th className="px-3 py-2 text-left">Type</th>
                        <th className="px-3 py-2 text-left">Resource Group</th>
                        <th className="px-3 py-2 text-left">Status</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y">
                      {discovered.map((r) => (
                        <tr key={r.resourceId} className={r.alreadyImported ? 'bg-gray-50 text-gray-400' : ''}>
                          <td className="px-3 py-2">
                            <input
                              type="checkbox"
                              disabled={r.alreadyImported}
                              checked={selectedResources.has(r.resourceId)}
                              onChange={(e) => {
                                const next = new Set(selectedResources);
                                if (e.target.checked) next.add(r.resourceId);
                                else next.delete(r.resourceId);
                                setSelectedResources(next);
                              }}
                            />
                          </td>
                          <td className="px-3 py-2 font-medium">{r.name}</td>
                          <td className="px-3 py-2 text-xs">{r.type}</td>
                          <td className="px-3 py-2 text-xs">{r.resourceGroup}</td>
                          <td className="px-3 py-2">
                            {r.alreadyImported ? (
                              <span className="text-xs bg-gray-200 text-gray-600 px-2 py-0.5 rounded">Already imported</span>
                            ) : r.existsInOrgLibrary ? (
                              <span className="text-xs bg-blue-100 text-blue-700 px-2 py-0.5 rounded">In org library</span>
                            ) : (
                              <span className="text-xs bg-green-100 text-green-700 px-2 py-0.5 rounded">New</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <div className="flex justify-end gap-3">
                  <button onClick={() => { setShowDiscover(false); setDiscovered([]); }} className="text-sm text-gray-600 hover:text-gray-800 px-3 py-1.5">
                    Cancel
                  </button>
                  <button
                    onClick={handleImportSelected}
                    disabled={importLoading || selectedResources.size === 0}
                    className="px-4 py-1.5 text-sm bg-green-600 text-white rounded hover:bg-green-700 disabled:opacity-50"
                  >
                    {importLoading ? 'Importing...' : `Import ${selectedResources.size} Selected`}
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* Component sections */}
      {summary.totalCount === 0 && !search && !typeFilter && !statusFilter ? (
        <div className="text-center py-16 text-gray-400">
          <p className="text-lg mb-2">No components yet</p>
          <p className="text-sm">Add People, Places, and Things that make up your system.</p>
        </div>
      ) : boundaries.length > 1 ? (
        // Group by boundary when multiple boundaries exist
        <div className="space-y-6">
          {boundaries.map((boundary) => {
            const boundaryComponents = components.filter(
              (c) => c.boundaryDefinitionId === boundary.id
            );
            if (boundaryComponents.length === 0) return null;
            return (
              <div key={boundary.id} className="rounded-lg border border-gray-200 bg-white shadow-sm">
                <div className="px-4 py-3 border-b border-gray-100 bg-gray-50 rounded-t-lg">
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold text-gray-700">{boundary.name}</h3>
                    <span className="text-xs bg-blue-50 text-blue-700 px-2 py-0.5 rounded">
                      {boundary.boundaryType}
                    </span>
                    {boundary.isPrimary && (
                      <span className="text-xs bg-green-100 text-green-800 px-2 py-0.5 rounded">
                        Primary
                      </span>
                    )}
                    <span className="text-xs text-gray-400 ml-auto">{boundaryComponents.length} components</span>
                  </div>
                </div>
                <div className="p-4 space-y-4">
                  {SECTIONS.map(({ title, type }) => {
                    const items = boundaryComponents.filter((c) => c.componentType === type);
                    if (items.length === 0) return null;
                    return (
                      <ComponentSection
                        key={`${boundary.id}-${type}`}
                        title={title}
                        type={type}
                        components={items}
                        count={items.length}
                        onEdit={handleEdit}
                        onDelete={(id) => setDeleteConfirm(id)}
                        onRelink={handleRelink}
                        riskMap={riskMap}
                      />
                    );
                  })}
                </div>
              </div>
            );
          })}
          {/* Components without boundary assignment or with unrecognized boundary */}
          {(() => {
            const boundaryIds = new Set(boundaries.map((b) => b.id));
            const unassigned = components.filter((c) => !c.boundaryDefinitionId || !boundaryIds.has(c.boundaryDefinitionId));
            if (unassigned.length === 0) return null;
            return (
              <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
                <div className="px-4 py-3 border-b border-gray-100 bg-gray-50 rounded-t-lg">
                  <h3 className="text-sm font-semibold text-gray-500">Unassigned</h3>
                </div>
                <div className="p-4 space-y-4">
                  {SECTIONS.map(({ title, type }) => {
                    const items = unassigned.filter((c) => c.componentType === type);
                  if (items.length === 0) return null;
                  return (
                    <ComponentSection
                      key={`unassigned-${type}`}
                      title={title}
                      type={type}
                      components={items}
                      count={items.length}
                      onEdit={handleEdit}
                      onDelete={(id) => setDeleteConfirm(id)}
                        riskMap={riskMap}
                    />
                  );
                })}
              </div>
            </div>
            );
          })()}
        </div>
      ) : (
        <div className="space-y-4">
          {SECTIONS.map(({ title, type }) => {
            const items = components.filter((c) => c.componentType === type);
            const count = type === 'Person' ? summary.personCount : type === 'Place' ? summary.placeCount : summary.thingCount;
            return (
              <ComponentSection
                key={type}
                title={title}
                type={type}
                components={items}
                count={count}
                onEdit={handleEdit}
                onDelete={(id) => setDeleteConfirm(id)}
                        riskMap={riskMap}
              />
            );
          })}
        </div>
      )}
    </>
  );
}
