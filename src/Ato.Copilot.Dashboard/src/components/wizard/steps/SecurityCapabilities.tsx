import { useState, useEffect, useCallback, useRef } from 'react';
import apiClient from '../../../api/client';
import { linkCapabilities, getCapabilityLinks, removeCapabilityLink } from '../../../api/capabilityLinks';
import type { SystemCapabilityLink } from '../../../types/dashboard';

interface Capability {
  id: string;
  name: string;
  provider: string;
  category: string;
  implementationStatus: string;
}

interface SecurityCapabilitiesProps {
  systemId: string;
  onNext: () => void;
  onErrors: (errors: Record<string, string[]>) => void;
}

export default function SecurityCapabilities({ systemId, onNext, onErrors }: SecurityCapabilitiesProps) {
  const [search, setSearch] = useState('');
  const [capabilities, setCapabilities] = useState<Capability[]>([]);
  const [linkedItems, setLinkedItems] = useState<SystemCapabilityLink[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Load existing links on mount
  useEffect(() => {
    getCapabilityLinks(systemId).then((res) => setLinkedItems(res.items)).catch(() => {});
  }, [systemId]);

  // Debounced search
  const fetchCapabilities = useCallback(async (query: string) => {
    setLoading(true);
    try {
      const { data } = await apiClient.get('/capabilities', {
        params: { search: query, pageSize: 50 },
      });
      setCapabilities(data.items ?? []);
    } catch {
      setCapabilities([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      fetchCapabilities(search);
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [search, fetchCapabilities]);

  const toggleSelect = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleLink = async () => {
    if (selectedIds.size === 0) return;
    setSaving(true);
    try {
      const result = await linkCapabilities(systemId, Array.from(selectedIds));
      setLinkedItems((prev) => [...prev, ...(result.items ?? [])]);
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
        const result = await linkCapabilities(systemId, Array.from(selectedIds));
        setLinkedItems((prev) => [...prev, ...(result.items ?? [])]);
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

  const linkedCapIds = new Set(linkedItems.map((l) => l.capabilityId));

  return (
    <div>
      <h2 className="text-xl font-semibold text-gray-900 mb-1">Step 2: Security Capabilities</h2>
      <p className="text-sm text-gray-500 mb-6">Search and link existing security capabilities to this system.</p>

      {/* Linked items */}
      {linkedItems.length > 0 && (
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-700 mb-2">Linked Capabilities ({linkedItems.length})</h3>
          <div className="space-y-1">
            {linkedItems.map((item) => (
              <div key={item.id} className="flex items-center justify-between rounded-md border border-green-200 bg-green-50 px-3 py-2 text-sm">
                <div>
                  <span className="font-medium text-gray-900">{item.capabilityName}</span>
                  {item.category && <span className="ml-2 text-xs text-gray-500">{item.category}</span>}
                </div>
                <button onClick={() => handleRemove(item.id)} className="text-red-500 hover:text-red-700 text-xs">Remove</button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Search */}
      <div className="mb-4">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          placeholder="Search capabilities by name or category..."
        />
      </div>

      {/* Results */}
      <div className="border border-gray-200 rounded-md max-h-64 overflow-y-auto">
        {loading ? (
          <p className="p-4 text-sm text-gray-500">Searching...</p>
        ) : capabilities.length === 0 ? (
          <p className="p-4 text-sm text-gray-400">No capabilities found</p>
        ) : (
          capabilities.map((cap) => {
            const alreadyLinked = linkedCapIds.has(cap.id);
            return (
              <label
                key={cap.id}
                className={`flex items-center gap-3 px-3 py-2 border-b border-gray-100 hover:bg-gray-50 text-sm ${alreadyLinked ? 'opacity-50' : 'cursor-pointer'}`}
              >
                <input
                  type="checkbox"
                  checked={selectedIds.has(cap.id)}
                  onChange={() => !alreadyLinked && toggleSelect(cap.id)}
                  disabled={alreadyLinked}
                  className="rounded"
                />
                <div className="flex-1">
                  <span className="font-medium text-gray-900">{cap.name}</span>
                  <span className="ml-2 text-xs text-gray-500">{cap.provider}</span>
                </div>
                <span className="text-xs text-gray-400">{cap.category}</span>
                <span className={`text-xs px-1.5 py-0.5 rounded ${cap.implementationStatus === 'Implemented' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600'}`}>
                  {cap.implementationStatus}
                </span>
              </label>
            );
          })
        )}
      </div>

      {selectedIds.size > 0 && (
        <button
          onClick={handleLink}
          disabled={saving}
          className="mt-3 rounded-md bg-green-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
        >
          {saving ? 'Linking...' : `Link ${selectedIds.size} Selected`}
        </button>
      )}

      <div className="mt-6 flex justify-end">
        <button
          onClick={handleNext}
          disabled={saving}
          className="rounded-md bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {saving ? 'Saving...' : 'Next'}
        </button>
      </div>
    </div>
  );
}
