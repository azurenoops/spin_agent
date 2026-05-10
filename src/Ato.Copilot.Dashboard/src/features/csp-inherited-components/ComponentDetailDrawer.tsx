import { useEffect, useState, type ReactElement, type ReactNode } from 'react';
import {
  archiveCspInheritedComponent,
  getCspInheritedComponent,
  listCspInheritedCapabilities,
  patchCspInheritedComponent,
  publishCspInheritedComponent,
  remapCspInheritedComponent,
  type CspComponentType,
  type CspInheritedCapability,
  type CspInheritedComponent,
} from './api';
import NeedsReviewQueue from './NeedsReviewQueue';

interface Props {
  componentId: string;
  canManage: boolean;
  onClose: () => void;
  /** Called whenever the drawer mutates the component, so the parent
   *  list can refresh. */
  onMutated: () => void;
}

const COMPONENT_TYPES: CspComponentType[] = [
  'Infrastructure',
  'Platform',
  'Service',
  'Identity',
  'Network',
  'Storage',
  'Compute',
];

/**
 * `ComponentDetailDrawer` — Feature 048 / US9 / T214 sub-component.
 *
 * Right-side drawer showing the full record for a single CSP-inherited
 * component plus its capability list. Read-only for non-CSP-Admin
 * callers; CSP-Admins additionally get edit / publish / archive / remap
 * controls and the `NeedsReviewQueue` panel.
 */
export default function ComponentDetailDrawer({
  componentId,
  canManage,
  onClose,
  onMutated,
}: Props): ReactElement {
  const [component, setComponent] = useState<CspInheritedComponent | null>(null);
  const [capabilities, setCapabilities] = useState<CspInheritedCapability[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Edit form state
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editType, setEditType] = useState<CspComponentType>('Service');

  useEffect(() => {
    let cancelled = false;
    setComponent(null);
    setCapabilities(null);
    setError(null);
    void (async () => {
      try {
        const [cmp, caps] = await Promise.all([
          getCspInheritedComponent(componentId),
          listCspInheritedCapabilities(componentId),
        ]);
        if (cancelled) return;
        setComponent(cmp);
        setCapabilities(caps);
        setEditName(cmp.name);
        setEditDescription(cmp.description ?? '');
        setEditType(cmp.componentType);
      } catch (err) {
        const e = err as { errorCode?: string; message?: string };
        if (!cancelled) {
          setError(e?.message ?? 'Failed to load component.');
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [componentId]);

  const refreshCapabilities = async () => {
    try {
      const caps = await listCspInheritedCapabilities(componentId);
      setCapabilities(caps);
    } catch {
      // ignore — surface inline if the parent fetch fails next time
    }
  };

  const handleSaveEdit = async () => {
    if (!component) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await patchCspInheritedComponent(
        component.id,
        {
          name: editName.trim(),
          description: editDescription.trim() || undefined,
          componentType: editType,
        },
        component.rowVersion,
      );
      setComponent(updated);
      setEditing(false);
      onMutated();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(
        e?.errorCode === 'ROW_VERSION_MISMATCH'
          ? 'This record was changed by another user. Reload to see the latest.'
          : (e?.message ?? 'Failed to save changes.'),
      );
    } finally {
      setBusy(false);
    }
  };

  const handlePublish = async () => {
    if (!component) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await publishCspInheritedComponent(component.id);
      setComponent(updated);
      onMutated();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(e?.message ?? 'Failed to publish.');
    } finally {
      setBusy(false);
    }
  };

  const handleArchive = async () => {
    if (!component) return;
    if (!window.confirm(`Archive “${component.name}”?`)) return;
    setBusy(true);
    setError(null);
    try {
      await archiveCspInheritedComponent(component.id);
      onMutated();
      onClose();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(e?.message ?? 'Failed to archive.');
    } finally {
      setBusy(false);
    }
  };

  const handleRemap = async () => {
    if (!component) return;
    setBusy(true);
    setError(null);
    try {
      await remapCspInheritedComponent(component.id);
      await refreshCapabilities();
      onMutated();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(
        e?.errorCode === 'AI_MAPPING_UNAVAILABLE'
          ? 'AI capability mapping service is unavailable. Try again later.'
          : (e?.message ?? 'Failed to remap.'),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <aside
      className="fixed inset-y-0 right-0 z-40 flex w-full max-w-xl flex-col border-l border-gray-200 bg-white shadow-xl"
      aria-label="Component detail"
    >
      {/* Header */}
      <header className="flex items-start justify-between gap-2 border-b border-gray-200 px-4 py-3">
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-base font-semibold text-gray-900">
            {component?.name ?? 'Loading…'}
          </h2>
          {component && (
            <p className="text-xs text-gray-500">
              {component.componentType} · {component.sourceFormat} ·{' '}
              <StatusBadge status={component.status} />
            </p>
          )}
        </div>
        <button
          type="button"
          onClick={onClose}
          className="rounded-md p-1 text-gray-500 hover:bg-gray-100"
          aria-label="Close detail panel"
        >
          ✕
        </button>
      </header>

      {/* Body */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {error && (
          <div role="alert" className="mb-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        {!component ? (
          <p className="text-sm text-gray-500">Loading component…</p>
        ) : (
          <div className="space-y-5">
            {/* Edit form */}
            {editing ? (
              <div className="space-y-3 rounded-md border border-indigo-200 bg-indigo-50 p-3">
                <label className="block text-xs font-medium text-gray-700">
                  Name
                  <input
                    type="text"
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    maxLength={256}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                </label>
                <label className="block text-xs font-medium text-gray-700">
                  Description
                  <textarea
                    value={editDescription}
                    onChange={(e) => setEditDescription(e.target.value)}
                    rows={3}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                </label>
                <label className="block text-xs font-medium text-gray-700">
                  Component type
                  <select
                    value={editType}
                    onChange={(e) => setEditType(e.target.value as CspComponentType)}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  >
                    {COMPONENT_TYPES.map((t) => (
                      <option key={t} value={t}>
                        {t}
                      </option>
                    ))}
                  </select>
                </label>
                <div className="flex justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => setEditing(false)}
                    disabled={busy}
                    className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={handleSaveEdit}
                    disabled={busy || editName.trim().length === 0}
                    className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
                  >
                    {busy ? 'Saving…' : 'Save changes'}
                  </button>
                </div>
              </div>
            ) : (
              <dl className="divide-y divide-gray-100 rounded-md border border-gray-200 text-sm">
                <Row label="Description">
                  {component.description ? (
                    component.description
                  ) : (
                    <em className="text-gray-400">(none)</em>
                  )}
                </Row>
                <Row label="Source file">
                  {component.sourceFileName ?? <em className="text-gray-400">(unknown)</em>}
                </Row>
                <Row label="Imported">
                  {new Date(component.importedAt).toLocaleString()} by{' '}
                  {component.importedBy ?? 'unknown'}
                </Row>
                {component.updatedAt && (
                  <Row label="Last updated">
                    {new Date(component.updatedAt).toLocaleString()} by{' '}
                    {component.updatedBy ?? 'unknown'}
                  </Row>
                )}
                <Row label="Capabilities">
                  {(component.capabilityMappedCount ?? 0)} mapped,{' '}
                  {(component.capabilityNeedsReviewCount ?? 0)} needs review
                </Row>
              </dl>
            )}

            {/* Action toolbar — CSP-Admin only */}
            {canManage && !editing && (
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={() => setEditing(true)}
                  disabled={busy}
                  className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                >
                  Edit
                </button>
                {component.status === 'Draft' && (
                  <button
                    type="button"
                    onClick={handlePublish}
                    disabled={busy}
                    className="rounded-md bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-emerald-300"
                  >
                    {busy ? 'Working…' : 'Publish'}
                  </button>
                )}
                {component.status !== 'Archived' && (
                  <button
                    type="button"
                    onClick={handleArchive}
                    disabled={busy}
                    className="rounded-md border border-red-300 bg-white px-3 py-1.5 text-xs font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
                  >
                    Archive
                  </button>
                )}
                <button
                  type="button"
                  onClick={handleRemap}
                  disabled={busy}
                  className="rounded-md border border-indigo-300 bg-white px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
                  title="Re-run AI capability mapping"
                >
                  Remap capabilities
                </button>
              </div>
            )}

            {/* Capability list */}
            <section>
              <h3 className="text-sm font-semibold text-gray-900">All capabilities</h3>
              {capabilities === null ? (
                <p className="mt-2 text-sm text-gray-500">Loading…</p>
              ) : capabilities.length === 0 ? (
                <p className="mt-2 text-sm text-gray-500">
                  No capabilities mapped to this component yet.
                </p>
              ) : (
                <ul className="mt-2 divide-y divide-gray-100 rounded-md border border-gray-200">
                  {capabilities.map((cap) => (
                    <li key={cap.id} className="px-3 py-2 text-sm">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <p className="font-medium text-gray-900">{cap.name}</p>
                        {cap.status === 'Mapped' ? (
                          <span className="inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
                            Mapped
                          </span>
                        ) : (
                          <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
                            Needs review
                          </span>
                        )}
                      </div>
                      {cap.mappedNistControlIds.length > 0 && (
                        <p className="mt-1 text-xs text-gray-600">
                          Controls: {cap.mappedNistControlIds.join(', ')}
                        </p>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </section>

            {/* Needs-review queue (CSP-Admin) */}
            {canManage && (
              <section>
                <h3 className="text-sm font-semibold text-gray-900">Resolve needs-review</h3>
                <div className="mt-2">
                  <NeedsReviewQueue componentId={component.id} canReview={canManage} />
                </div>
              </section>
            )}
          </div>
        )}
      </div>
    </aside>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactElement {
  return (
    <div className="grid grid-cols-3 gap-3 px-3 py-2">
      <dt className="font-medium text-gray-700">{label}</dt>
      <dd className="col-span-2 text-gray-900">{children}</dd>
    </div>
  );
}

function StatusBadge({ status }: { status: CspInheritedComponent['status'] }): ReactElement {
  const palette =
    status === 'Published'
      ? 'bg-emerald-100 text-emerald-800'
      : status === 'Draft'
        ? 'bg-indigo-100 text-indigo-800'
        : 'bg-gray-200 text-gray-700';
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${palette}`}>
      {status}
    </span>
  );
}
