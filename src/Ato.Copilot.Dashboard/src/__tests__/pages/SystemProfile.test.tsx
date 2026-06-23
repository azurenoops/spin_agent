/**
 * SystemProfile — isReadOnly logic (#517)
 *
 * Issue: Mission & Purpose form fields are disabled for ISSM and ISSO roles.
 * Bug: the original isReadOnly expression only allowed 'MissionOwner' to edit;
 *      ISSM and ISSO — the primary personas who fill out mission profiles — were
 *      locked out along with every other non-MissionOwner role.
 *
 * Fix: export EDITOR_ROLES + computeIsReadOnly from SystemProfile.tsx so the
 *      logic can be unit-tested in isolation, then expand EDITOR_ROLES to
 *      include 'ISSM' and 'ISSO'.
 *
 * TDD red→green:
 *   RED  — before the fix, ISSM/ISSO assertions fail because the exported
 *           function still contains the buggy `role !== 'MissionOwner'` check.
 *   GREEN — after the fix, all assertions pass.
 */
import { describe, it, expect } from 'vitest';
import { computeIsReadOnly } from '../../pages/SystemProfile';

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('SystemProfile isReadOnly logic (#517)', () => {
  // ── Primary fix: ISSM and ISSO must be able to edit ──────────────────────

  it('ISSM can edit a NotStarted section (was blocked — primary bug)', () => {
    expect(computeIsReadOnly('NotStarted', 'ISSM')).toBe(false);
  });

  it('ISSO can edit a NotStarted section (was blocked — primary bug)', () => {
    expect(computeIsReadOnly('NotStarted', 'ISSO')).toBe(false);
  });

  it('ISSM can edit a Draft section', () => {
    expect(computeIsReadOnly('Draft', 'ISSM')).toBe(false);
  });

  it('ISSO can edit a NeedsRevision section', () => {
    expect(computeIsReadOnly('NeedsRevision', 'ISSO')).toBe(false);
  });

  // ── MissionOwner must remain editable (regression guard) ─────────────────

  it('MissionOwner can edit a NotStarted section (unchanged)', () => {
    expect(computeIsReadOnly('NotStarted', 'MissionOwner')).toBe(false);
  });

  it('MissionOwner can edit a Draft section (unchanged)', () => {
    expect(computeIsReadOnly('Draft', 'MissionOwner')).toBe(false);
  });

  // ── Reviewer/approver roles must remain read-only ────────────────────────

  it('SCA is read-only on a NotStarted section', () => {
    expect(computeIsReadOnly('NotStarted', 'SCA')).toBe(true);
  });

  it('AO is read-only on a NotStarted section', () => {
    expect(computeIsReadOnly('NotStarted', 'AO')).toBe(true);
  });

  it('Engineer is read-only on a NotStarted section', () => {
    expect(computeIsReadOnly('NotStarted', 'Engineer')).toBe(true);
  });

  // ── UnderReview lock must always apply regardless of role ─────────────────

  it('UnderReview section is always read-only for ISSM', () => {
    expect(computeIsReadOnly('UnderReview', 'ISSM')).toBe(true);
  });

  it('UnderReview section is always read-only for ISSO', () => {
    expect(computeIsReadOnly('UnderReview', 'ISSO')).toBe(true);
  });

  it('UnderReview section is always read-only for MissionOwner', () => {
    expect(computeIsReadOnly('UnderReview', 'MissionOwner')).toBe(true);
  });

  it('Approved section is editable for ISSM (not UnderReview, not read-only role)', () => {
    expect(computeIsReadOnly('Approved', 'ISSM')).toBe(false);
  });

  // ── No role set → editable (open default for new/unassigned users) ────────

  it('No role set (empty string) is editable — open default for new users', () => {
    expect(computeIsReadOnly('NotStarted', '')).toBe(false);
  });

  it('No role set on Draft section is also editable', () => {
    expect(computeIsReadOnly('Draft', '')).toBe(false);
  });

  // ── Edge cases ────────────────────────────────────────────────────────────

  it('undefined governanceStatus with ISSM is editable', () => {
    expect(computeIsReadOnly(undefined, 'ISSM')).toBe(false);
  });

  it('undefined governanceStatus with SCA is read-only', () => {
    expect(computeIsReadOnly(undefined, 'SCA')).toBe(true);
  });
});
