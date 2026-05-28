import { useEffect, useMemo, useState, type FormEvent, type ReactElement, type ReactNode } from 'react';
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
import CapabilityDetailDrawer from './CapabilityDetailDrawer';
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
  // Feature 050 FR-001 — opt-in override that skips the new
  // vetted-by-default flow and marks the capability Mapped at creation.
  const [capMarkMappedImmediately, setCapMarkMappedImmediately] = useState(false);
  const [capError, setCapError] = useState<string | null>(null);
  const [capSubmitting, setCapSubmitting] = useState(false);

  // Feature 050 / US4 — Advanced disclosure + Remap confirm modal.
  // Collapsed by default per FR-007 acceptance; opening surfaces an
  // explanatory paragraph above the Remap CTA. Clicking Remap opens the
  // FR-008 confirm modal with an acknowledgement checkbox (Continue is
  // disabled until checked) and a default-focused Cancel button.
  const [advancedExpanded, setAdvancedExpanded] = useState(false);
  const [showRemapConfirm, setShowRemapConfirm] = useState(false);
  const [remapAcknowledged, setRemapAcknowledged] = useState(false);
  const [remapReviewerNote, setRemapReviewerNote] = useState('');

  // Linked-capabilities picker state — visual parity with the org
  // `ComponentLibrary` capability picker (search-filterable, scrollable
  // list). The CSP data model is 1:N (each capability has exactly one
  // parent component), so the picker is click-to-open rather than
  // multi-select reparent.
  const [capSearch, setCapSearch] = useState('');
  const [selectedCapabilityId, setSelectedCapabilityId] = useState<string | null>(null);

  const filteredCapabilities = useMemo<CspInheritedCapability[]>(() => {
    if (!capabilities) return [];
    const q = capSearch.trim().toLowerCase();
    if (q.length === 0) return capabilities;
    return capabilities.filter((c) => {
      if (c.name.toLowerCase().includes(q)) return true;
      if (c.mappedNistControlIds.some((id) => id.toLowerCase().includes(q))) return true;
      return false;
    });
  }, [capabilities, capSearch]);

  const resetCapForm = () => {
    setCapName('');
    setCapDescription('');
    setCapControls('');
    setCapMarkMappedImmediately(false);
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
        // Always forward the flag — defaults to false so the new
        // vetted-by-default behavior is what server records when the
        // creator leaves the checkbox unchecked (Feature 050 FR-001).
        markMappedImmediately: capMarkMappedImmediately,
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
      // Feature 050 / US4 — close the confirm modal and collapse the
      // Advanced disclosure on success so the safety gate re-arms before
      // the next click.
      setShowRemapConfirm(false);
      setAdvancedExpanded(false);
      setRemapAcknowledged(false);
      setRemapReviewerNote('');
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
            {/* Edit form — visual parity with the org `ComponentLibrary`
                edit form (Name *, Type * | Status *, Sub-Type, Description,
                Owner). The CSP data model only persists Name / Description
                / ComponentType; Status reflects the CSP lifecycle
                (Draft / Published / Archived, mutated via the Publish and
                Archive buttons), and Sub-Type / Owner are not stored at
                CSP scope — they render disabled with a helper line so a
                future schema extension can light them up without UI churn. */}
            {editing ? (
              <div className="space-y-3 rounded-md border border-indigo-200 bg-indigo-50 p-3">
                {/* Name (active) */}
                <label className="block text-xs font-medium text-gray-700">
                  Name *
                  <input
                    type="text"
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    maxLength={256}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                </label>

                {/* Type | Status (2-col, mirrors the org form's layout) */}
                <div className="grid grid-cols-2 gap-3">
                  <label className="block text-xs font-medium text-gray-700">
                    Type *
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
                  <div>
                    <label className="block text-xs font-medium text-gray-400 mb-1">Status *</label>
                    <select
                      value={component.status}
                      disabled
                      aria-disabled
                      className="block w-full rounded-md border border-gray-200 bg-gray-100 px-2 py-1.5 text-sm text-gray-500 cursor-not-allowed"
                    >
                      <option value="Draft">Draft</option>
                      <option value="Published">Published</option>
                      <option value="Archived">Archived</option>
                    </select>
                    <p className="mt-0.5 text-[11px] text-gray-400">
                      Use Publish / Archive to change.
                    </p>
                  </div>
                </div>

                {/* Sub-Type (visual parity, disabled) */}
                <div>
                  <label className="block text-xs font-medium text-gray-400 mb-1">Sub-Type</label>
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

                {/* Description (active) */}
                <label className="block text-xs font-medium text-gray-700">
                  Description
                  <textarea
                    value={editDescription}
                    onChange={(e) => setEditDescription(e.target.value)}
                    rows={3}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                </label>

                {/* Owner (visual parity, disabled) */}
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
                    Not stored at CSP scope — display-only for layout parity.
                  </p>
                </div>

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
                <Row label="Type">{component.componentType}</Row>
                <Row label="Status">
                  <StatusBadge status={component.status} />
                </Row>
                <Row label="Sub-Type">
                  <em className="text-gray-400">(not stored at CSP scope)</em>
                </Row>
                <Row label="Description">
                  {component.description ? (
                    component.description
                  ) : (
                    <em className="text-gray-400">(none)</em>
                  )}
                </Row>
                <Row label="Owner">
                  <em className="text-gray-400">(not stored at CSP scope)</em>
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

                {/* Feature 050 / US4 — Advanced disclosure (FR-007). Gates
                    Remap behind a deliberate click. Collapsed by default;
                    expanding reveals the FR-007 paragraph above a Remap
                    button that opens the FR-008 confirm modal. */}
                <button
                  type="button"
                  onClick={() => setAdvancedExpanded((prev) => !prev)}
                  disabled={busy}
                  aria-expanded={advancedExpanded}
                  aria-controls="csp-component-advanced-panel"
                  className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  data-testid="csp-component-advanced-toggle"
                >
                  Advanced {advancedExpanded ? '▴' : '▾'}
                </button>
              </div>
            )}

            {/* Advanced disclosure panel — only rendered when expanded so
                the explanatory copy + Remap CTA stay out of the way until
                the CSP-Admin explicitly opts in (FR-007). */}
            {canManage && advancedExpanded && (
              <div
                id="csp-component-advanced-panel"
                className="rounded-md border border-gray-200 bg-gray-50 p-3 space-y-2"
                data-testid="csp-component-advanced-panel"
              >
                <p className="text-xs text-gray-700">
                  This re-runs AI capability mapping for this component.
                  Capabilities you have approved (mappedBy = User) are
                  preserved. AI-mapped capabilities (mappedBy = AI) may be
                  replaced. Continue?
                </p>
                <button
                  type="button"
                  onClick={() => {
                    setRemapAcknowledged(false);
                    setRemapReviewerNote('');
                    setShowRemapConfirm(true);
                  }}
                  disabled={busy}
                  className="rounded-md border border-indigo-300 bg-white px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
                  data-testid="csp-component-advanced-remap"
                >
                  Remap capabilities
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

                {/* Feature 050 FR-001 — opt-in override. Default is now
                    NeedsReview; checking this skips the review step and
                    persists the capability as Mapped at creation time. */}
                <div className="rounded-md border border-amber-200 bg-amber-50/40 px-3 py-2">
                  <label className="flex items-start gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={capMarkMappedImmediately}
                      onChange={(e) => setCapMarkMappedImmediately(e.target.checked)}
                      className="mt-0.5 h-4 w-4 rounded border-gray-300 text-amber-600 focus:ring-amber-500"
                      data-testid="csp-add-capability-mark-mapped-immediately"
                    />
                    <span className="text-xs">
                      <span className="block font-medium text-gray-900">
                        Skip review and mark this capability Mapped now.
                      </span>
                      <span className="mt-0.5 block text-[11px] text-gray-600">
                        Use this when you are mapping the capability as you create it.
                        The default leaves the capability in NeedsReview so it shows up
                        in the review queue.
                      </span>
                    </span>
                  </label>
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

            {/* Linked Capabilities — picker-styled visual parity with the
                org `ComponentLibrary` capability picker. CSP capabilities
                are 1:N (each cap has exactly one parent component) so this
                renders as a click-to-open list rather than a multi-select
                reparent toggle. Each row visually mirrors the picker
                screenshot (search box, scrollable container, NIST family
                code on the right) and opens the capability detail drawer
                on click. */}
            <section data-testid="csp-linked-capabilities">
              <h3 className="text-sm font-semibold text-gray-900">
                Linked Capabilities{' '}
                {capabilities && capabilities.length > 0 && (
                  <span className="text-gray-400 font-normal">({capabilities.length})</span>
                )}
                {/* Feature 050 / US5 (FR-009) — rolled-up amber chip
                    surfaces the count of NeedsReview capabilities so a
                    CSP-Admin can scan the drawer header for outstanding
                    review work. Suppressed entirely when N = 0 per
                    spec.md US5 acceptance — NOT rendered as "(0 awaiting
                    review)". */}
                {(() => {
                  const needsReviewCount =
                    capabilities?.filter((c) => c.status === 'NeedsReview').length ?? 0;
                  if (needsReviewCount === 0) return null;
                  return (
                    <>
                      {' '}
                      <span
                        data-testid="csp-linked-capabilities-needs-review-chip"
                        aria-label={`${needsReviewCount} capabilities awaiting review`}
                        className="ml-1 text-amber-700 font-normal"
                      >
                        ({needsReviewCount} awaiting review)
                      </span>
                    </>
                  );
                })()}
              </h3>
              {capabilities === null ? (
                <p className="mt-2 text-sm text-gray-500">Loading…</p>
              ) : capabilities.length === 0 ? (
                <p className="mt-2 text-sm text-gray-500">
                  No capabilities mapped to this component yet.
                </p>
              ) : (
                <>
                  <input
                    type="text"
                    value={capSearch}
                    onChange={(e) => setCapSearch(e.target.value)}
                    placeholder="Search capabilities..."
                    aria-label="Search linked capabilities"
                    className="mt-2 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                    data-testid="csp-linked-cap-search"
                  />
                  <div className="mt-2 max-h-48 overflow-y-auto rounded-md border border-gray-200 bg-gray-50 p-2 space-y-1">
                    {filteredCapabilities.length === 0 ? (
                      <p className="px-1 py-2 text-xs text-gray-500">No capabilities match your search.</p>
                    ) : (
                      filteredCapabilities.map((cap) => {
                        const family =
                          cap.mappedNistControlIds[0]?.split('-')[0]?.toUpperCase() ?? '';
                        return (
                          <button
                            type="button"
                            key={cap.id}
                            onClick={() => setSelectedCapabilityId(cap.id)}
                            className="flex w-full items-center gap-2 rounded px-1 py-0.5 text-left text-sm hover:bg-white focus:bg-white focus:outline-none focus:ring-1 focus:ring-indigo-500"
                            data-testid={`csp-linked-cap-row-${cap.id}`}
                          >
                            {/* Linked-indicator — visual analog to the
                                org picker's checkbox column. Always
                                checked because every row in this list is,
                                by definition, linked to this component. */}
                            <span
                              aria-hidden="true"
                              className="inline-flex h-4 w-4 flex-shrink-0 items-center justify-center rounded-sm border border-indigo-500 bg-indigo-500 text-[10px] text-white"
                              title="Linked to this component"
                            >
                              ✓
                            </span>
                            <span className="truncate text-gray-700">{cap.name}</span>
                            {cap.status === 'NeedsReview' && (
                              <span className="ml-1 inline-flex flex-shrink-0 items-center rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-medium text-amber-800">
                                needs review
                              </span>
                            )}
                            {family && (
                              <span
                                className="ml-auto flex-shrink-0 text-xs text-gray-400"
                                title={NIST_FAMILIES[family] ?? family}
                              >
                                {family}
                              </span>
                            )}
                          </button>
                        );
                      })
                    )}
                  </div>
                  <p className="mt-1 text-[11px] text-gray-500">
                    Click a capability to open its detail panel. To move a
                    capability to a different component, archive it here and
                    re-add it under the new component.
                  </p>
                </>
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

      {/* Nested capability detail drawer — opened by clicking a row in the
          Linked Capabilities picker above. Stacks on top of this drawer
          (same z-40); user closes it to return here. */}
      {selectedCapabilityId && component && (
        <CapabilityDetailDrawer
          componentId={component.id}
          capabilityId={selectedCapabilityId}
          componentName={component.name}
          componentType={component.componentType}
          canManage={canManage}
          onClose={() => setSelectedCapabilityId(null)}
          onMutated={() => {
            void refreshCapabilities();
            onMutated();
          }}
        />
      )}

      {/* Feature 050 / US4 — Remap confirm modal (FR-008). Mounted only
          while open so the default-focus behavior re-applies on each
          open. Cancel is the default-focused button per spec.md US4
          acceptance; Continue is disabled until the acknowledgement
          checkbox is checked. */}
      {showRemapConfirm && (
        <RemapConfirmDialog
          acknowledged={remapAcknowledged}
          reviewerNote={remapReviewerNote}
          busy={busy}
          onAcknowledgeChange={setRemapAcknowledged}
          onReviewerNoteChange={setRemapReviewerNote}
          onCancel={() => {
            setShowRemapConfirm(false);
            setRemapAcknowledged(false);
            setRemapReviewerNote('');
          }}
          onContinue={() => {
            void handleRemap();
          }}
        />
      )}
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

// ---------------------------------------------------------------------------
// Feature 050 / US4 — Remap confirm dialog (FR-008)
// ---------------------------------------------------------------------------

interface RemapConfirmDialogProps {
  acknowledged: boolean;
  reviewerNote: string;
  busy: boolean;
  onAcknowledgeChange: (next: boolean) => void;
  onReviewerNoteChange: (next: string) => void;
  onCancel: () => void;
  onContinue: () => void;
}

/**
 * Confirm modal for the Advanced > Remap action. Cancel is the
 * default-focused button per spec.md US4 acceptance; Continue is
 * disabled until the acknowledgement checkbox is checked. The optional
 * reviewer-note textarea is captured but not yet wired through the
 * existing `POST .../remap` body — see Feature 050 task T045 deviation.
 */
function RemapConfirmDialog({
  acknowledged,
  reviewerNote,
  busy,
  onAcknowledgeChange,
  onReviewerNoteChange,
  onCancel,
  onContinue,
}: RemapConfirmDialogProps): ReactElement {
  // Move focus to Cancel as soon as the modal mounts. Using a ref callback
  // sidesteps a separate `useEffect` and keeps the focus deterministic on
  // first paint (vital for the keyboard-driven "Esc dismisses" UX even
  // though Esc handling lives at the modal-container level).
  const cancelRef = (node: HTMLButtonElement | null) => {
    if (node) {
      node.focus();
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="csp-remap-confirm-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      data-testid="csp-remap-confirm-dialog"
    >
      <div className="w-full max-w-md rounded-lg bg-white shadow-xl">
        <header className="border-b border-gray-200 px-4 py-3">
          <h2
            id="csp-remap-confirm-title"
            className="text-base font-semibold text-gray-900"
          >
            Remap capabilities
          </h2>
        </header>

        <div className="space-y-3 px-4 py-3">
          <p className="text-sm text-gray-700">
            Re-running AI mapping will overwrite AI-produced capabilities and
            reset their NeedsReview status. User-mapped capabilities are
            preserved. Continue?
          </p>

          <label className="flex items-start gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={acknowledged}
              onChange={(e) => onAcknowledgeChange(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
              data-testid="csp-remap-confirm-acknowledge"
            />
            <span className="text-xs text-gray-800">
              I understand AI-mapped capabilities may be replaced.
            </span>
          </label>

          <label className="block">
            <span className="block text-xs font-medium text-gray-700">
              Reviewer note <span className="text-gray-400">(optional)</span>
            </span>
            <textarea
              value={reviewerNote}
              onChange={(e) => onReviewerNoteChange(e.target.value)}
              rows={2}
              maxLength={500}
              placeholder="Why are you re-running the mapping?"
              className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-xs focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-remap-confirm-note"
            />
          </label>
        </div>

        <footer className="flex justify-end gap-2 border-t border-gray-200 px-4 py-3">
          <button
            ref={cancelRef}
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            data-testid="csp-remap-confirm-cancel"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onContinue}
            disabled={busy || !acknowledged}
            className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
            data-testid="csp-remap-confirm-continue"
          >
            {busy ? 'Remapping…' : 'Continue'}
          </button>
        </footer>
      </div>
    </div>
  );
}
