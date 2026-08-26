/**
 * verificationStateMachine.test.ts — state machine correctness tests (#a493ec1c)
 *
 * Covers:
 *   1. All legal transitions (one assertion each).
 *   2. One failing-case test per ILLEGAL transition — confirms the machine
 *      throws on every forbidden edge.
 *   3. Property test: idempotency — applying the same event twice in a row
 *      yields the same result as applying it once (same-event-same-state).
 *   4. isTerminalStatus helper.
 *
 * Uses Node's built-in test runner (node:test) — zero new dependencies.
 * Run: node --experimental-strip-types --test 'src/**\/*.test.ts'
 */

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  transitionVerificationStatus,
  isTerminalStatus,
} from '../verificationStateMachine.ts';
import type { VerificationStatus, VerificationEvent } from '../verificationStateMachine.ts';

// ---------------------------------------------------------------------------
// 1. Legal transitions
// ---------------------------------------------------------------------------

describe('verificationStateMachine — legal transitions', () => {
  it('unverified + machine_verified → machine_verified', () => {
    assert.equal(transitionVerificationStatus('unverified', 'machine_verified'), 'machine_verified');
  });

  it('unverified + contradicted → contradicted', () => {
    assert.equal(transitionVerificationStatus('unverified', 'contradicted'), 'contradicted');
  });

  it('unverified + reset → unverified', () => {
    assert.equal(transitionVerificationStatus('unverified', 'reset'), 'unverified');
  });

  it('machine_verified + human_confirmed → human_confirmed', () => {
    assert.equal(transitionVerificationStatus('machine_verified', 'human_confirmed'), 'human_confirmed');
  });

  it('machine_verified + contradicted → contradicted', () => {
    assert.equal(transitionVerificationStatus('machine_verified', 'contradicted'), 'contradicted');
  });

  it('machine_verified + reset → unverified', () => {
    assert.equal(transitionVerificationStatus('machine_verified', 'reset'), 'unverified');
  });

  it('human_confirmed + reset → unverified', () => {
    assert.equal(transitionVerificationStatus('human_confirmed', 'reset'), 'unverified');
  });

  it('contradicted + reset → unverified', () => {
    assert.equal(transitionVerificationStatus('contradicted', 'reset'), 'unverified');
  });
});

// ---------------------------------------------------------------------------
// 2. Illegal transitions — one test per forbidden edge
// ---------------------------------------------------------------------------

describe('verificationStateMachine — illegal transitions throw', () => {
  it('ILLEGAL: unverified + human_confirmed throws (must go through machine first)', () => {
    assert.throws(
      () => transitionVerificationStatus('unverified', 'human_confirmed'),
      { message: /illegal transition/ },
    );
  });

  it('ILLEGAL: human_confirmed + machine_verified throws (terminal state)', () => {
    assert.throws(
      () => transitionVerificationStatus('human_confirmed', 'machine_verified'),
      { message: /illegal transition/ },
    );
  });

  it('ILLEGAL: human_confirmed + contradicted throws (terminal state)', () => {
    assert.throws(
      () => transitionVerificationStatus('human_confirmed', 'contradicted'),
      { message: /illegal transition/ },
    );
  });

  it('ILLEGAL: contradicted + machine_verified throws (terminal state)', () => {
    assert.throws(
      () => transitionVerificationStatus('contradicted', 'machine_verified'),
      { message: /illegal transition/ },
    );
  });

  it('ILLEGAL: contradicted + human_confirmed throws (terminal state)', () => {
    assert.throws(
      () => transitionVerificationStatus('contradicted', 'human_confirmed'),
      { message: /illegal transition/ },
    );
  });
});

// ---------------------------------------------------------------------------
// 3. Property test: idempotency
//
// For every legal (state, event) pair, applying the event a second time
// on the resulting state must return the same result as the first application.
// i.e.  t(t(s, e), e) === t(s, e)   for all legal (s, e).
// ---------------------------------------------------------------------------

describe('verificationStateMachine — idempotency property', () => {
  const legalPairs: [VerificationStatus, VerificationEvent][] = [
    ['unverified',       'machine_verified'],
    ['unverified',       'contradicted'],
    ['unverified',       'reset'],
    ['machine_verified', 'machine_verified'],  // self-loop
    ['machine_verified', 'human_confirmed'],
    ['machine_verified', 'contradicted'],
    ['machine_verified', 'reset'],
    ['human_confirmed',  'human_confirmed'],   // self-loop
    ['human_confirmed',  'reset'],
    ['contradicted',     'contradicted'],      // self-loop
    ['contradicted',     'reset'],
  ];

  for (const [status, event] of legalPairs) {
    it(`idempotent: (${status}, ${event}) applied twice yields same result`, () => {
      const once = transitionVerificationStatus(status, event);
      // Applying the same event a second time to the result must return the same state.
      // This is the idempotency guarantee: same-event-same-state.
      const twice = transitionVerificationStatus(once, event);
      assert.equal(
        twice,
        once,
        `Expected idempotency: t(t('${status}', '${event}'), '${event}') === t('${status}', '${event}') = '${once}', but got '${twice}'`,
      );
    });
  }
});

// ---------------------------------------------------------------------------
// 4. isTerminalStatus helper
// ---------------------------------------------------------------------------

describe('isTerminalStatus', () => {
  it('human_confirmed is terminal', () => assert.equal(isTerminalStatus('human_confirmed'), true));
  it('contradicted is terminal', () => assert.equal(isTerminalStatus('contradicted'), true));
  it('unverified is NOT terminal', () => assert.equal(isTerminalStatus('unverified'), false));
  it('machine_verified is NOT terminal', () => assert.equal(isTerminalStatus('machine_verified'), false));
});
