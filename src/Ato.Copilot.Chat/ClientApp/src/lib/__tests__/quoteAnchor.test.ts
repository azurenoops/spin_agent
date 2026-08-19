/**
 * quoteAnchor.test.ts — Phase 3 (#1457)
 *
 * Covers:
 *   - createQuoteAnchor stores source_id, char_range, and a non-empty hash
 *   - Two anchors with identical text produce the same hash
 *   - Two anchors with different text produce different hashes
 *   - checkDrift returns drifted=false when text is unchanged
 *   - checkDrift returns drifted=true when text changes
 *   - findDriftedAnchors returns only drifted anchors
 *   - findDriftedAnchors returns no false alarms when source is unavailable (null)
 *
 * AAA (Arrange / Act / Assert) marked on each test.
 */

import { createQuoteAnchor, checkDrift, findDriftedAnchors, sha256hex } from '../quoteAnchor';

// Note: in the Jest JSDOM environment, crypto.subtle may not be available.
// sha256hex falls back to djb2 — the tests exercise the same logic path.

describe('createQuoteAnchor', () => {
  it('stores source_id and char_range', async () => {
    // Arrange / Act
    const anchor = await createQuoteAnchor('src-001', 'quoted text', [10, 20]);
    // Assert
    expect(anchor.source_id).toBe('src-001');
    expect(anchor.char_range).toEqual([10, 20]);
  });

  it('produces a non-empty hash string', async () => {
    // Arrange / Act
    const anchor = await createQuoteAnchor('src-001', 'some text', [0, 9]);
    // Assert
    expect(typeof anchor.hash_of_quoted_text).toBe('string');
    expect(anchor.hash_of_quoted_text.length).toBeGreaterThan(0);
  });

  it('same text produces the same hash (deterministic)', async () => {
    // Arrange
    const text = 'The quick brown fox';
    // Act
    const a1 = await createQuoteAnchor('src-1', text, [0, 19]);
    const a2 = await createQuoteAnchor('src-2', text, [5, 24]);
    // Assert
    expect(a1.hash_of_quoted_text).toBe(a2.hash_of_quoted_text);
  });

  it('different text produces different hashes', async () => {
    // Arrange / Act
    const a1 = await createQuoteAnchor('src-1', 'text A', [0, 6]);
    const a2 = await createQuoteAnchor('src-1', 'text B', [0, 6]);
    // Assert
    expect(a1.hash_of_quoted_text).not.toBe(a2.hash_of_quoted_text);
  });
});

describe('checkDrift', () => {
  it('returns drifted=false when text is unchanged', async () => {
    // Arrange
    const original = 'unchanged passage';
    const anchor = await createQuoteAnchor('src-001', original, [0, 17]);
    // Act
    const result = await checkDrift(anchor, original);
    // Assert
    expect(result.drifted).toBe(false);
  });

  it('returns drifted=true when text has changed', async () => {
    // Arrange
    const anchor = await createQuoteAnchor('src-001', 'original text', [0, 13]);
    // Act
    const result = await checkDrift(anchor, 'modified text');
    // Assert
    expect(result.drifted).toBe(true);
  });

  it('provides current_hash in result', async () => {
    // Arrange
    const anchor = await createQuoteAnchor('src-001', 'text', [0, 4]);
    // Act
    const result = await checkDrift(anchor, 'text');
    // Assert
    expect(result.current_hash).toBe(anchor.hash_of_quoted_text);
  });
});

describe('findDriftedAnchors', () => {
  it('returns only the anchors that have drifted', async () => {
    // Arrange
    const stable = await createQuoteAnchor('src-1', 'stable text', [0, 11]);
    const drifted = await createQuoteAnchor('src-2', 'original', [0, 8]);
    const fetchText = async (anchor: typeof stable) => {
      if (anchor.source_id === 'src-1') return 'stable text';
      return 'changed';
    };
    // Act
    const results = await findDriftedAnchors([stable, drifted], fetchText);
    // Assert
    expect(results).toHaveLength(1);
    expect(results[0].anchor.source_id).toBe('src-2');
  });

  it('returns no false alarms when source is unavailable (null)', async () => {
    // Arrange
    const anchor = await createQuoteAnchor('src-1', 'some text', [0, 9]);
    const fetchText = async () => null;
    // Act
    const results = await findDriftedAnchors([anchor], fetchText);
    // Assert
    expect(results).toHaveLength(0);
  });
});

describe('sha256hex fallback', () => {
  it('returns a non-empty string for any input', async () => {
    // Arrange / Act
    const hash = await sha256hex('test input');
    // Assert
    expect(hash.length).toBeGreaterThan(0);
  });
});
