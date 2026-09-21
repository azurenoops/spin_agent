# Issue #968: Mission Profile Authoring Permission Parity

Issue: https://github.com/azurenoops/spin_agent/issues/968
Branch: `fix/968-mission-profile-role-gates`, based on current `main`.

## Verified Finding

SystemProfile uses browser settings to permit MissionOwner, ISSM, ISSO, and an
unset role. SaveDraftAsync instead checks the requested system's active
MissionOwner, SystemOwner, or Issm assignments using RequireRoleAsync. The UI
therefore offers writes the server denies and excludes assigned SystemOwners.

## Repair and Verification Contract

- Return a server-computed `canEditProfile` capability with profile section data,
  including synthesized NotStarted sections, using the same actor and authorization
  decision as draft saves. Do not grant roles or add authentication bypasses.
- Use the capability, not browser persona or highest global role, for authoring.
  Missing/failed/loading capability is read-only. UnderReview remains locked.
- Preserve authenticated authorship, per-system/tenant checks, and save-time
  authorization. Role revocation after loading must still reject a save.
- Test qualifying, multiple, absent, inactive, wrong-user and wrong-system
  assignments; cover GET/PUT parity, reload, audit attribution, and UI role spoofing.
- Use synthetic unit, authenticated HTTP, and desktop/mobile browser tests. Run
  dashboard type checking/build and the required .NET build/test gates.
- Keep structured child-row persistence (#969), broader error feedback (#616),
  and review-role affordances outside this authoring-only fix.

Manual acceptance and exact external-write preview are required before declaring
the change ready or publishing a PR.

## Approved SQLite Scope Addition

Authenticated SQLite HTTP tests reproduced a first-save failure before capability
assertions: EF omitted `SystemProfileSections.RowVersion` from INSERT and SQLite
returned `NOT NULL constraint failed`. The user approved including a narrow
provider fix. Follow the neighboring BusinessContextDraft application-managed
SQLite token pattern, preserve SQL Server generated tokens, and verify stale-write
rejection as well as first-save persistence. This does not establish deployed SQL
Server acceptance or change browser RowVersion transport.

## Local Verification (2026-09-21)

- Red baseline: missing service capability; 21 dashboard permission failures;
  authenticated HTTP first-save SQLite failure, then missing capability fields.
- Green: 5,723 unit tests; 901 integration tests with 40 existing skips; 501
  dashboard tests; two isolated Playwright cases at 1440px and 390px.
- Authenticated HTTP tests cover all six section types, permitted author roles,
  inactive/unassigned/ISSO/wrong-user/wrong-system/wrong-tenant denials, ignored
  simulated-role headers, persistence, first-save audit, revocation, and stale writes.
- Focused coverage exercises all 28 changed executable C# lines. The previously
  partial SQLite state branch now reports 100% (4/4). Changed page permission,
  loading and error branches report no uncovered branches. Whole-page line coverage
  is 72.44% because untouched governance handlers are outside this focused suite;
  line coverage is not a claim of exhaustive path coverage.
- `tsc --noEmit`, dashboard production build, and solution build pass. Solution
  build reports 26 warnings outside changed lines; Vite reports a large bundle.
  Zero-warning compliance is not claimed.
- Browser tests replace API responses with synthetic fixtures. Authenticated
  persistence tests use SQLite, not deployed SQL Server. No production data changed.

## Manual Acceptance

The isolated worktree is `/Volumes/Internal/Downloads/repos/ato-copilot-fix-968`.
Its Vite preview is `http://127.0.0.1:4178/`. It requires a matching local backend;
the Playwright fixture is active only during the automated browser run.

1. With an active per-system MissionOwner, SystemOwner or ISSM assignment, open
   Mission & Purpose, save prefilled and edited scalar fields, and reload.
2. Verify Draft status, retained content, authenticated LastEditedBy and the
   first-save Drafted audit entry. Repeat for all six shared sections.
3. As ISSO-only or unassigned, verify read-only fields and no Save Draft. Changing
   the local persona must not enable edits. Repeat under the other organization.
4. Revoke an author assignment after load. Save must fail with the backend message;
   reload must become read-only. UnderReview must remain locked for authors.
5. Verify approved private-network SQL Server save/reload/audit behavior separately.

Manual acceptance and deployed SQL verification remain pending. No push or PR has
been authorized yet. The user requested the supplied Narratives UI mock as a
separate redesign after finishing #968; it is not part of this branch.