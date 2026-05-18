import { useState, useEffect, useRef } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import { useSettings } from '../hooks/useSettings';
import apiClient from '../api/client';
import {
  type OrgControlOverrideDto,
  listOrgControlOverrides,
} from '../api/orgControlOverrides';
import OrgControlOverridePanel from '../features/orgs/OrgControlOverridePanel';

// ─── Types ──────────────────────────────────────────────────────────────────

interface Framework {
  id: string;
  identifier: string;
  name: string;
  version: string;
  publisher: string;
  controlCount: number;
  importedAt: string | null;
  baselines: { id: string; level: string; controlCount: number; importedAt: string | null }[];
}

interface FrameworkControl {
  id: string;
  family: string;
  familyName: string;
  title: string;
  type: 'Control' | 'Enhancement';
  baselines: Record<string, boolean>;
}

interface FrameworkControlsResponse {
  items: FrameworkControl[];
  total: number;
  page: number;
  pageSize: number;
  frameworkId: string;
}

interface FrameworkControlDetail {
  id: string;
  family: string;
  familyName: string;
  title: string;
  description: string;
  type: string;
  parentControlId: string | null;
  withdrawn: boolean;
  withdrawnTo: string | null;
  baselines: Record<string, boolean>;
  enhancements: { controlId: string; title: string }[];
  framework: { identifier: string; name: string; version: string };
}

interface ImportFrameworksResponse {
  frameworksImported: number;
  totalControls: number;
  totalBaselines: number;
  errors: string[];
}

type SelectionView = 'all' | 'additional' | 'removed';

const FAMILIES = [
  'AC', 'AT', 'AU', 'CA', 'CM', 'CP', 'IA', 'IR', 'MA', 'MP',
  'PE', 'PL', 'PM', 'PS', 'PT', 'RA', 'SA', 'SC', 'SI', 'SR',
];

const FAMILY_NAMES: Record<string, string> = {
  AC: 'Access Control', AT: 'Awareness and Training', AU: 'Audit and Accountability',
  CA: 'Assessment, Authorization, and Monitoring', CM: 'Configuration Management',
  CP: 'Contingency Planning', IA: 'Identification and Authentication',
  IR: 'Incident Response', MA: 'Maintenance', MP: 'Media Protection',
  PE: 'Physical and Environmental Protection', PL: 'Planning',
  PM: 'Program Management', PS: 'Personnel Security',
  PT: 'PII Processing and Transparency', RA: 'Risk Assessment',
  SA: 'System and Services Acquisition', SC: 'System and Communications Protection',
  SI: 'System and Information Integrity', SR: 'Supply Chain Risk Management',
};

const PAGE_SIZE = 50;

async function fetchFrameworks(): Promise<Framework[]> {
  const { data } = await apiClient.get<Framework[]>('/frameworks');
  return data;
}

async function fetchFrameworkControls(frameworkId: string, params: {
  search?: string;
  family?: string;
  page: number;
  pageSize?: number;
}): Promise<FrameworkControlsResponse> {
  const query = new URLSearchParams();
  if (params.search) query.set('search', params.search);
  if (params.family) query.set('family', params.family);
  query.set('page', String(params.page));
  query.set('pageSize', String(params.pageSize ?? PAGE_SIZE));
  const { data } = await apiClient.get<FrameworkControlsResponse>(`/frameworks/${encodeURIComponent(frameworkId)}/controls?${query}`);
  return data;
}

// Legacy endpoint fallback for when no frameworks are imported yet
async function fetchLegacyControls(params: {
  search?: string;
  family?: string;
  page: number;
}): Promise<FrameworkControlsResponse> {
  const query = new URLSearchParams();
  if (params.search) query.set('search', params.search);
  if (params.family) query.set('family', params.family);
  query.set('page', String(params.page));
  query.set('pageSize', String(PAGE_SIZE));
  const { data } = await apiClient.get<{ items: FrameworkControl[]; total: number; page: number; pageSize: number }>(`/controls?${query}`);
  return { ...data, frameworkId: 'legacy' };
}

async function fetchFrameworkControlDetail(frameworkId: string, controlId: string): Promise<FrameworkControlDetail> {
  const { data } = await apiClient.get<FrameworkControlDetail>(
    `/frameworks/${encodeURIComponent(frameworkId)}/controls/${encodeURIComponent(controlId)}`
  );
  return data;
}

function CheckIcon() {
  return (
    <svg className="mx-auto h-4 w-4 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
    </svg>
  );
}

function DashIcon() {
  return <span className="mx-auto block h-4 w-4 text-center text-gray-300">—</span>;
}

// ─── Detail Drawer ──────────────────────────────────────────────────────────

function ControlDetailDrawer({ controlId, frameworkId, onClose }: { controlId: string; frameworkId: string; onClose: () => void }) {
  const [detail, setDetail] = useState<FrameworkControlDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!frameworkId) return;
    setLoading(true);
    fetchFrameworkControlDetail(frameworkId, controlId)
      .then(setDetail)
      .catch(() => setDetail(null))
      .finally(() => setLoading(false));
  }, [controlId, frameworkId]);

  const baselineKeys = detail ? Object.keys(detail.baselines) : [];

  return (
    <div className="fixed inset-0 z-50 flex justify-end" onClick={onClose}>
      <div className="absolute inset-0 bg-black/20" />
      <div
        className="relative w-full max-w-2xl bg-white shadow-xl overflow-y-auto"
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="sticky top-0 z-10 flex items-center justify-between border-b bg-white px-6 py-4">
          <div>
            <h2 className="text-lg font-bold text-gray-900">{controlId}</h2>
            {detail && <p className="text-sm text-gray-500">{detail.familyName}</p>}
          </div>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-20 text-gray-400">Loading...</div>
        ) : !detail ? (
          <div className="flex items-center justify-center py-20 text-gray-400">Control not found.</div>
        ) : (
          <div className="space-y-6 px-6 py-5">
            {/* Title & type */}
            <div>
              <h3 className="text-base font-semibold text-gray-900">{detail.title}</h3>
              <div className="mt-1 flex items-center gap-2">
                <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${
                  detail.type === 'Control' ? 'bg-indigo-50 text-indigo-700' : 'bg-gray-100 text-gray-600'
                }`}>{detail.type}</span>
                {detail.parentControlId && (
                  <span className="text-xs text-gray-500">Enhancement of {detail.parentControlId}</span>
                )}
                {detail.withdrawn && (
                  <span className="inline-block rounded bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
                    Withdrawn{detail.withdrawnTo ? ` → ${detail.withdrawnTo}` : ''}
                  </span>
                )}
              </div>
            </div>

            {/* Framework source */}
            <div className="rounded-lg bg-gray-50 border border-gray-200 p-3">
              <span className="text-xs font-semibold text-gray-500 uppercase">Source Framework</span>
              <p className="text-sm text-gray-700 mt-0.5">{detail.framework.name} {detail.framework.version}</p>
            </div>

            {/* Description */}
            {detail.description && (
              <div>
                <h4 className="text-sm font-semibold text-gray-700 mb-1">Description</h4>
                <p className="text-sm text-gray-600 leading-relaxed whitespace-pre-wrap">{detail.description}</p>
              </div>
            )}

            {/* Baseline Applicability — dynamic from DB */}
            {baselineKeys.length > 0 && (
              <div>
                <h4 className="text-sm font-semibold text-gray-700 mb-2">Baseline Applicability</h4>
                <div className="rounded-lg border border-gray-200 p-3">
                  <div className="space-y-1 text-sm">
                    {baselineKeys.map(key => (
                      <div key={key} className="flex justify-between">
                        <span className="text-gray-600">{key}</span>
                        {detail.baselines[key] ? <CheckIcon /> : <DashIcon />}
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            )}

            {/* Child Enhancements */}
            {detail.enhancements.length > 0 && (
              <div>
                <h4 className="text-sm font-semibold text-gray-700 mb-2">
                  Control Enhancements ({detail.enhancements.length})
                </h4>
                <div className="space-y-1">
                  {detail.enhancements.map(e => (
                    <div key={e.controlId} className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-gray-50">
                      <span className="font-medium text-gray-800 whitespace-nowrap">{e.controlId}</span>
                      <span className="text-gray-600">{e.title}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Framework name to identifier mapping ───────────────────────────────────

const FRAMEWORK_IDENTIFIER_MAP: Record<string, string> = {
  'NIST 800-53 Rev. 5': 'NIST-800-53-R5',
  'NIST 800-53 Rev. 4': 'NIST-800-53-R4',
  'FedRAMP Rev. 5': 'FEDRAMP-R5',
  'CNSSI 1253': 'NIST-800-171-R3',
};

// ─── Client-side filter helper (for Additional / Removed tabs) ──────────────

function filterControls(controls: FrameworkControl[], search: string, family: string): FrameworkControl[] {
  return controls.filter(c => {
    if (family && c.family !== family) return false;
    if (search) {
      const s = search.toLowerCase();
      if (!c.id.toLowerCase().includes(s) && !c.title.toLowerCase().includes(s) && !c.familyName.toLowerCase().includes(s)) return false;
    }
    return true;
  });
}

// ─── Main Component ─────────────────────────────────────────────────────────

/**
 * Optional scope hint. When `csp` (rendered by the CSP-scope branch of
 * `ControlsRoute`), the page hero hides the active-org chip and reads as a
 * CSP-wide catalog rather than the per-organization view. Defaults to `org`.
 */
export interface ControlCatalogProps {
  scope?: 'org' | 'csp';
}

export default function ControlCatalog({ scope = 'org' }: ControlCatalogProps = {}) {
  const { settings, updateSettings } = useSettings();
  const [search, setSearch] = useState('');
  const [familyFilter, setFamilyFilter] = useState('');
  const [levelFilter, setLevelFilter] = useState('');
  const [page, setPage] = useState(1);
  const [view, setView] = useState<SelectionView>('all');
  const [selectedControlId, setSelectedControlId] = useState<string | null>(null);
  const [frameworks, setFrameworks] = useState<Framework[]>([]);
  const [importing, setImporting] = useState(false);
  const [importMessage, setImportMessage] = useState<{ tone: 'success' | 'warning' | 'error'; text: string } | null>(null);
  const [defaultSaved, setDefaultSaved] = useState(false);

  // Org-level overrides (Feature 048 follow-up — user ask #2). Map by
  // controlId → most recent server-side override row, used to render the
  // amber badge on rows the org has diverged on.
  const [overrides, setOverrides] = useState<Map<string, OrgControlOverrideDto>>(new Map());
  const [overrideControl, setOverrideControl] = useState<{ id: string; title?: string } | null>(null);

  // Direct fetch state — no polling (control catalog data is static)
  const [items, setItems] = useState<FrameworkControl[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);

  // Comparison state for Additional / Removed tabs
  const [additionalControls, setAdditionalControls] = useState<FrameworkControl[]>([]);
  const [removedControls, setRemovedControls] = useState<FrameworkControl[]>([]);
  const prevIdentifierRef = useRef<string | null>(null);
  const controlsCacheRef = useRef<Map<string, FrameworkControl[]>>(new Map());

  // Resolve the active framework identifier from settings
  const activeIdentifier = FRAMEWORK_IDENTIFIER_MAP[settings.activeFramework] ?? 'NIST-800-53-R5';
  const activeFramework = frameworks.find(f => f.identifier === activeIdentifier);

  // Fetch frameworks list on mount
  useEffect(() => {
    fetchFrameworks()
      .then(setFrameworks)
      .catch(() => setFrameworks([]));
  }, []);

  // Load org-level overrides once on mount; refresh when the panel saves.
  useEffect(() => {
    let cancelled = false;
    listOrgControlOverrides()
      .then((rows) => {
        if (cancelled) return;
        setOverrides(new Map(rows.map((r) => [r.controlId, r])));
      })
      .catch(() => {
        // Non-fatal: overrides simply won't be badged.
        if (!cancelled) setOverrides(new Map());
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Build dynamic baseline columns from the active framework's baselines
  const baselineCols: { key: string; label: string }[] = activeFramework
    ? activeFramework.baselines.map(b => ({ key: b.level, label: b.level }))
    : [];

  // ─── Fetch controls for "All" tab when params change ──────────────────
  useEffect(() => {
    if (!activeIdentifier) return;
    let cancelled = false;
    setLoading(true);

    (async () => {
      try {
        const result = await fetchFrameworkControls(activeIdentifier, {
          search: search || undefined,
          family: familyFilter || undefined,
          page,
        });
        if (!cancelled) {
          setItems(result.items);
          setTotal(result.total);
        }
      } catch {
        try {
          const legacy = await fetchLegacyControls({
            search: search || undefined,
            family: familyFilter || undefined,
            page,
          });
          if (!cancelled) {
            setItems(legacy.items);
            setTotal(legacy.total);
          }
        } catch {
          if (!cancelled) { setItems([]); setTotal(0); }
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => { cancelled = true; };
  }, [activeIdentifier, search, familyFilter, page]);

  // ─── Compute Additional / Removed when framework source changes ───────
  useEffect(() => {
    if (!activeIdentifier) return;
    let cancelled = false;

    (async () => {
      // Fetch ALL controls for the current framework if not cached
      let currentAll = controlsCacheRef.current.get(activeIdentifier);
      if (!currentAll) {
        try {
          const result = await fetchFrameworkControls(activeIdentifier, { page: 1, pageSize: 5000 });
          currentAll = result.items;
          controlsCacheRef.current.set(activeIdentifier, currentAll);
        } catch { return; }
      }
      if (cancelled) return;

      const prev = prevIdentifierRef.current;
      if (prev && prev !== activeIdentifier) {
        // Fetch ALL controls for the previous framework if not cached
        let prevAll = controlsCacheRef.current.get(prev);
        if (!prevAll) {
          try {
            const result = await fetchFrameworkControls(prev, { page: 1, pageSize: 5000 });
            prevAll = result.items;
            controlsCacheRef.current.set(prev, prevAll);
          } catch { prevAll = []; }
        }
        if (cancelled) return;

        const prevIds = new Set(prevAll.map(c => c.id));
        const currentIds = new Set(currentAll.map(c => c.id));

        // Additional = controls in new source that weren't in previous source
        setAdditionalControls(currentAll.filter(c => !prevIds.has(c.id)));
        // Removed = controls in previous source that aren't in new source
        setRemovedControls(prevAll.filter(c => !currentIds.has(c.id)));
      } else {
        setAdditionalControls([]);
        setRemovedControls([]);
      }

      prevIdentifierRef.current = activeIdentifier;
    })();

    return () => { cancelled = true; };
  }, [activeIdentifier]);

  // ─── Derive display items based on active view tab ────────────────────
  let displayItems: FrameworkControl[];
  let displayTotal: number;

  if (view === 'additional') {
    const filtered = filterControls(additionalControls, search, familyFilter);
    displayTotal = filtered.length;
    displayItems = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);
  } else if (view === 'removed') {
    const filtered = filterControls(removedControls, search, familyFilter);
    displayTotal = filtered.length;
    displayItems = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);
  } else {
    displayItems = items;
    displayTotal = total;
  }

  const totalPages = Math.ceil(displayTotal / PAGE_SIZE);

  const handleSearch = (value: string) => { setSearch(value); setPage(1); };
  const handleFamilyChange = (value: string) => { setFamilyFilter(value); setPage(1); };
  const handleLevelChange = (value: string) => { setLevelFilter(value); setPage(1); };

  const handleImportFrameworks = async () => {
    setImporting(true);
    setImportMessage(null);
    try {
      const { data } = await apiClient.post<ImportFrameworksResponse>('/frameworks/import');
      const fws = await fetchFrameworks();
      setFrameworks(fws);
      if (data.frameworksImported <= 0) {
        const reason = data.errors[0] ?? 'Framework import failed. Check MCP logs for connectivity errors.';
        setImportMessage({ tone: 'error', text: reason });
      } else if (data.errors.length > 0) {
        setImportMessage({
          tone: 'warning',
          text: `Imported ${data.frameworksImported} framework(s) with warnings: ${data.errors[0]}`,
        });
      } else {
        setImportMessage({
          tone: 'success',
          text: `Imported ${data.frameworksImported} framework(s), ${data.totalControls.toLocaleString()} controls, ${data.totalBaselines} baselines.`,
        });
      }
    } catch (error) {
      const message = typeof error === 'object' && error && 'error' in error
        ? String((error as { error?: string }).error)
        : 'Framework import failed. Verify network access to GitHub or use embedded fallback data.';
      setImportMessage({ tone: 'error', text: message });
    } finally {
      setImporting(false);
    }
  };

  return (
    <PageLayout title="Control Catalog">
      <PageHero
        eyebrow={scope === 'csp' ? 'Controls · CSP scope' : 'Controls'}
        title="Control Catalog"
        description={
          scope === 'csp'
            ? "Browse and manage the CSP-wide control catalog and its associated frameworks. Drop into an organization via impersonation to see per-organization implementation status."
            : "Browse and manage the organization's control catalog and its associated frameworks."
        }
        showOrgName={scope !== 'csp'}
      />
      <div className="space-y-4">
        {/* Import banner — show when no frameworks are imported yet */}
        {frameworks.length === 0 && !loading && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 flex items-center justify-between">
            <div>
              <p className="text-sm font-medium text-amber-800">No frameworks imported yet</p>
              <p className="text-xs text-amber-600 mt-0.5">Import official NIST and FedRAMP frameworks from OSCAL GitHub repositories.</p>
              {importMessage && (
                <p
                  className={`mt-2 text-xs ${
                    importMessage.tone === 'error'
                      ? 'text-red-700'
                      : importMessage.tone === 'warning'
                        ? 'text-amber-700'
                        : 'text-green-700'
                  }`}
                >
                  {importMessage.text}
                </p>
              )}
            </div>
            <button
              onClick={handleImportFrameworks}
              disabled={importing}
              className="rounded-md bg-amber-600 px-4 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-50"
            >
              {importing ? 'Importing...' : 'Import Frameworks'}
            </button>
          </div>
        )}

        {frameworks.length > 0 && importMessage && (
          <div
            className={`rounded-lg border p-3 text-sm ${
              importMessage.tone === 'error'
                ? 'border-red-200 bg-red-50 text-red-700'
                : importMessage.tone === 'warning'
                  ? 'border-amber-200 bg-amber-50 text-amber-700'
                  : 'border-green-200 bg-green-50 text-green-700'
            }`}
          >
            {importMessage.text}
          </div>
        )}

        {/* Toolbar card */}
        <div className="rounded-lg border border-gray-200 bg-white p-4 space-y-3">
          {/* Row 1: Tabs + Selected count + Buttons */}
          <div className="flex items-center justify-between">
            {/* All / Additional / Removed tabs */}
            <div className="flex gap-1">
              {([
                { key: 'all' as const, label: 'All', count: displayTotal, icon: 'M4 6h16M4 12h16M4 18h16' },
                { key: 'additional' as const, label: 'Additional', count: additionalControls.length, icon: 'M12 4v16m8-8H4' },
                { key: 'removed' as const, label: 'Removed', count: removedControls.length, icon: 'M6 18L18 6M6 6l12 12' },
              ]).map(t => (
                <button
                  key={t.key}
                  onClick={() => { setView(t.key); setPage(1); }}
                  className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                    view === t.key
                      ? 'bg-gray-900 text-white'
                      : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
                  }`}
                >
                  <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d={t.icon} />
                  </svg>
                  {t.label}
                  {t.count > 0 && <span className="ml-1 rounded-full bg-white/20 px-1.5 text-xs">{t.count}</span>}
                </button>
              ))}
            </div>

            {/* Right side: count + buttons */}
            <div className="flex items-center gap-3">
              <span className="text-sm text-gray-500">
                {displayTotal.toLocaleString()} controls
                {activeFramework && <span className="text-gray-400"> ({activeFramework.controlCount} in framework)</span>}
              </span>
              <button
                onClick={() => {
                  updateSettings({ activeFramework: settings.activeFramework });
                  setDefaultSaved(true);
                  setTimeout(() => setDefaultSaved(false), 2000);
                }}
                className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                  defaultSaved
                    ? 'bg-green-600 text-white'
                    : 'bg-indigo-600 text-white hover:bg-indigo-700'
                }`}
              >
                {defaultSaved ? '✓ Default Saved' : 'Set Default Selection'}
              </button>
            </div>
          </div>

          {/* Row 2: Source + Filters */}
          <div className="flex items-center justify-between gap-4">
            {/* Source label + framework selector */}
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium text-gray-500">Sources:</span>
              <select
                value={settings.activeFramework}
                onChange={e => { updateSettings({ activeFramework: e.target.value as typeof settings.activeFramework }); setPage(1); }}
                className="rounded-md border border-gray-300 py-1 px-2 text-sm font-medium text-gray-700 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              >
                <option value="NIST 800-53 Rev. 5">NIST 800-53 Rev. 5</option>
                <option value="NIST 800-53 Rev. 4">NIST 800-53 Rev. 4</option>
                <option value="FedRAMP Rev. 5">FedRAMP Rev. 5</option>
                <option value="CNSSI 1253">NIST 800-171 Rev. 3</option>
              </select>
            </div>

            {/* Filters */}
            <div className="flex items-center gap-2">
              <div className="relative">
                <svg className="absolute left-2.5 top-2 h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <input
                  type="text"
                  placeholder="Search..."
                  value={search}
                  onChange={e => handleSearch(e.target.value)}
                  className="rounded-md border border-gray-300 py-1.5 pl-8 pr-3 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 w-48"
                />
              </div>
              <select
                value={familyFilter}
                onChange={e => handleFamilyChange(e.target.value)}
                className="rounded-md border border-gray-300 py-1.5 px-3 text-sm text-gray-600 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              >
                <option value="">Filter control families...</option>
                {FAMILIES.map(f => (
                  <option key={f} value={f}>{f} — {FAMILY_NAMES[f]}</option>
                ))}
              </select>
              <select
                value={levelFilter}
                onChange={e => handleLevelChange(e.target.value)}
                className="rounded-md border border-gray-300 py-1.5 px-3 text-sm text-gray-600 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              >
                <option value="">Filter security level...</option>
                {baselineCols.map(b => (
                  <option key={b.key} value={b.key}>{b.label}</option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {/* Table */}
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead>
              <tr className="bg-gray-50">
                <th className="px-4 py-3 text-left font-semibold text-gray-700">Ref</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700">Family</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700 min-w-[200px]">Name</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700">Type</th>
                {baselineCols.length > 0 && (
                  <th className="px-2 py-1 text-center font-semibold text-gray-500 text-xs" colSpan={baselineCols.length}>
                    Baselines
                  </th>
                )}
                <th className="px-3 py-3 text-center font-semibold text-gray-700 w-16">Actions</th>
              </tr>
              {baselineCols.length > 0 && (
                <tr className="bg-gray-50 border-t border-gray-100">
                  <th colSpan={4} />
                  {baselineCols.map(col => (
                    <th key={col.key} className="px-2 py-1 text-center text-xs font-medium text-gray-500">{col.label}</th>
                  ))}
                  <th />
                </tr>
              )}
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading && displayItems.length === 0 ? (
                <tr>
                  <td colSpan={5 + baselineCols.length} className="px-4 py-12 text-center text-gray-400">
                    Loading control catalog...
                  </td>
                </tr>
              ) : displayItems.length === 0 ? (
                <tr>
                  <td colSpan={5 + baselineCols.length} className="px-4 py-12 text-center text-gray-400">
                    {view === 'additional' ? 'No additional controls found. Switch sources to see differences.'
                      : view === 'removed' ? 'No removed controls found. Switch sources to see differences.'
                      : 'No controls match the current filters.'}
                  </td>
                </tr>
              ) : (
                displayItems.map(c => (
                  <tr key={c.id} className="hover:bg-gray-50 transition-colors cursor-pointer" onClick={() => setSelectedControlId(c.id)}>
                    <td className="px-4 py-2.5 font-medium text-indigo-700 whitespace-nowrap">
                      <div className="flex items-center gap-2">
                        <span>{c.id}</span>
                        {overrides.has(c.id) && (
                          <span
                            className="inline-block rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-amber-800"
                            title="This org has overridden the CSP default for this control."
                          >
                            Org override
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="px-4 py-2.5 text-gray-600 whitespace-nowrap">{c.familyName}</td>
                    <td className="px-4 py-2.5 text-gray-700">{c.title}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${
                        c.type === 'Control' ? 'bg-indigo-50 text-indigo-700' : 'bg-gray-100 text-gray-600'
                      }`}>{c.type}</span>
                    </td>
                    {baselineCols.map(col => (
                      <td key={col.key} className="px-2 py-2.5 text-center">
                        {c.baselines[col.key] ? <CheckIcon /> : null}
                      </td>
                    ))}
                    <td className="px-3 py-2.5 text-center whitespace-nowrap">
                      <button
                        onClick={e => { e.stopPropagation(); setSelectedControlId(c.id); }}
                        className="text-sm text-indigo-600 hover:text-indigo-800 font-medium"
                        title="View details"
                      >
                        View
                      </button>
                      <span className="mx-1 text-gray-300">|</span>
                      <button
                        onClick={e => { e.stopPropagation(); setOverrideControl({ id: c.id, title: c.title }); }}
                        className="text-sm text-amber-700 hover:text-amber-900 font-medium"
                        title="Override CSP default for this org"
                      >
                        Override
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="flex items-center justify-between">
            <span className="text-sm text-gray-500">
              Showing {((page - 1) * PAGE_SIZE) + 1}–{Math.min(page * PAGE_SIZE, displayTotal)} of {displayTotal.toLocaleString()} controls
            </span>
            <div className="flex gap-1">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="rounded border border-gray-300 px-3 py-1 text-sm disabled:opacity-40 hover:bg-gray-50"
              >
                Previous
              </button>
              {Array.from({ length: Math.min(totalPages, 7) }, (_, i) => {
                let pageNum: number;
                if (totalPages <= 7) {
                  pageNum = i + 1;
                } else if (page <= 4) {
                  pageNum = i + 1;
                } else if (page >= totalPages - 3) {
                  pageNum = totalPages - 6 + i;
                } else {
                  pageNum = page - 3 + i;
                }
                return (
                  <button
                    key={pageNum}
                    onClick={() => setPage(pageNum)}
                    className={`rounded border px-3 py-1 text-sm ${
                      pageNum === page
                        ? 'border-indigo-600 bg-indigo-50 text-indigo-700 font-medium'
                        : 'border-gray-300 hover:bg-gray-50'
                    }`}
                  >
                    {pageNum}
                  </button>
                );
              })}
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="rounded border border-gray-300 px-3 py-1 text-sm disabled:opacity-40 hover:bg-gray-50"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Detail drawer */}
      {selectedControlId && (
        <ControlDetailDrawer
          controlId={selectedControlId}
          frameworkId={activeIdentifier}
          onClose={() => setSelectedControlId(null)}
        />
      )}

      {/* Org-override panel (Feature 048 follow-up — user ask #2) */}
      {overrideControl && (
        <OrgControlOverridePanel
          controlId={overrideControl.id}
          controlTitle={overrideControl.title}
          onClose={() => setOverrideControl(null)}
          onSaved={(saved) => {
            setOverrides((prev) => {
              const next = new Map(prev);
              if (saved) {
                next.set(saved.controlId, saved);
              } else if (overrideControl) {
                next.delete(overrideControl.id);
              }
              return next;
            });
          }}
        />
      )}
    </PageLayout>
  );
}
