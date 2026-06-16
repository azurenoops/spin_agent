import { useState, useEffect, useCallback, useRef } from 'react';
import apiClient from '../../../api/client';
import { linkCapabilities, getCapabilityLinks, removeCapabilityLink } from '../../../api/capabilityLinks';
import type { SystemCapabilityLink } from '../../../types/dashboard';

// ─── Types ─────────────────────────────────────────────────────────────────────

/** CSP-inherited capability from /api/dashboard/capability-library */
interface CspCapability {
  id: string;
  name: string;
  description: string;
  provider: string;
  componentName: string;
  controlCount: number;
  mappedControls: string[];
  isSubscribed: boolean;
}

/** Org-owned capability from /api/dashboard/capabilities */
interface OrgCapability {
  id: string;
  name: string;
  provider: string;
  category: string;
  implementationStatus: string;
}

/** Unified row rendered in the picker list */
interface CapabilityRow {
  id: string;
  name: string;
  subtitle: string;      // "provider · componentName" or "provider"
  badge: string;         // category / implementationStatus / control count
  source: 'csp' | 'org';
  alreadyLinked: boolean;
}

interface SecurityCapabilitiesProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

// ─── Component ─────────────────────────────────────────────────────────────────

export default function SecurityCapabilities({ systemId, onNext, onErrors }: SecurityCapabilitiesProps) {
  const [search, setSearch] = useState('');
  const [cspCapabilities, setCspCapabilities] = useState<CspCapability[]>([]);
  const [orgCapabilities, setOrgCapabilities] = useState<OrgCapability[]>([]);
  const [linkedItems, setLinkedItems] = useState<SystemCapabilityLink[]>([]);
  const [cspSubscribedIds, setCspSubscribedIds] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ─── Initial load: existing org-links + CSP subscriptions ──────────────────

  useEffect(() => {
    let cancelled = false;
    async function loadInitial() {
      try {
        const [linksRes, subsRes] = await Promise.all([
          getCapabilityLinks(systemId),
          apiClient
            .get<{ items: { capabilityId: string }[]; totalCount: number }>(
              `/systems/${systemId}/capability-subscriptions`,
            )
            .catch(() => ({ data: { items: [], totalCount: 0 } })),
        ]);
        if (!cancelled) {
          setLinkedItems(linksRes.items);
          setCspSubscribedIds(new Set(subsRes.data.items.map((s) => s.capabilityId)));
        }
      } catch {
        if (!cancelled) onErrors({ _form: ['Failed to load capability links'] });
      }
    }
    void loadInitial();
    return () => { cancelled = true; };
  }, [systemId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ─── Parallel fetch: CSP library FIRST, then Org capabilities ──────────────

  const fetchCapabilities = useCallback(
    async (query: string) => {
      setLoading(true);
      setLoadError(null);
      try {
        const [cspRes, orgRes] = await Promise.all([
          // CSP capability library — Mapped capabilities published by the CSP provider
          apiClient
            .get<{ items: CspCapability[]; totalCount: number }>('/capability-library', {
              params: { search: query || undefined, systemId, pageSize: 50 },
            })
            .catch(() => ({ data: { items: [], totalCount: 0 } })),
          // Org-owned capabilities — this organisation's own capability catalog
          apiClient
            .get<{ items: OrgCapability[] }>('/capabilities', {
              params: { search: query || undefined, pageSize: 50 },
            })
            .catch(() => ({ data: { items: [] } })),
        ]);
        setCspCapabilities(cspRes.data.items ?? []);
        setOrgCapabilities(orgRes.data.items ?? []);
      } catch {
        setLoadError('Failed to load capabilities. Please try again.');
        setCspCapabilities([]);
        setOrgCapabilities([]);
      } finally {
        setLoading(false);
      }
    },
    [systemId],
  );

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      void fetchCapabilities(search);
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [search, fetchCapabilities]);

  // ─── Selection ─────────────────────────────────────────────────────────────

  const toggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  // ─── Link / subscribe action ────────────────────────────────────────────────
  //
  // CSP capabilities  → POST /systems/{id}/capability-subscriptions (CapabilitySubscriptionEndpoints)
  // Org capabilities  → POST /systems/{id}/capability-links         (via linkCapabilities())

  const linkSelected = async (ids: Set<string>): Promise<void> => {
    if (ids.size === 0) return;

    const cspIds = [...ids].filter((id) => cspCapabilities.some((c) => c.id === id));
    const orgIds = [...ids].filter((id) => orgCapabilities.some((c) => c.id === id));

    const cspJobs = cspIds.map((capabilityId) =>
      apiClient
        .post(`/systems/${systemId}/capability-subscriptions`, { capabilityId })
        .then(() => {
          setCspSubscribedIds((prev) => new Set([...prev, capabilityId]));
        }),
    );

    const orgJob =
      orgIds.length > 0
        ? linkCapabilities(systemId, orgIds).then((result) => {
            setLinkedItems((prev) => [...prev, ...(result.items ?? [])]);
          })
        : Promise.resolve();

    await Promise.all([...cspJobs, orgJob]);
  };

  const handleLink = async () => {
    if (selectedIds.size === 0) return;
    setSaving(true);
    try {
      await linkSelected(selectedIds);
      setSelectedIds(new Set());
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to link capabilities';
      onErrors({ _form: [msg] });
    } finally {
      setSaving(false);
    }
  };

  // Auto-link any checked-but-unsaved capabilities, then advance
  const handleNext = async () => {
    if (selectedIds.size > 0) {
      setSaving(true);
      try {
        await linkSelected(selectedIds);
        setSelectedIds(new Set());
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'Failed to link capabilities';
        onErrors({ _form: [msg] });
        setSaving(false);
        return; // don't advance if save failed
      } finally {
        setSaving(false);
      }
    }
    onNext();
  };

  const handleRemove = async (linkId: string) => {
    try {
      await removeCapabilityLink(systemId, linkId);
      setLinkedItems((prev) => prev.filter((l) => l.id !== linkId));
    } catch {
      // silently ignore
    }
  };

  // ─── Unified row lists ──────────────────────────────────────────────────────

  const linkedCapIds = new Set(linkedItems.map((l) => l.capabilityId));

  const cspRows: CapabilityRow[] = cspCapabilities.map((c) => ({
    id: c.id,
    name: c.name,
    subtitle: `${c.provider} · ${c.componentName}`,
    badge: `${c.controlCount} controls`,
    source: 'csp',
    alreadyLinked: cspSubscribedIds.has(c.id) || c.isSubscribed,
  }));

  const orgRows: CapabilityRow[] = orgCapabilities.map((c) => ({
    id: c.id,
    name: c.name,
    subtitle: c.provider,
    badge: c.category,
    source: 'org',
    alreadyLinked: linkedCapIds.has(c.id),
  }));

  const totalRows = cspRows.length + orgRows.length;

  // ─── Render ─────────────────────────────────────────────────────────────────

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 2: Security Capabilities</h2>
      <p className="text-sm text-gray-500 mb-6">
        Link security capabilities to this system. CSP-inherited capabilities are shown first,
        followed by your organisation&apos;s own capabilities.
      </p>

      {/* ── Linked items ─────────────────────────────────────────────────────── */}
      {linkedItems.length > 0 && (
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-700 mb-2">
            Linked Capabilities ({linkedItems.length})
          </h3>
          <div className="space-y-1">
            {linkedItems.map((item) => (
              <div
                key={item.id}
                className="flex items-center justify-between rounded-md border border-green-200 bg-green-50 px-3 py-2 text-sm"
              >
                <div>
                  <span className="font-medium text-gray-900">{item.capabilityName}</span>
                  {item.category && (
                    <span className="ml-2 text-xs text-gray-500">{item.category}</span>
                  )}
                </div>
                <button
                  onClick={() => void handleRemove(item.id)}
                  className="text-red-500 hover:text-red-700 text-xs"
                >
                  Remove
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Search ───────────────────────────────────────────────────────────── */}
      <div className="mb-4">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          placeholder="Search CSP and org capabilities by name, provider, or category…"
        />
      </div>

      {/* ── Source legend ─────────────────────────────────────────────────────── */}
      <div className="mb-3 flex items-center gap-4 text-xs text-gray-500">
        <span className="flex items-center gap-1.5">
          <span className="inline-block h-3 w-3 rounded-full border-2 border-indigo-400 bg-indigo-50" />
          CSP-inherited
        </span>
        <span className="flex items-center gap-1.5">
          <span className="inline-block h-3 w-3 rounded-full border-2 border-gray-300 bg-white" />
          Org capability
        </span>
      </div>

      {/* ── Results list ──────────────────────────────────────────────────────── */}
      <div className="border border-gray-200 rounded-md max-h-72 overflow-y-auto">
        {loading ? (
          <p className="p-4 text-sm text-gray-500">Searching…</p>
        ) : loadError ? (
          <p className="p-4 text-sm text-red-500">{loadError}</p>
        ) : totalRows === 0 ? (
          <p className="p-4 text-sm text-gray-400">No capabilities found</p>
        ) : (
          <>
            {/* CSP group — rendered first */}
            {cspRows.length > 0 && (
              <>
                <div className="sticky top-0 z-10 bg-indigo-50 px-3 py-1.5 border-b border-indigo-100">
                  <span className="text-xs font-semibold text-indigo-700 uppercase tracking-wide">
                    CSP-Inherited ({cspRows.length})
                  </span>
                </div>
                {cspRows.map((row) => (
                  <CapabilityRowItem
                    key={row.id}
                    row={row}
                    checked={selectedIds.has(row.id)}
                    onToggle={() => !row.alreadyLinked && toggleSelect(row.id)}
                  />
                ))}
              </>
            )}

            {/* Org group — rendered second */}
            {orgRows.length > 0 && (
              <>
                <div className="sticky top-0 z-10 bg-gray-50 px-3 py-1.5 border-b border-gray-100">
                  <span className="text-xs font-semibold text-gray-600 uppercase tracking-wide">
                    Org Capabilities ({orgRows.length})
                  </span>
                </div>
                {orgRows.map((row) => (
                  <CapabilityRowItem
                    key={row.id}
                    row={row}
                    checked={selectedIds.has(row.id)}
                    onToggle={() => !row.alreadyLinked && toggleSelect(row.id)}
                  />
                ))}
              </>
            )}
          </>
        )}
      </div>

      {/* ── Link button ───────────────────────────────────────────────────────── */}
      {selectedIds.size > 0 && (
        <button
          onClick={() => void handleLink()}
          disabled={saving}
          className="mt-3 rounded-md bg-green-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
        >
          {saving ? 'Linking…' : `Link ${selectedIds.size} Selected`}
        </button>
      )}

      <div className="mt-6 flex justify-end">
        <button
          onClick={() => void handleNext()}
          disabled={saving}
          className="rounded-md bg-indigo-600 px-6 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Next'}
        </button>
      </div>
    </div>
  );
}

// ─── Row sub-component ──────────────────────────────────────────────────────────

function CapabilityRowItem({
  row,
  checked,
  onToggle,
}: {
  row: CapabilityRow;
  checked: boolean;
  onToggle: () => void;
}) {
  const isCsp = row.source === 'csp';
  const borderClass = isCsp ? 'border-b border-indigo-50' : 'border-b border-gray-100';
  const hoverClass = row.alreadyLinked ? 'opacity-50' : 'hover:bg-gray-50 cursor-pointer';
  const leftAccentClass = isCsp ? 'border-l-2 border-l-indigo-400 pl-2' : 'pl-3';

  return (
    <label className={`flex items-center gap-3 px-3 py-2 text-sm ${borderClass} ${hoverClass}`}>
      <input
        type="checkbox"
        checked={checked}
        onChange={onToggle}
        disabled={row.alreadyLinked}
        className="rounded"
      />
      <div className={`flex flex-1 items-center justify-between gap-2 ${leftAccentClass}`}>
        <div className="min-w-0">
          <span className="font-medium text-gray-900 truncate">{row.name}</span>
          <span className="ml-2 text-xs text-gray-500">{row.subtitle}</span>
        </div>
        <div className="flex items-center gap-2 flex-shrink-0">
          {row.alreadyLinked && (
            <span className="text-xs font-medium text-green-700 bg-green-50 rounded px-1.5 py-0.5">
              ✓ Linked
            </span>
          )}
          <span className="text-xs text-gray-400">{row.badge}</span>
          {isCsp && (
            <span className="text-xs font-medium text-indigo-600 bg-indigo-50 rounded px-1.5 py-0.5">
              CSP
            </span>
          )}
        </div>
      </div>
    </label>
  );
}
