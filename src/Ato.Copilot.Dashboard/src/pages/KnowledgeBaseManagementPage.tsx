/**
 * Issue #202 — Knowledge Base Management Page (full CRUD).
 *
 * Wraps the read-only table (from Feature #134) with:
 *   - Add Document drawer/modal
 *   - Edit mode in the detail drawer
 *   - Delete (soft) with confirmation
 *   - Restore button for inactive documents
 *   - "Show inactive" toggle
 *   - PageLayout + PageHero consistent with other admin pages
 */
import { useState, useEffect, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import {
  listOverlayDocuments,
  createOverlayDocument,
  updateOverlayDocument,
  deleteOverlayDocument,
  type OverlayDocumentDto,
  type CreateOverlayDocumentRequest,
  type UpdateOverlayDocumentRequest,
} from '../api/overlayDocuments';

const OVERLAY_TYPES = ['CNSSI-1253', 'SECNAVINST', 'OPNAVINST', 'DoD-8140', 'DoD-8570'] as const;

// ─── Add Document form state ───────────────────────────────────────────────
interface AddFormState {
  type: string;
  title: string;
  controlId: string;
  content: string;
  sourceReference: string;
  tenantId: string;
}

const blankAddForm = (): AddFormState => ({
  type: OVERLAY_TYPES[0],
  title: '',
  controlId: '',
  content: '',
  sourceReference: '',
  tenantId: '',
});

// ─── Edit form state ──────────────────────────────────────────────────────
interface EditFormState {
  title: string;
  content: string;
  sourceReference: string;
}

export default function KnowledgeBaseManagementPage() {
  const [docs, setDocs] = useState<OverlayDocumentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [filterType, setFilterType] = useState('');
  const [filterControl, setFilterControl] = useState('');
  const [showInactive, setShowInactive] = useState(false);

  // Detail drawer
  const [selected, setSelected] = useState<OverlayDocumentDto | null>(null);

  // Edit mode inside drawer
  const [editMode, setEditMode] = useState(false);
  const [editForm, setEditForm] = useState<EditFormState>({ title: '', content: '', sourceReference: '' });
  const [editSaving, setEditSaving] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  // Delete confirmation
  const [deleteConfirm, setDeleteConfirm] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // Add drawer
  const [addOpen, setAddOpen] = useState(false);
  const [addForm, setAddForm] = useState<AddFormState>(blankAddForm());
  const [addSaving, setAddSaving] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  // ── Fetch ────────────────────────────────────────────────────────────────
  const fetchDocs = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listOverlayDocuments({
        type: filterType || undefined,
        controlId: filterControl || undefined,
        includeInactive: showInactive || undefined,
      });
      setDocs(data);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load overlay documents');
    } finally {
      setLoading(false);
    }
  }, [filterType, filterControl, showInactive]);

  useEffect(() => { void fetchDocs(); }, [fetchDocs]);

  // ── Open detail drawer ───────────────────────────────────────────────────
  const openDoc = (doc: OverlayDocumentDto) => {
    setSelected(doc);
    setEditMode(false);
    setEditForm({ title: doc.title, content: doc.content, sourceReference: doc.sourceReference ?? '' });
    setEditError(null);
    setDeleteConfirm(false);
  };

  const closeDrawer = () => {
    setSelected(null);
    setEditMode(false);
    setDeleteConfirm(false);
    setEditError(null);
  };

  // ── Edit ─────────────────────────────────────────────────────────────────
  const handleEditSave = async () => {
    if (!selected) return;
    setEditSaving(true);
    setEditError(null);
    try {
      const req: UpdateOverlayDocumentRequest = {
        title: editForm.title,
        content: editForm.content,
        sourceReference: editForm.sourceReference || undefined,
      };
      const updated = await updateOverlayDocument(selected.id, req);
      setSelected(updated);
      setEditMode(false);
      await fetchDocs();
    } catch (e: unknown) {
      setEditError((e as Error).message ?? 'Save failed');
    } finally {
      setEditSaving(false);
    }
  };

  // ── Delete (soft) ─────────────────────────────────────────────────────────
  const handleDelete = async () => {
    if (!selected) return;
    setDeleting(true);
    try {
      await deleteOverlayDocument(selected.id);
      closeDrawer();
      await fetchDocs();
    } catch (e: unknown) {
      setEditError((e as Error).message ?? 'Delete failed');
    } finally {
      setDeleting(false);
    }
  };

  // ── Restore ───────────────────────────────────────────────────────────────
  const handleRestore = async () => {
    if (!selected) return;
    setEditSaving(true);
    setEditError(null);
    try {
      const updated = await updateOverlayDocument(selected.id, { isActive: true });
      setSelected(updated);
      await fetchDocs();
    } catch (e: unknown) {
      setEditError((e as Error).message ?? 'Restore failed');
    } finally {
      setEditSaving(false);
    }
  };

  // ── Create ────────────────────────────────────────────────────────────────
  const handleAddSave = async () => {
    setAddSaving(true);
    setAddError(null);
    try {
      const req: CreateOverlayDocumentRequest = {
        type: addForm.type,
        title: addForm.title,
        controlId: addForm.controlId,
        content: addForm.content,
        sourceReference: addForm.sourceReference || undefined,
        tenantId: addForm.tenantId || undefined,
      };
      await createOverlayDocument(req);
      setAddOpen(false);
      setAddForm(blankAddForm());
      await fetchDocs();
    } catch (e: unknown) {
      setAddError((e as Error).message ?? 'Create failed');
    } finally {
      setAddSaving(false);
    }
  };

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <PageLayout title="Knowledge Base Management">
      <PageHero
        eyebrow="Administration"
        title="Knowledge Base Management"
        description="Manage NIST 800-53 overlay documents: Navy Instructions, DoD Directives, CNSSI-1253 NSS overlays."
        actions={
          <button
            onClick={() => { setAddOpen(true); setAddError(null); setAddForm(blankAddForm()); }}
            className="inline-flex items-center gap-2 rounded-lg bg-white/20 hover:bg-white/30 px-4 py-2 text-sm font-medium text-white ring-1 ring-white/40 transition-colors"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
            </svg>
            Add Document
          </button>
        }
      />

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3 mb-4">
        <select
          value={filterType}
          onChange={e => setFilterType(e.target.value)}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
        >
          <option value="">All Types</option>
          {OVERLAY_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
        </select>
        <input
          type="text"
          placeholder="Filter by control ID (e.g. AC-1)"
          value={filterControl}
          onChange={e => setFilterControl(e.target.value.toUpperCase())}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 w-56"
        />
        <button
          onClick={() => void fetchDocs()}
          className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
        >
          Apply
        </button>
        {/* Show inactive toggle */}
        <label className="ml-auto flex items-center gap-2 cursor-pointer select-none text-sm text-gray-600">
          <span
            role="switch"
            aria-checked={showInactive}
            tabIndex={0}
            onClick={() => setShowInactive(v => !v)}
            onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') setShowInactive(v => !v); }}
            className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 ${showInactive ? 'bg-indigo-600' : 'bg-gray-300'}`}
          >
            <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform ${showInactive ? 'translate-x-4' : 'translate-x-0.5'}`} />
          </span>
          Show inactive
        </label>
      </div>

      {/* Error banner */}
      {error && (
        <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-700 mb-4">{error}</div>
      )}

      {/* Table */}
      {loading ? (
        <div className="flex items-center gap-2 text-sm text-gray-500 py-8">
          <span className="h-4 w-4 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent" />
          Loading overlay documents…
        </div>
      ) : docs.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 p-8 text-center">
          <p className="text-sm text-gray-400">
            No overlay documents found.{' '}
            <button
              className="text-indigo-600 hover:underline"
              onClick={() => { setAddOpen(true); setAddError(null); setAddForm(blankAddForm()); }}
            >
              Add the first one
            </button>
            .
          </p>
        </div>
      ) : (
        <div className="overflow-hidden rounded-xl border border-gray-200 shadow-sm">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Control</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Type</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Title</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Source</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 bg-white">
              {docs.map(doc => (
                <tr
                  key={doc.id}
                  onClick={() => openDoc(doc)}
                  className={`cursor-pointer hover:bg-indigo-50 transition-colors ${!doc.isActive ? 'opacity-60' : ''}`}
                >
                  <td className="px-4 py-3 font-mono text-indigo-700">{doc.controlId}</td>
                  <td className="px-4 py-3">
                    <span className="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-800">
                      {doc.type}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-gray-900">{doc.title}</td>
                  <td className="px-4 py-3 text-gray-500 text-xs">{doc.sourceReference ?? '—'}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                      doc.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'
                    }`}>
                      {doc.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── Detail / Edit Drawer ─────────────────────────────────────────── */}
      {selected && (
        <div className="fixed inset-0 z-40 bg-black/30" onClick={closeDrawer}>
          <aside
            className="absolute right-0 top-0 h-full w-full max-w-lg bg-white shadow-2xl overflow-y-auto"
            onClick={e => e.stopPropagation()}
          >
            {/* Drawer header */}
            <div className="flex items-center justify-between border-b border-gray-200 px-5 py-4">
              <h2 className="text-base font-semibold text-gray-900 truncate">
                {editMode ? 'Edit Document' : selected.title}
              </h2>
              <button onClick={closeDrawer} className="text-gray-400 hover:text-gray-600 text-xl leading-none">×</button>
            </div>

            <div className="p-5 space-y-4">
              {/* Status + meta chips */}
              {!editMode && (
                <div className="flex flex-wrap gap-2 text-sm">
                  <span className="font-mono text-indigo-700 bg-indigo-50 px-2 py-1 rounded">{selected.controlId}</span>
                  <span className="bg-blue-100 text-blue-800 px-2 py-1 rounded text-xs font-medium">{selected.type}</span>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${selected.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
                    {selected.isActive ? 'Active' : 'Inactive'}
                  </span>
                </div>
              )}

              {/* View mode */}
              {!editMode && (
                <>
                  {selected.sourceReference && (
                    <p className="text-xs text-gray-500"><strong>Source:</strong> {selected.sourceReference}</p>
                  )}
                  <div className="rounded-lg bg-gray-50 border border-gray-200 p-4">
                    <p className="text-sm text-gray-700 whitespace-pre-wrap">{selected.content}</p>
                  </div>
                  <p className="text-xs text-gray-400">
                    Added by {selected.createdBy} · {new Date(selected.createdAt).toLocaleDateString()}
                    {selected.modifiedAt && ` · Edited ${new Date(selected.modifiedAt).toLocaleDateString()}`}
                  </p>
                </>
              )}

              {/* Edit mode form */}
              {editMode && (
                <div className="space-y-3">
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">Title</label>
                    <input
                      type="text"
                      value={editForm.title}
                      onChange={e => setEditForm(f => ({ ...f, title: e.target.value }))}
                      className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">Source Reference</label>
                    <input
                      type="text"
                      value={editForm.sourceReference}
                      onChange={e => setEditForm(f => ({ ...f, sourceReference: e.target.value }))}
                      placeholder="e.g. SECNAVINST 5239.3C Para 5.a"
                      className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">Content</label>
                    <textarea
                      value={editForm.content}
                      onChange={e => setEditForm(f => ({ ...f, content: e.target.value }))}
                      rows={8}
                      className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 font-mono"
                    />
                  </div>
                  {editError && (
                    <div className="rounded-lg bg-red-50 border border-red-200 p-2 text-xs text-red-700">{editError}</div>
                  )}
                </div>
              )}

              {/* Action buttons */}
              <div className="flex flex-wrap gap-2 pt-2">
                {!editMode ? (
                  <>
                    <button
                      onClick={() => { setEditMode(true); setEditError(null); }}
                      className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
                    >
                      Edit
                    </button>
                    {selected.isActive ? (
                      <button
                        onClick={() => setDeleteConfirm(true)}
                        className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700"
                      >
                        Delete
                      </button>
                    ) : (
                      <button
                        onClick={() => void handleRestore()}
                        disabled={editSaving}
                        className="rounded-lg bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
                      >
                        {editSaving ? 'Restoring…' : 'Restore'}
                      </button>
                    )}
                  </>
                ) : (
                  <>
                    <button
                      onClick={() => void handleEditSave()}
                      disabled={editSaving}
                      className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                      {editSaving && <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white border-t-transparent" />}
                      {editSaving ? 'Saving…' : 'Save Changes'}
                    </button>
                    <button
                      onClick={() => { setEditMode(false); setEditError(null); }}
                      className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                    >
                      Cancel
                    </button>
                  </>
                )}
              </div>

              {/* Delete confirmation */}
              {deleteConfirm && !editMode && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-4 space-y-3">
                  <p className="text-sm text-red-800 font-medium">Deactivate this document?</p>
                  <p className="text-xs text-red-700">
                    The document will be marked inactive and hidden from the knowledge base.
                    It can be restored later using the Restore button.
                  </p>
                  {editError && (
                    <div className="text-xs text-red-700">{editError}</div>
                  )}
                  <div className="flex gap-2">
                    <button
                      onClick={() => void handleDelete()}
                      disabled={deleting}
                      className="inline-flex items-center gap-2 rounded-lg bg-red-700 px-4 py-2 text-sm font-medium text-white hover:bg-red-800 disabled:opacity-50"
                    >
                      {deleting && <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white border-t-transparent" />}
                      {deleting ? 'Deleting…' : 'Confirm Delete'}
                    </button>
                    <button
                      onClick={() => setDeleteConfirm(false)}
                      className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              )}
            </div>
          </aside>
        </div>
      )}

      {/* ── Add Document Drawer ──────────────────────────────────────────── */}
      {addOpen && (
        <div className="fixed inset-0 z-50 bg-black/30" onClick={() => setAddOpen(false)}>
          <aside
            className="absolute right-0 top-0 h-full w-full max-w-lg bg-white shadow-2xl overflow-y-auto"
            onClick={e => e.stopPropagation()}
          >
            {/* Drawer header */}
            <div className="flex items-center justify-between border-b border-gray-200 px-5 py-4">
              <h2 className="text-base font-semibold text-gray-900">Add Overlay Document</h2>
              <button onClick={() => setAddOpen(false)} className="text-gray-400 hover:text-gray-600 text-xl leading-none">×</button>
            </div>

            <div className="p-5 space-y-4">
              {/* Type */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Overlay Type <span className="text-red-500">*</span>
                </label>
                <select
                  value={addForm.type}
                  onChange={e => setAddForm(f => ({ ...f, type: e.target.value }))}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                >
                  {OVERLAY_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                </select>
              </div>

              {/* Title */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Title <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  value={addForm.title}
                  onChange={e => setAddForm(f => ({ ...f, title: e.target.value }))}
                  placeholder="e.g. CNSSI-1253 SC Family Overlay"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
              </div>

              {/* Control ID */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Control ID <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  value={addForm.controlId}
                  onChange={e => setAddForm(f => ({ ...f, controlId: e.target.value.toUpperCase() }))}
                  placeholder="e.g. AC-1"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
              </div>

              {/* Content */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Content <span className="text-red-500">*</span>
                </label>
                <textarea
                  value={addForm.content}
                  onChange={e => setAddForm(f => ({ ...f, content: e.target.value }))}
                  rows={8}
                  placeholder="Overlay guidance text (plain text or Markdown)…"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 font-mono"
                />
              </div>

              {/* Source Reference */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Source Reference</label>
                <input
                  type="text"
                  value={addForm.sourceReference}
                  onChange={e => setAddForm(f => ({ ...f, sourceReference: e.target.value }))}
                  placeholder="e.g. CNSSI No. 1253 Annex D, SC Family"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
              </div>

              {/* Tenant ID (optional) */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Tenant ID <span className="text-gray-400">(optional — leave blank for platform-wide)</span>
                </label>
                <input
                  type="text"
                  value={addForm.tenantId}
                  onChange={e => setAddForm(f => ({ ...f, tenantId: e.target.value }))}
                  placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                />
              </div>

              {addError && (
                <div className="rounded-lg bg-red-50 border border-red-200 p-2 text-xs text-red-700">{addError}</div>
              )}

              {/* Actions */}
              <div className="flex gap-2 pt-2">
                <button
                  onClick={() => void handleAddSave()}
                  disabled={addSaving || !addForm.title || !addForm.controlId || !addForm.content}
                  className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {addSaving && <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white border-t-transparent" />}
                  {addSaving ? 'Saving…' : 'Add Document'}
                </button>
                <button
                  onClick={() => setAddOpen(false)}
                  className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
                >
                  Cancel
                </button>
              </div>
            </div>
          </aside>
        </div>
      )}
    </PageLayout>
  );
}
