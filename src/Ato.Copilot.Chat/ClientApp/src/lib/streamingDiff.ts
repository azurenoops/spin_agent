// =============================================================================
// streamingDiff.ts — Phase 2 (#1458)
//
// Token-level diff renderer for the streaming editor.
//
// Contract:
//   - buildDiffTokens() diffs an old text against a new (streaming) text and
//     produces DiffToken[] that the editor renders as added/removed/context runs.
//   - resolveBadge() builds a CitationBadge from a ProvenanceSpan; called once
//     per resolved span — never re-derived at render time.
//   - Tokens for unresolved/ungrounded spans carry no provenance → no badge.
//
// Performance note: this is a pure function with no I/O. The streaming pipeline
// calls it on each token event; latency budget is ≤150ms from span resolution
// to badge appearance (acceptance criteria).
// =============================================================================

import type { DiffToken, CitationBadge, ProvenanceSpan } from '../types/provenance';

// ── Diff engine ───────────────────────────────────────────────────────────────

/**
 * Minimal LCS-based word-level diff. Sufficient for the streaming use-case
 * where diffs are incremental (one or a few tokens added per event).
 *
 * @param oldText - previous editor text
 * @param newText - new (streaming) text
 * @param provenance - optional ProvenanceSpan already resolved for the new span
 * @returns DiffToken[] ready to render
 */
export function buildDiffTokens(
  oldText: string,
  newText: string,
  provenance?: ProvenanceSpan
): DiffToken[] {
  const oldWords = tokenize(oldText);
  const newWords = tokenize(newText);

  const lcs = computeLCS(oldWords, newWords);
  const result: DiffToken[] = [];

  let oi = 0;
  let ni = 0;

  for (const common of lcs) {
    // emit removed tokens before this common stretch
    while (oi < common.oldIndex) {
      result.push({ kind: 'removed', text: oldWords[oi++] });
    }
    // emit added tokens before this common stretch
    while (ni < common.newIndex) {
      const tok: DiffToken = { kind: 'added', text: newWords[ni++] };
      if (provenance) tok.provenance = provenance;
      result.push(tok);
    }
    // emit context token
    result.push({ kind: 'context', text: newWords[common.newIndex] });
    oi = common.oldIndex + 1;
    ni = common.newIndex + 1;
  }

  // trailing removals
  while (oi < oldWords.length) {
    result.push({ kind: 'removed', text: oldWords[oi++] });
  }
  // trailing additions
  while (ni < newWords.length) {
    const tok: DiffToken = { kind: 'added', text: newWords[ni++] };
    if (provenance) tok.provenance = provenance;
    result.push(tok);
  }

  return result;
}

// ── Badge factory ─────────────────────────────────────────────────────────────

/**
 * Build a CitationBadge from a resolved ProvenanceSpan.
 * labelMap maps source_id → display label; if absent, falls back to a
 * truncated source_id.
 *
 * Returns null when origin is not 'quoted' or 'imported' — only evidence-
 * grounded spans earn a badge (AC3: badge iff origin∈{quoted,imported}).
 *
 * Called once at span-resolution time. The returned object is stored on the
 * DiffToken and passed directly to the badge renderer — never re-computed.
 */
export function resolveBadge(
  span: ProvenanceSpan,
  userModified: boolean,
  labelMap: Map<string, string> = new Map()
): CitationBadge | null {
  if (span.origin !== 'quoted' && span.origin !== 'imported') return null;
  const label = labelMap.get(span.source_id) ?? abbreviateId(span.source_id);
  return {
    source_id: span.source_id,
    origin: span.origin,
    confidence: span.confidence,
    user_modified: userModified,
    label,
  };
}

// ── Internal helpers ──────────────────────────────────────────────────────────

/** Split text into word-level tokens preserving whitespace as separate tokens. */
function tokenize(text: string): string[] {
  // Split on whitespace boundaries, keeping the whitespace tokens for faithful reconstruction
  return text.split(/(\s+)/).filter((t) => t.length > 0);
}

interface LCSPair {
  oldIndex: number;
  newIndex: number;
}

/**
 * O(N*M) LCS over token arrays. Adequate for streaming increments (small N, M).
 * For very large diffs a Myers-diff implementation should replace this.
 */
function computeLCS(a: string[], b: string[]): LCSPair[] {
  const m = a.length;
  const n = b.length;
  // dp[i][j] = length of LCS of a[0..i-1], b[0..j-1]
  const dp: number[][] = Array.from({ length: m + 1 }, () => new Array(n + 1).fill(0));

  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      if (a[i - 1] === b[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1] + 1;
      } else {
        dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
      }
    }
  }

  // backtrack
  const pairs: LCSPair[] = [];
  let i = m;
  let j = n;
  while (i > 0 && j > 0) {
    if (a[i - 1] === b[j - 1]) {
      pairs.unshift({ oldIndex: i - 1, newIndex: j - 1 });
      i--;
      j--;
    } else if (dp[i - 1][j] > dp[i][j - 1]) {
      i--;
    } else {
      j--;
    }
  }
  return pairs;
}

function abbreviateId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 6)}…${id.slice(-4)}` : id;
}
