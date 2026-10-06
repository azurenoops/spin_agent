# Mission Owner Guide

> System mission authority — provides business context, user information, and system purpose details for RMF documentation.

---

## Role Overview

- **Full Title**: Mission Owner
- **Abbreviation**: MO
- **RBAC Role**: `MissionOwner`
- **Primary RMF Phases**: Categorize (Support), Implement (Support)
- **Key Responsibility**: Complete the System Profile with mission statement, user categories, data types, environment details, ports/protocols, and leveraged authorizations. Provide business-context narratives for flagged controls.
- **Reports to**: ISSM (for system security matters)
- **Primary Interface**: Dashboard (System Profile pages), VS Code (`@ato`)

---

## Permissions

Profile authoring is authorized per system and tenant. Active MissionOwner,
SystemOwner, or ISSM assignments permit draft saves; ISSO alone and unassigned
users are read-only. The dashboard uses the server's `canEditProfile` capability,
not the locally selected persona. Sections under review remain locked even for
authors. A revoked assignment is checked again when saving. Missing or failed
permission loading never enables editing.

| Capability | Allowed | Tool / UI |
|-----------|---------|-----------|
| View system profile | ✅ | `compliance_get_system_profile`, Dashboard Profile pages |
| Save profile section drafts | ✅ | `compliance_save_profile_section`, Dashboard Profile form |
| Submit sections for ISSM review | ✅ | `compliance_submit_profile_section`, Dashboard Submit button |
| Withdraw sections from review | ✅ | `compliance_submit_profile_section` (action=withdraw), Dashboard Withdraw button |
| View profile completeness | ✅ | `compliance_get_profile_completeness`, Dashboard completeness bar |
| Save business context drafts | ✅ | `compliance_save_business_context` |
| Review/approve profile sections | ❌ | ISSM only |
| Assign roles | ❌ | ISSM only |
| Write SSP narratives | ❌ | ISSO only |

---

## Typical Workflow

1. **Assignment**: ISSM assigns Mission Owner role for a specific system via `compliance_assign_rmf_role`
2. **Notification**: MO receives email notification and To Do task in dashboard
3. **Profile Completion**: MO fills in each profile section:
   - Mission & Purpose (mission statement, business purpose, operational justification, business functions)
   - Data types & sensitivity now summarizes classification/CUI, declared CIA,
     privacy applicability and retention rules. **Save Draft** and SSP preview
     are at the top; **Add data type** is beside Information types and handling.
     Open a type to record CUI category, Low/Moderate/High CIA declarations,
     privacy applicability, retention/disposal and categorization rationale/
     source reference, alongside the original description/source/destination/
     regulations. Record actual handling and evidence, not reference examples.

     The sidebar counts ten documentation fields per type plus CUI category
     where CUI is declared. Unknown CIA or a pending privacy review remains a
     gap. Its correction opens the named type; recorded categorization/privacy
     documents remain separate sources. Data context and rows are reviewed
     together, so the row badge states **Section** status, not a fabricated
     individual approval. Profile review does not approve categorization or
     a privacy determination, and SSP output preserves that distinction.

     **Local check:** refresh the Data tab on port 4196. In a system draft you
     own, add/open an information type, enter its actual classification/CUI,
     declared CIA, privacy applicability, retention/disposal and source record.
     Use the top Save Draft, reload and check the table, missing-field sidebar
     and SSP preview. Saving does not submit or approve the section. Do not
     populate shared demo records with example values.

     October 6 verification: the Data API is healthy and all nine nullable
     SQL Server fields exist. The live demo has no information types yet;
     its empty table, top save and new-field editor were inspected and
     cancelled without writes or status changes. Isolated tests verify
     save/reload and reviewed data in OSCAL/Word/PDF, including exclusion
     of later drafts. Runtime configuration and other containers were
     preserved; no commit/push was made for this follow-up.

   - Users & Access (access overview, authentication method, user categories)
     now summarizes identity/privilege, access/authentication, declared data
     access and each category's real review state. **Save Draft** is at the top;
     **Add user category** sits beside the table. Open a category to record
     Human/Workload identity, privilege, affiliation, responsible owner, user
     locations, permitted environments and authorized data descriptions.
     Existing count/description/sensitivity remain in details.

     The sidebar counts recorded documentation fields, not authorization
     readiness. Missing workload owner/environment is an explicit correction
     task. Record names/descriptions do not create data/environment links or
     account grants. Apply locally, save, then review each category independently
     of the system-wide access context. Draft changes never replace retained
     approved SSP sources; unknown legacy fields remain blank, not inferred.

     Local acceptance on 4196: create synthetic human/external and workload/
     privileged categories, fill access/authentication, owner/location/environment/
     data descriptions, apply locally and use the top Save Draft. Reload and
     inspect the named record; submit/review it individually and verify its
     retained values in SSP/native output. Change a later draft and confirm the
     approved source is unchanged. Read-only users cannot mutate records.

     Users API rollout targets `ato-copilot-mcp:users-record-1312bde8-20261006`
     over the existing overview/AI runtime. The startup schema adds nullable,
     rerunnable columns; no old approval/value is inferred. Preserve runtime
     environment/user/volumes/ports/peer services and retain the first-pass image
     for rollback. Docker used the existing package-only cached-feed build path
     after NuGet TLS/EOF failure, without dependency changes or disabled TLS.
     Do not save or approve shared demo categories for live rendering checks.

     October 6 live verification succeeded: updated API healthy with unchanged
     runtime configuration and peer containers. The real SQL Server-backed
     Users page on 4196 exposes all eight nullable fields and the top Save Draft/
     preview actions. Category inspection and readiness worked without profile
     writes/page errors; demo status/categories/approvals were unchanged.
     Existing unknown fields show real gaps, not sample screenshot values.
     Tests verified category-approved fields in OSCAL/Word/PDF and excluded
     later drafts. Refresh Users to test locally; no commit/push was made.
   - Components & system scope now shows the governed component register in a
     compact table: component, type/source, environment, boundary decision,
     responsible operator and design/source review states. **Save scope draft** is in
     the page header. Counts are computed from actual components and eligible
     source candidates; actors and hosting/documentation references are not
     counted as computing assets.

     **Add component** offers recorded CSP/organization sources or a manual
     record. Open a component to record its environment, named boundary,
     responsibility, rationale and external source reference. Apply stages
     changes locally; top Save scope draft saves the existing System design
     revision with a required reason. It does not change canonical source
     inventory, accept inheritance or approve/authorize anything.

     **Reconcile discoveries** creates source-change proposals for review.
     Complete missing fields through the sidebar and review the whole design
     in **System design**. Approved designs require **Start working revision**
     first; UnderReview is read-only. Source approval is shown separately from
     design approval. Original boundary definitions and source placement tools
     remain in the visible **Recorded boundary definitions** register.

     **Local check:** refresh Components & system scope on port 4196. In a draft
     you own, open a component, enter actual owner/environment/scope rationale,
     apply, use top Save scope draft with a reason, reload and inspect the SSP
     contribution preview. Confirm the named source/version is unchanged.
     Reviewed records feed OSCAL/Word/PDF; later drafts do not replace the
     approved output. Do not save example values into shared demo records.

     **Scope decisions:** An app and its API can share this system's scope.
     Included in this system, Outside this system, and Needs confirmation map
     to existing design dispositions. A provider-hosted app can be included;
     provider infrastructure does not become included simply through an association.
     Unknown/conflicting decisions need explicit correction. Record who operates
     or manages each component, rationale and security responsibility.

     **Advanced scope details:** Physical/Logical/Hybrid boundary definitions
     are not automatically internal system areas or proven authorization scope.
     Keep exact optional named-definition associations and source versions.
     Recorded groups, hosting environments and network zones are distinct.

     **Draft versus source:** Save scope draft preserves the reviewed baseline;
     Review scope changes opens the existing design review workflow.
     Saving a draft does not change the reviewed baseline or establish authorization.
     The secondary definition/placement workflow changes canonical records
     immediately, not a draft. External/shared services retain recorded connections,
     responsibilities and sources; unused excluded components are not consumed services.

   - Environment & Deployment (hosting model, network zones, DR posture)
   - Data Types (data overview, data type entries with sensitivity classifications)
   - Ports, Protocols & Services (PPS overview, PPS entries with justifications)
   - Leveraged Authorizations (optional — external authorization documentation)
4. **Submit for Review**: MO submits completed sections for ISSM review
5. **Address Feedback**: If ISSM requests revision, MO reviews comments and edits sections
6. **Business Context**: MO drafts business-context narratives for flagged controls (visible to ISSOs)

---

## Three-Tier Governance Model

| Tier | Description | Roles |
|------|-------------|-------|
| **One — Author** | Draft and edit system profile content | Mission Owner, System Owner |
| **Two — Review** | Approve or request revision of submitted content | ISSM |
| **Three — Incorporate** | Merge approved content into SSP narratives | ISSO |

---

## Dashboard Features

- **System definition → Mission** presents a **System record** with a supporting
  sidebar for **Used in your package**, **Review & ownership**, and **Related
  work**. The sidebar includes contribution preview, review status, last-edit and
  approved-snapshot information, team/history links, package readiness and next
  tasks. It sits beside the form on desktop and below it on narrow screens.
  The record includes:
  system name, owner, acronym, version/release, eMASS system ID, DITPR identifier,
  responsible organization, program office/division, mission statement and
  business purpose. Identity and identifiers are read-only registration records;
  the owner is read-only and sourced from **System team**. Save Draft updates the
  profile fields, not registration or role assignments. **Additional mission
  details** retains operational justification and business functions below the
  main card. Save, submit for ISSM review, and approve before relying on profile
  values in the SSP. SSP generation uses the retained approved profile snapshot;
  later drafts do not replace it. Missing identifiers remain unrecorded, not
  fabricated authorization metadata.

- **Associate CSP hosting and capabilities** is a separate guided task:
  select an authorized existing system → choose one of that system's existing
  CSP hosting allocations → select applicable published security capabilities →
  review the provider source and proposed responsibilities → explicitly confirm.
  In a system workspace, open **System Profile → Provider relationships** and
  select **Start guided association**. The entry CTA and task retain the current
  organization and system context and do not create anything when opened.
  Start at `provider-relationships/setup` in the organization workspace, or at
  `systems/{systemId}/provider-relationships/setup` for a specific system.
  This task does not request or create allocations, grant cloud scope, publish
  capabilities, assert authorization coverage, or accept control duties.
  The server rechecks system and tenant permissions and source revisions.
  Unavailable reads block the dependent step and offer retry rather than showing
  a misleading empty list. Nothing is written before final confirmation.
  Confirmation records a new relationship as undetermined (existing reviewed
  relationships are not overwritten) and separate capability subscriptions
  pinned to their published releases and applicability context. The adoption
  request uses the server-returned `applicability.snapshotHash` as its context
  hash; it never substitutes a release hash or a newly generated value. This is not
  an atomic transaction. Completed operations are reported
  individually and retained during same-page retries, using the same operation
  keys. Do not refresh or leave while a request outcome is uncertain; retry
  the original operation first. AO coverage review and ISSM/ISSO responsibility
  confirmation remain separate workflows.
  Capability adoption still follows the canonical ISSM/ISSO subscription
  authorization. A Mission Owner assignment alone does not grant that
  permission: published choices remain read-only when the server returns
  `canProposeAdoption: false`. The task does not bypass this restriction or
  create an approval request on the user's behalf.
  For an unassociated allocation that the server permits the user to associate,
  **Continue with hosting association only** provides an explicit limited
  confirmation path. Its result reports only the hosting relationship saved,
  with no capability subscription or pending request created, and displays a
  fresh read-only capability/duty view. A failed review read does not conceal
  the saved association; retry refreshes only that read. An authorized
  ISSM/ISSO can subsequently select the already-associated allocation and
  subscribe to its applicable published capabilities.
  For an authorized ISSM/ISSO completing this task, a capability can be selected
  for planning when only the hosting relationship prerequisite is missing.
  The first confirmation saves only that relationship. The task then reads the
  exact selected releases again and displays their refreshed duties. A separate
  **Confirm subscriptions** action is required before any capability subscription
  is saved. Both confirmations explicitly exclude duty acceptance and
  authorization coverage. Existing relationships skip the association write
  and use the normally reviewed current capability choices.
  A changed source requires explicit review and confirmation again; already
  saved operations remain visible and are not repeated.
  Already-associated allocations remain selectable for capability review.
  Their existing hosting relationship is retained without another association
  write; `canAssociate: false` on such a record does not mean read access is
  denied. Capability subscription permission is still checked independently.
  A definitive server stale-context rejection offers **Review current choices**.
  Refresh and review the current allocation or capability, then explicitly
  confirm the revised intent with a new operation key. Already confirmed
  operations remain retained. Network loss, timeout and unavailable-service
  responses do not permit this reset: retry the frozen original request first,
  because those responses may hide a committed operation.
- **Your Profile Tasks** panel (visible only when role = MissionOwner)
- **Profile Completeness** progress bar (5 mandatory sections, Leveraged Auth is optional)
- **Governance Status** badges per section (NotStarted, Draft, UnderReview, Approved, NeedsRevision)
- **Role Switcher** in top nav for dev/test simulation
- **ProfileIncompleteBanner** on System Detail page when sections are incomplete

### Local verification of the association task

1. In an authorized organization workspace, open
   `/workspaces/organizations/{tenantId}/provider-relationships/setup`.
   Select a named system. Its system access is checked again on the canonical
   system route before hosting allocations are requested.
   Alternatively, use the system's **Provider relationships** navigation entry
   and **Start guided association** to begin within that system.
2. Choose an existing hosting allocation, inspect its scope details, and select
   published capabilities. A system without existing allocations cannot proceed;
   there is no new allocation or cloud-scope request in this task.
3. Review the published version, source references, provider coverage, shared
   duties, customer duties, and outstanding decisions. Use **Back** to verify
   selections remain intact. No write should occur yet.
4. At confirmation, verify **Associate this allocation** (or **Confirm
   subscriptions** for an existing relationship) stays disabled until the
   explicit confirmation checkbox is selected. Confirm only in your authorized
   local test environment. For a new relationship with selected capabilities,
   this first confirmation saves only the relationship. Inspect the refreshed
   capability duties, select the confirmation checkbox again, and use
   **Confirm subscriptions** to save the subscriptions.
5. If an operation fails, verify the displayed completed operations are retained
   and **Retry incomplete operations** does not repeat completed work. An
   uncertain response is not reported as success. Keep the page open to retain
   the original operation keys.
6. After success, follow **Review subscription responsibilities** or **Open
   authorization review**. These are separate, role-authorized decisions; this
   task does not perform them.

Mock-backed regression tests exercise desktop and mobile layout, accessibility,
unavailable reads, explicit confirmation and partial retries without mutating a
live backend. They do not establish production API availability or authorization.
