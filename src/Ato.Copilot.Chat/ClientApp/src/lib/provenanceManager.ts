// =============================================================================
// provenanceManager.ts — Phase 1 (#940, #939)
//
// Pure functions that create and mutate ProvenanceState objects.
// Framework-agnostic — works with any editor that can store node attributes.
//
// Key invariants:
//   1. history is append-only: no entry is ever deleted.
//   2. source_id linkage survives split, merge, and user edits.
//   3. user_modified=true degrades confidence display; it does NOT break the link.
//   4. Legacy nodes (no ProvenanceState) are returned as-is (graceful fallback).
// =============================================================================

import type {
  ProvenanceSpan,
  ProvenanceState,
  ProvenanceHistoryEntry,
} from '../types/provenance';

// ── Factory ───────────────────────────────────────────────────────────────────

/**
 * Create a fresh ProvenanceState for a newly generated AI span.
 * Called at the moment the span is inserted into the editor.
 */
export function createProvenanceState(span: ProvenanceSpan): ProvenanceState {
  return { span, user_modified: false, history: [] };
}

// ── Mutations ─────────────────────────────────────────────────────────────────

/**
 * Mark a span as user-modified (e.g. the user typed inside it).
 * Does not change source_id or history — purely sets the flag.
 * Returns a new object; the caller is responsible for persisting it.
 */
export function markUserModified(state: ProvenanceState): ProvenanceState {
  if (state.user_modified) return state; // already marked — no-op
  const entry: ProvenanceHistoryEntry = { kind: 'edit', at: new Date().toISOString() };
  return { ...state, user_modified: true, history: [...state.history, entry] };
}

/**
 * Record that this span was split into two at a given character position.
 * The sibling_source_id is the source_id of the newly created sibling span.
 * Both halves inherit the original span's source_id link.
 */
export function recordSplit(
  state: ProvenanceState,
  sibling_source_id: string
): ProvenanceState {
  const entry: ProvenanceHistoryEntry = {
    kind: 'split',
    at: new Date().toISOString(),
    sibling_source_id,
  };
  return { ...state, history: [...state.history, entry] };
}

/**
 * Record that another span was merged into this one.
 * The consumed span's source_id is recorded for audit purposes.
 */
export function recordMerge(
  state: ProvenanceState,
  merged_source_id: string
): ProvenanceState {
  const entry: ProvenanceHistoryEntry = {
    kind: 'merge',
    at: new Date().toISOString(),
    merged_source_id,
  };
  return { ...state, history: [...state.history, entry] };
}

// ── Accessors ─────────────────────────────────────────────────────────────────

/**
 * Returns the effective confidence to display.
 * Degrades to half when user_modified=true to signal reduced reliability.
 * Legacy nodes (undefined state) return null — callers must hide badges.
 */
export function effectiveConfidence(state: ProvenanceState | undefined): number | null {
  if (!state) return null;
  return state.user_modified ? state.span.confidence * 0.5 : state.span.confidence;
}

/**
 * Type guard: returns true if a node attribute is a valid ProvenanceState.
 * Use this before reading provenance from any editor node to ensure
 * legacy/un-provenanced content does not throw.
 */
export function isProvenanceState(value: unknown): value is ProvenanceState {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.user_modified === 'boolean' &&
    Array.isArray(v.history) &&
    typeof v.span === 'object' &&
    v.span !== null
  );
}
