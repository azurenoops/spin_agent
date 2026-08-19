// =============================================================================
// quoteAnchor.ts — Phase 3 (#1457)
//
// Quote anchoring and drift detection for the editor.
//
// Workflow:
//   1. When a user pastes a quote (or the AI inserts a grounded quote),
//      createQuoteAnchor() hashes the text and stores it alongside the
//      source span coordinates.
//   2. On document load (or manual re-check), checkDrift() re-hashes the
//      current source passage and compares it to the stored hash.
//   3. Any mismatch triggers a visual warning — the quote anchor is NOT
//      deleted; the drift flag is purely advisory.
//
// Hashing: Web Crypto API (SHA-256). Returns a hex string.
// All functions are async because SubtleCrypto.digest() is async.
// =============================================================================

import type { QuoteAnchor, DriftCheckResult } from '../types/provenance';

// ── Factory ───────────────────────────────────────────────────────────────────

/**
 * Create a QuoteAnchor for a quoted passage.
 *
 * @param source_id    - stable id of the originating source document
 * @param quotedText   - the exact text being quoted (used for hash)
 * @param char_range   - [start, end) character offsets within the source
 */
export async function createQuoteAnchor(
  source_id: string,
  quotedText: string,
  char_range: [number, number]
): Promise<QuoteAnchor> {
  const hash = await sha256hex(quotedText);
  return { source_id, char_range, hash_of_quoted_text: hash };
}

// ── Drift check ───────────────────────────────────────────────────────────────

/**
 * Check whether the source passage a QuoteAnchor points to has changed.
 *
 * @param anchor       - the stored QuoteAnchor
 * @param currentText  - the current text of the source passage at char_range
 * @returns DriftCheckResult — drifted=true means the hash no longer matches
 *
 * Graceful fallback: if currentText cannot be retrieved, returns drifted=false
 * with current_hash equal to the stored hash (no false alarms).
 */
export async function checkDrift(
  anchor: QuoteAnchor,
  currentText: string
): Promise<DriftCheckResult> {
  const current_hash = await sha256hex(currentText);
  return {
    anchor,
    drifted: current_hash !== anchor.hash_of_quoted_text,
    current_hash,
  };
}

/**
 * Batch drift check: run checkDrift on multiple anchors in parallel.
 * Returns only the anchors that have drifted.
 */
export async function findDriftedAnchors(
  anchors: QuoteAnchor[],
  fetchSourceText: (anchor: QuoteAnchor) => Promise<string | null>
): Promise<DriftCheckResult[]> {
  const results = await Promise.all(
    anchors.map(async (anchor) => {
      const text = await fetchSourceText(anchor);
      if (text === null) {
        // Source unavailable — no false alarm
        return { anchor, drifted: false, current_hash: anchor.hash_of_quoted_text };
      }
      return checkDrift(anchor, text);
    })
  );
  return results.filter((r) => r.drifted);
}

// ── Hashing ───────────────────────────────────────────────────────────────────

/**
 * SHA-256 hex digest via Web Crypto API.
 * Falls back to a simple djb2 string hash in environments where SubtleCrypto
 * is unavailable (e.g., test runners without a DOM). This is intentional:
 * drift detection is a UX hint, not a security primitive.
 */
export async function sha256hex(text: string): Promise<string> {
  if (typeof crypto !== 'undefined' && crypto.subtle) {
    const encoded = new TextEncoder().encode(text);
    const buf = await crypto.subtle.digest('SHA-256', encoded);
    return Array.from(new Uint8Array(buf))
      .map((b) => b.toString(16).padStart(2, '0'))
      .join('');
  }
  // Fallback: djb2 (test/SSR environments)
  return djb2hex(text);
}

function djb2hex(text: string): string {
  let hash = 5381;
  for (let i = 0; i < text.length; i++) {
    hash = ((hash << 5) + hash) ^ text.charCodeAt(i);
    hash = hash >>> 0; // keep unsigned 32-bit
  }
  return hash.toString(16).padStart(8, '0');
}
