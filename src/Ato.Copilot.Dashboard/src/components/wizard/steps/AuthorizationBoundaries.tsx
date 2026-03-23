import { useState, useEffect, useCallback } from 'react';
import { fetchBoundaryDefinitions, createBoundaryDefinition, assignComponentToBoundary, listBoundaryComponents, addBoundaryResource } from '../../../api/boundaries';
import { getComponents, listComponents } from '../../../api/components';
import type { OrgComponentDto } from '../../../api/components';
import type { BoundaryDefinitionDto, CreateBoundaryDefinitionRequest, SystemComponentDto, BoundaryComponentDto } from '../../../types/dashboard';

interface AuthorizationBoundariesProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

export default function AuthorizationBoundaries({ systemId, onNext, onErrors }: AuthorizationBoundariesProps) {
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [form, setForm] = useState<CreateBoundaryDefinitionRequest>({ name: '', boundaryType: 'Logical', description: '', isPrimary: false });
  const [saving, setSaving] = useState(false);

  // Component assignment state
  const [selectedBoundary, setSelectedBoundary] = useState<string | null>(null);
  const [systemComponents, setSystemComponents] = useState<SystemComponentDto[]>([]);
  const [orgComponents, setOrgComponents] = useState<OrgComponentDto[]>([]);
  const [assignedComponentIds, setAssignedComponentIds] = useState<Set<string>>(new Set());
  const [compSearch, setCompSearch] = useState('');
  const [compTab, setCompTab] = useState<'system' | 'org'>('system');
  const [assigningId, setAssigningId] = useState<string | null>(null);

  useEffect(() => {
    fetchBoundaryDefinitions(systemId).then(setBoundaries).catch(() => {});
    getComponents(systemId).then((res) => setSystemComponents(res.items)).catch(() => {});
  }, [systemId]);

  // Load already-assigned components when a boundary is selected
  const loadAssignedComponents = useCallback(async (boundaryId: string) => {
    try {
      const res = await listBoundaryComponents(systemId, boundaryId, { pageSize: 200 });
      setAssignedComponentIds(new Set(res.items.map((item: BoundaryComponentDto) => item.componentId)));
    } catch {
      setAssignedComponentIds(new Set());
    }
  }, [systemId]);

  useEffect(() => {
    if (selectedBoundary) {
      loadAssignedComponents(selectedBoundary);
    } else {
      setAssignedComponentIds(new Set());
    }
  }, [selectedBoundary, loadAssignedComponents]);

  useEffect(() => {
    if (compTab === 'org') {
      listComponents({ search: compSearch || undefined, pageSize: 50 })
        .then((res) => setOrgComponents(res.items))
        .catch(() => {});
    }
  }, [compTab, compSearch]);

  const handleAdd = async () => {
    if (!form.name.trim()) {
      onErrors({ name: ['Boundary name is required'] });
      return;
    }
    setSaving(true);
    try {
      const result = await createBoundaryDefinition(systemId, {
        name: form.name.trim(),
        boundaryType: form.boundaryType,
        description: form.description,
        isPrimary: form.isPrimary,
      });
      setBoundaries((prev) => [...prev, result]);
      setForm({ name: '', boundaryType: 'Logical', description: '', isPrimary: false });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create boundary';
      onErrors({ _form: [msg] });
    } finally {
      setSaving(false);
    }
  };

  const handleAssignComponent = async (componentId: string, componentName: string, componentType: string, boundaryId: string) => {
    if (assignedComponentIds.has(componentId)) return;
    setAssigningId(componentId);
    try {
      await assignComponentToBoundary(componentId, systemId, boundaryId);
      // Also create a legacy AuthorizationBoundary resource entry so the RMF gate passes
      await addBoundaryResource(boundaryId, {
        resourceId: componentId,
        resourceType: componentType,
        resourceName: componentName,
      }).catch(() => {}); // Ignore 409 duplicates
      // Update local tracking immediately
      setAssignedComponentIds((prev) => new Set([...prev, componentId]));
      // Refresh boundaries to update counts
      const updated = await fetchBoundaryDefinitions(systemId);
      setBoundaries(updated);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to assign component';
      onErrors({ _form: [msg] });
    } finally {
      setAssigningId(null);
    }
  };

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 4: Authorization Boundaries</h2>
      <p className="text-sm text-gray-500 mb-6">Define authorization boundaries and assign components to them.</p>

      {/* Existing boundaries */}
      {boundaries.length > 0 && (
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-700 mb-2">Boundaries ({boundaries.length})</h3>
          <div className="space-y-1">
            {boundaries.map((b) => (
              <div
                key={b.id}
                onClick={() => setSelectedBoundary(selectedBoundary === b.id ? null : b.id)}
                className={`flex items-center justify-between rounded-md border px-3 py-2 text-sm cursor-pointer transition-colors ${
                  selectedBoundary === b.id ? 'border-blue-400 bg-blue-50' : 'border-gray-200 bg-gray-50 hover:bg-gray-100'
                }`}
              >
                <div>
                  <span className="font-medium text-gray-900">{b.name}</span>
                  <span className="ml-2 rounded bg-blue-100 px-1.5 py-0.5 text-xs text-blue-700">{b.boundaryType}</span>
                  {b.isPrimary && <span className="ml-2 rounded bg-amber-100 px-1.5 py-0.5 text-xs text-amber-700">Primary</span>}
                  {b.componentCount != null && b.componentCount > 0 && (
                    <span className="ml-2 text-xs text-gray-400">{b.componentCount} component{b.componentCount !== 1 ? 's' : ''}</span>
                  )}
                </div>
                <span className="text-xs text-blue-600">{selectedBoundary === b.id ? 'Selected' : 'Click to assign components'}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Component assignment panel — shown when a boundary is selected */}
      {selectedBoundary && (
        <div className="mb-6 rounded-md border border-blue-200 bg-blue-50/50 p-4">
          <h3 className="text-sm font-medium text-gray-700 mb-3">
            Assign Components to: <span className="text-blue-700">{boundaries.find((b) => b.id === selectedBoundary)?.name}</span>
          </h3>

          {/* Sub-tabs for system vs org components */}
          <div className="mb-3 flex border-b border-gray-200">
            <button
              onClick={() => setCompTab('system')}
              className={`px-3 py-1.5 text-xs font-medium border-b-2 -mb-px ${
                compTab === 'system' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              System Components
            </button>
            <button
              onClick={() => setCompTab('org')}
              className={`px-3 py-1.5 text-xs font-medium border-b-2 -mb-px ${
                compTab === 'org' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              Organization Library
            </button>
          </div>

          {compTab === 'org' && (
            <input
              value={compSearch}
              onChange={(e) => setCompSearch(e.target.value)}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm mb-2"
              placeholder="Search org components..."
            />
          )}

          <div className="max-h-40 overflow-y-auto space-y-1">
            {compTab === 'system' ? (
              systemComponents.length === 0 ? (
                <p className="text-xs text-gray-400">No system components. Add components in Step 3 first.</p>
              ) : (
                systemComponents.map((c) => {
                  const isAssigned = assignedComponentIds.has(c.id);
                  return (
                  <div key={c.id} className="flex items-center justify-between rounded border border-gray-100 bg-white px-3 py-1.5 text-sm">
                    <div>
                      <span className="font-medium">{c.name}</span>
                      <span className="ml-2 rounded bg-gray-200 px-1 py-0.5 text-xs text-gray-600">{c.componentType}</span>
                    </div>
                    {isAssigned ? (
                      <span className="rounded bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">Assigned &#10003;</span>
                    ) : (
                    <button
                      onClick={() => handleAssignComponent(c.id, c.name, c.componentType, selectedBoundary)}
                      disabled={assigningId === c.id}
                      className="rounded bg-blue-600 px-2 py-0.5 text-xs text-white hover:bg-blue-700 disabled:opacity-50"
                    >
                      {assigningId === c.id ? 'Assigning...' : 'Assign'}
                    </button>
                    )}
                  </div>
                  );
                })
              )
            ) : (
              orgComponents.length === 0 ? (
                <p className="text-xs text-gray-400">No organization components found.</p>
              ) : (
                orgComponents.map((oc) => {
                  const isAssigned = assignedComponentIds.has(oc.id);
                  return (
                  <div key={oc.id} className="flex items-center justify-between rounded border border-gray-100 bg-white px-3 py-1.5 text-sm">
                    <div>
                      <span className="font-medium">{oc.name}</span>
                      <span className="ml-2 rounded bg-gray-200 px-1 py-0.5 text-xs text-gray-600">{oc.componentType}</span>
                      {oc.subType && <span className="ml-1 text-xs text-gray-400">{oc.subType}</span>}
                    </div>
                    {isAssigned ? (
                      <span className="rounded bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">Assigned &#10003;</span>
                    ) : (
                    <button
                      onClick={() => handleAssignComponent(oc.id, oc.name, oc.componentType, selectedBoundary)}
                      disabled={assigningId === oc.id}
                      className="rounded bg-blue-600 px-2 py-0.5 text-xs text-white hover:bg-blue-700 disabled:opacity-50"
                    >
                      {assigningId === oc.id ? 'Assigning...' : 'Assign'}
                    </button>
                    )}
                  </div>
                  );
                })
              )
            )}
          </div>
        </div>
      )}

      {/* Create boundary form */}
      <div className="rounded-md border border-gray-200 p-4 space-y-3">
        <h3 className="text-sm font-medium text-gray-700">Add Boundary</h3>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-600">Name *</label>
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
              placeholder="e.g. Primary Cloud Boundary"
            />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-600">Type *</label>
            <select
              value={form.boundaryType}
              onChange={(e) => setForm({ ...form, boundaryType: e.target.value })}
              className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
            >
              <option value="Physical">Physical</option>
              <option value="Logical">Logical</option>
              <option value="Hybrid">Hybrid</option>
            </select>
          </div>
        </div>
        <div>
          <label className="mb-1 block text-xs font-medium text-gray-600">Description</label>
          <textarea
            value={form.description ?? ''}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            rows={2}
            className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
          />
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={form.isPrimary ?? false}
            onChange={(e) => setForm({ ...form, isPrimary: e.target.checked })}
            className="rounded"
          />
          Mark as primary boundary
        </label>
        <button
          onClick={handleAdd}
          disabled={saving || !form.name.trim()}
          className="rounded-md bg-green-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
        >
          {saving ? 'Creating...' : 'Add Boundary'}
        </button>
      </div>

      <div className="mt-6 flex justify-end">
        <button onClick={onNext} className="rounded-md bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700">
          Next
        </button>
      </div>
    </div>
  );
}
