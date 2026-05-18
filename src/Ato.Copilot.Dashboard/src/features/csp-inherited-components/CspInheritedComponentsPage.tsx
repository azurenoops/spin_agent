import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type FormEvent,
  type ReactElement,
} from 'react';
import { useNavigate } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import MetricCard from '../../components/cards/MetricCard';
import { useCspDashboardAvailable } from '../../components/layout/useCspDashboardAvailable';
import {
  createCspInheritedComponent,
  importCspInheritedComponents,
  isUnavailable,
  listCspInheritedComponents,
  type CspComponentType,
  type CspInheritedComponent,
  type CspInheritedComponentStatus,
  type CspInheritedComponentsPage,
  type ListComponentsParams,
} from './api';
import ComponentDetailDrawer from './ComponentDetailDrawer';
import ComponentExtractionPreview from '../csp-onboarding/steps/ComponentExtractionPreview';
import type { AtoUploadResponse } from '../csp-onboarding/api';

const COMPONENT_TYPE_OPTIONS: CspComponentType[] = [
  'Infrastructure',
  'Platform',
  'Service',
  'Identity',
  'Network',
  'Storage',
  'Compute',
];

const STATUS_OPTIONS: { value: CspInheritedComponentStatus | ''; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Published', label: 'Published' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Archived', label: 'Archived' },
];

const PAGE_SIZE = 50;
const MAX_FILE_BYTES = 50 * 1024 * 1024;
const ACCEPT = '.pdf,.docx,.json,.xlsx,.zip';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; page: CspInheritedComponentsPage }
  | { kind: 'unavailable'; reason: string }
  | { kind: 'error'; message: string };

/**
 * `CspInheritedComponentsPage` — Feature 048 / US9 / T214.
 *
 * Top-level management page for CSP-inherited components mounted at
 * `/csp/inherited-components`. Read-only to every authenticated user in
 * `MultiTenant` deployments (FR-104); CSP-Admins additionally see the
 * `Draft`/`Archived` rows, an Import button, and the in-drawer
 * Edit/Publish/Archive/Remap/Resolve controls.
 *
 * Self-hides defensively in `SingleTenant` deployments and while CSP
 * onboarding is not yet `Active` — the API surfaces 404
 * `SINGLE_TENANT_MODE` and 503 `CSP_ONBOARDING_INCOMPLETE` envelopes,
 * which we translate into a friendly fallback panel.
 */
export default function CspInheritedComponentsPage(): ReactElement {
  const navigate = useNavigate();
  const canManage = useCspDashboardAvailable() === true; // true only for CSP-Admin in active MT deployment
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState<CspInheritedComponentStatus | ''>(
    canManage ? '' : 'Published',
  );
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [importResult, setImportResult] = useState<AtoUploadResponse | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);
  const importInputRef = useRef<HTMLInputElement>(null);
  const [createOpen, setCreateOpen] = useState(false);

  // Debounce the search box so we don't hammer the API on every keystroke.
  useEffect(() => {
    const t = window.setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => window.clearTimeout(t);
  }, [search]);

  const params = useMemo<ListComponentsParams>(
    () => ({
      page,
      pageSize: PAGE_SIZE,
      status: statusFilter || undefined,
      search: debouncedSearch || undefined,
    }),
    [page, statusFilter, debouncedSearch],
  );

  const reload = useCallback(() => {
    let cancelled = false;
    setState({ kind: 'loading' });
    listCspInheritedComponents(params)
      .then((result) => {
        if (cancelled) return;
        if (isUnavailable(result)) {
          setState({ kind: 'unavailable', reason: result.reason });
          return;
        }
        setState({ kind: 'ready', page: result });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : 'Failed to load components.';
        setState({ kind: 'error', message });
      });
    return () => {
      cancelled = true;
    };
  }, [params]);

  useEffect(() => {
    return reload();
  }, [reload]);

  const handleImport = async (e: ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    if (importInputRef.current) importInputRef.current.value = '';
    if (files.length === 0) return;
    const oversized = files.find((f) => f.size > MAX_FILE_BYTES);
    if (oversized) {
      setImportError(`${oversized.name} exceeds the 50 MB per-file limit.`);
      return;
    }
    setImporting(true);
    setImportError(null);
    try {
      const result = await importCspInheritedComponents(files);
      setImportResult(result);
      reload();
    } catch (err) {
      const ex = err as { errorCode?: string; message?: string };
      setImportError(
        ex?.errorCode === 'CSP_ONBOARDING_INCOMPLETE'
          ? 'Complete CSP onboarding before importing additional ATO documents.'
          : (ex?.message ?? 'Import failed.'),
      );
    } finally {
      setImporting(false);
    }
  };

  if (state.kind === 'unavailable') {
    return (
      <PageLayout title="CSP Inherited">
        <div className="rounded-md border border-gray-200 bg-white p-6 text-sm text-gray-600">
          <p>
            CSP-inherited components are not available in this deployment
            (<span className="font-mono text-gray-700">{state.reason}</span>).
          </p>
          <button
            type="button"
            onClick={() => navigate('/', { replace: true })}
            className="mt-3 rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
          >
            Return to dashboard
          </button>
        </div>
      </PageLayout>
    );
  }

  return (
    <PageLayout title="CSP Inherited">
      <div data-testid="csp-inherited-components-page">
        <PageHero
          eyebrow="CSP Catalog"
          title="CSP Components"
          description="The hosting CSP's catalog of building blocks (Infrastructure, Platform, Service, Identity, Network, Storage, Compute). Every tenant inherits this read-only catalog and can map their systems against it; only CSP administrators can edit, publish, archive, or resolve needs-review items."
          actions={
            canManage ? (
              <>
                <button
                  type="button"
                  onClick={() => setCreateOpen(true)}
                  className="inline-flex items-center rounded-md border border-white bg-white px-4 py-1.5 text-sm font-medium text-indigo-700 hover:bg-indigo-50"
                  data-testid="csp-inherited-components-create-button"
                >
                  + Create component
                </button>
                <button
                  type="button"
                  onClick={() => importInputRef.current?.click()}
                  disabled={importing}
                  className="inline-flex items-center rounded-md bg-indigo-900/40 px-4 py-1.5 text-sm font-medium text-white ring-1 ring-inset ring-white/40 hover:bg-indigo-900/60 disabled:cursor-not-allowed disabled:opacity-60"
                  data-testid="csp-inherited-components-import-button"
                >
                  {importing ? 'Importing…' : 'Import ATO documents'}
                </button>
                <input
                  ref={importInputRef}
                  type="file"
                  multiple
                  accept={ACCEPT}
                  className="hidden"
                  onChange={handleImport}
                  aria-label="Import ATO documents"
                />
              </>
            ) : undefined
          }
        />

        {/* Summary metrics — mirrors org `ComponentInventory`'s MetricCard
            strip so the CSP catalog page reads at-a-glance the same way as
            the org system-components page. */}
        {state.kind === 'ready' && (
          <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-4">
            <MetricCard title="Total" value={state.page.totalItems} />
            <MetricCard
              title="Published"
              value={state.page.items.filter((c) => c.status === 'Published').length}
            />
            <MetricCard
              title="Draft"
              value={state.page.items.filter((c) => c.status === 'Draft').length}
            />
            <MetricCard
              title="Archived"
              value={state.page.items.filter((c) => c.status === 'Archived').length}
            />
          </div>
        )}

        {/* Import error banner */}
        {importError && (
          <div role="alert" className="mb-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {importError}
          </div>
        )}

        {/* Import result preview */}
        {importResult && (
          <div className="mb-4 rounded-md border border-emerald-200 bg-emerald-50 p-3">
            <h2 className="text-sm font-semibold text-emerald-900">Import complete</h2>
            <div className="mt-2">
              <ComponentExtractionPreview result={importResult} />
            </div>
            <button
              type="button"
              onClick={() => setImportResult(null)}
              className="mt-2 text-xs font-medium text-emerald-800 hover:underline"
            >
              Dismiss
            </button>
          </div>
        )}

        {/* Toolbar — search + status filter, matches org `CapabilityLibrary`
            and `ComponentInventory` toolbar pattern (flex-wrap row above the
            content card, plain bordered inputs, no per-input label chrome). */}
        <div className="mb-4 flex flex-wrap items-center gap-3">
          <input
            type="text"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder="Search components..."
            maxLength={200}
            className="min-w-[200px] flex-1 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
          <select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value as CspInheritedComponentStatus | '');
              setPage(1);
            }}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          >
            {STATUS_OPTIONS
              // Non-CSP-Admins only ever see Published rows; hide irrelevant filters.
              .filter((o) => canManage || o.value === '' || o.value === 'Published')
              .map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
          </select>
        </div>

        {/* Table */}
        {state.kind === 'loading' && (
          <p className="text-sm text-gray-500">Loading components…</p>
        )}
        {state.kind === 'error' && (
          <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {state.message}
          </div>
        )}
        {state.kind === 'ready' && (
          <ComponentsTable
            page={state.page}
            onSelect={(id) => setSelectedId(id)}
            onPageChange={setPage}
          />
        )}
      </div>

      {/* Detail drawer */}
      {selectedId !== null && (
        <ComponentDetailDrawer
          componentId={selectedId}
          canManage={canManage}
          onClose={() => setSelectedId(null)}
          onMutated={reload}
        />
      )}

      {/* Create-component modal */}
      {createOpen && (
        <CreateComponentModal
          onClose={() => setCreateOpen(false)}
          onCreated={() => {
            setCreateOpen(false);
            reload();
          }}
        />
      )}
    </PageLayout>
  );
}

// ---------------------------------------------------------------------------
// CreateComponentModal — manual-create surface used by CSP-Admins to author a
// CSP-inherited component without uploading an ATO document.
//
// Visually mirrors the org-level `ComponentFormInline` (Name, Type, Status,
// Sub-Type, Description, Owner, Linked Capabilities). The CSP-inherited
// entity schema today only persists `Name`, `Description`, and
// `ComponentType` (cloud taxonomy: Infrastructure | Platform | Service |
// Identity | Network | Storage | Compute — distinct from the org
// Person/Place/Thing/Policy axis). Status is auto-stamped `Published` by
// the service. Sub-Type, Owner, and Linked Capabilities are shown for
// layout parity but disabled with explicit "Not stored at CSP scope"
// helper copy so a future schema extension (FR-008 follow-on) can land
// without UI churn.
// ---------------------------------------------------------------------------

function CreateComponentModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: () => void;
}): ReactElement {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [componentType, setComponentType] = useState<CspComponentType>('Service');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);
    if (!name.trim() || !description.trim()) {
      setError('Name and description are required.');
      return;
    }
    setSubmitting(true);
    try {
      await createCspInheritedComponent({
        name: name.trim(),
        description: description.trim(),
        componentType,
      });
      onCreated();
    } catch (err) {
      const ex = err as { errorCode?: string; message?: string };
      setError(
        ex?.errorCode === 'CSP_ONBOARDING_INCOMPLETE'
          ? 'Complete CSP onboarding before creating CSP-inherited components.'
          : (ex?.message ?? 'Create failed.'),
      );
    } finally {
      setSubmitting(false);
    }
  };

  // Visual-parity helper — see comment above the function. We render the
  // org-form's Sub-Type / Owner / Linked-Capabilities widgets in a
  // visually disabled state so CSP-Admins immediately see the surface
  // area without being misled into entering data the schema would drop.
  const PARITY_HELP = 'Not stored at CSP scope — display-only for layout parity with org-level components.';

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="csp-create-component-title"
      onClick={onClose}
      data-testid="csp-create-component-modal"
    >
      <form
        onSubmit={handleSubmit}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-lg rounded-lg bg-white p-5 shadow-xl max-h-[90vh] overflow-y-auto"
      >
        <h2 id="csp-create-component-title" className="text-lg font-semibold text-gray-900">
          Create CSP-inherited component
        </h2>
        <p className="mt-1 text-xs text-gray-500">
          Manually-authored components publish immediately and become visible
          to every hosted organization with a violet “CSP” badge.
        </p>

        {error && (
          <div role="alert" className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </div>
        )}

        <div className="mt-4 space-y-3">
          {/* Name (active) */}
          <div>
            <label className="block text-sm font-medium text-gray-700">Name *</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={256}
              required
              autoFocus
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-create-component-name"
            />
          </div>

          {/* Type / Status row */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700">Type *</label>
              <select
                value={componentType}
                onChange={(e) => setComponentType(e.target.value as CspComponentType)}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                data-testid="csp-create-component-type"
              >
                {COMPONENT_TYPE_OPTIONS.map((t) => (
                  <option key={t} value={t}>{t}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-400">Status</label>
              <select
                value="Published"
                disabled
                aria-disabled
                title="CSP-inherited components publish immediately on create."
                className="mt-1 block w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-500 cursor-not-allowed"
              >
                <option value="Published">Published</option>
              </select>
              <p className="mt-0.5 text-[11px] text-gray-400">Auto-stamped at create.</p>
            </div>
          </div>

          {/* Sub-Type (visual parity, disabled) */}
          <div>
            <label className="block text-sm font-medium text-gray-400">Sub-Type</label>
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

          {/* Description (active) */}
          <div>
            <label className="block text-sm font-medium text-gray-700">Description *</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={2000}
              required
              rows={3}
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-create-component-description"
            />
          </div>

          {/* Owner (visual parity, disabled) */}
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

          {/* Linked Capabilities (visual parity, disabled) */}
          <div>
            <label className="block text-sm font-medium text-gray-400">Linked Capabilities</label>
            <input
              type="text"
              value=""
              disabled
              aria-disabled
              placeholder="Search capabilities…"
              className="block w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-1.5 text-sm text-gray-500 cursor-not-allowed"
            />
            <div className="mt-1 max-h-20 overflow-y-auto rounded-md border border-gray-200 bg-gray-50 p-2">
              <p className="text-xs italic text-gray-400">
                Capabilities are added per-component from the detail drawer
                after create. CSP capabilities are owned 1:N by their parent
                component — they are not linked many-to-many.
              </p>
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
            disabled={submitting || !name.trim()}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
            data-testid="csp-create-component-submit"
          >
            {submitting ? 'Creating…' : 'Create'}
          </button>
        </div>
      </form>
    </div>
  );
}

function ComponentsTable({
  page,
  onSelect,
  onPageChange,
}: {
  page: CspInheritedComponentsPage;
  onSelect: (id: string) => void;
  onPageChange: (next: number) => void;
}): ReactElement {
  if (page.items.length === 0) {
    return (
      <p className="rounded-md border border-gray-200 bg-white px-3 py-6 text-center text-sm text-gray-500">
        No components match the current filters.
      </p>
    );
  }

  return (
    <>
      <div className="overflow-x-auto rounded-md border border-gray-200 bg-white">
        <table className="min-w-full divide-y divide-gray-200 text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">
                Name
              </th>
              <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">
                Type
              </th>
              <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">
                Source
              </th>
              <th scope="col" className="px-3 py-2 text-left font-medium text-gray-700">
                Status
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium text-gray-700">
                Mapped
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium text-gray-700">
                Needs review
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {page.items.map((c) => (
              <Row key={c.id} component={c} onSelect={onSelect} />
            ))}
          </tbody>
        </table>
      </div>

      <Pagination page={page} onPageChange={onPageChange} />
    </>
  );
}

function Row({
  component,
  onSelect,
}: {
  component: CspInheritedComponent;
  onSelect: (id: string) => void;
}): ReactElement {
  return (
    <tr
      className="cursor-pointer hover:bg-gray-50"
      onClick={() => onSelect(component.id)}
      data-testid={`csp-component-row-${component.id}`}
    >
      <td className="px-3 py-2 text-gray-900">
        <button
          type="button"
          className="text-left font-medium text-indigo-700 hover:underline"
          onClick={(e) => {
            e.stopPropagation();
            onSelect(component.id);
          }}
        >
          {component.name}
        </button>
        {component.description && (
          <p className="mt-0.5 line-clamp-1 text-xs text-gray-500">{component.description}</p>
        )}
      </td>
      <td className="px-3 py-2 text-gray-700">{component.componentType}</td>
      <td className="px-3 py-2 text-gray-700">
        {component.sourceFileName ?? component.sourceFormat}
      </td>
      <td className="px-3 py-2">
        <StatusBadge status={component.status} />
      </td>
      <td className="px-3 py-2 text-right text-gray-900">
        {component.capabilityMappedCount ?? 0}
      </td>
      <td className="px-3 py-2 text-right text-gray-900">
        {component.capabilityNeedsReviewCount ?? 0}
      </td>
    </tr>
  );
}

function Pagination({
  page,
  onPageChange,
}: {
  page: CspInheritedComponentsPage;
  onPageChange: (next: number) => void;
}): ReactElement | null {
  if (page.totalPages <= 1) return null;
  return (
    <div className="mt-3 flex items-center justify-between text-sm text-gray-600">
      <span>
        Showing {(page.page - 1) * page.pageSize + 1}–
        {Math.min(page.page * page.pageSize, page.totalItems)} of {page.totalItems}
      </span>
      <div className="flex items-center gap-2">
        <button
          type="button"
          disabled={page.page <= 1}
          onClick={() => onPageChange(page.page - 1)}
          className="rounded-md border border-gray-300 bg-white px-2 py-1 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Prev
        </button>
        <span>
          Page {page.page} of {page.totalPages}
        </span>
        <button
          type="button"
          disabled={page.page >= page.totalPages}
          onClick={() => onPageChange(page.page + 1)}
          className="rounded-md border border-gray-300 bg-white px-2 py-1 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Next
        </button>
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: CspInheritedComponentStatus }): ReactElement {
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
