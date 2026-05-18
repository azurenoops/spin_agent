import { useEffect, useMemo, useState, type FormEvent, type ReactElement } from 'react';
import { Link } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import MetricCard from '../../components/cards/MetricCard';
import {
  addCspInheritedCapability,
  isUnavailable,
  listCspInheritedCapabilities,
  listCspInheritedComponents,
  type CspInheritedCapability,
  type CspInheritedCapabilityStatus,
  type CspInheritedComponent,
  type CspInheritedComponentsPage,
  type UnavailableState,
} from './api';

/**
 * CSP-level Capabilities surface mounted at `/capabilities` for CSP-Admins
 * who are not currently impersonating a tenant.
 *
 * Shows a flat, sortable, filterable list of every capability that lives on a
 * CSP-inherited component. Capabilities are CSP-scoped (`[GlobalReference]`)
 * and shared across every tenant in the deployment, so this is the canonical
 * view of what mission-owner tenants can inherit.
 *
 * Implementation note: the existing list endpoint only returns capability
 * counts on each component. To present a flat capabilities table we fetch the
 * (small) Published components page and fan out to
 * `GET /api/csp/inherited-components/{id}/capabilities` per component in
 * parallel. Acceptable for typical CSP catalog sizes (10s of components, low
 * 100s of capabilities). If catalogs grow, replace with a dedicated flat
 * `/api/csp/inherited-components/capabilities` endpoint.
 */
const STATUS_FILTERS: { value: '' | CspInheritedCapabilityStatus; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Mapped', label: 'Mapped' },
  { value: 'NeedsReview', label: 'Needs review' },
];

const PAGE_SIZE_COMPONENTS = 200; // Pull all components in one shot; CSP catalogs are small.

interface FlatCapabilityRow extends CspInheritedCapability {
  componentName: string;
  componentType: CspInheritedComponent['componentType'];
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'unavailable'; reason: UnavailableState['reason'] }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; rows: FlatCapabilityRow[]; components: CspInheritedComponent[] };

const STATUS_BADGE: Record<CspInheritedCapabilityStatus, string> = {
  Mapped: 'bg-emerald-100 text-emerald-800',
  NeedsReview: 'bg-amber-100 text-amber-800',
};

export default function CspCapabilitiesPage(): ReactElement {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [statusFilter, setStatusFilter] = useState<'' | CspInheritedCapabilityStatus>('');
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [sort, setSort] = useState<'name' | 'componentName' | 'status'>('name');
  const [order, setOrder] = useState<'asc' | 'desc'>('asc');

  // Modal visibility — the actual form state lives inside the modal
  // component (mirrors CreateComponentModal in CspInheritedComponentsPage).
  const [createOpen, setCreateOpen] = useState(false);

  // Splice the newly-created capability into the loaded set so it appears
  // without re-running the per-component fan-out.
  const handleCreated = (created: CspInheritedCapability, parent: CspInheritedComponent) => {
    setState((prev) => {
      if (prev.kind !== 'ready') return prev;
      const newRow: FlatCapabilityRow = {
        ...created,
        componentName: parent.name,
        componentType: parent.componentType,
      };
      return {
        kind: 'ready',
        rows: [newRow, ...prev.rows],
        components: prev.components,
      };
    });
    setCreateOpen(false);
  };

  useEffect(() => {
    const t = window.setTimeout(() => setDebouncedSearch(search.trim().toLowerCase()), 250);
    return () => window.clearTimeout(t);
  }, [search]);

  // Initial fetch: components + parallel capability fan-out.
  useEffect(() => {
    let cancelled = false;
    setState({ kind: 'loading' });

    (async () => {
      try {
        const componentsResult = await listCspInheritedComponents({
          page: 1,
          pageSize: PAGE_SIZE_COMPONENTS,
          status: 'Published',
        });
        if (cancelled) return;
        if (isUnavailable(componentsResult)) {
          setState({ kind: 'unavailable', reason: componentsResult.reason });
          return;
        }
        const page: CspInheritedComponentsPage = componentsResult;
        const components = page.items;

        // Fan out — bounded by typical CSP catalog size.
        const capabilityArrays = await Promise.all(
          components.map((c) =>
            listCspInheritedCapabilities(c.id).catch(() => [] as CspInheritedCapability[]),
          ),
        );
        if (cancelled) return;

        const rows: FlatCapabilityRow[] = [];
        for (let i = 0; i < components.length; i += 1) {
          const c = components[i];
          const caps = capabilityArrays[i];
          if (!c || !caps) continue;
          for (const cap of caps) {
            rows.push({
              ...cap,
              componentName: c.name,
              componentType: c.componentType,
            });
          }
        }
        setState({ kind: 'ready', rows, components });
      } catch (err: unknown) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : 'Failed to load CSP capabilities.';
        setState({ kind: 'error', message });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  // Pre-select the first component when the form opens, but never overwrite
  // a deliberate user choice. Handled inside CreateCapabilityModal now —
  // see the modal component below.

  const filteredSortedRows = useMemo<FlatCapabilityRow[]>(() => {
    if (state.kind !== 'ready') return [];
    let rows = state.rows;
    if (statusFilter) rows = rows.filter((r) => r.status === statusFilter);
    if (debouncedSearch) {
      rows = rows.filter(
        (r) =>
          r.name.toLowerCase().includes(debouncedSearch) ||
          (r.description ?? '').toLowerCase().includes(debouncedSearch) ||
          r.componentName.toLowerCase().includes(debouncedSearch) ||
          r.mappedNistControlIds.some((id) => id.toLowerCase().includes(debouncedSearch)),
      );
    }
    const dir = order === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = a[sort];
      const bv = b[sort];
      if (typeof av === 'string' && typeof bv === 'string') {
        return av.localeCompare(bv) * dir;
      }
      return 0;
    });
  }, [state, statusFilter, debouncedSearch, sort, order]);

  const hasNoComponents =
    state.kind === 'ready' && state.components.length === 0;

  return (
    <PageLayout title="Capabilities · CSP">
      <div data-testid="csp-capabilities-page">
        <PageHero
          eyebrow="CSP Catalog"
          title="CSP Capabilities"
          description="Canonical capabilities sourced from CSP-imported ATO documents (SSPs, OSCAL, eMASS). Every mission-owner organization inherits this list and can map their systems against it. Only CSP administrators see NeedsReview rows here; mission owners see Mapped only."
          actions={
            state.kind === 'ready' && !hasNoComponents ? (
              <button
                type="button"
                onClick={() => setCreateOpen(true)}
                className="inline-flex items-center rounded-md border border-white bg-white px-4 py-1.5 text-sm font-medium text-indigo-700 hover:bg-indigo-50"
                data-testid="csp-capabilities-create-toggle"
              >
                + Create capability
              </button>
            ) : undefined
          }
        />

        {/* Summary metrics — mirrors org `CapabilityLibrary`'s CoverageCards
            strip so the CSP catalog page reads at-a-glance the same way as
            the org capabilities page. */}
        {state.kind === 'ready' && (
          <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-4">
            <MetricCard title="Total capabilities" value={state.rows.length} />
            <MetricCard
              title="Mapped"
              value={state.rows.filter((r) => r.status === 'Mapped').length}
            />
            <MetricCard
              title="Needs review"
              value={state.rows.filter((r) => r.status === 'NeedsReview').length}
            />
            <MetricCard title="Source components" value={state.components.length} />
          </div>
        )}

        {/* If the catalog has no components yet, the create form has nothing
            to parent against. Surface the path to author one. */}
        {hasNoComponents && (
          <div
            className="mb-4 rounded-md border border-indigo-200 bg-indigo-50 p-4 text-sm text-indigo-900"
            data-testid="csp-capabilities-no-components"
          >
            <div className="font-semibold">No CSP-inherited components yet</div>
            <div className="mt-1">
              Capabilities are attached to CSP-inherited components. Author a
              component first, then come back here to add capabilities.
            </div>
            <div className="mt-2">
              <Link
                to="/components"
                className="font-medium underline hover:text-indigo-700"
              >
                Go to CSP-inherited components &rarr;
              </Link>
            </div>
          </div>
        )}

        {state.kind === 'unavailable' && (
          <div
            className="rounded-md border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800"
            role="alert"
            data-testid={`csp-capabilities-unavailable-${state.reason}`}
          >
            <div className="font-semibold">CSP capabilities unavailable</div>
            <div className="mt-1">
              {state.reason === 'SINGLE_TENANT_MODE'
                ? 'This deployment runs in SingleTenant mode; CSP-inherited capabilities are not applicable.'
                : state.reason === 'CSP_ONBOARDING_INCOMPLETE'
                  ? 'Complete the CSP onboarding wizard before opening CSP capabilities.'
                  : 'The CSP-inherited components service is unreachable. Try again in a moment.'}
            </div>
          </div>
        )}

        {state.kind === 'error' && (
          <div
            className="rounded-md border border-red-200 bg-red-50 p-4 text-sm text-red-700"
            role="alert"
            data-testid="csp-capabilities-error"
          >
            <div className="font-semibold">Failed to load CSP capabilities</div>
            <div className="mt-1">{state.message}</div>
          </div>
        )}

        {(state.kind === 'loading' || state.kind === 'ready') && (
          <>
            {/* Toolbar — search + status filter on the left, sort controls
                on the right. Matches the org `CapabilityLibrary` toolbar
                pattern (flex-wrap row above the content card, plain bordered
                inputs, primary action in the PageHero). */}
            <div className="mb-4 flex flex-wrap items-center gap-3">
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search capability, component, or NIST control..."
                className="min-w-[200px] flex-1 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                data-testid="csp-capabilities-search"
              />
              <select
                value={statusFilter}
                onChange={(e) =>
                  setStatusFilter(e.target.value as typeof statusFilter)
                }
                className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                data-testid="csp-capabilities-status-filter"
              >
                {STATUS_FILTERS.map((f) => (
                  <option key={f.value} value={f.value}>
                    {f.label}
                  </option>
                ))}
              </select>
              <select
                value={sort}
                onChange={(e) =>
                  setSort(e.target.value as 'name' | 'componentName' | 'status')
                }
                className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                data-testid="csp-capabilities-sort-field"
                aria-label="Sort by"
              >
                <option value="name">Sort: Capability</option>
                <option value="componentName">Sort: Component</option>
                <option value="status">Sort: Status</option>
              </select>
              <button
                type="button"
                onClick={() => setOrder((o) => (o === 'asc' ? 'desc' : 'asc'))}
                className="rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
                data-testid="csp-capabilities-sort-order"
              >
                {order === 'asc' ? '↑ Asc' : '↓ Desc'}
              </button>
            </div>

            <div
              className="rounded-lg border border-gray-200 bg-white shadow-sm"
              data-testid="csp-capabilities-table"
            >
              <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200 text-sm">
                <thead className="bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-600">
                  <tr>
                    <th className="px-3 py-2 text-left">Capability</th>
                    <th className="px-3 py-2 text-left">Component</th>
                    <th className="px-3 py-2 text-left">NIST controls</th>
                    <th className="px-3 py-2 text-left">Status</th>
                    <th className="px-3 py-2 text-left">Mapped by</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {state.kind === 'loading' && (
                    <tr>
                      <td
                        colSpan={5}
                        className="px-3 py-6 text-center text-sm text-gray-500"
                        data-testid="csp-capabilities-loading"
                      >
                        Loading CSP capabilities…
                      </td>
                    </tr>
                  )}
                  {state.kind === 'ready' && filteredSortedRows.length === 0 && (
                    <tr>
                      <td
                        colSpan={5}
                        className="px-3 py-6 text-center text-sm text-gray-500"
                        data-testid="csp-capabilities-empty"
                      >
                        No capabilities match the current filter.
                      </td>
                    </tr>
                  )}
                  {state.kind === 'ready' &&
                    filteredSortedRows.map((r) => (
                      <tr key={r.id} data-testid={`csp-capability-row-${r.id}`}>
                        <td className="whitespace-nowrap px-3 py-2 font-medium text-gray-900">
                          {r.name}
                          {r.description && (
                            <div className="max-w-md truncate text-xs font-normal text-gray-500">
                              {r.description}
                            </div>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-xs text-gray-700">
                          {r.componentName}
                          <div className="text-xs text-gray-400">{r.componentType}</div>
                        </td>
                        <td className="px-3 py-2 text-xs text-gray-700">
                          {r.mappedNistControlIds.length === 0 ? (
                            <span className="text-gray-400">—</span>
                          ) : (
                            <div className="flex flex-wrap gap-1">
                              {r.mappedNistControlIds.map((id) => (
                                <span
                                  key={id}
                                  className="inline-flex items-center rounded bg-indigo-50 px-1.5 py-0.5 text-[11px] font-medium text-indigo-700"
                                >
                                  {id}
                                </span>
                              ))}
                            </div>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2">
                          <span
                            className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${STATUS_BADGE[r.status]}`}
                          >
                            {r.status === 'NeedsReview' ? 'Needs review' : 'Mapped'}
                          </span>
                          {r.status === 'NeedsReview' && r.mappingFailureReason && (
                            <div className="mt-0.5 max-w-xs truncate text-[11px] text-amber-700">
                              {r.mappingFailureReason}
                            </div>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-xs text-gray-600">
                          {r.mappedBy}
                          {r.reviewedBy && (
                            <div className="text-[11px] text-gray-400">
                              by {r.reviewedBy}
                            </div>
                          )}
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>

            {state.kind === 'ready' && (
              <div
                className="border-t border-gray-100 px-4 py-2 text-xs text-gray-600"
                data-testid="csp-capabilities-summary"
              >
                {filteredSortedRows.length.toLocaleString()} of{' '}
                {state.rows.length.toLocaleString()} CSP capabilities
              </div>
            )}
            </div>
          </>
        )}
      </div>

      {/* Create-capability dialog — mirrors `CreateComponentModal` chrome
          so the CSP create surfaces look-and-feel identical to the org
          dialogs (`CapabilityForm` inside `CapabilityLibrary.tsx`). */}
      {createOpen && state.kind === 'ready' && !hasNoComponents && (
        <CreateCapabilityModal
          components={state.components}
          onClose={() => setCreateOpen(false)}
          onCreated={handleCreated}
        />
      )}
    </PageLayout>
  );
}

// ---------------------------------------------------------------------------
// CreateCapabilityModal — manual-create surface used by CSP-Admins to author
// a CSP-inherited capability without going through the import pipeline.
//
// Visually mirrors the org-level `CapabilityForm` (Name, Provider, Category,
// Description, Implementation Status, Owner). The CSP-inherited capability
// entity today only persists `Name`, `Description`, `MappedNistControlIds`
// and the parent `CspInheritedComponentId`; status is auto-stamped `Mapped`
// and `mappedBy = User` by the service. Provider, Category, Implementation
// Status, and Owner are shown for layout parity with the org-level surface
// but disabled with explicit "Not stored at CSP scope" helper copy so a
// future schema extension can light them up without UI churn.
//
// Same dialog chrome as `CreateComponentModal` in the components page:
// fixed inset-0 overlay, click-outside-to-close, role="dialog".
// ---------------------------------------------------------------------------

// Org-level NIST families dropdown labels — inlined to avoid coupling to the
// org-form's own constants and to keep CSP-scope copy explicit.
const CSP_NIST_FAMILIES: Record<string, string> = {
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
const CSP_CAP_STATUS_OPTIONS = [
  'Planned',
  'InProgress',
  'Implemented',
  'Deprecated',
] as const;

const PARITY_HELP =
  'Not stored at CSP scope — display-only for layout parity with org-level capabilities.';

function CreateCapabilityModal({
  components,
  onClose,
  onCreated,
}: {
  components: CspInheritedComponent[];
  onClose: () => void;
  onCreated: (created: CspInheritedCapability, parent: CspInheritedComponent) => void;
}): ReactElement {
  const [componentId, setComponentId] = useState<string>(components[0]?.id ?? '');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [controls, setControls] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);
    if (!componentId) {
      setError('Pick a CSP-inherited component to attach this capability to.');
      return;
    }
    const ids = controls
      .split(/[,\s]+/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
    if (!name.trim() || !description.trim()) {
      setError('Name and description are required.');
      return;
    }
    if (ids.length === 0) {
      setError('Provide at least one NIST control ID (e.g. AC-2, AC-2(1)).');
      return;
    }
    setSubmitting(true);
    try {
      const created = await addCspInheritedCapability(componentId, {
        name: name.trim(),
        description: description.trim(),
        mappedNistControlIds: ids,
      });
      const parent = components.find((c) => c.id === componentId);
      if (parent) onCreated(created, parent);
      else onClose();
    } catch (err) {
      const ex = err as { errorCode?: string; message?: string };
      setError(
        ex?.errorCode === 'CSP_ONBOARDING_INCOMPLETE'
          ? 'Complete CSP onboarding before creating CSP-inherited capabilities.'
          : (ex?.message ?? 'Create failed.'),
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="csp-create-capability-title"
      onClick={onClose}
      data-testid="csp-create-capability-modal"
    >
      <form
        onSubmit={handleSubmit}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-lg rounded-lg bg-white p-5 shadow-xl max-h-[90vh] overflow-y-auto"
      >
        <h2 id="csp-create-capability-title" className="text-lg font-semibold text-gray-900">
          Create CSP-inherited capability
        </h2>
        <p className="mt-1 text-xs text-gray-500">
          Manually-authored capabilities are recorded as a human mapping
          (<code>mappedBy=User</code>) and survive future AI remaps.
        </p>

        {error && (
          <div
            role="alert"
            className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
            data-testid="csp-create-capability-error"
          >
            {error}
          </div>
        )}

        <div className="mt-4 space-y-3">
          {/* Component (CSP-specific — capabilities are 1:N under a component) */}
          <div>
            <label className="block text-sm font-medium text-gray-700">
              Component *
            </label>
            <select
              value={componentId}
              onChange={(e) => setComponentId(e.target.value)}
              required
              autoFocus
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-create-capability-component"
            >
              {components.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.componentType})
                </option>
              ))}
            </select>
          </div>

          {/* Name (active) */}
          <div>
            <label className="block text-sm font-medium text-gray-700">Name *</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={256}
              required
              placeholder="e.g., Multi-Factor Authentication"
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-create-capability-name"
            />
          </div>

          {/* Provider (visual parity, disabled) */}
          <div>
            <label className="block text-sm font-medium text-gray-400">Provider</label>
            <input
              type="text"
              value=""
              disabled
              aria-disabled
              placeholder="—"
              className="mt-1 block w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-500 cursor-not-allowed"
            />
            <p className="mt-0.5 text-[11px] text-gray-400">{PARITY_HELP}</p>
          </div>

          {/* Category (visual parity, disabled) */}
          <div>
            <label className="block text-sm font-medium text-gray-400">Category</label>
            <select
              value=""
              disabled
              aria-disabled
              className="mt-1 block w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-500 cursor-not-allowed"
            >
              <option value="">Select a NIST family…</option>
              {Object.entries(CSP_NIST_FAMILIES).map(([code, label]) => (
                <option key={code} value={code}>
                  {code} — {label}
                </option>
              ))}
            </select>
            <p className="mt-0.5 text-[11px] text-gray-400">
              Use the Mapped NIST control IDs field below — that is the field
              actually persisted on a CSP capability.
            </p>
          </div>

          {/* Description (active) */}
          <div>
            <label className="block text-sm font-medium text-gray-700">Description *</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              maxLength={2000}
              required
              placeholder="Describe how this capability works…"
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-create-capability-description"
            />
          </div>

          {/* Mapped NIST control IDs (active, CSP-specific) */}
          <div>
            <label className="block text-sm font-medium text-gray-700">
              Mapped NIST control IDs *
            </label>
            <input
              type="text"
              value={controls}
              onChange={(e) => setControls(e.target.value)}
              placeholder="AC-2, AC-2(1), SC-7"
              required
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 font-mono text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-create-capability-controls"
            />
            <span className="mt-0.5 block text-[11px] text-gray-500">
              Comma- or space-separated. Recorded as a human mapping
              (<code>mappedBy=User</code>) so it survives a future AI remap.
            </span>
          </div>

          {/* Implementation Status / Owner row (visual parity, disabled) */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-400">
                Implementation Status
              </label>
              <select
                value="Implemented"
                disabled
                aria-disabled
                title="CSP capabilities use Mapped/NeedsReview, not the org Implementation Status enum."
                className="mt-1 block w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-500 cursor-not-allowed"
              >
                {CSP_CAP_STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
              <p className="mt-0.5 text-[11px] text-gray-400">
                CSP capabilities auto-stamp <code>Mapped</code> on create.
              </p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-400">Owner</label>
              <input
                type="text"
                value=""
                disabled
                aria-disabled
                placeholder="—"
                className="mt-1 block w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-500 cursor-not-allowed"
              />
              <p className="mt-0.5 text-[11px] text-gray-400">{PARITY_HELP}</p>
            </div>
          </div>
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={submitting}
            className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={submitting || !name.trim() || !componentId}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
            data-testid="csp-create-capability-submit"
          >
            {submitting ? 'Creating…' : 'Create'}
          </button>
        </div>
      </form>
    </div>
  );
}
