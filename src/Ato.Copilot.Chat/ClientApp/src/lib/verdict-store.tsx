// =============================================================================
// Verdict Store — Issue #2437 / TraceabilityPanel
//
// Holds NLI VerificationResult objects keyed by messageId.
// Implemented as React context + reducer (no external state library).
//
// Verdict lifecycle:
//   pending  → result arrives → active (SUPPORTED / PARTIALLY_SUPPORTED /
//               INSUFFICIENT / CONTRADICTED / NO_EVIDENCE)
//
// Usage:
//   Wrap the app (or ChatWindow) in <VerdictStoreProvider>.
//   Components call useVerdictStore() to read/dispatch.
//   External code (SignalR handler, ClaimShield path) calls
//   verdictDispatch({ type: 'ADD_RESULT', payload: result }) to feed data.
// =============================================================================

import React, { createContext, useContext, useReducer } from 'react';

// ─── Types ────────────────────────────────────────────────────────────────────

export type VerdictClass =
  | 'SUPPORTED'
  | 'PARTIALLY_SUPPORTED'
  | 'INSUFFICIENT'
  | 'CONTRADICTED'
  | 'NO_EVIDENCE'
  | 'PENDING';

export interface VerificationResult {
  /** Unique id for this verification result row. */
  id: string;
  /** The messageId this result belongs to. */
  messageId: string;
  /** The claim sentence being verified. */
  claim_sentence: string;
  /** Verdict class. */
  verdict: VerdictClass;
  /** Title of the grounding source document. */
  source_title: string;
  /** Section / location reference within the source (e.g., "§3.2"). */
  source_section?: string;
  /** Calibrated confidence 0–1 (optional). */
  calibrated_confidence?: number;
  /** Model or path that produced this verdict (e.g., "nli_deberta"). */
  grounding_source?: string;
  /** Span IDs that contributed (multi-hop). */
  contributing_span_ids?: string[];
  /** The contradicting excerpt text (CONTRADICTED rows). */
  contradicting_excerpt?: string;
  /** ID of the contradicting span (CONTRADICTED rows). */
  contradicting_span_id?: string;
  /** Batch run ID. */
  batch_id?: string;
}

// ─── Store shape ─────────────────────────────────────────────────────────────

export interface VerdictState {
  /** Map from messageId → array of VerificationResult */
  byMessage: Map<string, VerificationResult[]>;
}

// ─── Actions ─────────────────────────────────────────────────────────────────

export type VerdictAction =
  | { type: 'ADD_RESULT'; payload: VerificationResult }
  | { type: 'SET_RESULTS'; payload: { messageId: string; results: VerificationResult[] } }
  | { type: 'CLEAR_MESSAGE'; payload: string };

// ─── Reducer ─────────────────────────────────────────────────────────────────

function verdictReducer(state: VerdictState, action: VerdictAction): VerdictState {
  switch (action.type) {
    case 'ADD_RESULT': {
      const { messageId } = action.payload;
      const existing = state.byMessage.get(messageId) ?? [];
      const next = new Map(state.byMessage);
      next.set(messageId, [...existing, action.payload]);
      return { byMessage: next };
    }
    case 'SET_RESULTS': {
      const next = new Map(state.byMessage);
      next.set(action.payload.messageId, action.payload.results);
      return { byMessage: next };
    }
    case 'CLEAR_MESSAGE': {
      const next = new Map(state.byMessage);
      next.delete(action.payload);
      return { byMessage: next };
    }
    default:
      return state;
  }
}

const initialState: VerdictState = { byMessage: new Map() };

// ─── Context ─────────────────────────────────────────────────────────────────

interface VerdictContextValue {
  state: VerdictState;
  dispatch: React.Dispatch<VerdictAction>;
}

const VerdictContext = createContext<VerdictContextValue | null>(null);

export function VerdictStoreProvider({ children }: { children: React.ReactNode }) {
  const [state, dispatch] = useReducer(verdictReducer, initialState);
  return (
    <VerdictContext.Provider value={{ state, dispatch }}>
      {children}
    </VerdictContext.Provider>
  );
}

export function useVerdictStore(): VerdictContextValue {
  const ctx = useContext(VerdictContext);
  if (!ctx) throw new Error('useVerdictStore must be used within a VerdictStoreProvider');
  return ctx;
}

// ─── Selector helpers ────────────────────────────────────────────────────────

export function getResultsForMessage(
  state: VerdictState,
  messageId: string
): VerificationResult[] {
  return state.byMessage.get(messageId) ?? [];
}

export function countsByVerdict(results: VerificationResult[]): {
  verified: number;
  partial: number;
  contradicted: number;
  insufficient: number;
  noEvidence: number;
  pending: number;
  total: number;
} {
  const counts = {
    verified: 0,
    partial: 0,
    contradicted: 0,
    insufficient: 0,
    noEvidence: 0,
    pending: 0,
    total: results.length,
  };
  for (const r of results) {
    switch (r.verdict) {
      case 'SUPPORTED':      counts.verified++;     break;
      case 'PARTIALLY_SUPPORTED': counts.partial++; break;
      case 'CONTRADICTED':   counts.contradicted++;  break;
      case 'INSUFFICIENT':   counts.insufficient++;  break;
      case 'NO_EVIDENCE':    counts.noEvidence++;    break;
      case 'PENDING':        counts.pending++;       break;
    }
  }
  return counts;
}
