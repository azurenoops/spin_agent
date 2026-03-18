import { useState, useCallback } from 'react';
import { usePolling } from '../hooks/usePolling';
import {
  listComponents,
  createOrgComponent,
  updateOrgComponent,
  deleteOrgComponent,
  type OrgComponentDto,
  type OrgComponentListResponse,
} from '../api/components';
import type { CreateComponentRequest } from '../types/dashboard';

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
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [editItem, setEditItem] = useState<OrgComponentDto | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [showQuickAdd, setShowQuickAdd] = useState(false);

  // Form fields
  const [formName, setFormName] = useState('');
  const [formDescription, setFormDescription] = useState('');
  const [formSubType, setFormSubType] = useState('');

  const fetcher = useCallback(
    () => listComponents({ type: 'Policy', search: search || undefined, pageSize: 200 }),
    [search],
  );
  const { data, refresh } = usePolling<OrgComponentListResponse>(fetcher, 30000);
  const items = data?.items ?? [];

  const resetForm = () => {
    setFormName('');
    setFormDescription('');
    setFormSubType('');
    setFormError(null);
  };

  const openCreate = () => {
    resetForm();
    setEditItem(null);
    setShowCreate(true);
  };

  const openEdit = (item: OrgComponentDto) => {
    setFormName(item.name);
    setFormDescription(item.description ?? '');
    setFormSubType(item.subType ?? '');
    setFormError(null);
    setEditItem(item);
    setShowCreate(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formName.trim()) { setFormError('Name is required'); return; }
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
      if (editItem) {
        await updateOrgComponent(editItem.id, request);
      } else {
        await createOrgComponent(request);
      }
      setShowCreate(false);
      resetForm();
      setEditItem(null);
      refresh();
    } catch (err: any) {
      setFormError(err?.response?.data?.error ?? 'Failed to save');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteOrgComponent(id);
      setDeleteConfirm(null);
      refresh();
    } catch { /* ignore */ }
  };

  const handleQuickAdd = async (policy: typeof COMMON_POLICIES[number]) => {
    setSubmitting(true);
    try {
      await createOrgComponent({
        name: policy.name,
        componentType: 'Policy',
        description: policy.description,
        status: 'Active',
      });
      refresh();
    } catch { /* duplicate or error — ignore */ }
    finally { setSubmitting(false); }
  };

  const existingNames = new Set(items.map((i) => i.name));

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">Legal &amp; Regulatory</h2>
          <p className="mt-1 text-sm text-gray-500">
            Laws, regulations, and policies applicable to this system — required for FedRAMP SSP Section 13.
          </p>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setShowQuickAdd(!showQuickAdd)}
            className="inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Quick Add Common
          </button>
          <button
            type="button"
            onClick={openCreate}
            className="inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            + Legal &amp; Regulatory
          </button>
        </div>
      </div>

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
                  disabled={alreadyAdded || submitting}
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
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search policies..."
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
        <span className="self-center text-sm text-gray-500">
          {items.length} item{items.length !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Table */}
      <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
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
                  <span className="inline-flex rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700 border border-green-200">
                    {item.status}
                  </span>
                </td>
                <td className="whitespace-nowrap px-6 py-4 text-right text-sm">
                  <button
                    type="button"
                    onClick={() => openEdit(item)}
                    className="text-blue-600 hover:text-blue-800 mr-3"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => setDeleteConfirm(item.id)}
                    className="text-red-600 hover:text-red-800"
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
            {items.length === 0 && (
              <tr>
                <td colSpan={5} className="px-6 py-12 text-center text-sm text-gray-500">
                  No policies or regulations added yet. Click "+ Legal &amp; Regulatory" or use Quick Add to get started.
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
              {editItem ? 'Edit' : 'Add'} Legal &amp; Regulatory Item
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
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
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
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
              </div>
              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => { setShowCreate(false); setEditItem(null); resetForm(); }}
                  className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
                >
                  {submitting ? 'Saving...' : editItem ? 'Save Changes' : 'Add'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Delete Confirmation */}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Remove Item?</h3>
            <p className="mb-4 text-sm text-gray-600">
              This will remove the law/regulation from the component library.
            </p>
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setDeleteConfirm(null)} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
              <button type="button" onClick={() => handleDelete(deleteConfirm)} className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700">Remove</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
