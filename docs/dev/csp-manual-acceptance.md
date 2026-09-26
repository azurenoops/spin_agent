# CSP manual acceptance from a reset local dataset

## Scope and evidence

This is a **source-inspected manual test guide**, not a test execution report.
It covers CSP/provider features and the customer actions needed to check provider
handoff and access boundaries. It is not an exhaustive mission RMF checklist.

- Target: `http://localhost:5173`.
- Inspected checkout: `azurenoops/ato-copilot`,
  `feature/1002-workspace-ui-1025-1035`; preflight HEAD was
  `903185db` (`ato package examples`), with existing uncommitted work.
  The working source, not that commit alone, is the evidence.
- **Runtime verification by the author of this guide: none.**
  No tests, builds, Docker operations, runtime seeding, or application changes
  were performed for this guide. The reset baseline below was separately
  verified and reported by the parent session; it is not feature-test evidence.
- **45 scenarios; all results start NOT RUN.** Nine scenarios have an explicit
  **BLOCKED implementation status**: 29, 30, 32, and 37–42.
  The other 36 are source-wired or conditional, not 36 proven passes.
- Current route mappings, DTOs, service implementations, and DI take precedence
  over historical screenshots, comments, specifications, and analyzer-only tests.
  Recheck this guide against the build actually running before reporting a defect.

This approach follows [AGENTS.md — Verification Protocol][agent-rules] and the
[constitution — Documentation as Source of Truth, Test-Driven Development, and
Security][constitution]. This document adds acceptance instructions only; it does
not implement the paused features.

### Parent-verified reset baseline

The parent session reported the following reset facts for
**2026-09-24T15:54:59Z**. This guide's author did not rerun these checks.

| Area | Parent-verified state |
|---|---|
| Reset | Exactly five `ato-copilot` volumes removed and recreated |
| Containers | All five containers healthy |
| HTTP health | Dashboard, MCP, and Chat health checks returned HTTP 200 |
| Nonempty application SQL tables | Only `__MigrationFlags = 1`, `NistControls = 1196`, `OverlayDocuments = 4`, and `Tenants = 1` |
| Sole tenant, subsequently confirmed | ID `00000000-0000-0000-0000-000000000000`; DisplayName `Ato.Copilot.System`; `Status = 0`; `OnboardingState = 2` |
| CSP/provider data | Every `Csp*` / `Provider*` table empty, including profiles, packages, candidates, components, capabilities, offerings, decisions, boundaries, findings, and POA&Ms |
| Registered systems | `RegisteredSystems = 0` |
| Chat database | All tables empty |
| Files and cache | `/data` empty; Redis `DBSIZE = 0` |
| Browser | Left at `/login` with all nine simulation buttons visible; parent made no identity selection and seeded no scenarios after reset. Prior cookies may remain. |

The sole tenant is the confirmed **system bootstrap row**, not Flankspeed, Alpha,
Beta, Cedar, Juniper, or an enrolled customer organization. Its numeric
`OnboardingState` is not CSP-profile finalization: `CspProfiles` is still empty.
Reference controls and four overlay documents are bootstrap reference data,
not evidence that provider onboarding, membership, or customer setup is complete.
Healthy containers and HTTP 200 establish the reset baseline only.
**All 45 feature scenarios remain NOT RUN.**
The parent also recorded reset evidence in [the existing ingestion guide][ingestion-guide]
and linked this guide in MkDocs; neither action executes a scenario.

## Before starting

1. Use the parent-verified baseline above for this run; do **not** reset volumes
   again. Record which build is running. For a later run, have the stack owner
   confirm its baseline rather than assuming these counts are still current.
2. At `/login`, explicitly choose **Dev CSP-Admin** (`dev-cspadmin`), then open
   `http://localhost:5173/workspaces/csp/onboarding/csp`.
   The parent left the browser at `/login` without signing in. Prior browser cookies
   may remain; do not equate the reset with cleared browser storage or trust an
   old selected identity. Explicit Dev CSP-Admin selection replaces that identity.
   **CSP onboarding is the first setup workflow:** create/save the draft (02),
   account for optional intake (03), then finalize (04). `CspProfiles = 0` means
   there is no saved profile to resume initially. Provider-feature onboarding
   gates before Active mean **SETUP NEEDED**, not an instruction to bypass them.
3. Only afterward create Cedar/Juniper and their initial administrators through
   cases 06–07, grant memberships (08), and assign actual roles (09). There are
   no prior Flankspeed/Alpha/Beta, customer systems, packages, or provider records
   to select. Use the nine configured simulation identities below, but do not
   add a token, edit a database, run a seed script, or change configuration to
   grant access.
4. Use only fictional names and the repository's synthetic fixtures. Do not enter
   real credentials, tenant/subscription identifiers, personal data, or real ATOs.
   Do not run Azure discovery, live integrations, model-backed remapping, or AI
   text generation in the default run.
5. Use separate browser profiles for simultaneous actors. Tabs in one profile
   may share the selected identity. Two tabs of the **same actor** are useful for
   concurrency tests.
6. For each write, record the returned identifier/revision, reload the page, and
   re-open the record before declaring persistence. A toast or green wizard step
   alone is insufficient.
7. Browser developer tools may be used to **inspect the local UI's own requests
   and responses** for exact IDs, hashes, revisions, errors, and citations. Do
   not manually manufacture successful requests, query the DB, replay privileged
   tokens, or publish captured credentials. Record sanitized evidence only.
8. An unavailable control, unexpected error, or missing prerequisite is not PASS.
   Record FAIL for a reproducible deviation in an available feature; record
   BLOCKED with a reason when the test cannot reach its assertion. Keep separate
   sub-results when only the negative path was exercised. An initial
   **Unaffiliated** role, no customer workspace, or missing persisted membership
   is **SETUP NEEDED**: note the required normal enrollment/role step and leave
   its dependent case NOT RUN or BLOCKED by that prerequisite. This expected
   empty-state condition is not itself a defect, an access bypass, or permission
   to override access controls.

### What “complete” means here

| Term | Exact meaning; what it does **not** mean |
|---|---|
| Setup needed | A normal onboarding, membership, or role prerequisite has not been persisted. Not a grant of access, a feature PASS, or permission to bypass a gate. |
| Test PASS | The named assertions were manually observed, including refresh/persistence where applicable. Not feature-wide certification. |
| CSP onboarding Active | Identity, support, and classification were finalized. Not package approval or an authorization decision. |
| Organization setup complete | Durable tenant, Person when requested, membership, and initial Administrator stages completed. Not all organization onboarding or RMF work. |
| Analysis finished | Processing reached its reported terminal state. Every source entry still needs an honest disposition; unsupported/excluded is not analyzed. |
| Human reviewed / Mapped | A person reviewed that candidate, mapping, or reference. Not necessarily approved for publication. |
| Approved exact preview | Approval binds the precise revision, selection, hashes, dependencies, and context. It has not yet published anything. |
| Published | A persisted release or approved set was created. Earlier releases remain historical records. Not customer adoption, acceptance of duties, or ATO issuance. |
| Recorded external metadata | A person reviewed a retained source's stated decision. Not independent verification of its authority, signature, or current validity. |
| Customer setup Done | The setup operation's writes completed. Not responsibility review or covered-workload authorization. |
| Mission authorization | A separate authorized human decision. Never inferred from a provider role, hosting assignment, imported claim, or published capability. |

There is **no universal Complete button**. Use the action and completion criterion
in each case. Never close the nine blocked implementation cases because a warning
page, an analyzer test, or a backend class exists.

## Fictional data and identities

### Shared names

| Object | Use this value |
|---|---|
| Provider legal entity | `Fictional Azure Example Provider` |
| Provider display name | `Fictional Azure Provider` |
| Support | `support@example.test`; optional phone/logo left blank |
| Classification | `Unclassified` |
| Cedar organization | `Cedar Research Office` |
| Juniper organization | `Juniper Operations Office` |
| Cedar system | `Cedar Azure Records` |
| Juniper system | `Juniper Azure Training` |
| Main offering | `Synthetic Azure Shared Services` |
| Additional offering | `Synthetic Azure Training Services` |
| Boundary v1 | `Synthetic shared-services boundary v1` |
| Boundary scope | Four illustrative services: Microsoft Entra ID, Azure Monitor, Azure Key Vault, Azure Firewall; no actual deployment asserted |
| Exclusions | Customer application code, endpoints, mission databases, and user eligibility decisions |
| Provider responsibilities | Operate the illustrative shared services; maintain provider reference material |
| Customer responsibilities | Configure customer use, review audit output, implement customer duties, and obtain mission authorization |
| Manual components | `Synthetic Azure Monitor service`, `Synthetic Microsoft Entra identity`, and disposable `Synthetic scratch component` |
| Legacy capability | `Synthetic access assurance`, control `IA-2` |
| Canonical capability | `Synthetic audit review`, control `AU-6`, initially `Shared` |
| Reference title | `Synthetic audit review reference` |

Where a scope form requires Azure-shaped values, these are **fictional input
strings only**, not resources to discover or provision:

- Cloud/environment: select **Azure Government** (`AzureUSGovernment` where a
  raw cloud value is requested).
- Fictional Azure directory: `20000000-0000-0000-0000-000000000001`.
- Fictional subscription: `30000000-0000-0000-0000-000000000001`.
- Included resource-group scope:
  `/subscriptions/30000000-0000-0000-0000-000000000001/resourceGroups/rg-cedar-synthetic`.
- Example excluded scope:
  `/subscriptions/30000000-0000-0000-0000-000000000001/resourceGroups/rg-customer-app-synthetic`.

The fictional Azure directory above is **not** the local login directory below.
Do not use it when granting local simulation identities organization membership.

### Nine configured login choices

All nine configured identities use directory ID
`00000000-0000-0000-0000-000000000001`. The full object IDs below are identity
keys, **not Person IDs**. The application creates organization-local Person IDs.

| Login identity | Object ID | Fictional Person / intended persisted role |
|---|---|---|
| `dev-cspadmin` | `10000000-0000-0000-0000-000000000001` | Provider operator; configured `CSP.Admin` and `Global Reader` claims |
| `dev-isso` | `10000000-0000-0000-0000-000000000002` | Riley Chen, `riley.chen@example.test`; Isso |
| `dev-soc-analyst` | `10000000-0000-0000-0000-000000000003` | No membership initially; negative-access actor |
| `dev-orgadmin` | `10000000-0000-0000-0000-000000000004` | Morgan Reed, `morgan.reed@example.test`; Administrator |
| `dev-mission-owner` | `10000000-0000-0000-0000-000000000005` | Casey Rivera, `casey.rivera@example.test`; MissionOwner |
| `dev-system-owner` | `10000000-0000-0000-0000-000000000006` | Jordan Ellis, `jordan.ellis@example.test`; SystemOwner |
| `dev-issm` | `10000000-0000-0000-0000-000000000007` | Avery Shah, `avery.shah@example.test`; Issm |
| `dev-sca` | `10000000-0000-0000-0000-000000000008` | Taylor Brooks, `taylor.brooks@example.test`; Assessor |
| `dev-ao` | `10000000-0000-0000-0000-000000000009` | Quinn Patel, `quinn.patel@example.test`; AuthorizingOfficial |

These are intended assignments for this run, not preexisting permissions.
Selecting `dev-ao` does not assign AO authority. Even the configured ISSO claim
does not create an ordinary organization membership. Effective organization
access requires a persisted identity-to-Person membership and appropriate
persisted roles. Provider access is the separate configured CSP claim.
If an identity initially appears unaffiliated or has no organization membership,
label it **SETUP NEEDED**, then use the normal workflows in 06–09. The parent has
not selected an identity since reset; explicitly select one rather than relying
on old cookies. The system bootstrap tenant supplies no customer Person binding
or role.

### Record actual server identifiers

Fill these during testing; never substitute invented GUIDs or hashes:

| Record | Actual ID / revision / notes |
|---|---|
| Cedar tenant and enrollment key | ____________________ |
| Juniper tenant and enrollment key | ____________________ |
| Cedar / Juniper Morgan Person IDs | ____________________ |
| Other Cedar Person / membership / role IDs | ____________________ |
| Cedar / Juniper system IDs | ____________________ |
| Main / additional offering IDs | ____________________ |
| Boundary v1 / v2 IDs and snapshot hashes | ____________________ |
| Manual component / capability IDs | ____________________ |
| Canonical working revision / preview / release IDs | ____________________ |
| Package / operation / series / version IDs | ____________________ |
| Retained artifact IDs, archive paths, and locators | ____________________ |
| External decision record / revision IDs | ____________________ |
| Support session / correlation / expiry | ____________________ |

## Routes and current navigation

In the scenarios, expand these aliases using the actual identifiers:

| Alias | URL |
|---|---|
| `P` | `http://localhost:5173/workspaces/csp` |
| `C` | `http://localhost:5173/workspaces/organizations/{cedarTenantId}` |
| `J` | `http://localhost:5173/workspaces/organizations/{juniperTenantId}` |
| `SC` | `http://localhost:5173/workspaces/support/organizations/{cedarTenantId}` |
| `A` | `P/authorizations/offerings/{mainOfferingId}` |
| `K` | `A/packages/{packageId}` |

Provider navigation is **Overview, Organizations, Authorizations, Security
Capabilities, Controls, Audit Log, Knowledge Base**. Additional linked routes:

- Provider systems: `P/systems`.
- Organization creation: `P/organizations/new`.
- Enrollment: `P/organizations/{tenantId}/provisioning?key={creationKey}`.
- Provider membership management: `P/organizations/{tenantId}/memberships`.
- Ordinary organization membership management: `C/settings/memberships`.
- CSP onboarding: `P/onboarding/csp`.
- Provider reference library: `P/narrative-library`, then `/import`.
- **Legacy component management: `P/csp/inherited-components`.**
  `P/components` and `P/capabilities` redirect to grouped views of
  `P/security-capabilities`; they are not that component editor.
- `P/security-capabilities/imports` redirects to Authorizations.
  A legacy package-detail link redirects through
  `P/authorizations/import?packageId={id}` to its retained association.
- Ordinary organization role setup: `C/onboarding?stepNav=admin`.
- Minimal system setup: `C/systems/new`; roles:
  `C/systems/{systemId}/roles`; provider capability setup:
  `C/systems/{systemId}/security-capabilities`; responsibility review:
  `C/systems/{systemId}/inheritance/subscriptions`.
- `C/systems/{systemId}/provider-relationships` is a sidebar destination
  **without a mounted matching page in the inspected source**.

## Synthetic package fixtures

Select files from [the existing package-import examples][fixtures]. These files
do not appear in the reset database until you upload them normally.

| Fixture | Use and limits |
|---|---|
| [azure-example-package.json][azure-inventory] | Four illustrative Azure components and four capabilities using IA-2, AU-6, SC-12, SC-7; Shared duties. Useful for inventory intake during onboarding. Not a Microsoft ATO or verified deployment. |
| [azure-authorization-example.json][azure-authorization] | Authorization-led synthetic example, four components/four capabilities, one fictional decision, two boundary claims, two findings, two POA&M plans. Use for the typed-claim gap and honest authority denial. |
| [synthetic-inventory.json][small-inventory] | Synthetic audit service and event logging capability, AU-2 Provider; useful for dependency and duplicate review. |
| [synthetic-authorization-reference.json][legacy-reference] | Legacy AuthorizationReference; Synthetic test authority, issued `2026-01-01T00:00:00Z`, expires `2027-01-01T00:00:00Z`. Not an ATO. |
| [synthetic-oscal.json][oscal-fixture] | OSCAL SSP, version 1.0 / OSCAL 1.1.2; synthetic audit service and backup scheduler, AU-2. Source “operational” wording is not assessed reality. |
| [no-candidates.json][empty-fixture] | Valid structured source with no declared inventory candidates. Zero proposals must not be mislabeled a parser failure. |
| [unsupported.synthetic][unsupported-fixture] | Harmless unsupported format. Top-level UI rejects this extension. For archive coverage, the tester may create a ZIP **outside the repository** containing this and `synthetic-inventory.json`; no new repository fixture is required. |

Important metadata in `azure-authorization-example.json`:

- Reference `SYN-AZ-DECISION-001`; subject `Fictional Azure Example Provider`;
  authority `Fictional Local Test Authorizing Official`.
- Decision `Synthetic conditional provider decision`; status
  `Fictional test decision; not effective`; stated decision date `2026-09-01`,
  expiration `2027-08-31`. **Do not replace this with “ATO” to pass a gate.**
  Do not invent a missing effective date.
- Finding `SYN-AZ-FIND-001`: Moderate/Open, AU-6 audit-alert review gap.
  `SYN-AZ-FIND-002`: Low/Open, SC-12 recovery-rehearsal gap.
  Assessment date `2026-09-10`; POA&M milestones are in October 2026;
  submitted-evidence lists are empty.
- Analyzer-level expectations describe 23 proposals: 4 components, 4
  capabilities, 4 mappings, 4 responsibilities, 1 decision, 2 boundary claims,
  2 findings, 2 POA&M items. **This is not a verified browser result or a promise
  that all typed payloads reach the public API.** Record actual results.

## Feature coverage and setup order

“Wired” below means UI/service plumbing was inspected, not that runtime passed.

| Feature group | Cases | Source status | Trace |
|---|---|---|---|
| Identity, workspace selection, isolation | 01, 10 | Wired; persisted membership required | [Routes][routes], [workspace service][workspace-service], [login configuration][login-config] |
| CSP draft/finalization and optional intake | 02–04 | Wired; Active profile is locked | [Wizard][csp-wizard], [profile service][profile-service] |
| Provider overview and organization summaries | 05 | Wired; some projections explicitly unavailable | [Dashboard][dashboard], [organization pages][organization-pages] |
| Organization creation/enrollment/recovery | 06–07 | Wired, staged and idempotent | [Creation UI][organization-create], [enrollment UI][enrollment-ui], [endpoints][workspace-endpoints] |
| Memberships, actual roles, minimal systems | 08–09 | Wired; membership is not a role | [Membership UI][membership-ui], [membership service][membership-service], [role setup][role-setup] |
| Audited support | 11–12 | Wired; session/actor/target/expiry checked | [Entry UI][support-ui], [banner][support-banner], [support service][support-service] |
| Catalog, legacy components/capabilities/remap | 13–16 | Wired; remap execution conditional on no-network setup | [Legacy page][legacy-components], [legacy service][legacy-service], [lifecycle spec][lifecycle-spec] |
| Canonical working revisions/releases/subscribers | 17–19 | Wired for unlinked manual baseline; offering guard applies when linked | [Workspace UI][workspace-ui], [service][workspace-operations], [publication guard][publication-guard] |
| Provider narrative references | 20–21 | Wired; reference publication is separate | [Reference UI][reference-ui], [service][reference-service], [narrative spec][narrative-spec] |
| Offerings/boundaries | 22–23 | Wired; offering update backend has no matching edit form | [Authorizations UI][authorization-ui], [authorization service][authorization-service] |
| Package intake/coverage/review/duplicates | 24–28, 33 | Wired legacy inventory contract; publication caveats below | [Package UI][package-ui], [processor][package-processor], [package contract][package-contract] |
| Typed claim review / enrichment | 29–30 | **BLOCKED:** payload/DTO/route integration missing | [DTOs][package-dtos], [claim UI][claim-ui], [enrichment UI][enrichment-ui], [package routes][package-endpoints] |
| Authorization impact / offering-linked publication | 31–32 | Negative impact path wired; positive synthetic publication **BLOCKED** | [Impact UI][impact-ui], [impact service][impact-service], [guard][publication-guard] |
| External decisions/inherited references | 34–36 | Metadata workflow wired; source authority not verified | [Decision UI][decision-ui], [decision service][decision-service], [eligibility][decision-eligibility] |
| Hosting revisions/assignments | 37–38 | **BLOCKED:** service stubs, absent routes/DI | [Hosting UI][hosting-ui], [hosting service][hosting-service], [Program][program] |
| Findings/POA&M/evidence closure | 39 | **BLOCKED UI:** backend implemented/mapped, page placeholder | [Authorizations UI][authorization-ui], [finding service][finding-service], [finding routes][finding-endpoints] |
| New mission offering relationship/adoption/AO handoff | 40–42 | **BLOCKED:** no page/routes; service stubs | [Relationship client][relationship-client], [mission service][mission-service], [routes][routes] |
| Settings, audit, Controls, Knowledge Base | 43–45 | Linked/shared surfaces; admin authorization conditional | [Settings][settings-ui], [audit][audit-ui], [Controls][controls-ui], [Knowledge Base][knowledge-ui] |

Broader intended behavior is described by the [role-aware workspace
specification][workspace-spec], [provider authorization contract][authorization-contract],
and [tenant isolation specification][tenant-spec]. These do not establish that a
paused feature is implemented.

### Recommended dependency order

1. **01 (CSP login only) → 02 → 03 → 04 → remaining 01 checks**: start the
   provider draft from empty, account for optional durable package receipt, and
   finalize before testing ordinary provider operations or customer setup.
   Defer 01's other-identity checks until after CSP finalization, but before
   enrolling those identities. Case 03 can create the main offering/boundary;
   reuse those in 22–24 rather than making duplicates. If optional intake is
   unavailable before finalization, record that branch as setup-blocked, skip
   it, and exercise intake after 04.
2. **05 → 06 → 07 → 08 → 09 → 10–12**: create the two organizations normally,
   bind identities, assign actual roles, register minimal systems, then test
   isolation and support. Organization Administrator, Issm, and Isso are needed
   for the ordinary onboarding required-role steps.
3. **14 → 15 → 13 → 17 → 18 → 19**: create provider components using the legacy
   permalink before canonical Add capability. Keep the baseline manual capability
   **unlinked to an offering**. Preserve it for release/subscriber tests.
4. **20 → 21**: references after a reviewed capability exists. Case 16 can run
   as a cancel/safety test; do not enable a live AI dependency merely to finish it.
5. **22 → 23 → 24 → 25 → 26 → 27 → 28**: package lifecycle. Use scratch
   candidates for rejection/exclusion; retain one intact dependency set.
6. **34 → 35 → 36 → 31 → 32**: truthful decision metadata and authorization
   blockers. Cases 29–30 and 37–42 document blocked acceptance paths; they are
   not dependencies to bypass.
7. **33 and 43–45**: history/association, preferences, audit, shared references.
   Test destructive scratch operations last.

**Fresh-dataset bootstrap warning:** CSP profile onboarding comes first because
no profile exists. Normal initial-administrator enrollment then establishes
customer access; nine login choices are not nine enrolled users. Unaffiliated
identity/missing membership means **SETUP NEEDED**, not a reason to bypass
authorization. Another practical prerequisite is one provider component: the
canonical picker’s “Manage provider components” route goes to the grouped catalog,
so use `P/csp/inherited-components` for actual manual component creation after
provider onboarding is Active.

### Empty-database initial Administrator path

**Source-traced conclusion, not a runtime pass:** the current provider UI/API
has a new-Person enrollment path. It does **not** require an existing Person
record for the first customer administrator. Do not confuse a UI label such as
“Person record ID” / `PersonRecordId` with the configured user's object ID;
the current provisioning request uses `personId` only for an **existing**
organization-local Person.

After CSP onboarding, stay in the explicit provider workspace and use
`P/organizations/new`. On **Initial administrator**, retain enrollment and choose
**Create a Person record for this administrator**, not **Use an existing Person
record**. For Cedar, enter:

| Field | Value |
|---|---|
| Directory tenant ID | `00000000-0000-0000-0000-000000000001` |
| User object ID | `10000000-0000-0000-0000-000000000004` (`dev-orgadmin`) |
| Administrator name | `Morgan Reed` |
| Administrator email | `morgan.reed@example.test` |
| Person record ID | **Not supplied in create-new mode; assigned by the server** |

The inspected flow is:

1. [AdministratorInputs / administratorIntent][administrator-inputs] validates
   name/email in create-new mode and sends `initialAdministrator.newPerson`
   containing `displayName` and `email`, plus `directoryTenantId` and `objectId`.
   It does not send `personId` in that mode.
2. [The UI client][operations-api] posts to
   **`/api/csp/dashboard/tenants`** with its idempotency key.
   [The mapped creation handler][organization-create-endpoint] calls
   `CreateOrganizationAsync`, which persists the customer tenant and a
   provisioning operation with the administrator intent. The creation response
   alone is **not** completed Administrator enrollment.
3. [The provisioning page][enrollment-ui] performs one automatic continuation
   after confirmed create navigation when an initial administrator was requested.
   It uses **`PATCH /api/csp/organizations/{tenantId}/provisioning/{operationId}`**.
   Refresh/recovery reads saved state; it does not blindly rerun that automatic
   continuation. Use the page's incomplete-enrollment action if needed.
4. [The endpoint][workspace-endpoints] checks provider administration authority.
   [The provisioning implementation][workspace-operations] validates the new
   active customer tenant and creates the Person through
   [PersonService.StageLocalAsync][person-service]. That local path assigns a
   new GUID, leaves directory-link metadata unset, and does not invoke directory
   search. [Service registration][onboarding-di] supplies the Person and role
   services; the path is not dependent on a previously seeded Person.
5. The endpoint uses the persisted `BoundPersonId` for
   [GrantAsync, then EnrollAdministratorAsync][membership-service]. Initial
   enrollment is authorized by CSP administration in provider context, not by
   an already-existing customer Administrator. Person binding, membership, and
   Administrator assignment are separately durable stages, not one indivisible
   “immediate admin” write.
6. Record the actual Person ID from membership management or the local
   provisioning response's `boundPersonId`. The setup page may display
   **Created; available through membership management** instead of the GUID.
   Only after all stages complete should Morgan select `dev-orgadmin` and enter
   Cedar. Juniper needs its own newly created local Person, not Cedar's Person ID.

**Stop and flag a bootstrap gap if:** the running form offers only a mandatory
existing Person ID; the request sends `personId` instead of `newPerson` despite
create-new selection; the enrollment route is missing/denied in the correct
provider context; it reports **Authorized local Person creation is unavailable**;
or any Person/member/admin stage cannot be completed/recovered normally.
Record the build, exact request/error, operation ID, and persisted stages.
These would block the positive setup case; they are not permission to invent an
ID, use the all-zero system tenant as a customer, seed a record, or inject roles.
No such runtime outcome has been established by this guide. Merely seeing an
unaffiliated customer identity before completing enrollment remains **SETUP NEEDED**.

## Manual scenarios

Each case uses Arrange / Act / Assert. “Completion” is the precise assertion to
record, not an instruction to mark an entire feature complete. The result/notes
line is deliberately empty of execution claims.

### 01 — Fresh login and honest workspace discovery

**Status:** wired/unverified. **Arrange:** parent-verified reset dataset; browser
at `/login`, no post-reset identity selection by the parent, no CSP profile or
customer memberships. Prior browser cookies may remain.

**Act:** Open `/login`. Confirm all nine configured choices, explicitly select
**Dev CSP-Admin** (`dev-cspadmin`) to replace any previous identity, and open
`P/onboarding/csp`. Perform cases 02–04 first; do not begin by resuming
an assumed profile or selecting an assumed organization. After finalization,
but before cases 06–09, sign out; select `dev-orgadmin`, then `dev-ao`, then
`dev-soc-analyst`. Inspect available workspaces and try a provider deep link.
Return to `dev-cspadmin` for normal organization provisioning.

**Assert / edit boundary:** The configured CSP identity can enter provider
onboarding context; provider operations may remain onboarding-gated until Active.
Other identity labels do not create customer organizations, Person bindings,
memberships, or roles; provider administration is denied. An initial unaffiliated
role or absent customer workspace is **SETUP NEEDED**, not an access bypass.
A fresh customer workspace is not fabricated for a label. Re-login/refresh
preserves server-derived access rather than the previous actor’s cached menu.

**Negative/retry:** Do not repair missing organization access by changing
Settings → Role. Later enrollment, not a label change, must establish access.

**Completion:** all nine choices are accounted for and the tested unprovisioned
actors are denied privileged access, with no implicit persisted grants.

**Result: NOT RUN — Notes/evidence: ____________________**

### 02 — Save and resume the CSP draft

**Status:** wired/unverified. **Arrange:** `dev-cspadmin` selected normally after
reset; no persisted CSP profile initially (`CspProfiles = 0`). No preexisting
identity/contact/classification values are assumed.

**Act:** Open `P/onboarding/csp`. Enter the provider identity above, support email,
and Unclassified classification through Identity, SupportContact, and
Classification. Use each step’s save/continue controls. Reload between steps
and return to an earlier step to change a draft field and save it.

**Assert / edit boundary:** Saved values and draft progress return after reload.
Unsubmitted draft fields are editable. Required identity/support/classification
validation is not satisfied merely by visiting a step.

**Negative/retry:** Try a blank required name and malformed email, then correct
them. If a save has an uncertain response, reload persisted values before
repeating the write; do not assume the draft was lost.

**Completion:** all three required draft sections contain the intended persisted
data and invalid values cannot advance as valid setup. Profile is still draft.

**Result: NOT RUN — Notes/evidence: ____________________**

### 03 — Optional onboarding package intake is a receipt, not a review

**Status:** wired/unverified. **Arrange:** case 02; `dev-cspadmin`.

**Act:** At **Import an existing authorization package**, first confirm that
Continue works with no files selected; return before finalizing. On this fresh
run, create `Synthetic Azure Shared Services` and its exact v1 boundary using
the bounded scope above, name the package `Synthetic Azure inventory v1`, and
select `azure-example-package.json`. Choose **Upload package**.
Record the durable package/operation/association IDs, then **Continue**.
If recovering a prior attempt within this run, select its recorded IDs instead
of creating another offering/boundary.

**Assert / edit boundary:** A confirmed receipt allows wizard continuation while
analysis runs separately. Pending/unconfirmed upload does not count as received.
The exact offering/boundary association is retained. Original bytes are not
editable through wizard fields.

**Negative/retry:** Cancel file selection without an upload, and distinguish that
skip from a lost upload response. Recover an uncertain upload using the same
selected files/key rather than creating another package.

**Completion:** the optional skip and confirmed-receipt paths are distinguished;
the received package survives reload. No candidate is thereby reviewed,
approved, published, or externally authorized.

**Result: NOT RUN — Notes/evidence: ____________________**

### 04 — Finalize the CSP profile and verify the Active lock

**Status:** wired/unverified; post-finalize editing is not supported.
**Arrange:** case 02 complete; optional case 03 accounted for.

**Act:** Review the saved identity/contact/classification. Choose
**Submit & finalize onboarding**. Reload provider pages. In Settings, follow CSP
Administration back to `P/onboarding/csp?reentry=admin`.

**Assert / edit boundary:** Finalization persists `Active`. The profile service
rejects subsequent profile mutations. Reentry is not a supported unlock, even
if Settings copy suggests updating branding/profile.

**Negative/retry:** Before finalization, missing required sections must block
submission. Afterward, an attempted edit must not silently change Active data.
On an uncertain finalization response, reload state before retrying.

**Completion:** Active is persisted and protected. This completes provider
onboarding only. Do not mark post-finalize editing as working or treat uploaded
package analysis as complete.

**Result: NOT RUN — Notes/evidence: ____________________**

### 05 — Provider overview, organization summaries, and navigation

**Status:** wired/unverified. **Arrange:** `dev-cspadmin`; case 04. Repeat after 09.

**Act:** Open `P`, Organizations, and `P/systems`. Follow the actual provider
navigation links. After enrollment, open Cedar’s **Overview & systems**,
**Provider subscriptions**, and **Provider activity** tabs. Search/filter and
page through lists where available; reload.

**Assert / edit boundary:** Before setup, no Flankspeed/Alpha/Beta or fictional
customer records are assumed. After setup, saved Cedar/Juniper names and system
associations appear in the correct summaries. These are read projections, not
authorization-editing forms. “System authorization: Not provided” and unavailable
provider-change details must not be read as authorized/no work remaining.
Provider activity is a bounded recent list, not complete audit history.

**Negative/retry:** A failed/partial load must not be interpreted as an empty
organization or zero risk. Retry the affected read and compare saved records.

**Completion:** navigation resolves to the intended workspace, summaries match
actual setup, and unavailable fields are recorded honestly.

**Result: NOT RUN — Notes/evidence: ____________________**

### 06 — Create Cedar with a real initial Administrator enrollment

**Status:** wired/unverified. **Arrange:** `dev-cspadmin`; no Cedar organization.

**Act:** Organizations → Add organization (`P/organizations/new`). Enter
`Cedar Research Office`, fictional legal/POC information, and select **Create a
Person record for this administrator** for Morgan Reed. Enter Morgan's name,
email, and configured login directory/object IDs from the bootstrap table above,
not the fictional Azure IDs. Do **not** supply a Person record ID.
Review and submit. Retain the creation key and follow Setup status through the
separate enrollment continuation; resume incomplete enrollment if necessary.

**Assert / edit boundary:** One tenant is created. Enrollment tracks Organization,
Person, Membership, and Administrator separately; the Person belongs to Cedar,
the membership binds Morgan’s identity, and the Administrator assignment is
persisted. This does not send an invitation or verify a directory account.

**Negative/retry:** Blank required names/invalid identifiers do not create a valid
setup. For a lost response, reopen recovery/status using the same creation key,
not another Add organization submission. If create-new mode is absent or the
server demands a preexisting Person ID, record a bootstrap blocker using the
stop criteria above; do not seed or fabricate a Person to make this case pass.

**Completion:** durable setup reports complete with actual Person/member/admin
records, and `dev-orgadmin` can discover Cedar after re-login.

**Result: NOT RUN — Notes/evidence: ____________________**

### 07 — Deferred Juniper enrollment and interrupted-operation recovery

**Status:** wired/unverified. **Arrange:** `dev-cspadmin`; no Juniper organization.

**Act:** Create `Juniper Operations Office`, deferring the initial administrator.
Save its key/status URL. Reload; confirm the organization exists but admin setup
is incomplete. Open provisioning and use its existing pending operation; choose
**Start enrollment** only if no saved operation exists. Select **Create a Person
record for this administrator** and provide Morgan's name/email and the same
configured identity, without Cedar's Person ID or any invented Person ID.
Use the offered **Continue enrollment**, **Resume incomplete enrollment**, or
retry action only for an incomplete stage.

**Assert / edit boundary:** Completed stages survive navigation/reload and are
not duplicated. A bound identity/Person is not silently replaced during resume.
Juniper’s Person ID is different from Cedar’s. Organization creation alone is
not membership or Administrator completion.

**Negative/retry:** If a genuine partial failure occurs, capture its stage, fix
the permitted input, and retry only incomplete enrollment. Optional browser
offline interruption must not be reported as a server-stage failure unless
observed. Do not crash/reset services to manufacture one.

**Completion:** Juniper reaches persisted setup completion once; refresh and
recovery do not create another tenant/Person/grant. Record an unexercised
partial-failure branch separately.

**Result: NOT RUN — Notes/evidence: ____________________**

### 08 — Person, membership, revocation, and last-admin protection

**Status:** wired/unverified. **Arrange:** cases 06–07; provider operator, then Morgan.

**Act:** At `P/organizations/{cedarTenantId}/memberships`, create local contacts
for Riley, Avery, Casey, Jordan, Taylor, and Quinn. Grant each ordinary membership
using the correct local Person and configured identity pair. Re-submit one
identical grant. Open `C/settings/memberships` as Morgan. Revoke/regrant a
non-admin test membership, then attempt to revoke the last active Administrator.

**Assert / edit boundary:** Creating a contact creates no login grant.
Membership grants create no RMF role. Identical grants are idempotent; conflicting
active bindings are rejected. Revocation retains history and removes access.
The last active Administrator is protected (`LAST_ADMIN_PROTECTED`).

**Negative/retry:** A Juniper Person cannot be used for Cedar membership.
To test self-revocation, first provide another authorized administrator through
normal roles; do not strand the organization. Re-login after revocation and
confirm stale navigation does not preserve access.

**Completion:** membership lifecycle and last-admin safety persist independently
of roles. Restore memberships needed by later cases.

**Result: NOT RUN — Notes/evidence: ____________________**

### 09 — Assign actual roles and create only the systems needed for handoff

**Status:** wired/unverified. **Arrange:** Morgan enrolled in Cedar/Juniper;
Cedar contacts/memberships from 08.

**Act:** As Morgan, open `C/onboarding?stepNav=admin`; complete organization
context and the Roles step using local Persons. Assign the intended roles from
the table, especially Administrator, Issm, and Isso. Save and finish required
ordinary onboarding; skip optional Azure/import actions. Re-login as Riley,
Avery, and Quinn to inspect their actual effective roles. Use Systems →
**Register system** (`C/systems/new`) to register `Cedar Azure Records` with
synthetic required intake data; do likewise for Juniper as authorized. Use each
system’s Roles & Permissions page for required system assignments.

**Assert / edit boundary:** Role assignments persist separately from memberships
and login labels. Each system belongs to its selected organization. Registration
does not discover Azure resources or issue an ATO.

**Negative/retry:** A member without the required actual role cannot assign roles
or perform restricted system work. Do not use AI generation to fill intake.

**Completion:** minimum organization setup, effective roles, and two scoped
systems exist for handoff; no broader mission RMF completion is asserted.

**Result: NOT RUN — Notes/evidence: ____________________**

### 10 — Cross-organization and provider privilege boundaries

**Status:** wired/unverified. **Arrange:** Riley is a Cedar member only; Morgan
has separately provisioned memberships in both organizations.

**Act:** As Riley, open Cedar, then substitute Juniper’s tenant/system ID in an
ordinary workspace URL. Try `P/authorizations` and provider memberships. As Morgan,
switch between Cedar and Juniper and compare local Persons/systems. As
`dev-cspadmin`, try ordinary Cedar access without granting that identity an
ordinary membership. Switch actor and refresh an already-open privileged page.

**Assert / edit boundary:** Riley cannot read/change Juniper or administer the
provider. Morgan’s organization context changes deliberately. CSP claim alone
does not become an ordinary Cedar role or disable tenant filtering there.
Cached data/actions do not survive an unauthorized actor/context change.

**Negative/retry:** Expect denial/not-found/no accessible workspace as appropriate,
not another organization’s data. A hidden button is insufficient evidence:
inspect the UI-triggered server response where a request occurs.

**Completion:** attempted out-of-scope access is denied without persisted changes
or cross-organization data disclosure.

**Result: NOT RUN — Notes/evidence: ____________________**

### 11 — Enter an explicitly audited support workspace

**Status:** wired/unverified. **Arrange:** `dev-cspadmin`; Cedar exists.

**Act:** Open Cedar’s provider detail and choose **Audited support**. Cancel once.
Reopen; enter reason `Synthetic Cedar onboarding assistance`, optional reference
`LOCAL-CSP-011`, and the explicit acknowledgment. Choose **Start audited support**.
Record the session target/actor/expiry/correlation and inspect the support banner.

**Assert / edit boundary:** Cancel creates no support session. Entry persists a
targeted, reasoned, time-limited session and navigates to `SC`. This is not an
ordinary membership or AO role. Unsaved tab changes may be discarded as warned.

**Negative/retry:** A reason shorter than three characters, missing acknowledgment,
or overlength fields cannot be accepted as a valid request. Altering the support
URL to Juniper or changing the logged-in actor must not transfer the session.
Provider package mutation controls must not become available through support
context as an alternative to explicit provider context.

**Completion:** a valid session is visibly support-scoped and durable, with correct
actor/target and no permanent role grant.

**Result: NOT RUN — Notes/evidence: ____________________**

### 12 — Exit, expiration, and no support-session replay

**Status:** wired/unverified. **Arrange:** case 11; record the actual expiry.

**Act:** From the banner choose **Exit**; cancel its confirmation once, then
confirm. Reload the old support URL. Start a separate valid session and leave it
until its actual configured expiration; then refresh and try a support action.
Do not change clocks, DB timestamps, or configuration to shorten the wait.

**Assert / edit boundary:** Cancel preserves the active session; confirmed exit
ends it. Expired/exited sessions fail server revalidation and cannot be revived
by an old tab. Ordinary access still requires membership. Session history is
retained; it is not an editable grant.

**Negative/retry:** If exit returns an error, the UI must not pretend exit
succeeded. Restore connectivity and retry/refresh to determine persisted state.
Switching actor cannot use the prior actor’s session.

**Completion:** exit and expiry are each observed independently and old context
cannot perform writes. If expiry was not waited out, mark that branch NOT RUN,
not PASS based on the countdown.

**Result: NOT RUN — Notes/evidence: ____________________**

### 13 — Browse and reconcile the provider catalog

**Status:** wired/unverified. **Arrange:** provider operator; repeat after 14–19
and package review.

**Act:** Open `P/security-capabilities`. Change capability/component grouping,
search `Synthetic`, filter lifecycle/review state/component, sort in both
directions, and use pagination when there are enough rows. Open source details,
**View source package** when present, an offering link when present, and
subscriber/adoption information. Follow legacy `P/components`, `P/capabilities`,
and import links.

**Assert / edit boundary:** Filters change projections, not inventory records.
Manual and imported provenance stay distinct. Saved working revisions and
subscriber releases are not conflated. Legacy links resolve to the documented
canonical destinations; an absent source package is not fabricated.

**Negative/retry:** Search with no match gives an honest empty result. Read errors
or partial contributor resolution are not proof of zero subscribers/zero duties.
Refresh after writes and compare exact identifiers.

**Completion:** the catalog accurately presents available records, provenance,
filters, and current release state; unexercised multi-page behavior is noted.

**Result: NOT RUN — Notes/evidence: ____________________**

### 14 — Bootstrap manual components; edit and archive without inventing releases

**Status:** wired/unverified. **Arrange:** Active CSP profile; provider operator.

**Act:** Use **`P/csp/inherited-components`**, not `P/components`.
Choose **+ New Component** for the two baseline components and the scratch
component; enter description and an appropriate type. Open the scratch drawer,
edit/save description/type, reload, then archive it with confirmation. Inspect
the Archived filter. **Import ATO documents** should route to Authorizations.

**Assert / edit boundary:** Manual create stores a Manual-source component as
**Published immediately**. This is not a canonical capability release or ATO.
Editable fields are name/description/type; disabled owner/subtype/provider/category
fields do not imply persisted CSP metadata. Archive retains the row. No restore
workflow is claimed.

**Negative/retry:** Two-tab stale edits must not silently replace a newer row
version. The draft-only Publish action is conditional on an actual Draft record;
fresh manual create is not such a record. Offering-linked published inventory
must not be editable through this legacy bypass.

**Completion:** baseline components persist; scratch edits/archive are confirmed.
Do not archive baseline contributors needed below.

**Result: NOT RUN — Notes/evidence: ____________________**

### 15 — Legacy capability review, move, history, and archival

**Status:** wired/unverified. **Arrange:** case 14; two eligible unlinked components.

**Act:** In the identity component drawer choose **+ Add capability**; create
`Synthetic access assurance`, description, and IA-2 mapping, leaving review pending.
Open the review queue/drawer, resolve the mapping with an explicit human note,
then inspect history. Edit its description, move a disposable capability to the
other component using the Move dialog, and review it again. Archive only a
disposable capability. Separately exercise the create-time “mark mapped” option.

**Assert / edit boundary:** Default creation is NeedsReview with user mapping.
Human resolution records Mapped, reviewer/time/note/controls. Move preserves
fields/mappings but resets review to NeedsReview. Create-time mapped selection
must have corresponding review history, not merely a green label.
History remains read-only; filter/page it where data permits.

**Negative/retry:** Invalid target/stale row version must reject rather than
silently move or overwrite. Published offering-linked records are guarded from
legacy mutations.

**Completion:** each exercised transition and human review is durable; archival
does not erase history. None of these actions alone is canonical publication.

**Result: NOT RUN — Notes/evidence: ____________________**

### 16 — Remap safety and preservation of human mappings

**Status:** wired; actual remap execution is environment-conditional.
**Arrange:** provider operator, unlinked component with reviewed User mappings.

**Act:** Open the component’s **Advanced → Remap capabilities** action (or
**Remap parent component** from a capability). Inspect the acknowledgment,
then cancel. Record existing mapped controls/history.
Only if the stack owner confirms a configured **local/no-network** analyzer,
repeat with acknowledgment and inspect the resulting review queue/history.

**Assert / edit boundary:** Cancel changes nothing. A permitted execution should
preserve approved User mappings while allowing analyzer-managed mappings to be
reconsidered; new suggestions are not human approval. Remap must not mutate
offering-linked publication history through a legacy path.

**Negative/retry:** An analyzer failure must remain visible, not appear as
success/no changes. Do not configure credentials or invoke a live model/Azure
service just to finish this case.

**Completion:** cancel/acknowledgment safety can pass independently. Mark actual
mapping-preservation execution BLOCKED (environment) unless a safe analyzer is
confirmed and its persisted outcome observed.

**Result: NOT RUN — Notes/evidence: ____________________**

### 17 — Create and save an exact canonical working revision

**Status:** wired/unverified. **Arrange:** case 14; provider operator.

**Act:** At `P/security-capabilities`, choose **Add capability**. Select the
Monitor component, enter `Synthetic audit review` and its description, then
create. Record the capability ID. Review its AU-6 mapping through the legacy
review surface if still NeedsReview. Open
`P/security-capabilities/{capabilityId}`. Set classification `Unclassified`,
service category `Audit`, select/link the identity contributor, and enter
`AU-6: Shared`. Choose **Save working revision**.

**Assert / edit boundary:** Creation does not release or subscribe anything.
Linking a component stages a choice until save. Saved working revision/hash and
duties persist independently of any release; later edits clear approval/preview.

**Negative/retry:** Missing primary component/name/description blocks create.
An uncertain create response must be reconciled in the catalog, not duplicated.
Use two tabs to trigger a stale save; **Reconciliation required → Reload latest
revision** must require deliberate reconciliation, not silent last-write-wins.

**Completion:** one canonical capability has a saved, reviewed working baseline
with exact component IDs/duties and no release yet.

**Result: NOT RUN — Notes/evidence: ____________________**

### 18 — Approve and publish the unlinked manual capability

**Status:** wired/unverified. **Arrange:** case 17; keep this manual baseline
unlinked to an offering. Do not detach imported records to bypass authorization.

**Act:** Choose **Review publication** / **Review and publish**.
Select **Generate publication preview**. Review changes, contributors,
subscribers, and projected review work; record preview ID/hash/revision.
Check **Source evidence and coverage reviewed** and **Provider and customer
duties reviewed**. Choose **Approve exact preview**, then **Publish release**.
Reload and inspect the resulting release.

**Assert / edit boundary:** Approval alone creates no release. Publication creates
an immutable release bound to the exact saved/approved preview. Working fields
remain separately editable. Notification preview is projected work, not proof
that messages were sent.

**Negative/retry:** Unsaved changes, missing acknowledgments, stale/expired
preview, or changed revision must block publication. Regenerate/review/reapprove.
If the response is uncertain, recover/retry the same operation; it must not
produce duplicate releases.

**Completion:** one persisted release with exact revision is confirmed; no customer
narrative, duty acceptance, or ATO changes merely because it was published.

**Result: NOT RUN — Notes/evidence: ____________________**

### 19 — Existing customer setup, subscribers, and a second release

**Status:** wired/unverified; distinct from blocked new offering relationships.
**Arrange:** cases 09 and 18; actual Cedar system authoring permission.

**Act:** As the authorized Cedar actor, open
`C/systems/{systemId}/security-capabilities` and its setup dialog. Choose
**Use a provider capability**, select `Synthetic audit review`, Continue,
review components/duties, acknowledge, and **Apply setup**. If presented,
use **Prepare provider subscription**; use **Retry incomplete writes** only for
incomplete Record/Component links/Subscription outcomes. Finish with **Done** /
**View capability**. Review responsibility records at inheritance/subscriptions.
As provider, inspect Subscribers, then save a changed AU-6 duty revision and
repeat case 18 for release two.

**Assert / edit boundary:** Setup creates durable links/subscription, not accepted
responsibilities or ATO. Existing subscription/source version remains traceable.
Release two preserves release one and creates durable changed-duty review work,
not silent customer acceptance or rewritten approved narratives.

**Negative/retry:** Deny actors without system rights; retry preserves successful
writes. If no approved customer narrative exists, record that preservation
assertion as not exercised rather than inventing one.

**Completion:** setup, provider subscriber visibility, two releases, and customer
review work are verified separately. Do not claim new offering adoption works.

**Result: NOT RUN — Notes/evidence: ____________________**

### 20 — Extract and save provider narrative-reference drafts

**Status:** wired/unverified. **Arrange:** provider operator; case 17 for
capability-specific scope.

**Act:** Open `P/narrative-library` → **Upload narratives**. Enter the shared
reference title. Choose Provider scope and **Paste narratives**, using:
`AU-6: Synthetic reference only. The customer reviews audit output; the provider
maintains the illustrative collection service. This is not implementation evidence.`
Choose **Extract passages**. Edit a passage, assign AU-6 and Policy/Technical as
appropriate, remove a disposable passage, and **Save draft mappings**. Repeat
with ProviderCapability scope and the actual mapped capability if desired.

**Assert / edit boundary:** Extraction creates a draft with retained source
provenance/hash. Draft text/mappings/scope remain editable until publication.
No mission implementation narrative is approved by extraction.

**Negative/retry:** Blank/unmapped material must not become a published reference.
File intake supports the displayed formats and 5 MB limit; do not claim binary
format extraction was tested when only paste was used. Reload saved drafts.

**Completion:** reviewed draft passages/mappings and provenance persist; draft is
not yet published. Record extraction failures without invoking live AI as a repair.

**Result: NOT RUN — Notes/evidence: ____________________**

### 21 — Publish reference versions and check customer applicability

**Status:** wired/unverified. **Arrange:** case 20; mapped capability and Cedar
subscription from 19 for the capability-scoped branch.

**Act:** Save any scope/mapping edits. Check **I reviewed these reference claims
and mappings**, then **Publish references**. Reimport the same title and scope
with a small clearly synthetic text correction, review/save/publish again.
Inspect version provenance and the earlier text. As the Cedar actor, inspect
applicable references while working with the subscribed capability.

**Assert / edit boundary:** Published reference rows are immutable; corrected
content forms a successor in the reference version series, not an overwrite.
Capability-scoped publication requires a Mapped capability and controls within
its reviewed mapping. Customer applicability depends on the proper active
subscription and provider state; it does not grant private package access.

**Negative/retry:** Unsaved scope changes, missing acknowledgment, or an unrelated
control must not publish as reviewed applicable material. A Juniper actor without
the subscription must not gain capability-specific applicability.

**Completion:** two reference versions preserve provenance and scope; no approved
customer implementation text changes automatically.

**Result: NOT RUN — Notes/evidence: ____________________**

### 22 — Offering identity, environments, list, and navigation

**Status:** wired/unverified; no current offering-edit form.
**Arrange:** provider operator; reuse the offering created in 03.

**Act:** Open `P/authorizations`. Use **Create an offering** for
`Synthetic Azure Training Services`; enter a synthetic description and select
Azure Commercial/Azure Government only as appropriate. Search for both offerings,
open each, inspect recorded environments and sidebar destinations, and reload.
If 03 was skipped, create the main offering now.

**Assert / edit boundary:** Each creation persists an offering identity/environment
record. It does not allocate Azure resources, grant organization access, or issue
authorization. Backend update support is not a browser editing feature: no
rename/environment-edit form is present in the inspected UI.

**Negative/retry:** Missing required data should reject creation. After an uncertain
response, inspect the list before submitting another create. A similarly named
offering must not cause an existing package to be reassociated automatically.

**Completion:** both actual offerings can be recovered and distinguished by ID;
the absence of UI editing is documented, not marked passed.

**Result: NOT RUN — Notes/evidence: ____________________**

### 23 — Explicit boundary revisions and successor concurrency

**Status:** wired/unverified. **Arrange:** main offering; provider operator.

**Act:** Open `A/boundary`. Reuse or create v1 with the shared bounded scope,
included services, synthetic Azure scope, exclusions/rationale, and separate
provider/customer responsibilities. Supply actual immutable component snapshot
IDs only if available, otherwise leave optional values blank. Choose **Save
boundary revision**. Use **Prepare successor of v1**, make a small explicit
scope/rationale correction, and save v2.

**Assert / edit boundary:** New revisions have their own immutable snapshot/hash
and predecessor. V1 remains readable; package/decision references to v1 do not
silently move to v2. Empty included resources are not universal Azure coverage.
Editing is by successor, not replacement of a historical snapshot.

**Negative/retry:** Two tabs using the same predecessor/offering revision must not
both silently replace current context. Refresh/review the current predecessor
before resubmitting. Unsupported citations or invalid scopes must remain errors.

**Completion:** exact v1/v2 scope, hashes, exclusions, and predecessor are retained;
later packages explicitly choose a boundary version.

**Result: NOT RUN — Notes/evidence: ____________________**

### 24 — Package upload, durable receipt, private originals, and recovery

**Status:** wired/unverified. **Arrange:** provider operator; offering/boundary.

**Act:** Authorizations → **Import existing authorization package**. Select the
exact offering and boundary, name the authorization-led package, select
`azure-authorization-example.json`, and **Upload package**. Record receipt IDs.
Also test **Upload package** directly on an existing offering card and its
detail header: the form must retain that offering without another offering
picker or creation form. Select an exact boundary and upload; verify that the
receipt/review link belongs to the same offering and no duplicate offering or
automatic authorization-field update is created. A denied/missing offering must
not expose its upload form. This entry-point test does not assert extraction
completeness or bypass any review/publication blocker.
Open `K`, refresh during processing, inspect retained sources, and download an
original through the authenticated UI. Exercise the same workflow for
`synthetic-oscal.json` to cover structured OSCAL intake.

**Assert / edit boundary:** Receipt persists independently of analysis/review.
Original bytes/provenance are immutable and privately served; a download is not
an authority endorsement. Association is exact, not inferred from a name.

**Negative/retry:** Unsupported top-level extension, empty file, excessive count
(over 1,000) or total size (over 50 MiB) must not yield a false receipt.
For uncertain upload use **Retry same upload** with the same bytes/key; after
refresh reselect those files to recover. Definitive validation/auth/size errors
permit correction. Do not count unexercised limits as passed.

**Completion:** confirmed receipts and original-source retrieval survive refresh;
unknown-response recovery produces no duplicate operation.

**Result: NOT RUN — Notes/evidence: ____________________**

### 25 — Coverage, partial processing, resume, exclusions, and zero proposals

**Status:** wired/unverified; failure/retry branches require an observed condition.
**Arrange:** provider operator; cases 24 and fixture table.

**Act:** Upload `no-candidates.json` and inspect its coverage/result. For archive
coverage, upload the outside-repo ZIP described above. At package detail inspect
every original/archive child and its Pending/Processed/Unsupported/Unreadable/
Failed/Excluded disposition. If Failed/NeedsAttention occurs, choose **Retry
unfinished analysis**. On the unsupported scratch entry use **Exclude source
entry**, enter `Synthetic unsupported archive child; no implementation evidence`,
then **Exclude entry**.

**Assert / edit boundary:** Zero candidates can be a successful structured parse.
Every entry is accounted for. Retry preserves completed/excluded entries and
human reviews; it resumes incomplete work using retained checkpoints/bytes.
Exclusion has a rationale, may cascade to children, and invalidates affected
approval; it is not analysis.

**Negative/retry:** Excluding evidence already used by published records must be
rejected. Missing/invalid checkpoints must produce an explicit error, not a claim
that new reupload repaired that receipt.

**Completion:** coverage is honest after refresh; observed retries do not duplicate
reviews/candidates. Mark absent failure/checkpoint branches NOT RUN.

**Result: NOT RUN — Notes/evidence: ____________________**

### 26 — Candidate editing, human review, rejection, and stale revisions

**Status:** wired legacy inventory contract; typed claims are case 29.
**Arrange:** analyzed inventory package; provider operator.

**Act:** At `K`, open a Component and Capability proposal. Correct draft name/
description, classification/category, contributor IDs, control mappings/duties
using only retained source meaning; **Save changes**. Read citations and dependency
information, acknowledge review, then **Mark reviewed**. On a disposable proposal,
choose **Reject candidate** and enter a concrete rationale. Reopen after refresh.

**Assert / edit boundary:** Edits create a new candidate revision/NeedsReview and
invalidate old approval. Human review records a decision on that exact revision.
Rejection is retained with rationale; it neither deletes source bytes nor
publishes inventory. Citations are immutable provenance, not editable claims.
Published candidates are locked.

**Negative/retry:** Empty rejection rationale/missing review acknowledgment blocks
the action. Two-tab stale edits/reviews must be rejected; use **Reload current
package**, inspect changes, and review again. Changing fields after review must
not leave an old approved state valid.

**Completion:** edits, review, and rejection each persist with correct revision
semantics; rejected material is not treated as selected publication evidence.

**Result: NOT RUN — Notes/evidence: ____________________**

### 27 — Resolve duplicates and capability dependencies explicitly

**Status:** wired/unverified. **Arrange:** inventory proposals plus actual manual
Published components; provider operator.

**Act:** Use `synthetic-inventory.json` where another small dependency set is
needed. Inspect duplicate matches. Resolve one as **KeepSeparate** with an
honest rationale. For a Component reuse branch, choose **ReusePublished** and
identify exactly one eligible existing Published component in contributors.
For capabilities, set required classification/category, control duties, and
actual component contributors; review. Select an intact component/capability set
for **Preview selected revisions**.

**Assert / edit boundary:** Reuse links to the selected existing record rather
than overwriting it. Capability dependencies must be selected component
candidates or eligible Published components. Supporting ControlMapping,
Responsibility, and AuthorizationReference proposals are not standalone
publishable inventory.

**Negative/retry:** Unresolved duplicates, missing dependencies, multiple reuse
targets, archived/unknown contributors, or absent duties must not produce a clean
eligible selection. Change a reviewed dependency and confirm stale approval is
invalidated. Authorization blockers may remain independently.

**Completion:** duplicate/dependency decisions are persisted and publication
cannot silently guess resolution. No publication pass is claimed here.

**Result: NOT RUN — Notes/evidence: ____________________**

### 28 — Review a legacy AuthorizationReference without calling it an ATO

**Status:** wired/unverified. **Arrange:** upload
`synthetic-authorization-reference.json` normally; provider operator.

**Act:** Open its AuthorizationReference proposal. Check the retained source
reference, issuer `Synthetic test authority`, and exact issued/expiry timestamps.
Use the candidate review workflow and explicit acknowledgment. Refresh and
inspect its reviewed state. When a catalog record has a relevant source-package
link, follow **View source package** and inspect reviewed references.

**Assert / edit boundary:** Review persists source metadata, not verified external
authority. Proposed/rejected references must not appear as human-reviewed
references. This supporting record cannot be selected as a publishable
Component/Capability. Source citations remain immutable.

**Negative/retry:** Edit metadata after review and re-review the new revision;
stale revisions must not overwrite current review. Do not turn its issuer/type
into Microsoft/ATO wording. If no published catalog link exists because case 32
is blocked, leave that display branch blocked by dependency.

**Completion:** legacy reference review is durable and clearly distinguished
from a provider decision, publication, and real authorization.

**Result: NOT RUN — Notes/evidence: ____________________**

### 29 — Typed decision/boundary/finding/POA&M claim review

**Implementation status: BLOCKED.** **Arrange:** authorization-led fixture receipt;
provider operator.

**Act now:** Open its candidate list and inspect typed proposals if emitted.
Open their review surface; capture unavailable payload/error state without
substituting legacy metadata or inventing fields.

**Intended acceptance when wired:** Review the exact source-stated decision,
included/excluded boundary claims, both finding severities/statuses, and separate
POA&M milestones. Save a reviewed/rejected decision with rationale on each exact
revision, refresh, and confirm concurrent edits invalidate stale reviews.
Importing a finding or plan must never create closure or evidence acceptance.

**Persistence / edit boundary:** The intended claim payload, snapshot, citations,
relationships, and review decisions must survive processing/API round trips.
Human corrections must preserve original provenance.

**Why blocked:** Analyzer typed extraction exists, but processor/public
`PackageCandidateResponse` do not carry the required typed `claim` payload/hash.
The UI's `.../candidates/{id}/claim-reviews` call is not mapped.

**Completion:** only end-to-end persisted typed review can pass. Seeing a proposal
count or an unavailable panel cannot.

**Result: NOT RUN — Notes/evidence: ____________________**

### 30 — Profile-2 enrichment and semantic-family coverage

**Implementation status: BLOCKED.** **Arrange:** retained package; provider operator.

**Act now:** Inspect package detail and its actual status/entry responses.
Record missing analysis-profile/family-coverage information; do not infer “all
families processed” from legacy coverage counters. The enrichment panel may not
render because its mounting condition depends on the missing profile field.

**Intended acceptance when wired:** Request enrichment of retained bytes, follow
the durable analysis operation, refresh/resume it, and inspect per-entry family
dispositions. Preserve previous candidate reviews/publications and original
sources. Repeated requests must recover the same intended operation rather than
duplicate claim families.

**Persistence / edit boundary:** Analysis version/family coverage is recorded
processing evidence, not a user-editable completion checkbox. Unsupported or
unreadable families remain explicit.

**Why blocked:** Public status omits `analysisProfileVersion`; entry responses
omit `familyCoverage`. The enrichment and analysis-operation endpoints called
by the UI are not mapped.

**Completion:** persisted enrichment, recovery, and all requested family
dispositions must be observable through the real API/UI. Analyzer support alone
cannot pass this case.

**Result: NOT RUN — Notes/evidence: ____________________**

### 31 — Exact authorization-impact preview and honest negative disposition

**Status:** negative path wired/unverified. **Arrange:** exact boundary IDs/hash
from 23, retained package IDs, and truthful decision metadata from 34.

**Act:** Open `A/impact`. Select the exact boundary/auth/package context.
For changed object use **Boundary** and its actual revision/hash, avoiding the
unexposed candidate payload hash. Choose **Generate impact preview**. Inspect
context/preview hashes, expiration, blockers, and affected targets/pages. Choose
**RequestChanges** or **Reject**, give a reason such as `Synthetic source is not
an effective provider authorization`, then **Save impact disposition**.

**Assert / edit boundary:** The disposition binds exact context and persists
without publishing anything. Missing/ineligible provider authority prevents
**AcceptForPublication**. A stored pending review is not a reusable live approval
token; regenerate the exact preview as required.

**Negative/retry:** Change a boundary/source/decision after preview; stale context
must be rejected. Wrong object hash/version must not be accepted. Correct a stale
form only using current server data and repeat human review.

**Completion:** honest blockers and a persisted non-accepting disposition are
verified. This negative pass does not complete case 32.

**Result: NOT RUN — Notes/evidence: ____________________**

### 32 — Exact offering-linked package approval and publication

**Implementation status: BLOCKED for the default synthetic acceptance flow.**
**Arrange:** reviewed inventory/dependencies, exact offering association.

**Act now:** Select a reviewed exact set at `K`, inspect impact requirements, and
choose **Preview selected revisions**. Capture the honest guard rejection or
blockers. Do not relabel the fixture’s decision as “ATO,” fabricate a candidate
hash, detach the package, or bypass the guard.

**Intended acceptance when resolved:** Review the exact 1–100 selected candidate
revisions, citations, coverage exceptions, duplicates, dependencies, and required
impact context. **Approve exact preview** must persist approval without inventory
publication. **Publish approved set** must then create only the approved
Components/Capabilities and their canonical releases. Refresh recovers the
outcome; retrying the same intent creates no duplicate. A subset yields
PartiallyPublished when inventory remains; later reviewed sets can finish it.

**Persistence / edit boundary:** Published candidates/releases are immutable;
unpublished candidates remain reviewable. Previous releases/approved customer
narratives survive. Changed selection/context requires new preview/approval;
the same idempotency key cannot authorize a different set.

**Why blocked:** Offering publication requires accepted current impact reviews
and an eligible effective provider decision. The fixture explicitly supplies
neither valid authority nor a positive decision. The impact form also needs a
candidate payload hash the candidate DTO does not expose. This conflicts with
the reviewed-inventory-without-current-authorization intent recorded in
[package ingestion guidance][ingestion-guide]; the running guard must not be
misrepresented.

**Completion:** a real persisted exact-set publication and preservation/retry
assertions, after the contract/product gap is resolved. Honest denial alone is
only a negative-path result.

**Result: NOT RUN — Notes/evidence: ____________________**

### 33 — Package series, successors, association, and historical preservation

**Status:** wired/unverified; legacy association branch conditional.
**Arrange:** provider operator; an existing retained package/version.

**Act:** From **Packages and claims**, use the offered successor intake and select
the exact latest predecessor. Upload a separately identified synthetic revision
through the normal form; preserve fictional/not-authoritative semantics. If a
local edited source is needed, keep it outside the repository and label its
change explicitly. Record series/version/boundary association. Reopen the prior
receipt and original download. Follow a legacy package URL through its redirect.

**Assert / edit boundary:** Successor stays in the proper series and preserves
earlier bytes, candidates, receipts, and published records if any exist.
Existing association cannot be silently changed to another offering.

**Negative/retry:** Stale predecessor/wrong series must reject. A genuinely
unassociated retained legacy package may be explicitly associated through
`P/authorizations/import?packageId={id}` without reupload, invalidating approvals.
Do not expect one on a fresh run or seed one. Current normal intake already
associates packages.

**Completion:** version lineage and old receipts survive refresh. Mark
preservation of published package releases dependent on blocked case 32, not
passed using unpublished data.

**Result: NOT RUN — Notes/evidence: ____________________**

### 34 — Record truthful, source-backed external decision metadata

**Status:** wired/unverified; typed claim promotion is not required for manual entry.
**Arrange:** retained authorization fixture, exact boundary, provider operator.

**Act:** Open `A` → **Overview and decisions** and create a provider decision
draft. Enter `SYN-AZ-DECISION-001`, the fictional authority and decision exactly
as stated, bounded scope, conditions, and source-stated dates/expiry basis.
Leave unstated effective date unknown. Add actual retained package/artifact
citations with the exact archive path/locator/quote; inspect local source
responses for IDs if necessary. Leave optional typed candidate references blank
while 29 is blocked. **Save draft**. Use **Review metadata**, acknowledge with
an honest rationale, then **Record external metadata**.

**Assert / edit boundary:** Draft is Unconfirmed; record creates human-reviewed
metadata with provenance. Neither is a verified ATO. CurrentAsRecorded, if shown,
is not an independent authority verdict; semantic eligibility is separate.

**Negative/retry:** Missing authority/scope/citations, invented quote, or a foreign
package reference must not record. If exact retained citation data cannot be
obtained normally, stop the positive branch rather than fabricate it.

**Completion:** source-faithful metadata and review persist, with the fixture’s
ineffective/non-ATO meaning intact.

**Result: NOT RUN — Notes/evidence: ____________________**

### 35 — Decision successors, history, standing, and lifecycle guardrails

**Status:** wired/unverified; positive lifecycle events require suitable source.
**Arrange:** case 34; provider operator.

**Act:** Select the recorded decision, **Revise draft**, make a source-faithful
scope clarification, and **Save successor draft**. Review/record as appropriate;
open **View history** and compare prior snapshot/citations. Use two tabs to test
stale revision handling: **Refresh current records**, then **Use refreshed
revision with retained inputs**, re-review and reacknowledge.
Inspect **Withdraw or supersede** and its validation.

**Assert / edit boundary:** Successors preserve earlier records. Unconfirmed
standing is Undetermined; source-stated future/expired dates must be treated
according to their dates, and missing expiry basis is not “never expires.”
Do not change source dates merely to manufacture each standing.

**Negative/retry:** **Save lifecycle event** requires a recorded current decision,
source-stated effective date, rationale, and citations; supersession also needs
a different eligible replacement. The supplied fixture has no withdrawal source
or eligible positive replacement. Test rejection, but leave successful
withdrawal/supersession unexercised unless legitimate synthetic source evidence
is separately supplied.

**Completion:** successor/history/concurrency and observed lifecycle denials
persist honestly; unsupported lifecycle events are not invented.

**Result: NOT RUN — Notes/evidence: ____________________**

### 36 — Keep inherited Microsoft references separate from provider authority

**Status:** metadata surface wired; valid-source positive branch conditional.
**Arrange:** provider operator; `A/inherited-coverage`.

**Act:** Inspect the inherited-reference decision panel and its distinct record
kind. Open the draft form and confirm explicit boundary/reference/source fields.
Cancel rather than labeling the fictional provider fixture as Microsoft-issued.
Exercise missing-source/acknowledgment validation. If a separately retained,
clearly synthetic Microsoft-reference test source is available, enter only its
stated metadata/citations, save, review, record, and inspect history using 34–35.

**Assert / edit boundary:** `InheritedMicrosoftReference` and `ProviderDecision`
are different records. An inherited reference alone cannot substitute for an
eligible provider decision, expand scope, or authorize a customer workload.
Recorded versions are preserved; correction is by successor.

**Negative/retry:** An unrelated retained provider reference must not be cited as
proof of Microsoft authority. Imported wording, dates, and identity cannot be
silently upgraded to verified external authorization.

**Completion:** separation and negative validation are observed. Default fixtures
do not establish a valid Microsoft-reference positive test; record that source
prerequisite explicitly, without fetching real credentials or authorization data.

**Result: NOT RUN — Notes/evidence: ____________________**

### 37 — Hosting-scope revisions

**Implementation status: BLOCKED.** **Arrange:** provider operator; offering and
boundary; only fictional scope values.

**Act now:** Open `A/inherited-coverage`, inspect the hosting form, and record its
unavailable/error behavior. Do not treat a visible form as saved hosting.

**Intended acceptance when wired:** Enter Azure Government, exact included scope,
exclusions/rationale, explicit boundary relationship, and retained citations.
Choose **Save hosting scope revision**; reload. **Prepare successor** must create
a new immutable revision, preserving prior scopes/hash and rejecting stale
predecessors.

**Persistence / edit boundary:** Hosting is a technical assertion separate from
the authorized boundary. Excluded customer scopes remain excluded. No discovered
Azure resources, permissions, or customer AO decision are implied.

**Negative/retry:** Missing scope/acknowledgment, invalid hierarchy, foreign
boundary, stale revision, or broader-than-supported claim must fail without a
persisted success. Recovery must not duplicate revisions.

**Why blocked:** `ProviderHostingService` methods throw `NotImplementedException`;
hosting routes and DI registration are absent.

**Completion:** actual saved and retrievable versioned scope plus denials is
required. A screenshot of the form/error cannot pass.

**Result: NOT RUN — Notes/evidence: ____________________**

### 38 — Assign exact hosting revisions to organizations

**Implementation status: BLOCKED.** **Arrange:** case 37 would need to pass;
Cedar/Juniper exist.

**Act now:** Inspect **Assign using hosting revision** / **Save hosting assignment**
on `A/inherited-coverage`; record unavailability.

**Intended acceptance when wired:** Assign the exact synthetic hosting revision
to Cedar with an explicit bounded customer allocation and acknowledgment.
Refresh provider and customer views; then create a changed assignment as an
auditable successor, not an in-place rewrite.

**Persistence / edit boundary:** Assignment binds organization, environment,
scope, and revision. It grants no ordinary membership or mission authorization.
Initial authorization relationship remains Undetermined until the separate
authorized review.

**Negative/retry:** Reject Juniper/foreign IDs substituted into a Cedar operation,
excluded resource scope, missing acknowledgments, stale hosting revision, and
replayed intent with changed payload. Preserve the earlier assignment and
recover same-intent retries without duplicate allocations.

**Why blocked:** the same hosting service/routes/DI gap as 37 covers assignment
operations.

**Completion:** durable assignment/version/scoping and retry/denial assertions;
never mark complete merely because an organization appears in a picker.

**Result: NOT RUN — Notes/evidence: ____________________**

### 39 — Provider findings, POA&M, retained evidence, and human closure

**Implementation status: BLOCKED for manual UI acceptance.**
**Arrange:** provider operator; offering; synthetic findings are not closed.

**Act now:** Open `A/findings` (**Findings and evidence**). Record that the
inspected route renders a generic warning instead of a findings/evidence panel.
Do not use invented UI buttons or direct API writes as a substitute.

**Intended acceptance when UI exists:** Create/list a provider finding and
separate POA&M; edit allowed Open/InProgress/ReadyForReview remediation status.
Submit/download retained synthetic evidence. An authorized human must separately
choose KeepOpen or AcceptClosure with rationale and exact finding revision.
Accepted closure must cite retained evidence for that same offering/finding.

**Persistence / edit boundary:** POA&M progress is not closure. Evidence and review
provenance remain retained. Imported finding/POA&M proposals do not create
accepted operational findings or close them.

**Negative/retry:** Reject closure without evidence/rationale, cross-finding
evidence, stale reviews, and unauthorized download. Failed review must leave
the finding open.

**Why blocked:** `ProviderFindingService` and routes/DI exist, including closure
guards; the browser page is missing. This is **not** an entirely stubbed backend.

**Completion:** the complete UI-to-persistence evidence/review lifecycle, not a
placeholder or imported Open claim.

**Result: NOT RUN — Notes/evidence: ____________________**

### 40 — Customer relationship to an exact provider offering

**Implementation status: BLOCKED.** **Arrange:** Cedar authorized system actor;
offering/assignment would be required.

**Act now:** Follow the system sidebar’s provider-relationships link
(`C/systems/{systemId}/provider-relationships`). Record the missing usable page.

**Intended acceptance when wired:** Associate Cedar’s system with the exact
offering, hosting assignment/revision, environment, and in-scope allocation.
Refresh as provider and customer; preserve explicit Undetermined or
separate-mission-boundary status until an authorized review.

**Persistence / edit boundary:** Association is a versioned relationship, not a
mission ATO or provider access grant. Changing scope/assignment requires a fresh
review context; historical relationships remain traceable.

**Negative/retry:** Reject a Juniper assignment/system substitution, mismatched
environment, excluded scope, and actors lacking system rights. Provider support
must not impersonate customer authorization.

**Why blocked:** sidebar/client definitions exist, but no mounted page or mapped
mission relationship endpoints; `ProviderMissionService` methods are stubs.

**Completion:** persisted exact relationship and cross-scope denials. Existing
legacy capability setup in 19 does not pass this new offering relationship case.

**Result: NOT RUN — Notes/evidence: ____________________**

### 41 — Applicable provider capabilities and offering-linked adoption

**Implementation status: BLOCKED.** **Arrange:** case 40, published eligible
offering capability, actual customer permissions would be required.

**Act now:** Do not invent an adoption panel; record the same missing route and
service dependency.

**Intended acceptance when wired:** From the system’s provider relationship,
list applicable capabilities for its exact offering/environment/scope. Select
a particular published version, review duties/source evidence, and adopt it.
Refresh both customer adoption and provider subscriber views. Repeat the same
intent safely; later provider releases should require deliberate customer review.

**Persistence / edit boundary:** Adoption stores exact release/source revision
and responsibility links. It does not copy over approved customer narratives,
accept all shared duties, or follow unpublished working changes automatically.
Earlier release/adoption history remains available.

**Negative/retry:** Deny an unrelated offering/organization, unpublished or
inapplicable capability, stale version, excluded hosting scope, and unauthorized
actor; no partial duplicate adoption.

**Why blocked:** applicable-capability/adoption client paths are present but
mission service implementation and endpoint/UI wiring are absent.

**Completion:** real offering-scoped applicability/adoption and preservation.
Case 19 covers a different existing path and cannot substitute.

**Result: NOT RUN — Notes/evidence: ____________________**

### 42 — Mission AO-only covered-workload review

**Implementation status: BLOCKED.** **Arrange:** a valid relationship, exact
preview context, and Quinn’s actual persisted mission AO authority would be needed.

**Act now:** Record missing relationship/review UI; do not simulate an AO decision
by choosing the `dev-ao` login label.

**Intended acceptance when wired:** As Quinn, preview exact provider decision,
boundary, hosting revision, assignment, and system scope; review source evidence
and explicitly decide whether the workload is covered or has a separate mission
boundary. Save with rationale and reload. Changes to any bound context must
invalidate a stale decision/preview.

**Persistence / edit boundary:** Covered-workload authority belongs to the
properly assigned mission AO. Provider CSP.Admin, org Administrator, system owner,
ISSO, assessor, and support actor cannot supply it merely by their other roles.
Undetermined is not approval. Historical decisions remain auditable.

**Negative/retry:** Test each non-AO actor, cross-system/organization context,
missing rationale, expired preview, and changed source snapshot; no silent
upgrade to covered status.

**Why blocked:** mission authorization/preview/review methods throw
`NotImplementedException`; no usable mapped UI/API flow.

**Completion:** persisted authorized human decision and all applicable denied
roles; never an inferred authorization.

**Result: NOT RUN — Notes/evidence: ____________________**

### 43 — Settings are preferences/context, not role administration

**Status:** wired/unverified. **Arrange:** provider operator, Morgan, and one
ordinary member in their respective explicit workspaces.

**Act:** Open Settings. Inspect **Profile & Identity** and server-derived workspace/
effective roles. Change a harmless display/theme preference, save/reload, then
restore it. Inspect dashboard/export/chat/integration preferences without running
live integrations. As Morgan open `C/settings/org`, change a synthetic
organization-context field and save. Follow the membership administration link
and compare it with actual role assignment in case 09.

**Assert / edit boundary:** Explicit-workspace identity/roles are read-only server
facts. Any legacy unscoped Role dropdown is a preference, not a grant.
Organization settings do not edit provider identity or membership roles.
Reset settings resets preferences, not organizations, packages, memberships, or
Docker data. Integration toggles do not prove a live connection.

**Negative/retry:** A non-admin cannot obtain admin/AO/CSP permission through
settings. CSP Administration reentry still encounters the Active profile lock.

**Completion:** preference/context changes persist only in their intended scope,
with no privilege or authorization changes.

**Result: NOT RUN — Notes/evidence: ____________________**

### 44 — Inspect audit records without promising a universal audit grid

**Status:** wired/unverified. **Arrange:** provider operator after several actual
writes; retain operation IDs/times.

**Act:** Open `P/audit-log` through **Audit Log**. Use available action/tenant
filters, **Search**, **Refresh**, and pagination. Compare returned rows with
operations that actually write to this audit store. Open provider organization
activity and package/decision history separately. Repeat privileged audit access
as an ordinary non-CSP actor.

**Assert / edit boundary:** Audit is read-only; actor may be a raw object ID.
IP/surface may be unavailable in the current projection. Filtering must not mix
unrelated tenant IDs. Package audit, provider authorization audit, login audit,
and onboarding structured logs are different stores; this grid does not promise
to show every operation from all of them.

**Negative/retry:** Ordinary users must not gain the CSP-only audit endpoint.
An empty filtered result or failed read must not be called proof that no action
occurred. Use retry and inspect the appropriate record-specific history.

**Completion:** authorized rows/filters and denied access are observed; missing
cross-store aggregation remains a documented limitation.

**Result: NOT RUN — Notes/evidence: ____________________**

### 45 — Linked Controls and Knowledge Base are reference surfaces

**Status:** wired/shared; Knowledge Base mutation requires actual AdminOnly access.
**Arrange:** provider operator; no live catalog imports.

**Act:** Use **Controls** in `P`; search AU-6, inspect details, filters, and
pagination where populated. Use **Knowledge Base**; inspect list/details.
If the actual identity is authorized for overlay administration, **Add Document**
with title `SYNTHETIC acceptance note`, AU-6, a clearly fictional body, and one
displayed document type. Edit it, soft **Delete**, select **Show inactive**,
then **Restore**. Do not alter existing reference documents.

**Assert / edit boundary:** Browsing a global control does not apply it, establish
inheritance, or prove implementation. A CSP-inherited capability-count column is
not implemented simply because Controls is in provider navigation. Overlay
mutations persist as reference data, not official policy or package evidence;
soft deletion retains the record.

**Negative/retry:** A CSP.Admin claim is not assumed to satisfy the separate
AdminOnly overlay policy. If denied, record correct denial and leave positive
CRUD blocked by access; do not broaden permissions. Non-admin mutations must
not succeed. Do not trigger catalog imports/network retrieval.

**Completion:** reference browsing and actual authorized/denied overlay behavior
are observed, with no claim of compliance or live integration.

**Result: NOT RUN — Notes/evidence: ____________________**

## Closeout: what may safely be marked complete

At authoring time, **no manual scenario is passed and no feature is certified
complete**. Source inspection supports attempting the wired scenarios; it does
not supply runtime evidence.

After a human run:

- Mark a scenario PASS only for its observed assertions, with actual IDs/revisions
  and reload evidence. Record conditional or unexercised branches separately.
- Mark organization setup complete only when enrollment stages and actual
  membership/Administrator access agree. Mark CSP onboarding complete only for
  persisted Active, acknowledging its current lock.
- Report package receipt, processing coverage, human review, exact approval, and
  actual publication as separate milestones.
- Record immutable release/reference versions as published only after retrieving
  them again. A working revision or preview is not the published version.
- A correct denial can pass a negative test. It cannot convert a blocked positive
  flow into complete functionality.
- Do not describe the fixture as a valid provider/Microsoft/mission ATO, regardless
  of a metadata standing label. Do not use this guide as compliance attestation.

### Known blockers and limitations to carry into the test report

| Item | Evidence-based disposition |
|---|---|
| Typed claims (29) | Extraction exists; typed candidate persistence/DTO and claim-review endpoint are missing. |
| Enrichment (30) | Missing public profile/family coverage plus unmapped enrichment/operation endpoints; panel may be absent. |
| Offering-linked synthetic publication (32) | Current guard demands accepted impact/current eligible provider authority; supplied source is explicitly ineffective. Candidate payload hash is also absent from the response needed by impact UI. Do not manufacture authority/hash. |
| Hosting scope and assignment (37–38) | Visible forms, but service methods are NotImplemented and routes/DI absent. |
| Finding/POA&M/evidence UI (39) | Backend logic and endpoints exist; offering route renders placeholder instead of lifecycle UI. |
| Mission relationships/adoption/AO review (40–42) | Sidebar/client paths only; no mounted page/mapped endpoints and service methods are NotImplemented. |
| CSP post-finalize profile edit | Backend rejects Active profile edits despite reentry/update-oriented copy. |
| Offering edit | Backend update capability does not provide a current browser edit form. |
| Component bootstrap | Canonical Add capability needs an existing component; legacy CRUD permalink is needed because `/components` redirects to grouped catalog. |
| Source prerequisites | Default fixtures contain no valid external authorization, withdrawal evidence, or official Microsoft reference. Positive authority/lifecycle cases cannot be fabricated. |
| Remap | Execution may use configured AI; default no-network run covers cancellation/gating only unless a safe local analyzer is confirmed. |
| Shared admin/audit | CSP role is not assumed equivalent to AdminOnly overlay permission; audit stores are not one unified grid. |

### Run summary to fill in

- Running build / date / tester: ____________________
- Reset confirmed by stack owner: ____________________
- PASS: ___ / 45; FAIL: ___; BLOCKED: ___; NOT RUN: ___
- Source-known implementation-blocked cases still open: ____________________
- Conditional branches not exercised: ____________________
- Persisted milestones achieved (setup / analysis / review / approval /
  publication / customer responsibility review): ____________________
- Unexpected access or persistence failures: ____________________
- Existing published versions compared and preserved: ____________________

## Source links

These links identify the implementation inspected, not runtime proof. In
particular, [Program][program] is the composition/mapping check for the distinction
between a service class that exists and an endpoint that is actually available.

[agent-rules]: ../../AGENTS.md
[constitution]: ../../.specify/memory/constitution.md
[routes]: ../../src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx
[program]: ../../src/Ato.Copilot.Mcp/Program.cs
[workspace-service]: ../../src/Ato.Copilot.Mcp/Services/Tenancy/WorkspaceService.cs
[login-config]: ../../src/Ato.Copilot.Mcp/appsettings.Development.json
[csp-wizard]: ../../src/Ato.Copilot.Dashboard/src/features/csp-onboarding/CspWizard.tsx
[profile-service]: ../../src/Ato.Copilot.Core/Services/Tenancy/CspProfileService.cs
[dashboard]: ../../src/Ato.Copilot.Dashboard/src/features/csp-dashboard/CspDashboardPage.tsx
[organization-pages]: ../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/OrganizationPages.tsx
[organization-create]: ../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/AddOrganizationPage.tsx
[administrator-inputs]: ../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/OrganizationSetupPresentation.tsx
[operations-api]: ../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/api.ts
[organization-create-endpoint]: ../../src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs
[enrollment-ui]: ../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/OrganizationProvisioningPage.tsx
[workspace-endpoints]: ../../src/Ato.Copilot.Mcp/Endpoints/Workspaces/WorkspaceOperationsEndpoints.cs
[membership-ui]: ../../src/Ato.Copilot.Dashboard/src/features/workspaces/MembershipAdministrationPage.tsx
[membership-service]: ../../src/Ato.Copilot.Mcp/Services/Tenancy/OrganizationMembershipService.cs
[person-service]: ../../src/Ato.Copilot.Agents/Compliance/Services/Onboarding/PersonService.cs
[onboarding-di]: ../../src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs
[role-setup]: ../../src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.tsx
[support-ui]: ../../src/Ato.Copilot.Dashboard/src/features/workspaces/SupportWorkspaceButton.tsx
[support-banner]: ../../src/Ato.Copilot.Dashboard/src/features/auth/ImpersonationBanner.tsx
[support-service]: ../../src/Ato.Copilot.Mcp/Services/Tenancy/TenantImpersonationService.cs
[legacy-components]: ../../src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspInheritedComponentsPage.tsx
[legacy-service]: ../../src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs
[workspace-ui]: ../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/WorkspaceOperationsPage.tsx
[workspace-operations]: ../../src/Ato.Copilot.Core/Services/Workspaces/WorkspaceOperationsService.cs
[reference-ui]: ../../src/Ato.Copilot.Dashboard/src/components/narratives/ReferenceLibraryManager.tsx
[reference-service]: ../../src/Ato.Copilot.Agents/Compliance/Services/ProviderNarrativeLibraryService.cs
[authorization-ui]: ../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/AuthorizationsPage.tsx
[authorization-service]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderAuthorizationService.cs
[package-ui]: ../../src/Ato.Copilot.Dashboard/src/features/package-imports/PackageImportsPage.tsx
[package-processor]: ../../src/Ato.Copilot.Core/Services/PackageImports/CspPackageProcessor.cs
[package-dtos]: ../../src/Ato.Copilot.Core/Interfaces/PackageImports/ICspPackageService.cs
[package-endpoints]: ../../src/Ato.Copilot.Mcp/Endpoints/Csp/CspPackageImportEndpoints.cs
[claim-ui]: ../../src/Ato.Copilot.Dashboard/src/features/package-imports/ClaimReview.tsx
[enrichment-ui]: ../../src/Ato.Copilot.Dashboard/src/features/package-imports/PackageEnrichment.tsx
[impact-ui]: ../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/ImpactPanel.tsx
[impact-service]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderImpactService.cs
[publication-guard]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderPublicationGuard.cs
[decision-ui]: ../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/DecisionPanel.tsx
[decision-service]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderAuthorizationService.Decisions.cs
[decision-eligibility]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderDecisionEligibility.cs
[hosting-ui]: ../../src/Ato.Copilot.Dashboard/src/features/provider-authorizations/HostingPanel.tsx
[hosting-service]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderHostingService.cs
[finding-service]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderFindingService.cs
[finding-endpoints]: ../../src/Ato.Copilot.Mcp/Endpoints/Csp/ProviderFindingEndpoints.cs
[relationship-client]: ../../src/Ato.Copilot.Dashboard/src/features/provider-relationships/api.ts
[mission-service]: ../../src/Ato.Copilot.Core/Services/ProviderAuthorizations/ProviderMissionService.cs
[settings-ui]: ../../src/Ato.Copilot.Dashboard/src/components/settings/SettingsPanel.tsx
[audit-ui]: ../../src/Ato.Copilot.Dashboard/src/pages/AuditLogPage.tsx
[controls-ui]: ../../src/Ato.Copilot.Dashboard/src/pages/ControlsRoute.tsx
[knowledge-ui]: ../../src/Ato.Copilot.Dashboard/src/pages/KnowledgeBaseManagementPage.tsx
[workspace-spec]: ../../specs/078-role-aware-workspaces/spec.md
[authorization-contract]: ../../specs/078-role-aware-workspaces/contracts/provider-authorizations.md
[package-contract]: ../../specs/078-role-aware-workspaces/contracts/package-imports.md
[tenant-spec]: ../../specs/048-tenant-isolation/spec.md
[lifecycle-spec]: ../../specs/050-csp-capability-lifecycle/spec.md
[narrative-spec]: ../../specs/074-policy-technical-narrative/spec.md
[ingestion-guide]: csp-package-ingestion.md
[fixtures]: ../examples/package-imports/
[azure-inventory]: ../examples/package-imports/azure-example-package.json
[azure-authorization]: ../examples/package-imports/azure-authorization-example.json
[small-inventory]: ../examples/package-imports/synthetic-inventory.json
[legacy-reference]: ../examples/package-imports/synthetic-authorization-reference.json
[oscal-fixture]: ../examples/package-imports/synthetic-oscal.json
[empty-fixture]: ../examples/package-imports/no-candidates.json
[unsupported-fixture]: ../examples/package-imports/unsupported.synthetic
