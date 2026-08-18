// =============================================================================
// captionRegistry.ts — Phase 4 (#1458)
//
// Session-scoped Caption Registry.
//
// Responsibilities:
//   - Deduplicate captions on insert: a second insert for the same figure_id
//     is silently dropped (returns the existing entry).
//   - Back-link each caption to its source_id (may be undefined for manual captions).
//   - Provide a React context + hook so any component can read or register captions.
//
// Scope: session only — registry is not persisted and resets on page reload.
// =============================================================================

import React, {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
} from 'react';
import type { CaptionEntry } from '../types/provenance';

// ── Pure registry operations ──────────────────────────────────────────────────

/**
 * Insert a caption into the registry map. If the figure_id already exists,
 * returns the existing entry unchanged (dedup-on-insert).
 *
 * @param registry - current registry map (figure_id → CaptionEntry)
 * @param entry    - candidate entry to insert
 * @returns [updatedRegistry, wasInserted]
 */
export function registryInsert(
  registry: Map<string, CaptionEntry>,
  entry: Omit<CaptionEntry, 'registered_at'>
): [Map<string, CaptionEntry>, boolean] {
  if (registry.has(entry.figure_id)) {
    return [registry, false]; // duplicate — no change
  }
  const full: CaptionEntry = { ...entry, registered_at: new Date().toISOString() };
  const next = new Map(registry);
  next.set(entry.figure_id, full);
  return [next, true];
}

/**
 * Look up a caption by figure_id. Returns undefined for unknown ids (graceful
 * fallback — callers must render nothing, not throw).
 */
export function registryLookup(
  registry: Map<string, CaptionEntry>,
  figure_id: string
): CaptionEntry | undefined {
  return registry.get(figure_id);
}

/**
 * Return all captions that back-link to a given source_id.
 */
export function captionsBySource(
  registry: Map<string, CaptionEntry>,
  source_id: string
): CaptionEntry[] {
  return Array.from(registry.values()).filter((e) => e.source_id === source_id);
}

// ── React context ─────────────────────────────────────────────────────────────

export interface CaptionRegistryContextValue {
  /** All registered captions (figure_id → CaptionEntry). */
  registry: Map<string, CaptionEntry>;
  /**
   * Register a caption. Dedup on insert — calling this for an existing
   * figure_id is a no-op; the existing entry is returned.
   */
  register: (entry: Omit<CaptionEntry, 'registered_at'>) => CaptionEntry;
  /** Look up a caption by figure_id. Returns undefined if not registered. */
  lookup: (figure_id: string) => CaptionEntry | undefined;
  /** All captions that reference a given source_id. */
  bySource: (source_id: string) => CaptionEntry[];
}

const CaptionRegistryContext = createContext<CaptionRegistryContextValue | undefined>(
  undefined
);

// ── Provider ──────────────────────────────────────────────────────────────────

interface CaptionRegistryProviderProps {
  children: React.ReactNode;
}

export function CaptionRegistryProvider({ children }: CaptionRegistryProviderProps) {
  const [registry, setRegistry] = useState<Map<string, CaptionEntry>>(new Map());

  // Stable ref so callbacks don't close over stale registry
  const registryRef = useRef(registry);
  registryRef.current = registry;

  const register = useCallback(
    (entry: Omit<CaptionEntry, 'registered_at'>): CaptionEntry => {
      const existing = registryRef.current.get(entry.figure_id);
      if (existing) return existing;

      const full: CaptionEntry = { ...entry, registered_at: new Date().toISOString() };
      setRegistry((prev) => {
        if (prev.has(entry.figure_id)) return prev; // double-check under React batching
        const next = new Map(prev);
        next.set(entry.figure_id, full);
        return next;
      });
      return full;
    },
    []
  );

  const lookup = useCallback(
    (figure_id: string) => registryRef.current.get(figure_id),
    []
  );

  const bySource = useCallback(
    (source_id: string) =>
      Array.from(registryRef.current.values()).filter((e) => e.source_id === source_id),
    []
  );

  const value = useMemo<CaptionRegistryContextValue>(
    () => ({ registry, register, lookup, bySource }),
    [registry, register, lookup, bySource]
  );

  return (
    <CaptionRegistryContext.Provider value={value}>
      {children}
    </CaptionRegistryContext.Provider>
  );
}

// ── Hook ──────────────────────────────────────────────────────────────────────

export function useCaptionRegistry(): CaptionRegistryContextValue {
  const ctx = useContext(CaptionRegistryContext);
  if (!ctx) {
    throw new Error('useCaptionRegistry must be used inside <CaptionRegistryProvider>');
  }
  return ctx;
}
