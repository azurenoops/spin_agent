import { useState, useCallback } from 'react';
import { Link, useParams } from '../features/workspaces/workspaceNavigation';
import { usePolling } from '../hooks/usePolling';
import { useWorkspaceSession } from '../features/workspaces/WorkspaceBoundary';
import { useSystemMutationPermission } from '../components/permissions/useSystemMutationPermission';
import { SystemTaskHeading, systemPrimaryAction, systemSecondaryAction } from '../features/systems/SystemTaskPresentation';
import {
  getComponents,
  listComponents,
  createOrgComponent,
  assignToSystem,
  removeAssignment,
  type OrgComponentDto,
} from '../api/components';
import type { CreateComponentRequest, SystemComponentDto } from '../types/dashboard';

const COMMON_POLICIES = [
  { name: 'FISMA 2014', description: 'Federal Information Security Modernization Act — requires federal agencies to implement information security programs.' },
  { name: 'Privacy Act of 1974', description: 'Governs the collection, maintenance, use, and dissemination of personally identifiable information by federal agencies.' },
  { name: 'E-Government Act of 2002', description: 'Requires federal agencies to conduct privacy impact assessments for electronic information systems.' },
  { name: 'OMB Circular A-130', description: 'Managing Information as a Strategic Resource — establishes policy for the planning, budgeting, governance, and security of federal information resources.' },
  { name: 'HIPAA', description: 'Health Insurance Portability and Accountability Act — sets standards for the protection of health information.' },
  { name: 'FIPS 199', description: 'Standards for Security Categorization of Federal Information and Information Systems.' },
  { name: 'FIPS 200', description: 'Minimum Security Requirements for Federal Information and Information Systems.' },
  { name: 'NIST SP 800-53 Rev 5', description: 'Security and Privacy Controls for Information Systems and Organizations.' },
  { name: 'NIST SP 800-37 Rev 2', description: 'Risk Management Framework for Information Systems and Organizations.' },
  { name: 'FedRAMP Authorization Act', description: 'Codifies the Federal Risk and Authorization Management Program for cloud security assessment.' },
];

export default function LegalRegulatory() {
  const { id: systemId } = useParams<{ id: string }>();
  const canManage = useSystemMutationPermission(systemId, 'canManageSystem');
  const workspace = useWorkspaceSession();
  const canCreate = canManage && workspace?.workspace.permissions.canManageOrganization === true;
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [showAssign, setShowAssign] = useState(false);
  const [removeConfirm, setRemoveConfirm] = useState<{ componentId: string; name: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [showQuickAdd, setShowQuickAdd] = useState(false);

  // Form fields for new items
  const [formName, setFormName] = useState('');
  const [formDescription, setFormDescription] = useState('');
  const [formSubType, setFormSubType] = useState('');

  // Org library items for "Assign Existing"
  const [orgItems, setOrgItems] = useState<OrgComponentDto[]>([]);
  const [orgSearch, setOrgSearch] = useState('');
  const [loadingOrg, setLoadingOrg] = useState(false);
  const [orgError, setOrgError] = useState<string | null>(null);
  const message = (reason: unknown) => reason instanceof Error ? reason.message
    : reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string'
      ? reason.error : 'The policy operation failed. Review the current assignment and retry.';
  const requireManagement = () => {
    if (canManage) return true;
    setFormError('Your current system permission does not allow policy assignments.');
    return false;
  };

  // System-scoped: only policies assigned to THIS system
  const fetcher = useCallback(
    () => systemId
      ? getComponents(systemId, { type: 'Policy', search: search || undefined, pageSize: 200 })
      : Promise.reject('No systemId'),
    [systemId, search],
  );
  const { data, loading, error, refresh } = usePolling<{ systemId: string; items: SystemComponentDto[]; totalCount: number }>(fetcher, 30000);
  const items = data && data.systemId === systemId ? data.items : [];

  const resetForm = () => {
    setFormName('');
    setFormDescription('');
    setFormSubType('');
    setFormError(null);
  };

  const openCreate = () => {
    if (!canCreate) { setFormError('Organization-management permission is required to create a library policy.'); return; }
    resetForm();
    setShowCreate(true);
  };

  // Create org-wide policy and assign to this system
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!requireManagement() || !canCreate || submitting) return;
    if (!formName.trim() || !systemId) { setFormError('Name is required'); return; }
    setSubmitting(true);
    setFormError(null);
    const request: CreateComponentRequest = {
      name: formName.trim(),
      componentType: 'Policy',
      subType: formSubType.trim() || undefined,
      description: formDescription.trim() || undefined,
      status: 'Active',
    };
    try {
      const orgComp = await createOrgComponent(request);
      await assignToSystem(orgComp.id, { registeredSystemId: systemId });
      setShowCreate(false);
      resetForm();
      refresh();
    } catch (err: unknown) {
      setFormError(message(err));
    } finally {
      setSubmitting(false);
    }
  };

  // Remove assignment from this system (not the org-wide component)
  const handleRemoveAssignment = async (componentId: string) => {
    if (!systemId || !requireManagement() || submitting) return;
    setSubmitting(true);
    setFormError(null);
    try {
      // Find the assignment for this system
      const orgData = await listComponents({ type: 'Policy', pageSize: 200 });
      const comp = orgData.items.find(c => c.id === componentId);
      const assignment = comp?.systemAssignments?.find(a => a.registeredSystemId === systemId);
      if (!assignment) throw new Error('The current policy assignment was not returned. Refresh the source library before removing it.');
      await removeAssignment(componentId, assignment.id);
      setRemoveConfirm(null);
      refresh();
    } catch (reason) { setFormError(message(reason)); }
    finally { setSubmitting(false); }
  };

  // Quick Add: create org-wide (if not exists) and assign to system
  const handleQuickAdd = async (policy: typeof COMMON_POLICIES[number]) => {
    if (!systemId || !requireManagement() || submitting) return;
    setSubmitting(true);
    setFormError(null);
    try {
      // Check org-wide library first
      const orgData = await listComponents({ type: 'Policy', search: policy.name, pageSize: 10 });
      let orgComp = orgData.items.find(c => c.name === policy.name);
      if (!orgComp) {
        if (!canCreate) throw new Error('This policy is not in the library. An organization administrator must create it before assignment.');
        orgComp = await createOrgComponent({
          name: policy.name,
          componentType: 'Policy',
          description: policy.description,
          status: 'Active',
        });
      }
      await assignToSystem(orgComp.id, { registeredSystemId: systemId });
      refresh();
    } catch (reason) { setFormError(message(reason)); }
    finally { setSubmitting(false); }
  };

  // Open assign-from-library dialog
  const openAssignExisting = async () => {
    if (!requireManagement()) return;
    setShowAssign(true);
    setLoadingOrg(true);
    setOrgSearch('');
    setOrgError(null);
    setFormError(null);
    try {
      const orgData = await listComponents({ type: 'Policy', pageSize: 200 });
      setOrgItems(orgData.items);
    } catch (reason) { setOrgItems([]); setOrgError(message(reason)); }
    finally { setLoadingOrg(false); }
  };

  const handleAssignExisting = async (comp: OrgComponentDto) => {
    if (!systemId || !requireManagement() || submitting) return;
    setSubmitting(true);
    setFormError(null);
    try {
      await assignToSystem(comp.id, { registeredSystemId: systemId });
      refresh();
      // Refresh org list to update assignment state
      const orgData = await listComponents({ type: 'Policy', pageSize: 200 });
      setOrgItems(orgData.items);
    } catch (reason) { setFormError(message(reason)); }
    finally { setSubmitting(false); }
  };

  const assignedIds = new Set(items.map((i) => i.id));
  const existingNames = new Set(items.map((i) => i.name));

  return (
    <div className="space-y-6">
      {/* Header */}
      <SystemTaskHeading title="Applicable policies & references"
        description="Review source policies and record why they apply to this system."
        action={<button type="button" disabled={!canManage} onClick={() => void openAssignExisting()}
          className={systemPrimaryAction}>Add policy reference</button>} />
      <section className="rounded-[10px] border border-slate-200 bg-white p-5 dark:border-slate-700 dark:bg-slate-900">
        <h2 className="text-lg font-semibold">System policy references</h2>
        <p className="mt-2 text-sm text-slate-500">Assignments link retained organization-library sources to this system. A source marked Active is not a separate approval of system applicability.</p>
        <div className="mt-4 flex flex-wrap gap-2">
          <button
            type="button"
            disabled={!canManage}
            onClick={() => setShowQuickAdd(!showQuickAdd)}
            className="inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Quick Add Common
          </button>
          <button
            type="button"
            disabled={!canManage}
            onClick={() => void openAssignExisting()}
            className="inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Assign Existing
          </button>
          <button
            type="button"
            onClick={openCreate}
            disabled={!canCreate}
            className="inline-flex items-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
          >
            Create library policy
          </button>
        </div>
        <Link className="mt-4 inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/narratives`}>Review related implementation narratives</Link>
      </section>
      {formError && <p role="alert" className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">{formError}</p>}
      {error && <div className="rounded-lg border border-amber-200 p-4 text-sm"><p role="alert">{message(error)}</p>
        <button type="button" className={`mt-3 ${systemSecondaryAction}`} onClick={refresh}>Retry policy references</button></div>}
      {loading && <p role="status">Loading system policy references…</p>}

      {/* Quick Add Panel */}
      {showQuickAdd && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
          <h3 className="text-sm font-semibold text-amber-800 mb-2">Common Laws &amp; Regulations</h3>
          <p className="text-xs text-amber-600 mb-3">Click to add items not already in your list.</p>
          <div className="flex flex-wrap gap-2">
            {COMMON_POLICIES.map((p) => {
              const alreadyAdded = existingNames.has(p.name);
              return (
                <button
                  key={p.name}
                  type="button"
                  disabled={!canManage || alreadyAdded || submitting}
                  onClick={() => handleQuickAdd(p)}
                  className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                    alreadyAdded
                      ? 'border-green-300 bg-green-50 text-green-600 cursor-default'
                      : 'border-amber-300 bg-white text-amber-700 hover:bg-amber-100 cursor-pointer'
                  }`}
                >
                  {alreadyAdded ? '✓ ' : '+ '}{p.name}
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* Search */}
      <div className="flex gap-3">
        <input
          aria-label="Search system policy references"
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search policies..."
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
        <span className="self-center text-sm text-gray-500">
          {items.length} item{items.length !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Table */}
      <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Name</th>
              <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Category</th>
              <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Description</th>
              <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Status</th>
              <th className="px-6 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {items.map((item) => (
              <tr key={item.id} className="hover:bg-gray-50">
                <td className="whitespace-nowrap px-6 py-4 text-sm font-medium text-gray-900">{item.name}</td>
                <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-500">{item.subType || '—'}</td>
                <td className="px-6 py-4 text-sm text-gray-500 max-w-md truncate">{item.description || '—'}</td>
                <td className="whitespace-nowrap px-6 py-4">
                  <span className="inline-flex rounded-full bg-slate-50 px-2 py-0.5 text-xs font-medium text-slate-700 border border-slate-200">
                    {item.status}
                  </span>
                </td>
                <td className="whitespace-nowrap px-6 py-4 text-right text-sm">
                  <button
                    type="button"
                    disabled={!canManage || submitting}
                    onClick={() => setRemoveConfirm({ componentId: item.id, name: item.name })}
                    className="text-red-600 hover:text-red-800"
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
            {!loading && !error && items.length === 0 && (
              <tr>
                <td colSpan={5} className="px-6 py-12 text-center text-sm text-gray-500">
                  No policy references are assigned to this system. Add a reference from the organization library to begin.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Create / Edit Modal */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-lg rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">
              Add Legal &amp; Regulatory Item
            </h3>
            {formError && <p className="mb-3 text-sm text-red-600">{formError}</p>}
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
                <input
                  type="text"
                  value={formName}
                  onChange={(e) => setFormName(e.target.value)}
                  placeholder="e.g., FISMA 2014"
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  autoFocus
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Category</label>
                <select
                  value={formSubType}
                  onChange={(e) => setFormSubType(e.target.value)}
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
                >
                  <option value="">Select category...</option>
                  <option value="Federal Law">Federal Law</option>
                  <option value="Regulation">Regulation</option>
                  <option value="Executive Order">Executive Order</option>
                  <option value="OMB Policy">OMB Policy</option>
                  <option value="NIST Standard">NIST Standard</option>
                  <option value="DoD Policy">DoD Policy</option>
                  <option value="Agency Policy">Agency Policy</option>
                  <option value="Other">Other</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Description / Citation</label>
                <textarea
                  value={formDescription}
                  onChange={(e) => setFormDescription(e.target.value)}
                  rows={3}
                  placeholder="Brief description or legal citation..."
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              </div>
              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => { setShowCreate(false); resetForm(); }}
                  className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                >
                  {submitting ? 'Saving...' : 'Add'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Remove Assignment Confirmation */}
      {removeConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Remove from System?</h3>
            <p className="mb-4 text-sm text-gray-600">
              This will remove <strong>{removeConfirm.name}</strong> from this system. The item will remain in the organization library.
            </p>
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setRemoveConfirm(null)} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
              <button type="button" disabled={!canManage || submitting} onClick={() => void handleRemoveAssignment(removeConfirm.componentId)} className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700">Remove</button>
            </div>
          </div>
        </div>
      )}

      {/* Assign Existing from Org Library */}
      {showAssign && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-lg rounded-lg bg-white p-6 shadow-xl max-h-[80vh] overflow-y-auto">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">Assign from Organization Library</h3>
            {orgError && <div className="mb-3 text-sm text-amber-900"><p>{orgError}</p><button type="button" className="underline" onClick={() => void openAssignExisting()}>Retry organization library</button></div>}
            {formError && <p className="mb-3 text-sm text-amber-900">{formError}</p>}
            <input
              type="text"
              value={orgSearch}
              onChange={(e) => setOrgSearch(e.target.value)}
              placeholder="Search policies..."
              className="mb-4 w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
            {loadingOrg ? (
              <p className="text-sm text-gray-500">Loading...</p>
            ) : (
              <div className="space-y-2">
                {orgItems
                  .filter(c => !orgSearch || c.name.toLowerCase().includes(orgSearch.toLowerCase()))
                  .map((comp) => {
                    const isAssigned = assignedIds.has(comp.id);
                    return (
                      <div key={comp.id} className="flex items-center justify-between rounded-md border border-gray-200 px-3 py-2">
                        <div>
                          <span className="text-sm font-medium text-gray-900">{comp.name}</span>
                          {comp.description && <p className="text-xs text-gray-500 truncate max-w-xs">{comp.description}</p>}
                        </div>
                        {isAssigned ? (
                          <span className="text-xs font-medium text-green-600 bg-green-50 px-2 py-0.5 rounded-full">Assigned ✓</span>
                        ) : (
                          <button
                            type="button"
                            disabled={!canManage || submitting}
                            onClick={() => handleAssignExisting(comp)}
                            className="rounded-md bg-indigo-600 px-3 py-1 text-xs font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                          >
                            Assign
                          </button>
                        )}
                      </div>
                    );
                  })}
                {!orgError && orgItems.filter(c => !orgSearch || c.name.toLowerCase().includes(orgSearch.toLowerCase())).length === 0 && (
                  <p className="text-sm text-gray-500 text-center py-4">No policies found in the organization library.</p>
                )}
              </div>
            )}
            <div className="mt-4 flex justify-end">
              <button
                type="button"
                onClick={() => setShowAssign(false)}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
