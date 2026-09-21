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