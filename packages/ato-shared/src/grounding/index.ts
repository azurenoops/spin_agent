/**
 * grounding/index.ts — GroundingPort contract (#a493ec1c)
 *
 * The GroundingPort is the SOLE path through which any agent may attach a
 * claim to a document.  Direct prose insertion without calling bind() is a
 * contract violation.  Enforcement happens at two levels:
 *
 *   1. Type level:  bind() accepts NonEmptyArray<EvidenceBinding>, so passing
 *      an empty array is a TypeScript compile error caught by `tsc --noEmit`.
 *
 *   2. CI level:  check-grounding-port.mjs scans the repo for any content-
 *      insert call site that does not route through GroundingPort and fails
 *      the build.
 *
 * Implementations:
 *   - Friday / Mr. Terrific own the persistence implementation.
 *   - This file is the contract only — no implementation lives here.
 *
 * @module grounding
 */

import type { ClaimNode, EvidenceBinding, VerificationStatus } from '../research-workflow/types.ts';
import type { NonEmptyArray } from '../research-workflow/types.ts';

export type { ClaimNode, EvidenceBinding, VerificationStatus };
export type { NonEmptyArray };

export { transitionVerificationStatus, isTerminalStatus } from './verificationStateMachine.ts';
export { backfillLegacyCitation, buildMigrationReport } from './migration.ts';
export type { LegacyCitation, MigrationResult, MigrationReport } from './migration.ts';

// ---------------------------------------------------------------------------
// validateEvidenceBinding — runtime span guard for GroundingPort.bind()
// ---------------------------------------------------------------------------

/**
 * Validates an EvidenceBinding before it is accepted by GroundingPort.bind().
 *
 * Rules:
 *   1. Zero-length span [n, n) where start >= end is INVALID — it cannot
 *      point to any real text in the source document.
 *
 * Migration exception (Option A decision — see AGENTS.md §Engineering Principles):
 *   A binding produced by backfillLegacyCitation() carries rawLegacyCitationText
 *   and uses [0, 0] as a span-unknown sentinel.  This binding is permitted
 *   through GroundingPort.bind() because it satisfies the ≥1-binding rule:
 *   provenance is preserved in rawLegacyCitationText even when no source span
 *   is recoverable.  The sweep runner (Phase 3) will replace [0, 0] once
 *   re-extraction succeeds.
 *
 *   Rationale: Tony Stark's architecture doc says "rejects zero-length spans [n,n)"
 *   and also says "preserve all provenance across migration via rawLegacyCitationText".
 *   These two requirements are in tension for unresolvable legacy citations.
 *   Option A resolves this by treating rawLegacyCitationText presence as the
 *   authoritative migration-path signal that waives the span rule.
 *
 * @throws {Error} if the binding has a zero-length span and is NOT a migration binding.
 */
export function validateEvidenceBinding(binding: EvidenceBinding): void {
  const [start, end] = binding.evidenceSpan;

  // A span is zero-length when start >= end.
  if (start >= end) {
    // Migration bindings carry rawLegacyCitationText — they are exempt.
    if (binding.rawLegacyCitationText !== undefined) {
      return; // OK — span-unknown sentinel on a migration binding.
    }
    throw new Error(
      `GroundingPort: invalid zero-length evidenceSpan [${start}, ${end}) ` +
      `on binding for claim '${binding.claimId}'. ` +
      `A real binding must point to text (start < end). ` +
      `If this is a migration binding, populate rawLegacyCitationText.`,
    );
  }
}

// ---------------------------------------------------------------------------
// GroundingPort — the sole content-write interface
// ---------------------------------------------------------------------------

/**
 * Every agent that inserts a claim into a document MUST do so through this
 * interface.  Bypassing it is a contract violation.
 *
 * Ownership of concrete implementations:
 *   - Friday: backend persistence (EF Core / AtoCopilotContext)
 *   - Mr. Terrific: eval harness adapter
 */
export interface GroundingPort {
  /**
   * Attach a claim to its evidence.  The evidence array must be non-empty —
   * passing [] is a compile-time error (NonEmptyArray enforces this).
   *
   * Implementations MUST persist both the ClaimNode and all EvidenceBindings
   * atomically.  On success, the claim's initial verificationStatus is
   * 'unverified'; verification is async and triggered separately.
   *
   * @throws if the claim already exists with a different agentOrigin.
   */
  bind(claim: ClaimNode, evidence: NonEmptyArray<EvidenceBinding>): Promise<void>;

  /**
   * Re-run verification for a previously bound claim.
   *
   * Idempotent: calling this multiple times on the same claimId with the same
   * underlying sources must produce the same VerificationStatus.  Callers may
   * invoke this on every document edit without side-effects.
   *
   * @returns the new (or unchanged) VerificationStatus after re-verification.
   * @throws if claimId does not exist in the ledger.
   */
  reverify(claimId: string): Promise<VerificationStatus>;

  /**
   * Remove a claim and all its bindings from the ledger.
   *
   * Used when a claim node is deleted from the document.  Implementations
   * MUST hard-delete only when the verificationStatus is 'unverified'.
   * For verified or confirmed claims, implementations SHOULD soft-delete
   * (preserve for audit trail) unless the caller explicitly passes
   * `{ force: true }`.
   *
   * @throws if claimId does not exist in the ledger.
   */
  unbind(claimId: string, options?: { force?: boolean }): Promise<void>;
}
