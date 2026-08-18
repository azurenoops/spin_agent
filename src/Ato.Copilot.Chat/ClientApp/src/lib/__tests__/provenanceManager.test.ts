/**
 * provenanceManager.test.ts — Phase 1 (#940, #939)
 *
 * Covers:
 *   - createProvenanceState produces correct initial state
 *   - markUserModified flips flag and appends 'edit' history entry
 *   - markUserModified is idempotent (calling twice doesn't double-append)
 *   - recordSplit appends 'split' history entry with sibling_source_id
 *   - recordMerge appends 'merge' history entry with merged_source_id
 *   - effectiveConfidence returns null for undefined state (legacy fallback)
 *   - effectiveConfidence returns full confidence when not modified
 *   - effectiveConfidence returns halved confidence when user_modified
 *   - isProvenanceState type guard accepts valid + rejects invalid values
 *   - history is append-only: prior entries survive after each mutation
 *
 * AAA (Arrange / Act / Assert) marked on each test.
 */

import {
  createProvenanceState,
  markUserModified,
  recordSplit,
  recordMerge,
  effectiveConfidence,
  isProvenanceState,
} from '../provenanceManager';
import type { ProvenanceSpan } from '../../types/provenance';

// ── Fixture ───────────────────────────────────────────────────────────────────

function makeSpan(overrides: Partial<ProvenanceSpan> = {}): ProvenanceSpan {
  return {
    source_id: 'src-001',
    origin: 'quoted',
    span_start: 0,
    span_end: 100,
    confidence: 0.9,
    generated_at: '2026-08-17T00:00:00.000Z',
    ...overrides,
  };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('createProvenanceState', () => {
  it('creates initial state with user_modified=false and empty history', () => {
    // Arrange
    const span = makeSpan();
    // Act
    const state = createProvenanceState(span);
    // Assert
    expect(state.user_modified).toBe(false);
    expect(state.history).toHaveLength(0);
    expect(state.span).toBe(span);
  });
});

describe('markUserModified', () => {
  it('sets user_modified=true and appends an edit history entry', () => {
    // Arrange
    const state = createProvenanceState(makeSpan());
    // Act
    const updated = markUserModified(state);
    // Assert
    expect(updated.user_modified).toBe(true);
    expect(updated.history).toHaveLength(1);
    expect(updated.history[0].kind).toBe('edit');
  });

  it('is idempotent — calling twice does not add a second history entry', () => {
    // Arrange
    const state = createProvenanceState(makeSpan());
    const once = markUserModified(state);
    // Act
    const twice = markUserModified(once);
    // Assert
    expect(twice.history).toHaveLength(1);
    expect(twice.user_modified).toBe(true);
  });

  it('does not mutate the original state (immutability)', () => {
    // Arrange
    const state = createProvenanceState(makeSpan());
    // Act
    markUserModified(state);
    // Assert
    expect(state.user_modified).toBe(false);
    expect(state.history).toHaveLength(0);
  });
});

describe('recordSplit', () => {
  it('appends a split entry with sibling_source_id', () => {
    // Arrange
    const state = createProvenanceState(makeSpan());
    // Act
    const updated = recordSplit(state, 'src-002');
    // Assert
    expect(updated.history).toHaveLength(1);
    expect(updated.history[0].kind).toBe('split');
    expect(updated.history[0].sibling_source_id).toBe('src-002');
  });

  it('preserves the original source_id after split', () => {
    // Arrange
    const state = createProvenanceState(makeSpan({ source_id: 'original' }));
    // Act
    const updated = recordSplit(state, 'sibling');
    // Assert
    expect(updated.span.source_id).toBe('original');
  });
});

describe('recordMerge', () => {
  it('appends a merge entry with merged_source_id', () => {
    // Arrange
    const state = createProvenanceState(makeSpan());
    // Act
    const updated = recordMerge(state, 'src-consumed');
    // Assert
    expect(updated.history).toHaveLength(1);
    expect(updated.history[0].kind).toBe('merge');
    expect(updated.history[0].merged_source_id).toBe('src-consumed');
  });
});

describe('history is append-only', () => {
  it('prior entries survive across multiple mutations', () => {
    // Arrange
    let state = createProvenanceState(makeSpan());
    // Act
    state = markUserModified(state);
    state = recordSplit(state, 'sibling');
    state = recordMerge(state, 'consumed');
    // Assert
    expect(state.history).toHaveLength(3);
    expect(state.history.map((e) => e.kind)).toEqual(['edit', 'split', 'merge']);
  });
});

describe('effectiveConfidence', () => {
  it('returns null for undefined (legacy node fallback)', () => {
    // Arrange / Act / Assert
    expect(effectiveConfidence(undefined)).toBeNull();
  });

  it('returns full confidence when not user-modified', () => {
    // Arrange
    const state = createProvenanceState(makeSpan({ confidence: 0.85 }));
    // Act / Assert
    expect(effectiveConfidence(state)).toBeCloseTo(0.85);
  });

  it('returns halved confidence when user_modified=true', () => {
    // Arrange
    const state = markUserModified(createProvenanceState(makeSpan({ confidence: 0.8 })));
    // Act / Assert
    expect(effectiveConfidence(state)).toBeCloseTo(0.4);
  });
});

describe('isProvenanceState', () => {
  it('returns true for a valid ProvenanceState', () => {
    // Arrange
    const state = createProvenanceState(makeSpan());
    // Act / Assert
    expect(isProvenanceState(state)).toBe(true);
  });

  it('returns false for null', () => {
    expect(isProvenanceState(null)).toBe(false);
  });

  it('returns false for a plain object missing required fields', () => {
    expect(isProvenanceState({ span: {}, history: 'not-array' })).toBe(false);
  });

  it('returns false for a string', () => {
    expect(isProvenanceState('src-001')).toBe(false);
  });
});
