/**
 * streamingDiff.test.ts — Phase 2 (#1458)
 *
 * Covers:
 *   - buildDiffTokens produces only 'context' tokens for identical text
 *   - buildDiffTokens marks new tokens as 'added' and removed as 'removed'
 *   - added tokens carry provenance when a ProvenanceSpan is supplied
 *   - added tokens carry no provenance when none is supplied
 *   - resolveBadge returns badge with correct fields for quoted/imported spans
 *   - resolveBadge returns null for ai/user spans (AC3 property guard)
 *   - resolveBadge abbreviates unknown source_id
 *   - resolveBadge uses labelMap when provided
 *   - property test: badge iff origin∈{quoted,imported} with source_id (M3/AC3)
 *
 * AAA (Arrange / Act / Assert) marked on each test.
 */

import { buildDiffTokens, resolveBadge } from '../streamingDiff';
import type { ProvenanceSpan, SpanOrigin } from '../../types/provenance';

function makeSpan(overrides: Partial<ProvenanceSpan> = {}): ProvenanceSpan {
  return {
    source_id: 'src-001',
    origin: 'quoted',
    span_start: 0,
    span_end: 50,
    confidence: 0.9,
    generated_at: '2026-08-17T00:00:00.000Z',
    ...overrides,
  };
}

describe('buildDiffTokens', () => {
  it('produces only context tokens for identical text', () => {
    // Arrange
    const text = 'hello world';
    // Act
    const tokens = buildDiffTokens(text, text);
    // Assert
    expect(tokens.every((t) => t.kind === 'context')).toBe(true);
  });

  it('marks appended words as added', () => {
    // Arrange
    const old = 'hello';
    const next = 'hello world';
    // Act
    const tokens = buildDiffTokens(old, next);
    // Assert
    const added = tokens.filter((t) => t.kind === 'added');
    expect(added.length).toBeGreaterThan(0);
    expect(added.some((t) => t.text === 'world')).toBe(true);
  });

  it('marks removed words as removed', () => {
    // Arrange
    const old = 'hello world';
    const next = 'hello';
    // Act
    const tokens = buildDiffTokens(old, next);
    // Assert
    const removed = tokens.filter((t) => t.kind === 'removed');
    expect(removed.some((t) => t.text === 'world')).toBe(true);
  });

  it('attaches provenance to added tokens when span is provided', () => {
    // Arrange
    const span = makeSpan();
    // Act
    const tokens = buildDiffTokens('', 'new content', span);
    // Assert
    const added = tokens.filter((t) => t.kind === 'added');
    expect(added.length).toBeGreaterThan(0);
    added.forEach((t) => expect(t.provenance).toBe(span));
  });

  it('added tokens carry no provenance when none is supplied', () => {
    // Arrange / Act
    const tokens = buildDiffTokens('', 'new content');
    // Assert
    tokens.filter((t) => t.kind === 'added').forEach((t) => {
      expect(t.provenance).toBeUndefined();
    });
  });

  it('empty old + empty new produces empty token list', () => {
    // Arrange / Act / Assert
    expect(buildDiffTokens('', '')).toHaveLength(0);
  });
});

describe('resolveBadge', () => {
  it('returns a badge with correct source_id, confidence, user_modified for quoted span', () => {
    // Arrange
    const span = makeSpan({ source_id: 'src-abc', confidence: 0.75, origin: 'quoted' });
    // Act
    const badge = resolveBadge(span, false);
    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.source_id).toBe('src-abc');
    expect(badge!.confidence).toBeCloseTo(0.75);
    expect(badge!.user_modified).toBe(false);
    expect(badge!.origin).toBe('quoted');
  });

  it('returns a badge for imported spans', () => {
    // Arrange
    const span = makeSpan({ origin: 'imported' });
    // Act
    const badge = resolveBadge(span, false);
    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.origin).toBe('imported');
  });

  it('returns null for ai-origin spans (no badge)', () => {
    // Arrange
    const span = makeSpan({ origin: 'ai' });
    // Act
    const badge = resolveBadge(span, false);
    // Assert
    expect(badge).toBeNull();
  });

  it('returns null for user-origin spans (no badge)', () => {
    // Arrange
    const span = makeSpan({ origin: 'user' });
    // Act
    const badge = resolveBadge(span, false);
    // Assert
    expect(badge).toBeNull();
  });

  it('reflects user_modified=true', () => {
    // Arrange
    const span = makeSpan({ origin: 'quoted' });
    // Act
    const badge = resolveBadge(span, true);
    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.user_modified).toBe(true);
  });

  it('uses labelMap label when source_id is present in map', () => {
    // Arrange
    const span = makeSpan({ source_id: 'src-001', origin: 'quoted' });
    const map = new Map([['src-001', 'NIST SP 800-53']]);
    // Act
    const badge = resolveBadge(span, false, map);
    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.label).toBe('NIST SP 800-53');
  });

  it('abbreviates long source_id when not in labelMap', () => {
    // Arrange
    const longId = 'abcdefghijklmnopqrstuvwxyz';
    const span = makeSpan({ source_id: longId, origin: 'quoted' });
    // Act
    const badge = resolveBadge(span, false);
    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.label).toContain('…');
    expect(badge!.label.length).toBeLessThan(longId.length);
  });

  /**
   * Property test (M3 / AC3):
   * badge iff origin∈{quoted,imported} AND source_id is present.
   *
   * Exhaustively verifies all four origin values so CI catches any future
   * regression if the guard condition in resolveBadge is changed.
   */
  it('property: badge rendered iff origin∈{quoted,imported} with source_id', () => {
    const BADGE_ORIGINS: SpanOrigin[] = ['quoted', 'imported'];
    const NO_BADGE_ORIGINS: SpanOrigin[] = ['ai', 'user'];

    for (const origin of BADGE_ORIGINS) {
      // Arrange
      const span = makeSpan({ origin, source_id: 'doc-xyz' });
      // Act
      const badge = resolveBadge(span, false);
      // Assert — badge must exist and reference the source
      expect(badge).not.toBeNull();
      expect(badge!.source_id).toBe('doc-xyz');
    }

    for (const origin of NO_BADGE_ORIGINS) {
      // Arrange
      const span = makeSpan({ origin, source_id: 'doc-xyz' });
      // Act
      const badge = resolveBadge(span, false);
      // Assert — no badge for AI-generated or user-typed spans
      expect(badge).toBeNull();
    }
  });
});
