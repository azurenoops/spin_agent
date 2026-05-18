import { useEffect, useState, type FormEvent, type ReactElement, type ReactNode } from 'react';
import {
  addCspInheritedCapability,
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

// NIST 800-53 Rev 5 family codes — kept in sync with
// `src/components/forms/CapabilityForm.tsx` so the CSP-inherited capability
// form has visual parity with the org-level form.
const NIST_FAMILIES: Record<string, string> = {
  AC: 'Access Control',
  AT: 'Awareness and Training',
  AU: 'Audit and Accountability',
  CA: 'Assessment, Authorization, and Monitoring',
  CM: 'Configuration Management',
  CP: 'Contingency Planning',
  IA: 'Identification and Authentication',
  IR: 'Incident Response',
  MA: 'Maintenance',
  MP: 'Media Protection',
  PE: 'Physical and Environmental Protection',
  PL: 'Planning',
  PM: 'Program Management',
  PS: 'Personnel Security',
  PT: 'PII Processing and Transparency',
  RA: 'Risk Assessment',
  SA: 'System and Services Acquisition',
  SC: 'System and Communications Protection',
  SI: 'System and Information Integrity',
  SR: 'Supply Chain Risk Management',
};

// Visual parity with `CapabilityForm`'s Status dropdown. CSP-inherited
// capabilities have a different lifecycle (`Mapped` / `NeedsReview` set by
// the AI pipeline or the review flow), so this status is display-only.
const CSP_CAP_STATUS_OPTIONS = ['Planned', 'InProgress', 'Implemented', 'Deprecated'] as const;

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

  // Add-capability form state
  const [addingCap, setAddingCap] = useState(false);
  const [capName, setCapName] = useState('');
  const [capDescription, setCapDescription] = useState('');
  const [capControls, setCapControls] = useState('');
  const [capError, setCapError] = useState<string | null>(null);
  const [capSubmitting, setCapSubmitting] = useState(false);

  const resetCapForm = () => {
    setCapName('');
    setCapDescription('');
    setCapControls('');
    setCapError(null);
  };

  const handleAddCapability = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setCapError(null);
    const ids = capControls
      .split(/[,\s]+/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
    if (!capName.trim() || !capDescription.trim()) {
      setCapError('Name and description are required.');
      return;
    }
    if (ids.length === 0) {
      setCapError('Provide at least one NIST control ID (e.g. AC-2, AC-2(1)).');
      return;
    }
    setCapSubmitting(true);
    try {
      await addCspInheritedCapability(componentId, {
        name: capName.trim(),
        description: capDescription.trim(),
        mappedNistControlIds: ids,
      });
      // Refresh both: counts on the parent component row and the
      // capability list.
      try {
        const refreshed = await getCspInheritedComponent(componentId);
        setComponent(refreshed);
      } catch {
        // best-effort — the parent reload below will pick it up.
      }
      await refreshCapabilities();
      resetCapForm();
      setAddingCap(false);
      onMutated();
    } catch (err) {
      const ex = err as { errorCode?: string; message?: string };
      setCapError(ex?.message ?? 'Failed to add capability.');
    } finally {
      setCapSubmitting(false);
    }
  };

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
                <button
                  type="button"
                  onClick={() => {
                    resetCapForm();
                    setAddingCap((prev) => !prev);
                  }}
                  disabled={busy}
                  className="rounded-md border border-emerald-300 bg-white px-3 py-1.5 text-xs font-medium text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                  data-testid="csp-add-capability-toggle"
                >
                  {addingCap ? 'Cancel add capability' : '+ Add capability'}
                </button>
              </div>
            )}

            {/* Add-capability form (CSP-Admin only) — visually mirrors the
                org-level `CapabilityForm` (Name, Provider, Category,
                Description, Status, Owner) with one CSP-specific override:
                a Mapped-NIST-Control-IDs text input. The CSP entity only
                persists `Name`, `Description`, and `MappedNistControlIds`;
                Provider / Owner / Status / Category are present for layout
                parity and disabled with a "Not stored at CSP scope" helper
                line so a future schema extension can light them up without
                UI churn. */}
            {canManage && addingCap && (
              <form
                onSubmit={handleAddCapability}
                className="space-y-3 rounded-md border border-emerald-200 bg-emerald-50 p-3"
                data-testid="csp-add-capability-form"
              >
                <h3 className="text-sm font-semibold text-gray-900">Add capability</h3>
                {capError && (
                  <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-2 py-1 text-xs text-red-700">
                    {capError}
                  </div>
                )}

                {/* Name (active) */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Name *</label>
                  <input
                    type="text"
                    value={capName}
                    onChange={(e) => setCapName(e.target.value)}
                    maxLength={256}
                    required
                    placeholder="e.g., Multi-Factor Authentication"
                    className="block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500"
                    data-testid="csp-add-capability-name"
                  />
                </div>

                {/* Provider (visual parity, disabled) */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-1">Provider</label>
                  <input
                    type="text"
                    value=""
                    disabled
                    aria-disabled
                    placeholder="—"
                    className="block w-full rounded-md border border-gray-200 bg-gray-100 px-2 py-1.5 text-sm text-gray-500 cursor-not-allowed"
                  />
                  <p className="mt-0.5 text-[11px] text-gray-400">
                    Not stored at CSP scope — display-only for layout parity.
                  </p>
                </div>

                {/* Category (visual parity, disabled) */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-1">Category</label>
                  <select
                    value=""
                    disabled
                    aria-disabled
                    className="block w-full rounded-md border border-gray-200 bg-gray-100 px-2 py-1.5 text-sm text-gray-500 cursor-not-allowed"
                  >
                    <option value="">Select a NIST family…</option>
                    {Object.entries(NIST_FAMILIES).map(([code, label]) => (
                      <option key={code} value={code}>{code} — {label}</option>
                    ))}
                  </select>
                  <p className="mt-0.5 text-[11px] text-gray-400">
                    Use the Mapped NIST control IDs field below instead — that
                    is the field actually persisted on a CSP capability.
                  </p>
                </div>

                {/* Description (active) */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Description *</label>
                  <textarea
                    value={capDescription}
                    onChange={(e) => setCapDescription(e.target.value)}
                    rows={3}
                    maxLength={2000}
                    required
                    placeholder="Describe how this capability works…"
                    className="block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500"
                    data-testid="csp-add-capability-description"
                  />
                </div>

                {/* Mapped NIST control IDs (active, CSP-specific) */}
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Mapped NIST control IDs *</label>
                  <input
                    type="text"
                    value={capControls}
                    onChange={(e) => setCapControls(e.target.value)}
                    placeholder="AC-2, AC-2(1), SC-7"
                    required
                    className="block w-full rounded-md border border-gray-300 px-2 py-1.5 font-mono text-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500"
                    data-testid="csp-add-capability-controls"
                  />
                  <span className="mt-0.5 block text-[11px] text-gray-500">
                    Comma- or space-separated. The capability is recorded as
                    a human mapping (mappedBy=User) and will survive a future
                    AI remap.
                  </span>
                </div>

                {/* Status / Owner (visual parity, disabled) */}
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-medium text-gray-400 mb-1">Status</label>
                    <select
                      value="Implemented"
                      disabled
                      aria-disabled
                      className="block w-full rounded-md border border-gray-200 bg-gray-100 px-2 py-1.5 text-sm text-gray-500 cursor-not-allowed"
                    >
                      {CSP_CAP_STATUS_OPTIONS.map((s) => (
                        <option key={s} value={s}>{s}</option>
                      ))}
                    </select>
                    <p className="mt-0.5 text-[11px] text-gray-400">
                      CSP capabilities use Mapped/NeedsReview.
                    </p>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-400 mb-1">Owner</label>
                    <input
                      type="text"
                      value=""
                      disabled
                      aria-disabled
                      placeholder="—"
                      className="block w-full rounded-md border border-gray-200 bg-gray-100 px-2 py-1.5 text-sm text-gray-500 cursor-not-allowed"
                    />
                    <p className="mt-0.5 text-[11px] text-gray-400">
                      Not stored at CSP scope.
                    </p>
                  </div>
                </div>

                <div className="flex justify-end gap-2 pt-1">
                  <button
                    type="button"
                    onClick={() => {
                      resetCapForm();
                      setAddingCap(false);
                    }}
                    disabled={capSubmitting}
                    className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={capSubmitting}
                    className="rounded-md bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-emerald-300"
                    data-testid="csp-add-capability-submit"
                  >
                    {capSubmitting ? 'Adding…' : 'Add capability'}
                  </button>
                </div>
              </form>
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
