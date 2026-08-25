import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactElement } from 'react';
import { Link } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import { useCspDashboardAvailable } from '../../components/layout/useCspDashboardAvailable';
import {
  addCspInheritedCapability,
  archiveCspInheritedCapability,
  isUnavailable,
  listCspInheritedCapabilities,
  listCspInheritedComponents,
  type CspInheritedCapability,
  type CspInheritedCapabilityStatus,
  type CspInheritedComponent,
  type CspInheritedComponentsPage,
  type UnavailableState,
} from './api';
import CapabilityDetailDrawer from './CapabilityDetailDrawer';

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
  { value: 'Archived', label: 'Archived' },
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
  Archived: 'bg-gray-200 text-gray-700',
};

export default function CspCapabilitiesPage(): ReactElement {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [statusFilter, setStatusFilter] = useState<'' | CspInheritedCapabilityStatus>('');
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  /**
   * Currently-expanded capability card id (null = all collapsed). Mirrors the
   * org `CapabilityLibrary` accordion pattern — one row open at a time so
   * Control Mappings panels never stack.
   */
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // CSP-Admin gating mirrors the components page — true only for the
  // CSP-Admin role in an active multi-tenant deployment.
  const canManage = useCspDashboardAvailable() === true;

  // Modal visibility — the actual form state lives inside the modal
  // component (mirrors CreateComponentModal in CspInheritedComponentsPage).
  const [createOpen, setCreateOpen] = useState(false);
  const [archiveError, setArchiveError] = useState<string | null>(null);

  // Detail-drawer selection — row click sets this; the drawer mutates
  // it back to null on close. We store both ids so the drawer can render
  // the parent component header without an extra fetch.
  const [selectedCap, setSelectedCap] = useState<{
    componentId: string;
    capabilityId: string;
  } | null>(null);

  // Splice the newly-created capability into the loaded set so it appears
  // without re-running the per-component fan-out.
  const handleCreated = (created: CspInheritedCapability, parent: CspInheritedComponent) => {
    setState((prev) => {
      if (prev.kind !== 'ready') return prev;
      const newRow: FlatCapabilityRow = {
        ...created,
        componentId: parent.id,
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

  // Initial fetch + reload (post-mutation refresh) — components page +
  // parallel capability fan-out. CSP-Admins additionally see Draft /
  // Archived components so the catalog stays auditable.
  const cancelTokenRef = useRef(0);
  const reload = useCallback(async () => {
    const token = ++cancelTokenRef.current;
    setState((prev) => (prev.kind === 'ready' ? prev : { kind: 'loading' }));
    try {
      const componentsResult = await listCspInheritedComponents({
        page: 1,
        pageSize: PAGE_SIZE_COMPONENTS,
        // CSP-Admins should see Draft / Archived components too so the
        // capability list stays auditable; mission owners only see
        // Published.
        ...(canManage ? {} : { status: 'Published' as const }),
      });
      if (token !== cancelTokenRef.current) return;
      if (isUnavailable(componentsResult)) {
        setState({ kind: 'unavailable', reason: componentsResult.reason });
        return;
      }
      const page: CspInheritedComponentsPage = componentsResult;
      const components = page.items;

      const capabilityArrays = await Promise.all(
        components.map((c) =>
          listCspInheritedCapabilities(c.id).catch(() => [] as CspInheritedCapability[]),
        ),
      );
      if (token !== cancelTokenRef.current) return;

      const rows: FlatCapabilityRow[] = [];
      for (let i = 0; i < components.length; i += 1) {
        const c = components[i];
        const caps = capabilityArrays[i];
        if (!c || !caps) continue;
        for (const cap of caps) {
          rows.push({
            ...cap,
            // The wire DTO key is `cspInheritedComponentId`, not
            // `componentId` — pin the parent id explicitly so the
            // drawer row-click handler has it.
            componentId: c.id,
            componentName: c.name,
            componentType: c.componentType,
          });
        }
      }
      setState({ kind: 'ready', rows, components });
    } catch (err: unknown) {
      if (token !== cancelTokenRef.current) return;
      const message = err instanceof Error ? err.message : 'Failed to load CSP capabilities.';
      setState({ kind: 'error', message });
    }
  }, [canManage]);

  useEffect(() => {
    void reload();
    return () => {
      // Bump the cancel token so an in-flight fetch can detect unmount.
      cancelTokenRef.current += 1;
    };
  }, [reload]);

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
    // Default sort: capability name ascending. The org `CapabilityLibrary`
    // does not expose sort controls in the toolbar — visual parity wins over
    // power-user sort affordances, which CSP-Admins can still apply via the
    // search/filter combo.
    return [...rows].sort((a, b) => a.name.localeCompare(b.name));
  }, [state, statusFilter, debouncedSearch]);

  const hasNoComponents =
    state.kind === 'ready' && state.components.length === 0;

  return (
    <PageLayout title="Security Capabilities">
      <div data-testid="csp-capabilities-page">
        {archiveError && (
          <div className="mb-4 flex items-center justify-between rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800">
            <span>{archiveError}</span>
            <button type="button" onClick={() => setArchiveError(null)} className="ml-4 opacity-60 hover:opacity-100">✕</button>
          </div>
        )}
        <PageHero
          eyebrow="Capabilities"
          title="Security Capabilities"
          description="CSP-wide capabilities sourced from CSP-imported ATO documents (SSPs, OSCAL, eMASS). Every mission-owner organization inherits this list and can map their systems against it. Only CSP administrators see NeedsReview rows here; mission owners see Mapped only."
          showOrgName={false}
        />

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
            {/* Toolbar — visual parity with org `CapabilityLibrary`: search +
                status filter on the left, count text, primary action
                right-aligned. */}
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
              {state.kind === 'ready' && (
                <span className="text-sm text-gray-500" data-testid="csp-capabilities-count">
                  {filteredSortedRows.length.toLocaleString()} of{' '}
                  {state.rows.length.toLocaleString()} capabilities
                </span>
              )}
              {canManage && state.kind === 'ready' && !hasNoComponents && (
                <div className="ml-auto flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    onClick={() => setCreateOpen(true)}
                    className="inline-flex items-center rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
                    data-testid="csp-capabilities-create-toggle"
                  >
                    + New Capability
                  </button>
                </div>
              )}
            </div>

            {state.kind === 'loading' && (
              <p className="text-sm text-gray-500" data-testid="csp-capabilities-loading">
                Loading CSP capabilities…
              </p>
            )}

            {state.kind === 'ready' && filteredSortedRows.length === 0 && !hasNoComponents && (
              <p
                className="rounded-md border border-gray-200 bg-white px-3 py-6 text-center text-sm text-gray-500"
                data-testid="csp-capabilities-empty"
              >
                No capabilities match the current filter.
              </p>
            )}

            {state.kind === 'ready' && filteredSortedRows.length > 0 && (
              <div className="space-y-3" data-testid="csp-capabilities-list">
                {filteredSortedRows.map((r) => (
                  <CspCapabilityCard
                    key={r.id}
                    row={r}
                    isExpanded={expandedId === r.id}
                    canManage={canManage}
                    onToggle={() =>
                      setExpandedId((prev) => (prev === r.id ? null : r.id))
                    }
                    onOpenDrawer={() =>
                      setSelectedCap({ componentId: r.componentId, capabilityId: r.id })
                    }
                    onArchive={async () => {
                      if (
                        !window.confirm(
                          `Archive "${r.name}"? Mission owners will no longer see it. CSP-Admins can still view it via the Archived filter.`,
                        )
                      ) {
                        return;
                      }
                      try {
                        await archiveCspInheritedCapability(r.componentId, r.id);
                        setArchiveError(null);
                        void reload();
                      } catch (err) {
                        const ex = err as { message?: string };
                        setArchiveError(ex?.message ?? 'Archive failed.');
                      }
                    }}
                  />
                ))}
              </div>
            )}
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

      {/* Capability detail drawer — opened by clicking a row in the table.
          Read-only for non-CSP-Admin viewers; CSP-Admins get edit /
          archive / review / parent-component remap actions. */}
      {selectedCap && state.kind === 'ready' && (() => {
        const parent = state.components.find((c) => c.id === selectedCap.componentId);
        if (!parent) {
          // Stale selection (e.g., the parent component was archived in
          // another tab). Render nothing — the next reload will repopulate
          // and the user can close the empty drawer click. We avoid
          // setState-in-render here.
          return null;
        }
        return (
          <CapabilityDetailDrawer
            componentId={selectedCap.componentId}
            capabilityId={selectedCap.capabilityId}
            componentName={parent.name}
            componentType={parent.componentType}
            canManage={canManage}
            onClose={() => setSelectedCap(null)}
            onMutated={() => {
              void reload();
            }}
          />
        );
      })()}
    </PageLayout>
  );
}

// ---------------------------------------------------------------------------
// CspCapabilityCard — collapsible row visual matching the org
// `CapabilityCard`. Header always visible (name, parent component, category
// chip, status chip, control count, caret); when expanded reveals the
// description, owner, linked component chip, action buttons, and the nested
// Control Mappings list. Category is derived from the first NIST family in
// `mappedNistControlIds` (e.g. `CP-9` → `CP / Contingency Planning`).
// ---------------------------------------------------------------------------

function CspCapabilityCard({
  row,
  isExpanded,
  canManage,
  onToggle,
  onOpenDrawer,
  onArchive,
}: {
  row: FlatCapabilityRow;
  isExpanded: boolean;
  canManage: boolean;
  onToggle: () => void;
  onOpenDrawer: () => void;
  onArchive: () => void;
}): ReactElement {
  // Derive the "category" chip from the first NIST family prefix in the
  // capability's mapped controls. Mirrors the org `CapabilityCard` category
  // chip, which is sourced from the catalog category lookup. CSP capabilities
  // don't carry a category field, so the NIST family stand-in keeps the chip
  // populated for visual parity.
  const familyCode = row.mappedNistControlIds[0]?.split('-')[0]?.toUpperCase() ?? '';
  const familyLabel = familyCode ? (CSP_NIST_FAMILIES[familyCode] ?? familyCode) : null;

  return (
    <div
      className="rounded-lg border border-violet-200 bg-white shadow-sm transition-shadow hover:shadow-md"
      data-testid={`csp-capability-card-${row.id}`}
    >
      <button
        type="button"
        onClick={onToggle}
        className="w-full rounded-lg p-4 text-left focus:outline-none focus:ring-2 focus:ring-indigo-300"
        aria-expanded={isExpanded}
      >
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <h3 className="truncate text-sm font-semibold text-gray-900">{row.name}</h3>
              <span className="inline-flex shrink-0 items-center rounded bg-violet-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-violet-700">
                CSP
              </span>
            </div>
            <p className="text-xs text-gray-500">
              {row.componentName}
              <span className="ml-1 text-gray-400">· {row.componentType}</span>
            </p>
          </div>
          <div className="ml-4 flex shrink-0 items-center gap-3">
            {familyLabel && (
              <span className="hidden items-center rounded bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700 sm:inline-flex">
                {familyLabel}
              </span>
            )}
            <span
              className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${STATUS_BADGE[row.status]}`}
            >
              {row.status === 'NeedsReview' ? 'Needs review' : row.status}
            </span>
            <span className="text-xs text-gray-400">
              {row.mappedNistControlIds.length} control{row.mappedNistControlIds.length === 1 ? '' : 's'}
            </span>
            <svg
              className={`h-4 w-4 text-gray-400 transition-transform ${isExpanded ? 'rotate-180' : ''}`}
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          </div>
        </div>
      </button>

      {isExpanded && (
        <div className="space-y-3 border-t border-violet-100 px-4 pb-4 pt-3">
          {row.description && (
            <p className="line-clamp-3 text-sm text-gray-600">{row.description}</p>
          )}
          {row.status === 'NeedsReview' && row.mappingFailureReason && (
            <p className="text-xs text-amber-700">
              <span className="font-medium">Review note:</span> {row.mappingFailureReason}
            </p>
          )}
          <div className="flex items-center justify-between text-xs text-gray-500">
            <span>
              Owner: <span className="text-gray-700">{row.createdBy ?? 'CSP'}</span>
              {row.reviewedBy && (
                <span className="ml-2 text-gray-400">reviewed by {row.reviewedBy}</span>
              )}
            </span>
            <span>Mapped by {row.mappedBy}</span>
          </div>
          <div>
            <div className="text-[11px] font-medium uppercase tracking-wide text-gray-500">
              Linked Component
            </div>
            <div className="mt-1 flex flex-wrap gap-1">
              <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-[11px] font-medium text-gray-600">
                {row.componentName}
              </span>
            </div>
          </div>

          {canManage && (
            <div className="flex gap-2 pt-1">
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  onOpenDrawer();
                }}
                className="rounded bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700"
                data-testid={`csp-capability-card-edit-${row.id}`}
              >
                Edit
              </button>
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  onOpenDrawer();
                }}
                className="rounded border border-indigo-600 px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-50"
                data-testid={`csp-capability-card-remap-${row.id}`}
              >
                Remap parent
              </button>
              {row.status !== 'Archived' && (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    onArchive();
                  }}
                  className="rounded bg-red-50 px-3 py-1.5 text-xs font-medium text-red-700 hover:bg-red-100"
                  data-testid={`csp-capability-card-archive-${row.id}`}
                >
                  Archive
                </button>
              )}
            </div>
          )}

          {/* Control Mappings panel — mirrors the org expanded card pattern
              (indented list under the card body). CSP capabilities carry
              just a flat list of NIST control ids, so each row renders the
              control id chip plus the resolved family name and a "Global
              (All Systems)" scope chip matching the org screenshot's
              per-mapping scope chip. */}
          <div className="ml-2 mt-2 border-l-2 border-indigo-200 pl-3">
            <div className="flex items-center justify-between">
              <div className="text-[11px] font-medium uppercase tracking-wide text-gray-500">
                Control Mappings ({row.mappedNistControlIds.length})
              </div>
            </div>
            {row.mappedNistControlIds.length === 0 ? (
              <p className="mt-1 text-xs text-gray-400">No NIST controls mapped.</p>
            ) : (
              <ul className="mt-1 space-y-1">
                {row.mappedNistControlIds.map((id) => {
                  const fam = id.split('-')[0]?.toUpperCase() ?? '';
                  const famName = CSP_NIST_FAMILIES[fam];
                  return (
                    <li
                      key={id}
                      className="flex flex-wrap items-center gap-2 rounded border border-gray-100 bg-gray-50/50 px-2 py-1 text-xs"
                    >
                      <span className="inline-flex items-center rounded bg-indigo-50 px-1.5 py-0.5 font-medium text-indigo-700">
                        {id}
                      </span>
                      {famName && <span className="text-gray-700">{famName}</span>}
                      <span className="ml-auto inline-flex items-center rounded bg-violet-50 px-1.5 py-0.5 text-[10px] font-medium text-violet-700">
                        Global (All Systems)
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
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
