/**
 * grounding/verificationStateMachine.ts — idempotent state machine (#a493ec1c)
 *
 * Encodes the legal verification lifecycle transitions:
 *
 *   unverified  ──────────────────► machine_verified
 *   unverified  ──────────────────► contradicted
 *   machine_verified ─────────────► human_confirmed
 *   machine_verified ─────────────► contradicted
 *   human_confirmed  — TERMINAL (no further transitions)
 *   contradicted     — TERMINAL (no further transitions)
 *
 * Idempotency guarantee:
 *   Applying the same event to the same status always returns the same result.
 *   Re-running verification on an already-terminal status returns the current
 *   status unchanged (does NOT throw).
 *
 * This is a pure function module — no side effects, no I/O.
 */

import type { VerificationStatus } from '../research-workflow/types.ts';

export type { VerificationStatus };

/**
 * Events that drive the state machine.
 *
 *   machine_verified — automated verification passed (NLI score ≥ threshold).
 *   human_confirmed  — a human reviewer confirmed the claim-source link.
 *   contradicted     — automated or human review found the source contradicts
 *                      the claim.
 *   reset            — system rollback to unverified (e.g. source updated).
 */
export type VerificationEvent =
  | 'machine_verified'
  | 'human_confirmed'
  | 'contradicted'
  | 'reset';

// ---------------------------------------------------------------------------
// Transition table
// ---------------------------------------------------------------------------

type TransitionTable = Readonly<
  Record<VerificationStatus, Partial<Record<VerificationEvent, VerificationStatus>>>
>;

const TRANSITIONS: TransitionTable = {
  unverified: {
    // Self-loop: re-running unverified verification returns unverified (idempotent).
    machine_verified: 'machine_verified',
    contradicted: 'contradicted',
    // 'human_confirmed' not allowed from unverified — must go through machine first.
    reset: 'unverified',
  },
  machine_verified: {
    // Self-loop: re-running machine verification on an already-machine-verified
    // binding returns machine_verified (idempotent reverification).
    machine_verified: 'machine_verified',
    human_confirmed: 'human_confirmed',
    contradicted: 'contradicted',
    reset: 'unverified',
  },
  human_confirmed: {
    // Self-loop: human confirming an already-confirmed binding is a no-op.
    human_confirmed: 'human_confirmed',
    // Terminal — only 'reset' is permitted (e.g. source was retracted).
    reset: 'unverified',
  },
  contradicted: {
    // Self-loop: re-flagging a contradicted binding is a no-op.
    contradicted: 'contradicted',
    // Terminal — only 'reset' is permitted (e.g. claim was edited to resolve).
    reset: 'unverified',
  },
};

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Advance the verification state machine by one event.
 *
 * Idempotent: if the current status already matches the target implied by
 * the event (e.g. calling 'machine_verified' when status is already
 * 'machine_verified'), this returns the current status without error.
 *
 * @param current - The current VerificationStatus of the binding.
 * @param event   - The event to apply.
 * @returns The resulting VerificationStatus.
 * @throws {Error} if the event is not a legal transition from the current
 *   status (e.g. 'human_confirmed' from 'unverified').
 */
export function transitionVerificationStatus(
  current: VerificationStatus,
  event: VerificationEvent,
): VerificationStatus {
  const allowed = TRANSITIONS[current];
  if (event in allowed) {
    return allowed[event]!;
  }
  throw new Error(
    `VerificationStateMachine: illegal transition — ` +
    `cannot apply '${event}' to status '${current}'. ` +
    `Legal events from '${current}': ${Object.keys(allowed).join(', ') || '(none)'}.`,
  );
}

/**
 * Returns true if the status has no further transitions (excluding reset).
 * Terminal statuses are 'human_confirmed' and 'contradicted'.
 */
export function isTerminalStatus(status: VerificationStatus): boolean {
  return status === 'human_confirmed' || status === 'contradicted';
}
