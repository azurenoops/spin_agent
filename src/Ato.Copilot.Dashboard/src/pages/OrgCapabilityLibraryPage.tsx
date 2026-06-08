import { useState, useCallback, useEffect } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import apiClient from '../api/client';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface OrgCapability {
  id: string;
  name: string;
  description: string;
  provider: string;
  componentName: string;
  controlCount: number;
  mappedControls: string[];
  isSubscribed: boolean;
}

interface ListResponse {
  items: OrgCapability[];
  totalCount: number;
}

// ─── NIST control family labels ───────────────────────────────────────────────

const NIST_FAMILIES: Record<string, string> = {
  AC: 'Access Control', AT: 'Awareness & Training', AU: 'Audit & Accountability',
  CA: 'Assessment, Authorization & Monitoring', CM: 'Configuration Management',
  CP: 'Contingency Planning', IA: 'Identification & Authentication',
  IR: 'Incident Response', MA: 'Maintenance', MP: 'Media Protection',
  PE: 'Physical & Environmental Protection', PL: 'Planning',
  PM: 'Program Management', PS: 'Personnel Security',
  RA: 'Risk Assessment', SA: 'System & Services Acquisition',
  SC: 'System & Communications Protection', SI: 'System & Information Integrity',
};

const PROVIDER_OPTIONS = ['Infrastructure', 'Platform', 'Service', 'Identity'];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function controlFamilies(controls: string[]): string[] {
  const families = new Set<string>();
  for (const c of controls) {
    const family = c.split('-')[0].toUpperCase();
    if (family) families.add(family);
  }
  return Array.from(families).sort();
}

// ─── Capability card ──────────────────────────────────────────────────────────

function CapabilityCard({
  capability,
  systemId,
  onSubscribeToggle,
  subscribing,
}: {
  capability: OrgCapability;
  systemId?: string;
  onSubscribeToggle: (cap: OrgCapability) => Promise<void>;
  subscribing: boolean;
}) {
  const families = controlFamilies(capability.mappedControls);

  return (
    <div className="flex flex-col rounded-lg border border-gray-200 bg-white p-5 shadow-sm hover:shadow-md transition-shadow">
      {/* Header */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="text-sm font-semibold text-gray-900 truncate">{capability.name}</h3>
            {capability.isSubscribed && (
              <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                ✓ Subscribed
              </span>
            )}
          </div>
          <p className="mt-0.5 text-xs text-gray-500">{capability.provider} · {capability.componentName}</p>
        </div>
        <span className="flex-shrink-0 rounded-full bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700">
          {capability.controlCount} controls
        </span>
      </div>

      {/* Description */}
      <p className="mt-3 text-sm text-gray-600 line-clamp-2">{capability.description}</p>

      {/* Control families */}
      {families.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1">
          {families.slice(0, 6).map((f) => (
            <span key={f} className="rounded bg-gray-100 px-1.5 py-0.5 text-[11px] font-medium text-gray-600">
              {f}
            </span>
          ))}
          {families.length > 6 && (
            <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[11px] text-gray-400">
              +{families.length - 6}
            </span>
          )}
        </div>
      )}

      {/* Actions */}
      <div className="mt-4 flex items-center gap-3 border-t border-gray-100 pt-3">
        <Link
          to={`/capability-library/${capability.id}${systemId ? `?systemId=${systemId}` : ''}`}
          className="text-xs font-medium text-indigo-600 hover:text-indigo-800 hover:underline"
        >
          View Details →
        </Link>
        {systemId && (
          <button
            type="button"
            onClick={() => onSubscribeToggle(capability)}
            disabled={subscribing}
            className={`ml-auto rounded px-3 py-1.5 text-xs font-medium transition-colors ${
              capability.isSubscribed
                ? 'border border-gray-300 text-gray-700 hover:bg-gray-50'
                : 'bg-indigo-600 text-white hover:bg-indigo-700'
            }`}
          >
            {capability.isSubscribed ? 'Subscribed' : 'Subscribe to System'}
          </button>
        )}
      </div>
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

/**
 * UF-CSP-01 — Browse Capability Library & Subscribe (spec-070 T013–T016).
 *
 * Org-user view of the Published CSP capability catalog. Distinct from the
 * CSP-Admin CapabilityLibrary.tsx at /capabilities — this page is for ISSOs
 * and ISSMs to browse and subscribe capabilities to their systems.
 *
 * Route: /capability-library?systemId=<id>
 * systemId is optional — without it the page is read-only (no Subscribe button).
 */
export default function OrgCapabilityLibraryPage() {
  const [searchParams] = useSearchParams();
  const systemId = searchParams.get('systemId') ?? undefined;

  const [capabilities, setCapabilities] = useState<OrgCapability[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [providerFilter, setProviderFilter] = useState('');
  const [familyFilter, setFamilyFilter] = useState('');
  const [subscribing, setSubscribing] = useState(false);
  const [toast, setToast] = useState<{ message: string; type: 'success' | 'error' } | null>(null);

  const fetchCapabilities = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string> = {};
      if (search) params.search = search;
      if (providerFilter) params.provider = providerFilter;
      if (systemId) params.systemId = systemId;

      const res = await apiClient.get<ListResponse>('/capability-library', { params });
      setCapabilities(res.data.items);
    } catch {
      setCapabilities([]);
    } finally {
      setLoading(false);
    }
  }, [search, providerFilter, systemId]);

  useEffect(() => {
    void fetchCapabilities();
  }, [fetchCapabilities]);

  // Client-side family filter
  const filtered = familyFilter
    ? capabilities.filter((c) =>
        controlFamilies(c.mappedControls).includes(familyFilter))
    : capabilities;

  const handleSubscribeToggle = useCallback(async (cap: OrgCapability) => {
    if (!systemId) return;
    setSubscribing(true);
    try {
      if (cap.isSubscribed) {
        await apiClient.delete(`/systems/${systemId}/capability-subscriptions/${cap.id}`);
        setCapabilities((prev) =>
          prev.map((c) => c.id === cap.id ? { ...c, isSubscribed: false } : c));
        setToast({ message: `Unsubscribed from ${cap.name}`, type: 'success' });
      } else {
        await apiClient.post(`/systems/${systemId}/capability-subscriptions`, {
          capabilityId: cap.id,
        });
        setCapabilities((prev) =>
          prev.map((c) => c.id === cap.id ? { ...c, isSubscribed: true } : c));
        setToast({ message: `Subscribed to ${cap.name}`, type: 'success' });
      }
    } catch {
      setToast({ message: 'Action failed — please try again.', type: 'error' });
    } finally {
      setSubscribing(false);
      setTimeout(() => setToast(null), 3000);
    }
  }, [systemId]);

  // Available families from current results
  const allFamilies = Array.from(
    new Set(capabilities.flatMap((c) => controlFamilies(c.mappedControls))),
  ).sort();

  return (
    <PageLayout>
      <PageHero
        title="Capability Library"
        description="Browse CSP-published capabilities and subscribe to add inherited controls to your system's SSP."
      />

      {/* Toast */}
      {toast && (
        <div
          className={`mb-4 rounded-md px-4 py-3 text-sm font-medium ${
            toast.type === 'success' ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'
          }`}
        >
          {toast.message}
        </div>
      )}

      {/* Filters */}
      <div className="mb-5 flex flex-wrap items-center gap-3">
        <input
          type="text"
          placeholder="Search capabilities…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
        />
        <select
          value={providerFilter}
          onChange={(e) => setProviderFilter(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm"
        >
          <option value="">All providers</option>
          {PROVIDER_OPTIONS.map((p) => (
            <option key={p} value={p}>{p}</option>
          ))}
        </select>
        <select
          value={familyFilter}
          onChange={(e) => setFamilyFilter(e.target.value)}
          className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm"
        >
          <option value="">All control families</option>
          {allFamilies.map((f) => (
            <option key={f} value={f}>{f} — {NIST_FAMILIES[f] ?? f}</option>
          ))}
        </select>
        {systemId && (
          <span className="ml-auto rounded-full bg-indigo-50 px-2.5 py-0.5 text-xs text-indigo-700">
            System context: {systemId}
          </span>
        )}
      </div>

      {/* Results */}
      {loading ? (
        <div className="py-12 text-center text-sm text-gray-500">Loading capabilities…</div>
      ) : filtered.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 py-16 text-center">
          <p className="text-sm text-gray-500">No capabilities match your filters.</p>
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {filtered.map((cap) => (
            <CapabilityCard
              key={cap.id}
              capability={cap}
              systemId={systemId}
              onSubscribeToggle={handleSubscribeToggle}
              subscribing={subscribing}
            />
          ))}
        </div>
      )}
    </PageLayout>
  );
}
