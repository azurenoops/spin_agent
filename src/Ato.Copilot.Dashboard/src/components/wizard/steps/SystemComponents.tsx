import { useState, useEffect } from 'react';
import { getComponents, createComponent, listComponents, assignToSystem } from '../../../api/components';
import type { OrgComponentDto } from '../../../api/components';
import type { SystemComponentDto, CreateComponentRequest, ComponentType } from '../../../types/dashboard';

interface SystemComponentsProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

const EMPTY_FORM: CreateComponentRequest = {
  name: '',
  componentType: 'Thing',
  subType: '',
  description: '',
  owner: '',
  personName: '',
  email: '',
  status: 'Active',
};

export default function SystemComponents({ systemId, onNext, onErrors }: SystemComponentsProps) {
  const [components, setComponents] = useState<SystemComponentDto[]>([]);
  const [form, setForm] = useState<CreateComponentRequest>({ ...EMPTY_FORM });
  const [saving, setSaving] = useState(false);
  const [activeTab, setActiveTab] = useState<'org' | 'new'>('org');

  // Org-level component search
  const [orgSearch, setOrgSearch] = useState('');
  const [orgComponents, setOrgComponents] = useState<OrgComponentDto[]>([]);
  const [orgLoading, setOrgLoading] = useState(false);
  const [assigning, setAssigning] = useState<string | null>(null);

  useEffect(() => {
    getComponents(systemId).then((res) => setComponents(res.items)).catch(() => {});
  }, [systemId]);

  // Load org components on tab switch or search
  useEffect(() => {
    if (activeTab !== 'org') return;
    setOrgLoading(true);
    listComponents({ search: orgSearch || undefined, pageSize: 50 })
      .then((res) => setOrgComponents(res.items))
      .catch(() => {})
      .finally(() => setOrgLoading(false));
  }, [activeTab, orgSearch]);

  const assignedIds = new Set(components.map((c) => c.id));

  const handleAssignOrg = async (orgComp: OrgComponentDto) => {
    setAssigning(orgComp.id);
    try {
      await assignToSystem(orgComp.id, { registeredSystemId: systemId });
      // Refresh the system component list
      const res = await getComponents(systemId);
      setComponents(res.items);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to assign component';
      onErrors({ _form: [msg] });
    } finally {
      setAssigning(null);
    }
  };

  const handleAdd = async () => {
    if (!form.name.trim()) {
      onErrors({ name: ['Component name is required'] });
      return;
    }
    setSaving(true);
    try {
      const result = await createComponent(systemId, {
        ...form,
        name: form.name.trim(),
      });
      setComponents((prev) => [...prev, result]);
      setForm({ ...EMPTY_FORM });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to add component';
      onErrors({ _form: [msg] });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 3: System Components</h2>
      <p className="text-sm text-gray-500 mb-6">Add existing org-level components or create new system-level components.</p>

      {/* System components list */}
      {components.length > 0 && (
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-700 mb-2">System Components ({components.length})</h3>
          <div className="space-y-1">
            {components.map((comp) => (
              <div key={comp.id} className="flex items-center justify-between rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm">
                <div>
                  <span className="font-medium text-gray-900">{comp.name}</span>
                  <span className="ml-2 rounded bg-gray-200 px-1.5 py-0.5 text-xs text-gray-600">{comp.componentType}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Tab switcher */}
      <div className="mb-4 flex border-b border-gray-200">
        <button
          onClick={() => setActiveTab('org')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'org' ? 'border-indigo-600 text-indigo-600' : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Add from Organization Library
        </button>
        <button
          onClick={() => setActiveTab('new')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'new' ? 'border-indigo-600 text-indigo-600' : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Create New Component
        </button>
      </div>

      {/* Org component search & assign */}
      {activeTab === 'org' && (
        <div className="rounded-md border border-gray-200 p-4 space-y-3">
          <input
            value={orgSearch}
            onChange={(e) => setOrgSearch(e.target.value)}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            placeholder="Search organization components..."
          />
          {orgLoading ? (
            <p className="text-sm text-gray-400">Loading...</p>
          ) : orgComponents.length === 0 ? (
            <p className="text-sm text-gray-400">No organization components found.</p>
          ) : (
            <div className="max-h-56 overflow-y-auto space-y-1">
              {orgComponents.map((oc) => {
                const alreadyAssigned = assignedIds.has(oc.id) || oc.systemAssignments?.some((a) => a.registeredSystemId === systemId);
                return (
                  <div key={oc.id} className="flex items-center justify-between rounded-md border border-gray-100 px-3 py-2 text-sm hover:bg-gray-50">
                    <div>
                      <span className="font-medium text-gray-900">{oc.name}</span>
                      <span className="ml-2 rounded bg-gray-200 px-1.5 py-0.5 text-xs text-gray-600">{oc.componentType}</span>
                      {oc.subType && <span className="ml-1 text-xs text-gray-400">{oc.subType}</span>}
                    </div>
                    <button
                      onClick={() => handleAssignOrg(oc)}
                      disabled={alreadyAssigned || assigning === oc.id}
                      className="rounded-md bg-indigo-600 px-3 py-1 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                      {alreadyAssigned ? 'Assigned' : assigning === oc.id ? 'Adding...' : 'Add'}
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* Create new component form */}
      {activeTab === 'new' && (
        <div className="rounded-md border border-gray-200 p-4 space-y-3">
          <h3 className="text-sm font-medium text-gray-700">Create New Component</h3>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-600">Name *</label>
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                placeholder="e.g. Web Application Server"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-600">Type *</label>
              <select
                value={form.componentType}
                onChange={(e) => setForm({ ...form, componentType: e.target.value as ComponentType })}
                className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
              >
                <option value="Person">Person</option>
                <option value="Place">Place</option>
                <option value="Thing">Thing</option>
              </select>
            </div>
          </div>
          {form.componentType === 'Person' && (
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Person Name</label>
                <input
                  value={form.personName ?? ''}
                  onChange={(e) => setForm({ ...form, personName: e.target.value })}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Email</label>
                <input
                  value={form.email ?? ''}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                  className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
                />
              </div>
            </div>
          )}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-600">Sub-Type</label>
              <input
                value={form.subType ?? ''}
                onChange={(e) => setForm({ ...form, subType: e.target.value })}
                className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-600">Owner</label>
              <input
                value={form.owner ?? ''}
                onChange={(e) => setForm({ ...form, owner: e.target.value })}
                className="w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm"
              />
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
          <button
            onClick={handleAdd}
            disabled={saving || !form.name.trim()}
            className="rounded-md bg-green-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
          >
            {saving ? 'Adding...' : 'Add Component'}
          </button>
        </div>
      )}

      <div className="mt-6 flex justify-end">
        <button onClick={onNext} className="rounded-md bg-indigo-600 px-6 py-2 text-sm font-medium text-white hover:bg-indigo-700">
          Next
        </button>
      </div>
    </div>
  );
}
