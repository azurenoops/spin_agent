/**
 * grounding/migration.ts — dual-write legacy-citation migration (#a493ec1c)
 *
 * Provides the shape and helpers for the migration from today's implicit
 * citation model to the Claim↔Evidence Ledger.
 *
 * Migration phases (implemented by Friday + Mr. Terrific):
 *   Phase 1 — Dual-write: every new citation also writes an EvidenceBinding.
 *   Phase 2 — Backfill: existing legacy citations are converted via
 *              backfillLegacyCitation() with verificationStatus='unverified'.
 *   Phase 3 — Verification sweep: async job calls GroundingPort.reverify()
 *              on each backfilled binding to promote or flag it.
 *   Phase 4 — Coverage gate: legacy citation path is removed only after
 *              ≥98% of claims in the corpus carry a non-unverified binding.
 *   Phase 5 — Cleanup: remove dual-write shim once gate passes.
 *
 * This file is the contract + helper layer only.  Persistence and sweep
 * scheduling are owned by Friday.
 */

import type { EvidenceBinding } from '../research-workflow/types.ts';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * A legacy citation as it exists before ledger migration.
 *
 * `rawText` is the verbatim formatted citation string (e.g. APA/MLA output).
 * `sourceId` may be absent if the citation cannot be matched to a SourceRecord.
 * `evidenceSpan` may be absent if no char-offset span can be recovered.
 */
export interface LegacyCitation {
  /** Verbatim formatted citation text, preserved for audit. */
  rawText: string;
  /** Matched SourceRecord.id, if recoverable. */
  sourceId?: string;
  /** Character-offset span within the source, if recoverable. */
  evidenceSpan?: [start: number, end: number];
  /** Confidence score from the legacy fuzzy-match, if available (0–1). */
  matchConfidence?: number;
}

/**
 * Summary returned by the backfill sweep runner.
 */
export interface MigrationResult {
  /** Number of legacy citations successfully backfilled as EvidenceBindings. */
  bound: number;
  /** Number skipped because they were already in the ledger. */
  skipped: number;
  /**
   * Number that could not be matched to a SourceRecord and were preserved
   * with a null sourceId placeholder — flagged for human review.
   */
  flagged: number;
}

/**
 * Recovery-rate report produced by buildMigrationReport().
 *
 * AC#5 success metric: 0 legacy citations dropped; ≥95% resolved to a real
 * evidenceSpan; remainder preserved as rawLegacyCitationText.
 */
export interface MigrationReport {
  /** Total number of EvidenceBindings examined. */
  total: number;
  /**
   * Bindings with a real, non-sentinel evidenceSpan (start < end and
   * sourceId !== 'UNRESOLVED').
   */
  resolved: number;
  /**
   * Bindings with a span-unknown sentinel [0,0] or sourceId === 'UNRESOLVED'.
   * All of these have rawLegacyCitationText — provenance is preserved.
   */
  unresolved: number;
  /**
   * Recovery rate as a fraction 0–1.
   * resolved / total (0 when total === 0).
   */
  recoveryRate: number;
  /**
   * Whether the ≥95% recovery gate passes.
   * True when recoveryRate >= 0.95 OR total === 0 (vacuously true).
   */
  meetsRecoveryGate: boolean;
  /**
   * Verbatim text of every unresolved legacy citation.
   * Populated so the sweep runner can report exactly which citations need
   * manual span recovery without losing any provenance.
   */
  unresolvedCitations: string[];
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Convert a single LegacyCitation to an EvidenceBinding for use in the
 * dual-write backfill path.
 *
 * Contract:
 *   - verificationStatus is always 'unverified' for backfilled bindings.
 *   - rawLegacyCitationText is always populated from rawText.
 *   - If sourceId is absent, a sentinel value of 'UNRESOLVED' is used;
 *     the sweep runner can identify these by checking for 'UNRESOLVED'.
 *   - If evidenceSpan is absent, [0, 0] is used as a placeholder; the
 *     sweep runner must treat [0, 0] as "span unknown, needs re-extraction".
 *
 * @param claimId  - The ClaimNode.id this binding will belong to.
 * @param legacy   - The legacy citation to convert.
 * @returns A new EvidenceBinding with verificationStatus='unverified'.
 */
export function backfillLegacyCitation(
  claimId: string,
  legacy: LegacyCitation,
): EvidenceBinding {
  return {
    claimId,
    sourceId: legacy.sourceId ?? 'UNRESOLVED',
    evidenceSpan: legacy.evidenceSpan ?? [0, 0],
    retrievalConfidence: legacy.matchConfidence ?? 0,
    verificationStatus: 'unverified',
    rawLegacyCitationText: legacy.rawText,
    schemaVersion: '1',
  };
}

/**
 * Returns true when the EvidenceBinding originated from the migration path
 * and has not yet been verified.  Used by the sweep runner to find bindings
 * that need a reverify() call.
 */
export function isMigrationPending(binding: EvidenceBinding): boolean {
  return (
    binding.verificationStatus === 'unverified' &&
    binding.rawLegacyCitationText !== undefined
  );
}

/**
 * Coverage check helper.
 *
 * Returns true when the fraction of bindings that are NOT 'unverified' meets
 * or exceeds the ≥98% gate required before the legacy citation path can be
 * removed (Phase 4).
 *
 * @param bindings - All EvidenceBindings for the document corpus being checked.
 */
export function meetsLegacyRemovalGate(bindings: readonly EvidenceBinding[]): boolean {
  if (bindings.length === 0) return false;
  const verified = bindings.filter(b => b.verificationStatus !== 'unverified').length;
  return verified / bindings.length >= 0.98;
}

/**
 * Build a recovery-rate report for a set of migration EvidenceBindings.
 *
 * A binding is considered "resolved" when it has a real evidenceSpan
 * (start < end) AND a real sourceId (not 'UNRESOLVED').
 *
 * A binding is "unresolved" when:
 *   - evidenceSpan is [0, 0] (span-unknown sentinel), OR
 *   - sourceId is 'UNRESOLVED'.
 *
 * Unresolved bindings MUST carry rawLegacyCitationText so that zero
 * provenance is lost — the report surfaces their verbatim text.
 *
 * AC#5 success metric: recoveryRate ≥ 0.95.
 *
 * @param bindings - The migration EvidenceBindings to analyse.
 *   Pass only bindings that originated from the migration path (i.e. those
 *   where isMigrationPending() was true at backfill time, OR all bindings
 *   if you want a full-corpus report).
 */
export function buildMigrationReport(bindings: readonly EvidenceBinding[]): MigrationReport {
  const total = bindings.length;

  if (total === 0) {
    return {
      total: 0,
      resolved: 0,
      unresolved: 0,
      recoveryRate: 0,
      meetsRecoveryGate: true, // vacuously — nothing to migrate
      unresolvedCitations: [],
    };
  }

  const unresolvedCitations: string[] = [];
  let resolved = 0;

  for (const b of bindings) {
    const [start, end] = b.evidenceSpan;
    const hasRealSpan = start < end;
    const hasRealSource = b.sourceId !== 'UNRESOLVED';

    if (hasRealSpan && hasRealSource) {
      resolved++;
    } else {
      // Preserve the verbatim text for the report — never silently drop.
      unresolvedCitations.push(
        b.rawLegacyCitationText ?? `[no rawLegacyCitationText — claimId: ${b.claimId}]`,
      );
    }
  }

  const unresolved = total - resolved;
  const recoveryRate = resolved / total;

  return {
    total,
    resolved,
    unresolved,
    recoveryRate,
    meetsRecoveryGate: recoveryRate >= 0.95,
    unresolvedCitations,
  };
}
