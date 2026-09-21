# Workspace Dependencies and Delivery Gates

**Feature**: [#1002](https://github.com/azurenoops/spin_agent/issues/1002)

**Checked**: 2026-09-21

**Base**: `origin/main` at `c3d74d9b`

**Scope**: Implementation authorized; dependency contracts and release gates remain open.

Issue states below were read from GitHub. An open issue is not evidence that
every reported defect remains reproducible on the current base. Its historical
reproduction and the current source findings must be distinguished; rerun the
actual acceptance tests before declaring a dependency resolved.

## Dependency matrix

| Dependency | Observed state | Required contract / outcome | Gate for #1002 |
|---|---|---|---|
| [#943](https://github.com/azurenoops/spin_agent/issues/943) public login configuration | Open | Anonymous public bootstrap configuration without granting tenant/privileged context; protected routes remain protected | Real ordinary-login E2E; verify current middleware before implementing a duplicate fix |
| [#941](https://github.com/azurenoops/spin_agent/issues/941) fresh CSP bootstrap deadlock | Open | Authorized initial provider administrator can complete bootstrap without an already-active customer tenant | Fresh-deployment acceptance; established-workspace UI can be developed independently |
| [#944](https://github.com/azurenoops/spin_agent/issues/944) controlled CSP registration | Open | Explicit initial-admin enrollment; registration hidden after the single hosting CSP becomes active | Fresh-deployment journey only; #1002 must reuse it, not create another login/enrollment flow |
| [#942](https://github.com/azurenoops/spin_agent/issues/942) organization identity association | Open | One authoritative identity/membership resolver, supported administration/repair workflow, ordinary organization access | Hard prerequisite for real multi-organization workspace selection and US1/US2 sign-off |
| [#950](https://github.com/azurenoops/spin_agent/issues/950) incorrect organization header | Open | Active scope uses server-authorized isolation tenant; profile/subgroup is separately labeled and refreshed; stale responses discarded | Context identity and cache-coherence acceptance; integrate one shared solution |
| [#968](https://github.com/azurenoops/spin_agent/issues/968) mission profile role gates | Closed at final status check | Server-authoritative complete assignment-based profile edit permission, fail-closed UI and structured errors | Consume the merged fix for Mission Profile permissions in US3 rather than duplicate it |
| [PR #1005](https://github.com/azurenoops/spin_agent/pull/1005) profile permission fix | Merged on 2026-09-21 at 16:55:42 UTC as `05924787` | Implementation for #968; PR body reports tests and remaining manual/SQL Server gates | Synchronize the feature branch with updated main before implementation and verify the merged contract; reported results are not this branch's test results |
| [#957](https://github.com/azurenoops/spin_agent/issues/957) subscription responsibility handoff | Open | Authoritative per-control Inherited/Shared/Customer allocation, applicable-baseline reconciliation, provenance/overrides/overlap/unsubscribe rules | US4 designation/CRM/SSP acceptance; do not derive inheritance from mapped IDs |
| [#1001](https://github.com/azurenoops/spin_agent/issues/1001) state-aware narratives and Narrative Library | Open; partial foundation merged in #1014 | Server-persisted scoped references, upload/extraction/mapping/review/publication, separate policy/technical freshness, preserved approved versions | Integrate the merged foundation, then verify remaining US4/US5 contracts; an open requirement is not satisfied by a placeholder link |
| [PR #1014](https://github.com/azurenoops/spin_agent/pull/1014) Narrative Library foundation | Merged as `1cbbc9e6` on 2026-09-21 at 17:28:39 UTC | Imports, reviewed publication, manual proposals, freshness and system narrative workspace routes | Synchronize after backend membership edits are checkpointed; PR explicitly lists automatic proposals, scope/reviewer policy, concurrency, SQL Server, regression and manual-acceptance gaps |
| [#937](https://github.com/azurenoops/spin_agent/issues/937) Mission Owner assignment | Closed | Existing supported Person/role resolution foundation | Regression coverage, not a new membership model or an assumed fix for #942 |

Historical issues #68, #89, #94, #221 and #209 are references from #1002, not
new implementation tasks. Their closure/behavior was not independently audited
for this planning phase.

During planning, #1005 changed from open/conflicted to merged and #968 closed.
The final check found `origin/main` at `05924787`, one commit ahead of this
feature branch's inspected base. No merge/rebase or runtime changes were made
under the documentation-only authorization. Source observations in the plan are
anchored to `c3d74d9b`; revalidate changed profile contracts after synchronization.

During implementation, #1005 was integrated. A later dependency check found
#1014 merged and `origin/main` at `1cbbc9e6`, while #1001 remains open. Its changes
overlap the in-progress membership context file, so the shared branch is not
merged underneath the active backend work. Incorporate it after that work is
checkpointed, preserving both sets of schema/context additions.

## Recommended order

```text
Approve workspace design and child-story traceability
  |
  +-- Verify/integrate #943 ----------- ordinary-login bootstrap
  |       +-- #941 + #944 ------------ fresh-deployment journey
  |
  +-- #942 membership contract ------- per-request context authorization
  |       +-- US1 workspace entry
  |       +-- US2 URL/tab scope + #950 identity/cache coherence
  |
  +-- #968 / PR #1005 ---------------- scoped profile permissions
  |       +-- US3 persona navigation/action alignment
  |
  +-- #957 responsibility contract --- persisted system allocations
  |       +-- US4 responsibility presentation/reconciliation
  |
  +-- #1001 reference/change contracts
          +-- US4 provider change -> customer review
          +-- US5 scoped Narrative Library

All required integrations -> real-API persona E2E -> local manual acceptance
                         -> exact external-write preview/approval -> feature PR
```

The branches of this graph may be developed independently after contract
approval; the final feature is not complete until they converge. A UI-only
increment may be reviewed as partial work, but must not claim to close #1002.

## Approved child-story publication

The user approved the exact titles, bodies and parent linkage on 2026-09-21.
The five stories below were created and linked to #1002.

| Local story / GitHub issue | Title | Required task checklist for the issue body |
|---|---|---|
| US1 / [#1015](https://github.com/azurenoops/spin_agent/issues/1015) | Enter CSP and organization workspaces through existing login | Consume authorized membership contract; resolve landing; preserve authorized deep links; deny unknown identities; test ordinary organization login |
| US2 / [#1016](https://github.com/azurenoops/spin_agent/issues/1016) | Preserve workspace context across tabs and navigation | Scoped routes/request selector; context header; ordinary switcher; stale-response/cache isolation; legacy routes; refresh/history/two-tab tests |
| US3 / [#1017](https://github.com/azurenoops/spin_agent/issues/1017) | Align workspace actions with scoped RMF permissions | Scoped permission projection; persona navigation/action matrix; Mission Owner negative cases; separate audited support; UI and direct-HTTP tests |
| US4 / [#1018](https://github.com/azurenoops/spin_agent/issues/1018) | Connect provider changes to customer responsibility review | Consume #957 allocations; display provenance/prerequisites; connect #1001 change proposals; preserve approved narratives; overlap/unsubscribe/tenant tests |
| US5 / [#1019](https://github.com/azurenoops/spin_agent/issues/1019) | Scope the Narrative Library to each authorized workspace | Consume #1001 API; provider/org/system entry points; scope all uploads/downloads/jobs; deny foreign drafts; provenance and browser tests |

Publication procedure:

1. Present the exact title and full body of each proposed issue in readable form.
2. Obtain approval for GitHub writes.
3. Create each issue and add its actual sub-issue relationship to #1002.
4. Replace proposed references in the specification and plan with real links.
5. Keep tasks and completion evidence synchronized. A task checklist is not a
   substitute for the actual GitHub parent linkage.

## Contract decisions before implementation

### Confirmed by the user

- Plan before implementation.
- Independent tab/deep-link contexts, validated by the server.
- Start implementation after design review; publish the five previewed child
  stories and link them to #1002.
- Implement #942's identity/membership prerequisite on this branch before
  continuing the dependent workspace UI (confirmed during implementation).
- Use explicit membership records bound to the trusted directory/object-ID pair
  and linked to existing organization-local Person records.
- Permit grants/revocations by CSP administrators and already-authorized
  administrators of the selected organization. Existing unmapped users need
  an explicit grant; emails, directory membership and empty-org bootstrap do
  not implicitly grant membership.
- Require an applicable organization/system role assignment for ordinary system
  visibility. Membership alone is not an all-systems read grant; administrative
  and CSP oversight remain separately authorized.
- Continue through all remaining dependencies to complete #1002, rather than
  publishing a bounded foundation PR. This includes the remaining #957/#1001
  integrations, per-tool authorization, transport isolation and support-session
  revocation. None of those gates may be silently waived.

### Proposed in this feature

- Explicit workspace-kind and organization route scope; ordinary context cannot
  inherit support impersonation from another tab.
- Central request context plus scoped action permissions rather than a browser
  persona or a cached successful CSP endpoint probe.
- Preserve old routes using authorized resolution and replace redirects; a
  forbidden legacy link cannot silently redirect into another tenant.
- Keep authentication tokens out of URLs and use the existing MSAL/cookie
  authentication; route identifiers are selectors, not credentials.

### Dependency-owner decisions still required

- **#942**: canonical identity key, grant/revoke authority, shared-directory and
  guest semantics, data repair/migration and compatibility of existing mappings.
  No new membership schema or directory-wide grant is silently assumed here.
- **#941/#944**: trusted first-administrator enrollment mechanism. Workspace
  selection cannot bootstrap privilege.
- **#957**: allocation source/version and conflict/overlap reconciliation contract.
- **#1001**: exact library routes/API, source visibility/publication and provider
  change/review event contract. Existing component documents are not renamed as
  a completed Narrative Library.

These decisions are explicit implementation blockers, not reasons to replace
dependencies with local guesses.

## Evidence that changes the implementation approach

- The traced `select-tenant` handler audits selection and optionally remembers
  it; the ordinary selection is not consumed as per-tab request scope by the
  current tenant middleware. The new workspace selector therefore needs backend
  integration, not just a renamed dropdown.
- Current support scope is cookie-backed. Retaining it as an unconditional
  override on new ordinary requests would violate the user's independent-tab
  decision. Request mode, target validation and compatibility must be designed
  together.
- Organization-owned capability regeneration and CSP-owned publication are
  different paths. The investigation did not establish that CSP publication
  triggers customer narrative regeneration, and did not reproduce an approved
  narrative overwrite. Existing approved-snapshot/version semantics must be
  reused and tested, not replaced based on that unverified assumption.
- The context-regeneration helper emitted errors/malformed output on macOS.
  Its changes were discarded. The [plan](plan.md#planning-artifact-checks-and-tooling-limitation)
  records the exact limitation; resolving it is a tooling follow-up, not a
  workspace authorization workaround.

## Completion gates

1. Design and implementation approved by the user on 2026-09-21; dependency
   contract/ownership decisions remain required.
2. Approved child issues #1015-#1019 exist and are linked to #1002.
3. Membership and scoped permission contracts are integrated and tested.
4. Approved dependency implementations are incorporated without duplicate login,
   permission or narrative lifecycle logic.
5. Failing-first unit/integration/browser tests precede each production change.
6. Full-pipeline tenant tests run without the tenant-resolution test bypass.
7. Targeted suites, builds/static checking and integration/browser acceptance pass;
   skipped tests and warnings are reported, not counted as success.
8. User has a runnable local fixture and an opportunity to manually test every
   changed experience before the feature is declared complete.
9. Exact PR title/body and push target are previewed and approved before external
   writes. This planning phase neither pushes nor creates a PR.
