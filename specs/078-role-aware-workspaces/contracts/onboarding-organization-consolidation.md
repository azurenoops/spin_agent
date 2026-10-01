# Organization setup and tenant activation consolidation

**Status:** Phase 0 reviewed contract; local Phase 3 implementation and focused
backend/frontend verification complete. Live cloud/SQL Server and user acceptance
remain open.
**Source baseline:** `052120a18647bf0078afceacd7b14d0fcea8aab0`, inspected on
2026-09-30 on `078-onboarding-consolidation`.
**Ownership:** #1031 organization setup, #942 identity association, #939 tenant
draft retention, #938 shared timezone validation; feature #1002.
Issue ownership comes from the approved session plan and existing feature
documents, not a fresh GitHub status check. No issue updates are made here.

This closes the organization/tenant source-inventory holds in the session plan.
It does **not** close directory integration, SQL Server, visual acceptance, or
deployment-readiness gates. During Phase 0 no tests, builds, live application
requests, enrollment, or other business writes were performed. Subsequent
authorized local implementation and verification are recorded separately in §10.
The branch already existed at preflight; origin was
`https://github.com/azurenoops/ato-copilot.git`. Existing unrelated design files
were left untouched.

## 1. Domain boundaries and source notation

References below are repository-relative paths with source line numbers at the
baseline above. Revalidate numbers after implementation. For readability:

- `Core/` = `src/Ato.Copilot.Core/`
- `Mcp/` = `src/Ato.Copilot.Mcp/`
- `Dash/` = `src/Ato.Copilot.Dashboard/src/`
- `Agents/` = `src/Ato.Copilot.Agents/`

“Organization” in the provider workspace means the isolation **Tenant**.
`Organizations` contains organizational profiles/subgroups inside that tenant;
`Org.Profile` must not create a second isolation tenant. These meanings are
already distinguished in `../spec.md:389-402`. Preserve the September 23
organization-creation intent/recovery requirements in `../spec.md:346-378`;
the approved consolidation adds explicit server draft saves before confirmation.

Feature 048's historical contract
`../../048-tenant-isolation/contracts/tenant-onboarding.openapi.yaml:1-105`
describes a progress response, not editable saved values. Its “runs only when
not Active” wording is not current UI behavior: active administrators can reenter
the wizard. Feature 048 US4 (`../../048-tenant-isolation/spec.md:104-118`)
predates the explicit membership resolver; do not revive directory-based
automatic membership from that historical wording.

Five independent facts remain independent:

1. Trusted deployment mode/bootstrap policy.
2. Tenant lifecycle and activation (`Status` versus `OnboardingState`).
3. Organization creation and the requested enrollment operation.
4. Live local Person, compound identity membership, and scoped Administrator.
5. Current actor's actual access and permitted destination.

No provider-receipt, system-registration, hosting, or monitoring implementation
is specified here. An organization contact or tenant AO field is descriptive;
neither is a membership grant, a system RMF assignment, nor an AO decision.

## 2. Current organization execution: complete write/read trace

### 2.1 Caller and endpoint disposition

All paths in this table are existing, not newly proposed.

| Named caller/component | Endpoint or callee | Disposition and exact boundary |
|---|---|---|
| `ApplicationRoutes.tsx:151-154` → `WorkspaceOperationsPage.tsx:1214-1216` | `/organizations/new`, `/organizations/:tenantId/provisioning` | Repurpose `AddOrganizationPage` and `OrganizationProvisioningPage`, retain bookmarks and `?key=` recovery. Workspace navigation prefixes the selected workspace (`features/workspaces/workspaceNavigation.tsx:32-80`). |
| `AddOrganizationPage.tsx:47-107` → `features/workspace-operations/api.ts:140-155` | `POST /api/csp/dashboard/tenants`, header `Idempotency-Key`; `GET /api/csp/organization-creations/{key}` | Retain canonical creation and read-only recovery. Current details/admin/review are local until confirmation; URL key survives refresh, unsent fields do not. New draft routes below add persistence, not another creation engine. |
| `OrganizationProvisioningPage.tsx:59-139` → `api.ts:158-195` | `POST /api/csp/organizations/{tenantId}/provisioning`; `GET .../provisioning?idempotencyKey=...`; `GET .../provisioning/current`; `PATCH .../provisioning/{operationId}` | Retain all four operations. GETs do not write. Begin can reuse an existing operation. PATCH binds intent, then grants only missing enrollment stages. |
| `OrganizationPages.tsx:210-245`, `OrganizationSetupHandoff.tsx:7-36` | `GET /api/csp/organizations`, `GET /api/csp/organizations/{tenantId}`, current provisioning | Retain catalog/detail summaries; add independent live-access summary instead of relabeling all old attempts completed. Provider handoff explicitly grants no customer-system access. |
| `OrganizationSetupPresentation.tsx:16-46,83-126` | `AdministratorInputs`, validation, `enrollmentComplete`, `SetupSteps`, `SetupInfo`, `SetupSummary` | Reuse domain-specific fields and predicates; consolidate presentation to the mock hierarchy. Current directory select fills intent only; manual IDs remain unverified. |
| `EntraUserPicker.tsx:7-56` → workspace-operation directory API | `GET /api/csp/directory/connections`; `GET /api/csp/directory/users?connectionId=...&query=...` | Retain scoped discovery, cancellation and explicit selection. Do not grant on select or return fabricated success on discovery failure. |
| `features/workspaces/api.ts:57-81`, membership management; provisioning PATCH | `/api/tenants/{tenantId}/membership-persons`, `/memberships`, `/administrator-assignments` | Retain supported local Person/membership/initial-admin administration. These are the only enrollment services; no wizard membership table. |
| `SetupDialog` and capability setup callers | Existing catalog/capability dialogs | Not retired by this contract. Shared dialog primitives and unrelated applicability pickers are not organization bootstrap logic. |

### 2.2 Create, recover, begin, bind, grant, resume

1. **Create authorization and result.** `Mcp/Endpoints/Csp/CspDashboardEndpoints.cs:295-361`
   checks configured SingleTenant mode first, then requires `IsCspAdmin` without
   impersonation. It calls `IWorkspaceOperationsService.CreateOrganizationAsync`;
   a new result is HTTP 201 with `Location`, exact replay HTTP 200. The body has
   `tenantId`, `operationId`, `displayName`, lifecycle `status`, `onboardingState`,
   and `existing`. Invalid input/name conflict is 422; changed idempotent intent
   is 409 `IDEMPOTENCY_CONFLICT`; inactive replay is 403.
2. **Canonical create.** `Core/Services/Workspaces/WorkspaceOperationsService.cs:617-698`
   trims descriptive fields, lowercases POC email, validates lengths and optional
   administrator intent. It hashes normalized intent, retaining the historical
   four-property hash when no administrator was supplied. Same key/different hash
   conflicts. It creates **Tenant Active/Pending**, normalized-name reservation,
   and provisioning operation in one `SaveChangesAsync`. It does not create a
   Person, membership, role, subgroup, or system. A uniqueness race re-reads the
   winning operation or reservation; a name collision is not authorized reuse.
3. **Read recovery.** `WorkspaceOperationsService.Provisioning.cs:14-23` finds
   only an operation with the key and a non-null creation hash; it projects through
   `WorkspaceOperationsService.cs:726-737`. Missing key is 404, orphaned creation
   is conflict, and inactive/sentinel tenants are denied. GET does not resume
   enrollment or update timestamps. Endpoint guards are ordinary CSP-only
   (`Mcp/Endpoints/Workspaces/WorkspaceOperationsEndpoints.cs:283-296`).
4. **Begin.** `WorkspaceOperationsService.cs:584-615` uses an execution strategy
   and relational serializable transaction. It requires an active nonsentinel
   customer tenant; an existing key for a different tenant conflicts. With no
   key match it chooses a prior creation-hash operation first, then earliest
   `CreatedAt`, then ID. Only absence of any prior operation creates a row.
   Returning a previous operation may therefore return its **original key**.
5. **Read by key/current.** `WorkspaceOperationsService.cs:700-723` requires
   active customer scope. Current uses latest `UpdatedAt`, then latest `CreatedAt`,
   then descending ID. This is different from begin's reuse order and must not
   silently select a different historical request during exact-operation resume.
6. **Bind intent before grants.** `WorkspaceOperationsService.cs:740-806` normalizes
   nonempty directory/object GUIDs plus exactly one existing Person or new Person
   request (`.Provisioning.cs:26-39`). It uses serializable persistence, validates
   local Person tenancy and active identity/Person conflicts, rejects a different
   existing Administrator, and refuses changes to bound intent. An already-bound
   sibling operation must have the same intent and supplies the same Person.
   `IPersonService.StageLocalAsync` may create a local Person and audit in this
   transaction; it does not directory-link that Person
   (`Agents/Compliance/Services/Onboarding/PersonService.cs:70-112`).
   The operation stores original intent JSON, binding IDs and timestamp, clears
   error and increments its EF concurrency revision.
7. **Explicit enrollment.** `Mcp/Endpoints/Workspaces/WorkspaceOperationsEndpoints.cs:229-266`
   first requires ordinary CSP authority and
   `IOrganizationMembershipService.AuthorizeAdministrationAsync`. It pushes
   target tenant scope, binds, grants membership if absent, reprojects, enrolls
   Administrator if absent, and reprojects again. These are separate durable
   transactions: a later failure does not undo successful earlier stages.
   After binding has returned, an exception records `LastError` (truncated to
   200 characters) on the same operation (`WorkspaceOperationsService.cs:809-821`).
   Failure before that point is not recorded as post-bind failure. Do not claim
   this is one all-or-nothing enrollment transaction.
8. **Resume after refresh.** `OrganizationProvisioningPage.tsx:91-121` sends saved
   exact identity. On an error it re-reads saved status; inability to confirm is
   shown separately. Automatic continuation occurs only from confirmed creation's
   `autoResume` navigation state, which is cleared; ordinary refresh is read-only.
   Existing “Finish later” is navigation, not a pre-confirmation draft save.

Mutation error mapping is `WorkspaceOperationsEndpoints.cs:535-553`: invalid
input 400, missing record 404, concurrency 409 `STALE_REVISION`, domain conflict
409 `CONFLICT`, workspace exceptions retain status/code; unknown exceptions
are rethrown. There is currently no client `expectedRevision` in
`UpdateProvisioningRequest` (`Core/Interfaces/Workspaces/IWorkspaceOperationsService.cs:130-145`).
An EF concurrency token is not equivalent to a client review precondition.

### 2.3 Preserve successful reconciliation; do not generalize it incorrectly

`WorkspaceOperationsService.Provisioning.cs:65-95` already ignores stale saved
membership/admin flags and projects **live rows for the operation's bound
Person**:

- Local Person must exist in the operation's tenant.
- Membership must be active and match tenant, Person, directory tenant and object.
- Administrator must be active for that tenant and Person.
- Both live membership and Administrator suppress an old `LastError` in the
  response without deleting its stored history.
- A new-Person request is completed only when the bound local Person exists.
- Intent becomes uneditable when bound or when its local Person is already valid.

This is working source logic to **retain**, not a missing feature to replace.
`OrganizationSetupAsync` (`.Provisioning.cs:98-113`) projects the latest
operation, counts active memberships, and returns `NotStarted` without an
operation. It does **not** independently discover an unrelated live admin when
the historical operation lacks a binding. Its role flag alone may be completed
when membership has been revoked; the combined result correctly remains
incomplete. Member count alone is not proof of a usable Administrator.

| Current facts versus retained request | Required interpretation |
|---|---|
| Same bound Person, exact active membership, active Administrator | Preserve current live projection and error suppression; GET reconciles without another grant. |
| Same Person/admin, membership revoked | Admin role history remains; requested access incomplete. Repair explicitly through membership administration, never from GET. |
| Other valid Administrator, unbound historical operation | Organization has current admin access; old attempt is still unbound. New summary must show both facts, not silently bind the old attempt. |
| Other valid Administrator, retained request explicitly names a different identity | Existing administrator is usable, requested enrollment is unresolved/conflicting. Do not mark the requested identity successful. Initial-admin service must keep its conflict. |
| No valid Administrator | Resume only authorized missing stages for the same operation. Contact text, role labels and stale “Completed” flags are insufficient. |

Existing source tests preserving this behavior include
`tests/Ato.Copilot.Tests.Integration/Tenancy/OrganizationCreationFlowTests.cs:21-128`
and `.Recovery.cs:108-209`: deferred read-only recovery, idempotent binding,
legacy hashes, stale completion flags, partial failure/retry and begin reuse.
They were read, **not executed** in Phase 0.

## 3. Identity, directory and membership: exact existing limits

`Mcp/Services/Tenancy/WorkspaceService.cs:34-40,50-76,79-182` validates an
authenticated nonempty token `(tid, oid)`, discovers explicit grants using both
components, and revalidates target membership plus local Person on ordinary
workspace resolution. It does not grant from email or directory affiliation.
Ordinary CSP-member requests set `IsCspAdmin=false` for tenant filtering.
Support requires the explicit mode and an actor/directory/target-bound support
cookie. No automatic provider-to-customer transition is allowed.

`OrganizationMembershipEndpoints.cs:14-95` exposes:

- GET/POST `membership-persons`: authorized local search/create, not an identity
  verification or grant. New contact lengths are checked; conflicts are explicit.
- GET/POST `memberships`, GET/DELETE `memberships/{membershipId}`: authorize
  administration, preserve compound identity and revocation audit.
- POST `administrator-assignments`, GET `administrator-assignments/{assignmentId}`:
  existing **initial** Administrator enrollment, not a multi-admin replacement API.

`OrganizationMembershipService.cs:76-96` permits a CSP administrator or the
existing scoped organization Administrator, enforces selected organization
target matching, and requires an active target. `GrantAsync:119-170` validates
GUIDs/local Person, rejects Person/identity reassociation, reuses an exact active
grant, or explicitly reactivates a revoked grant with audit under serializable
transaction. Unique conflicts map to 409. `EnrollAdministratorAsync:47-73`
additionally requires provider workspace/CSP authority and active membership;
same existing admin is reused, another existing admin yields 409
`ADMINISTRATOR_ALREADY_ENROLLED`. `RevokeAsync:174-204` protects the last
accessible Administrator while preserving role history.

Directory discovery is server-configured:
`EntraDirectoryService.cs:17-89` restricts connections by the actor's directory
and ordinary CSP workspace; requires configured IDs/credentials/cloud; searches
only the selected cloud's Graph host with escaped prefix filters, top 20, and an
explicit `hasMore`. It reports unconfigured, consent, auth, throttled, timeout,
unavailable and invalid-response errors, never “no matches” on those failures.
`EntraDirectoryEndpoints.cs:10-24` sets `Cache-Control: no-store`.

**Guest validation boundary:** current search returns the selected connection's
directory ID plus Graph user object ID, display name/email/UPN. It does not fetch
`userType`, invitation/redemption state, `accountEnabled`, or validate the later
sign-in token against that result. Membership grant validates structure and
database conflicts, **not Graph existence**. Manual entry remains unverified.
Neither a home-directory ID paired with a guest's resource-directory object ID,
nor a matching email, proves an authenticatable compound identity.

Proposed UI must label directory-selected identities “Found in [directory]”,
not “sign-in verified.” Keep exact resource-directory/object pair; never convert
it from UPN/email. Do not invent guest invitation/redemption behavior or a new
identity engine. Real guest ordinary-login acceptance remains an explicit
integration gate: grant the exact pair actually emitted by the approved sign-in
configuration, then prove workspace discovery; wrong-directory and same-object
other-directory fixtures must fail. Stale/deleted/disabled directory users cannot
be shown as verified sign-ins merely because local enrollment saved.

## 4. Tenant activation: each step, hydration and guards

### 4.1 Persisted state and current defects, by sequential path

`TenantWizard/api.ts:34-42` currently gets only tenant ID, step/completed steps,
onboarding state and first organization ID. No editable values or revisions.
`TenantWizard/index.tsx:149-202,375-463` loads that progress and conditionally mounts
each step; it passes no saved field values. Sidebar navigation unmounts local
step state (`index.tsx:282-286`). A new transition to Active navigates home;
already-active reentry stays open (`index.tsx:173-202`).

| Step/component and source | Current initialization → submit/persistence | Required disposition |
|---|---|---|
| `steps/LegalEntityStep.tsx:10-27` | Empty legal entity, component, timezone → `/legal-entity`; service trims required legal name, optional component, updates timezone only if nonblank (`TenantOnboardingService.cs:72-87`) | Controlled shell draft and server hydration. #938 owns IANA validation/correction; do not silently replace an invalid saved timezone. |
| `steps/HqAddressStep.tsx:10-34` | Empty address fields, country `US` → `/hq-address`; required line1/city/state/postal/country, optional line2 (`service:89-109`) | Preserve all six fields including optional line2; saved country wins over default. Blank partial draft may save but cannot activate. |
| `steps/ClassificationStep.tsx:27-46` | `Unclassified` every mount → `/classification`; `Enum.TryParse` then persisted enum (`service:111-126`) | Never remount to a lower default. Hydrate saved value, visibly handle unsupported legacy value, retain authoritative validation. Do not represent this field as deployment certification. |
| `steps/AoStep.tsx:10-25` | Empty name/email → `/ao`; nonempty checks and tenant metadata (`service:128-140`) | Retain descriptive metadata only. No system AO grant or authorization. |
| `steps/PrimaryPocStep.tsx:10-33` | Empty name/email/phone → `/primary-poc`; tenant contact write (`service:142-156`) | Hydrate/preserve contact separately from enrollment. |
| `steps/OrgProfileStep.tsx:10-31` | Empty name/description → `/org-profile`; selects tenant's first subgroup **ordered by ID**, not timestamp, and updates or creates (`service:158-199`) | Reuse existing subgroup by recorded ID. Do not merge by name or reinterpret subgroup as Tenant. Source comment mentions timestamp but implementation orders by ID. |
| `steps/ReviewStep.tsx:13-51` | Static “all captured” checklist → `/submit` | Render actual saved/dirty/missing facts, not unconditional claims. Final activation stays server-owned. |
| `JobStatusPanel`, `SkipStepModal`; `index.tsx:393-440,475-489` | AO optional job-ID cast has no job ID in the tenant DTO/service result. HQ/classification skip uses general onboarding API; supplied reason is discarded by parent despite its comment | Retain shared components for their real callers. Remove unsupported tenant skip/job claims only with replacement tests; do not infer a tenant job exists. |

`Core/Services/Tenancy/TenantOnboardingService.cs:277-340` writes step audit and
tenant together, changes Pending to InWizard, and derives completed steps from
`AuditLogs` action names. The six completed step names exclude Submitted.
`SubmitFinalAsync:202-269` checks nonempty legal/address/AO/POC fields and an
existing subgroup; it does **not** require all six audit markers or explicitly
validate classification/timezone at this final point. It sets
`OnboardingState=Active`, not lifecycle `Status`, audits Submitted, and invalidates
both onboarding/status cache keys. Preserve cache invalidation and active reentry.

`SkipStepModal.tsx:55-64` only forwards its reason to the parent. It does not send
an API request internally. Therefore the comment in `TenantWizard/index.tsx:481`
is not evidence that a reason is persisted. The tenant final service still
requires HQ fields: skipping presentation cannot satisfy activation.

### 4.2 Full host gates, not endpoint comments

- Pipeline order is CAC authentication → tenant/workspace resolution → coarse
  compliance authorization → audit → ASP.NET authorization
  (`Mcp/Program.cs:599-624`).
- MultiTenant CSP first-use gate precedes tenant resolution and even the explicit
  test bypass. Unfinished hosting profile yields 503
  `CSP_ONBOARDING_INCOMPLETE` outside its allowlist/preparation metadata.
  SingleTenant skips this gate
  (`TenantResolutionMiddleware.cs:95-175`).
- MultiTenant or explicit workspace headers require authenticated workspace
  resolution. Ordinary organization mode requires membership. CSP workspace
  returns before customer lifecycle/onboarding evaluation
  (`TenantResolutionMiddleware.cs:182-209`; `WorkspaceService.cs:79-182`).
- Customer suspended mutations return 423; disabled returns 401. Incomplete
  onboarding permits `/api/onboarding/tenant`, `/api/auth`, `/api/deployment`,
  `/api/tenants`; other paths return 403 `TENANT_ONBOARDING_INCOMPLETE`
  (`TenantResolutionMiddleware.cs:81-87,255-289`).
- **Source-level integration blocker:** `TenantOnboardingEndpoints.cs:23-170`
  does not attach `WorkspaceAuthorizedEndpoint` or an action authorization
  policy. The coarse gate permits workspace GET/HEAD, ordinary CSP writes under
  `/api/csp`, or explicitly marked endpoints; it otherwise returns 403
  `WORKSPACE_OPERATION_NOT_AUTHORIZED`
  (`ComplianceAuthorizationMiddleware.cs:263-296`). Thus an ordinary scoped
  tenant wizard POST is not shown to be reachable by the source trace. The
  existing `TenantOnboardingWizardTests.cs:20-39` uses mutable fixture context;
  its happy-path source is not evidence of this real workspace pipeline passing.
  **No HTTP reproduction was run.** First implementation test must exercise the
  full pipeline without `Tenant:Resolution:BypassForTests`, not weaken the gate.
- `ApplicationFrame.tsx:18-29` invokes the tenant guard only for organization
  managers, retains legacy guards without workspace scope, and does not probe
  provider onboarding from ordinary organization pages.
  `TenantOnboardingGuard.tsx:25-59` is an inert-on-error UX probe, not security:
  it passes children while loading and on any request failure. Preserve the
  server gates; proposed errors must be visible without treating unavailable
  state as Active.
- Trusted `DeploymentOptions.Mode/DefaultTenantId/Tenants.AllowSelfOnboarding`
  are configuration, not editable setup data
  (`Mcp/Configuration/DeploymentOptions.cs:19-53`). `/api/deployment/mode`
  returns mode/default tenant only (`DeploymentEndpoints.cs:28-60`).
  An HTTP 200 availability probe is not deployment readiness. The inspected
  tenant classification command validates enum parsing, **not a demonstrated
  deployment handling ceiling**. Do not claim otherwise; consume an approved
  trusted handling-policy projection if provided by its owner, otherwise show
  the handling limit as unavailable and do not invent a selectable deployment
  limit.

The broad clean-deployment outcome remains #1036, with #941/#944 bootstrap
dependencies separate. This contract must not close them or replace their
deployment/initial-admin policies.

## 5. Proposed exact organization draft/resume contract

Everything in §§5–8 is proposed, not an existing endpoint guarantee.

### 5.1 State owner, DTOs and storage

Extend `IWorkspaceOperationsService`, its `.Provisioning.cs` partial and existing
endpoint group. Add **one provider-owned draft record**, not an orchestration
engine. The existing `OrganizationProvisioningOperation.TenantId` is nonnullable,
its default tenant state is Completed, and its replay hash is immutable
(`Core/Models/Workspaces/WorkspaceModels.cs:105-124`); reusing it for an unsaved
organization would require fabricating a tenant or breaking legacy semantics.

Add `OrganizationOnboardingDraft` alongside those models:

```text
Id: Guid PK (client UUID, known before first save)
ProviderId: Guid (server-resolved hosting provider; immutable)
CreatedByDirectoryTenantId: Guid; CreatedByObjectId: Guid
Revision: long concurrency token, initial 1
SchemaVersion: int = 1
ValuesJson: string (typed JSON below, max 32 KiB UTF-8)
CurrentStep: string(32), one of details|administrator|review
State: string(24), Draft|Confirmed|Discarded
CreationKey: string(100), server-assigned stable UUID string
ConfirmedIntentHash: string(64)?; ConfirmedIntentJson: string?
TenantId: Guid?; OperationId: Guid?
CreatedAt/UpdatedAt: DateTimeOffset
UpdatedByDirectoryTenantId/UpdatedByObjectId: Guid
```

Drafts are **`[ProviderScoped]`, never `[GlobalReference]`**, using the existing
private provider ownership pattern. `ProviderId` is resolved by the service from
the hosting `CspProfile`, never accepted from the client or inferred from a
customer tenant. Every load, list, mutation and replay includes
`ProviderId == resolvedProviderId`, plus ordinary CSP workspace authorization,
as `CspPackageService.ProviderAsync/LoadAsync` do
(`Core/Services/PackageImports/CspPackageService.cs:27-47`). Missing provider
profile returns an explicit unavailable/not-found result; no placeholder global
owner is manufactured. Creation keys are unique on `(ProviderId, CreationKey)`;
the canonical operation still retains its existing globally unique UUID key.

The inspected guards make the reason for this correction concrete:

- `GlobalReferenceAttribute.cs:3-18` designates cross-tenant-readable reference
  entities. `OrganizationProvisioningOperation` currently carries that marker
  (`WorkspaceModels.cs:105-124`), and its mapping (`AtoCopilotContext.cs:3469-3475`)
  does not install a private filter. Its existing guarded HTTP endpoints do
  **not** establish generic-reader privacy. Retaining this legacy operation is
  not a reason to copy its classification to new POC/identity draft data.
- `Models/Tenancy/Attributes/ProviderScopedAttribute.cs:3-5` and
  `Models/ProviderAuthorizations/ProviderAuthorizationModels.cs:19-46` establish
  the private marker pattern. `AtoCopilotContext.ApplyTenantQueryFilters:3530-3538`
  installs `TenantFilterDisabled || TenantFilterCspAdminAll` for it. The latter
  requires CSP authority without impersonation (`AtoCopilotContext.cs:79-86`);
  ordinary organization and support-context generic EF reads therefore return
  no draft rows. Do not add `IgnoreQueryFilters` to draft reads.
- The model self-check requires `[ProviderScoped]`, a `Guid ProviderId`, and a
  private filter, rejecting conflicting markers
  (`AtoCopilotContext.cs:4035-4051`). Do not add this type to
  `TenantScopingExceptions` or the global-reference allowlist.
- Package aggregates use explicit private filters even without the marker
  (`AtoCopilotContext.cs:3393-3405`). They additionally enforce provider ownership
  in the service; the marker/filter does not substitute for that predicate.
- `TenantScopedQueryGuardInterceptor.cs:97-164` rejects HTTP reads with no
  ambient context unless every queried table is global-reference. A new private
  draft table must remain outside that exception. Background/no-HTTP queries
  are allowed, and the filter is intentionally permissive without context:
  this is **not** claimed as database/RLS isolation for unscoped background code.
  No generic background consumer is authorized to expose drafts.
- `TenantStampingSaveChangesInterceptor.cs:80-93` skips non-tenant-scoped types.
  Do not claim it stamps or protects ProviderId. The existing workspace service
  must explicitly authorize each draft mutation, set and preserve its server
  ProviderId, and use provider-qualified conditional revision writes.

Created/updated actor identity is audit attribution, not permission to bypass
current authority. All currently authorized ordinary CSP admins for the resolved
single hosting provider may continue that provider's drafts; ordinary tenant
readers, support users and generic public catalog projections may not.
Do not embed draft JSON/identity/contact values in `CspProfile`, tenant list
responses, public reference records, or generic audit Details; audit draft ID,
provider ID, action, revision and actor instead. No automatic deletion/TTL:
explicit discard tombstones retain recovery/audit. A discarded draft is not
recreated. Once linked, Tenant/Operation deletion is not cascade deletion of
recovery data; missing referenced records report conflict.

```typescript
type OrganizationDraftValues = {
  organizationChoice: "create" | "existing";
  existingTenantId: string | null; // ID, never name-based resolution
  displayName: string | null;
  legalEntityName: string | null;
  primaryPocName: string | null;
  primaryPocEmail: string | null;
  administratorChoice: "existing" | "other" | "deferred" | null;
  administrator: {
    directoryTenantId: string | null;
    objectId: string | null;
    personId: string | null;
    newPerson: { displayName: string | null; email: string | null } | null;
  } | null;
  discovery: {
    source: "directory" | "manual";
    connectionId: string | null;
  } | null; // provenance of selection; not server verification
  deferralReason: string | null; // <= 1000; explicit user text, no PII logging
};
type SaveOrganizationDraft = {
  schemaVersion: 1; expectedRevision: number; currentStep: "details" | "administrator" | "review";
  values: OrganizationDraftValues;
};
```

Response adds `draftId`, `revision`, `state`, `savedAt`, stable `creationKey`,
nullable linked `tenantId/operationId`, and per-field validation issues.
`expectedRevision=0` creates a new UUID draft; all later saves require exact
revision. Save permits missing required values but rejects oversized/unknown
fields, unsupported schema/enum values, and malformed nonblank GUIDs.
Confirmation enforces existing canonical creation/binding validation; no default
identity, inferred grant, or implied confirmation from draft save.

### 5.2 Exact routes and side effects

| Route | Body/result and permitted effects |
|---|---|
| `PUT /api/csp/organization-onboarding/drafts/{draftId:guid}` | `SaveOrganizationDraft`; 201 on first save, 200 update. Persist only draft/audit. No Tenant/Person/membership/role/name reservation. |
| `GET /api/csp/organization-onboarding/drafts/{draftId:guid}` | Exact authorized draft, links and current validation. No writes. |
| `GET /api/csp/organization-onboarding/drafts?page=1&pageSize=25` | Provider-authorized list, bounded to 100/page; stable `UpdatedAt DESC, Id DESC`; named resumable records, not duplicated tasks. |
| `POST /api/csp/organization-onboarding/drafts/{draftId:guid}/discard` | `{ expectedRevision }`; only Draft → Discarded; 200 tombstone. Confirmed operation cannot be discarded or deleted here. |
| `POST /api/csp/organization-onboarding/drafts/{draftId:guid}/confirm` | `{ expectedRevision, confirmed: true }`; freeze normalized exact creation/reuse intent and link canonical result. New creation uses the stored CreationKey; authorized existing selection revalidates Tenant ID, never creates/renames it. Does **not** grant enrollment. |
| `GET /api/csp/organizations/{tenantId:guid}/setup-summary?operationId={id}&administratorPage=1&administratorPageSize=25` | Live access + exact historical request + actor actions as below. Omit operationId only to request explicitly labeled latest operation. Administrator pages are bounded to 100; read-only. |
| Existing `PATCH .../provisioning/{operationId}` | Add optional `expectedRevision` to legacy binding request; new client must supply it. Continue existing explicit membership/initial-admin service stages only. |

Every route rechecks ordinary CSP authority/no support, active target when one
exists, and data ownership. Standard errors: 400 invalid DTO/schema, 401 invalid
identity, 403 forbidden, 404 missing draft/target, 409 `STALE_REVISION`,
`DRAFT_CONFIRMED`, `DRAFT_DISCARDED`, `IDEMPOTENCY_CONFLICT`, or
`ADMINISTRATOR_ALREADY_ENROLLED`. Legacy routes/envelopes remain unchanged;
new routes use `{status, data, error:{code,message}}`, with no-store responses.
No GET changes completion, counters, audit history or grants.

Confirmation must persist frozen intent and canonical creation/link in one
relational transaction using a refactored internal create core in the same
service/context, retaining the current hash algorithm and unique-name/key
constraints. A lost response is recovered by draft ID or existing creation key.
Exact replay of confirmed revision/intent returns the same linked record with
200; modified input conflicts. Explicit reuse freezes the authorized tenant ID
and obtains/reuses its canonical provisioning operation only if enrollment work
is requested; existing ready access needs no new operation.

No new work is launched by confirm replay or GET. After confirmed review,
the client separately invokes existing PATCH if the user explicitly selected
enrollment; refresh must re-read before that mutation. Saving a partially filled
administrator with “finish later” cannot start enrollment.

Revision comparison and increment must be a database conditional update, not a
read-then-unconditional overwrite. Concurrent first PUTs use the draft PK.
EF `Revision.IsConcurrencyToken()` plus serializable transactions/unique keys
must work on SQLite and SQL Server. Update legacy operation failure writes to
increment Revision as well as binding writes; return Revision additively in
`OrganizationProvisioningResult`. An `expectedRevision` check applies once at
entry to explicit PATCH, not to its own subsequent internal stage re-projections.
Existing legacy callers may omit it but retain immutable binding, serialized
grant and idempotency protections; do not relax them.

### 5.3 Live summary versus requested outcome

Return a proposed `OrganizationSetupSummary`:

```text
tenant: { id, displayName, lifecycle, onboardingState }
observedAt: DateTimeOffset
liveAccess: {
  state: Available|Missing|Unavailable,
  activeMemberCount: int?,
  administrators: {
    items: [
      { personId, displayName, membershipId, directoryTenantId, objectId, assignmentId }
    ],
    page: int, pageSize: int, total: int?
  }
}
requestedOperation: null | {
  operationId, idempotencyKey, revision, initialAdministrator,
  personState, membershipState, administratorState, lastError,
  canEditAdministrator, reconciliation: SameIdentity|DifferentIdentity|Unbound|None
}
actorActions: { canManageMemberships, canResumeEnrollment, canEnterOrganization }
```

`administrators.items` is a live join of existing local Person + active membership
+ active scoped Administrator **in the same tenant**, not role-name parsing.
Default page 1, size 25; reject page <1, size outside 1–100 and overflowing offsets
with 400. Apply paging in the database with stable order
`Person.DisplayName, Person.Id, Assignment.Id, Membership.Id`, after deduplicating
the exact joined record. Return `total` for the filtered join. Do not load all
administrators before paging. Current-admin availability and same/different
requested-identity comparison use separate scoped `Any`/exact-match queries over
the full join, never the first page. Unavailable query is explicit
failure/unavailable with null counts/total, never a fabricated zero or success.
`canEnterOrganization` comes from the existing ordinary workspace resolver for
the current token identity, never from its provider role or the chosen admin.
For a different requested identity, the summary does not mark that request
complete. “Use existing administrator” records an explicit draft choice; it
does not rewrite an old bound operation. “Select another administrator” when an
admin already exists leads to the existing authorized role-management workflow,
not initial-admin PATCH. If the actor lacks that workflow's authority, show a
handoff task/unavailable action. No replacement or additional grant is invented.

## 6. Proposed tenant draft and compatibility contract

### 6.1 Domain-owned state and exact routes

Extend **existing `TenantOnboardingService`**, not Feature 047's generic wizard
engine. Add to `Tenant`:

```text
OnboardingDraftJson: string? (typed TenantDraftValues, max 32 KiB UTF-8)
OnboardingDraftSchemaVersion: int = 1
OnboardingDraftRevision: long = 0 (concurrency token)
OnboardingDraftStep: string(32)? (existing TenantWizardStep names)
OnboardingFirstOrganizationId: Guid? (explicit subgroup reference)
```

Use tenant UpdatedAt/UpdatedBy and existing `AuditLogs` for save attribution;
no duplicate membership, activation or job state table. Explicit draft saves
must not add the existing `TenantOnboarding.<step>` completion audit actions.
Use `TenantOnboarding.DraftSaved/Discarded` as distinct audit events and exclude
them from progress derivation. Existing final activation remains authoritative.

`TenantDraftValues` has nullable slices named `legalEntity`, `hqAddress`,
`classification`, `ao`, `primaryPoc`, `orgProfile`. Each slice has exactly the
fields in the existing request DTO in `TenantWizard/api.ts:44-81`, made nullable
for partial capture; `orgProfile` additionally includes nullable
`firstOrganizationId`. Bounds match `Tenant.cs` and the Feature 048 schema.
No additional admin identity fields are accepted here.

| Route | Proposed additive behavior |
|---|---|
| Existing `GET /api/onboarding/tenant/state` | Retain old progress fields; add `submittedValues`, `draft:{schemaVersion,revision,currentStep,values,savedAt}|null`, `firstOrganizationId`, `missingRequiredFields`, `allowedActions`, and trusted-policy availability. Read persisted values, not audit payload reconstruction. |
| `PUT /api/onboarding/tenant/draft` | `{schemaVersion:1, expectedRevision, currentStep, values}`; partial save and audit only, 200 with full state. Does not complete steps, change activation, create subgroup, grant roles or run a job. |
| `POST /api/onboarding/tenant/draft/discard` | `{expectedRevision}`; clear draft, increment revision, retain submitted values/audit/activation, return state. |
| Existing six step POSTs | Retain field names and progress envelope; optionally accept `expectedRevision` for new client. Validate/apply only that slice through the existing service, update matching draft slice/revision atomically and preserve other dirty slices. |
| Existing `POST /api/onboarding/tenant/submit` | New client sends `{expectedRevision, confirmed:true}`. Require no unapplied draft changes and authoritative required-field validation; activate only persisted submitted facts. Legacy empty-body submission remains supported over persisted values; it cannot apply a draft implicitly. |

Authoritative new/legacy workspace writes require an authenticated **ordinary
organization context**, local Person, current active membership and persisted
Administrator assignment in its effective tenant. Add an action policy using
the existing `OnboardingAdministratorPolicy` workspace branch
(`Mcp/Authorization/OnboardingAdministratorPolicy.cs:59-68`) and explicit
ordinary-mode/effective-target checks; only then attach
`WorkspaceAuthorizedEndpoint` so the coarse gate admits the request.
Do not attach the metadata without its server authorization policy.

Retain SingleTenant's resolved default tenant path. Its authorization must use
the resolved tenant and existing supported administrator/bootstrap policy,
not assume directory `tid` equals the isolation ID: the policy's legacy branch
currently reads `tid` directly (`OnboardingAdministratorPolicy.cs:71-116`).
Preserve genuine existing bootstrap handling; do not create a new
first-user-wins grant or make a provider-only actor a customer admin. Tests must
establish the supported SingleTenant bootstrap path before rollout. Support
and ordinary member writes cannot borrow CSP creation authority.

Every relevant legacy step mutation must advance the same
`OnboardingDraftRevision`; otherwise a stale new client could overwrite an old
client's submitted fields undetected. New client stale saves/submissions return
409 and retain local edits. Use conditional revision update within the same
transaction as Tenant/subgroup/audit changes; do not rely on an assumed SQLite
rowversion trigger. Existing `Tenant.RowVersion` remains preserved.
On first subgroup creation persist its exact ID in the new field atomically;
later submits update only that same tenant-scoped subgroup. No second subgroup
on concurrent first submit and no cross-tenant subgroup adoption.

### 6.2 Shell hydration and navigation

- Initialize a shell-owned controlled draft once per effective tenant/revision:
  explicit saved draft overlay over submitted values, never blank defaults over
  saved values. Server state supplies actual default classification/timezone.
  Blank unsaved fields are not a lower-classification migration.
- Keep all slices mounted in state across sidebar/Back/Next navigation; navigation
  alone writes nothing. Explicit Save & finish later waits for successful PUT
  before showing Saved/navigating. On failure retain all edits and current step.
- Continue applies/validates only its submitted slice; Review distinguishes
  saved draft from applied fields and renders missing requirements. Activation
  cannot silently apply a partial draft.
- Preserve `/onboarding/tenant` and existing seven step discriminators, active
  admin reentry, first-session activation redirect and cache invalidation.
  Keep the activation shell separate from the six organization mock states.
- A delayed response from another tenant/tab/account must not replace local data.
  Reuse current auth/workspace request capture and stale-response handling
  (`features/auth/interceptors.ts:61-104`), not browser-wide identity state.
- HQ remains activation-required. Replace its misleading optional skip with
  saved-exit rather than weakening final validation. Classification defer may
  retain an existing server default, but must not claim the step submitted.
  Persist an explicit deferral reason in the domain draft if exposed; do not
  cast tenant step names to the unrelated generic wizard API.
- Retain `JobStatusPanel` for actual domain jobs; no AO spinner/job promise
  without a real job result. Remove only its unreachable tenant AO hookup,
  with tests, when the consolidated shell is delivered.

## 7. Six mock states and acceptance mapping

The required designs are `docs/design/onboarding-mock/app.js:57-86` and its
screen inventory. These are six states, not six duplicate page engines.

| Mock state | Production route/state and real data | Required actions and assertions |
|---|---|---|
| `o-details` | Repurposed `/organizations/new?draft={id}` in provider workspace; draft plus authorized organization list/detail | Same identity/legal/contact grouping. Existing selection by authorized ID; no name-based reuse. Save partial data explicitly; primary contact creates no Person or membership. |
| `o-admin` | Same page, administrator step; live summary for existing target; `AdministratorInputs` + directory picker | Existing active admin reuse, explicitly different identity, or defer. A newly proposed Person is not labeled an existing member. Existing-admin conflict routes to proper management, never force initial enrollment. |
| `directory-offline` | Same retained draft, directory error substate | Distinguish no connection, unconfigured, consent, timeout, throttled, no matches. Existing authorized identity/manual-unverified/defer choices; retain values and show no invitation. |
| `o-review` | Same draft review state plus latest live facts | Enumerate create versus reuse, new Person versus existing, membership/role writes versus no writes/deferred. Explicit confirmation, stale-revision handling and immutable creation key; no synthetic count/identity or success. |
| `o-ready` | Existing `/organizations/{id}/provisioning?key=...` or organization detail; exact operation + live summary | Independent Tenant created, activation, membership, admin and pending tasks. Provider-only actor sees provider handoff; ordinary authorized user may navigate through the workspace boundary. No generic unconditional “Start system” link. |
| `o-repair` | Existing provisioning route plus explicit historical operation ID and live summary | Current admin facts beside interrupted attempt. Same-identity current success read-reconciles with no grants. Other identity remains unresolved. No valid admin → explicit missing-stage retry; unavailable read is not successful recovery. |

Use guided-setup header with Save & finish later, numbered steps, breadcrumb,
panels/support guidance, status/choice cards and Back/primary footer at 1440px,
390px and intermediate widths. Use production theme/assets. Do not ship mock
screen selector, sample-only confirmation, sample counts or sessionStorage
“saved” simulation. Side-by-side review, keyboard/focus/error announcements and
real action tests are required; screenshot resemblance is insufficient.
Any permission-driven deviation from the pictured action must be documented
for user acceptance, not replaced by the old wizard without explanation.

## 8. Persistence disposition and migration

| Existing persisted record | Owner/callers | Migration/retention |
|---|---|---|
| `Tenant` profile, lifecycle, onboarding, timezone, rowversion | Tenant service, resolver, catalog/detail | Preserve IDs/values/status. Add nullable draft/first-subgroup fields and numeric draft revision; never infer Active from completed enrollment or an audit marker. |
| `Organizations` first subgroup | Tenant `Org.Profile`, profile consumers | Preserve rows. Initialize explicit first ID to the same tenant's lowest-ID row (current rule), without renaming/deleting any duplicate profiles; surface ambiguity for review. No inference by display name. |
| `AuditLogs` `TenantOnboarding.*` | Tenant progress/service | Preserve all historical rows and step strings. New draft events excluded from completed-step calculation. |
| `OrganizationProvisioningOperations` | Workspace create/read/bind/failure/projection | Preserve key, hash, original intent, binding, IDs, revision and failure history. Add response revision, not a second operation store. No backfill grants or rewritten hashes. |
| New `OrganizationOnboardingDraft` | Existing workspace operations service only | Private `[ProviderScoped]` + immutable server ProviderId, private query filter and provider-qualified authorized reads/writes. No global-reference or public catalog projection. |
| `OrganizationNameReservations` | Canonical create, startup backfill | Retain unique normalized-name protection and existing deterministic backfill. Name reservations do not resolve authorized reuse. |
| `Person`, `OrganizationMembership`, `OrganizationRoleAssignment` | Person/membership/role services, resolver | Preserve scoped identities, compound membership uniqueness, audits, removed/revoked history and initial-admin constraints. No schema change to identity/grant engine. |
| Directory configuration | `EntraDirectoryService`, server options | Keep server-owned destinations/credentials. Draft stores selected connection ID only, never secrets/tokens or claimed directory verification. |

Current operation mapping declares globally unique key and Revision concurrency
in `AtoCopilotContext.cs:3469-3475`. SQLite additive columns and create table are
in `WorkspaceOperationsSchemaAdditions.cs:244-262,355-363`; SQL Server equivalents
are at `486-514`. Startup calls name backfill and separate abandoned
**capability** setup cleanup (`:30-67`); the latter must not be repurposed to
delete organization drafts or historical organization operations.

Implement additive organization draft schema in that existing schema module
and model/context mapping. Add the five tenant columns in
`Core/Data/Migrations/EnsureSchemaAdditions/TenantsAndOrganizationsSchemaAdditions.cs`
with fresh-create definitions **and** guarded upgrades: SQL Server
`COL_LENGTH` checks; SQLite `PRAGMA table_info` checks before `ALTER TABLE`.
Its current code only creates missing tables (`:21-64,68-130,185-238`); modifying
CREATE alone would not upgrade existing installations. `Mcp/Program.cs:1326-1341`
calls this module before `WorkspaceOperationsSchemaAdditions`, so tenant
additions/backfill are available before organization draft schema and readers.
Preserve that ordering. `OnboardingFirstOrganizationId` is a nullable reference
validated against `(Organization.Id, TenantId)` by the service; no cascade delete
or invented cross-tenant foreign-key match. Dual-provider types:
GUID→TEXT/UNIQUEIDENTIFIER, JSON→TEXT/NVARCHAR(MAX), revision→INTEGER/BIGINT,
timestamps→TEXT/DATETIMEOFFSET. Add unique `(ProviderId,CreationKey)` and
`(ProviderId,State,UpdatedAt,Id)` indexes, and a non-cascading ProviderId foreign
key to `CspProfiles.Id`. Add `[ProviderScoped]` mapping to the existing context;
verify the model's final query filter and scope self-check after all model
configuration runs. No mandatory backfill of draft rows: historical confirmed operations
remain resumable directly. Show unsaved former browser-only edits as unavailable;
do not fabricate them from contact or audit history.

Migration sequence: deploy additive nullable schema → old/new server compatible
reads → new explicit-save UI → legacy route adapters. Repeated startup must be
idempotent. Existing active tenants remain active with no compulsory rerun;
partial/legacy skipped data remains incomplete when genuinely incomplete.
Rollback is application rollback with added data retained, not a destructive
schema down migration. Do not retire old write routes in this increment.

Complexity justification for review: a small domain-owned pre-creation draft
table is necessary because no Tenant exists yet; a tenant-owned JSON draft is
necessary to keep partial edits out of authoritative applied fields. Rejected:
generic onboarding engine, nullable “Completed” provisioning tenants, browser
storage as server save, and silently overwriting submitted profile columns.
Carry this justification into the feature plan before implementation.

## 9. Named test and local acceptance plan — not executed

Write failing tests first with AAA markers in these **existing** files; then
minimal implementation. No test listed here is claimed passing in this session.

| Existing test file | Required retained/additional cases |
|---|---|
| `tests/Ato.Copilot.Tests.Integration/Tenancy/OrganizationCreationFlowTests.cs` and `.Recovery.cs` | Retain current deferred/read-only recovery, original hashes, same Person idempotency, independent failure, no system writes, provider/support denial. Add draft save/no domain writes; confirmed response loss; exact link reuse; same live admin/stale error versus different requested admin; no-operation existing admin; revoked membership; reference deleted; malformed binding correction; no reads grant. |
| `tests/Ato.Copilot.Tests.Unit/Workspaces/WorkspaceOperationsServiceTests.cs` | Current live projection with stored flags/errors, new summary scoped join, target mismatch, immutable intent, expected revision and failure revision; draft incomplete/invalid/oversize, dual-key races, explicit deferred facts. |
| `tests/Ato.Copilot.Tests.Unit/Workspaces/WorkspaceOperationsContractTests.cs` | Additive legacy DTO serialization and absent revision compatibility; new exact DTO/enum/error shapes; prohibit changing old creation hash serialization. |
| `tests/Ato.Copilot.Tests.Unit/Data/WorkspaceOperationsSchemaTests.cs` | Old DB upgrade, fresh schema, repeat startup, provider-key draft uniqueness/concurrency, ProviderId FK/restrict deletion, `[ProviderScoped]` model/self-check and private filter, retained legacy operations and normalized names, no cleanup of organization drafts. |
| `tests/Ato.Copilot.Tests.Unit/Tenancy/TenantScopedQueryGuardGlobalReferenceTests.cs` | New draft table is not exempt as global-reference; missing-context HTTP query rejects, ordinary organization/support generic EF reads return no rows, ordinary provider reads remain privately scoped by service. Existing background allowance is documented, not asserted as an HTTP privacy bypass. |
| `tests/Ato.Copilot.Tests.Unit/Data/EnsureSchemaAdditionsAsyncTests.cs` | Tenant draft columns on old/fresh SQLite schema, repeated application and real startup ordering; schema errors still fail startup rather than bypassing incomplete initialization. |
| `tests/Ato.Copilot.Tests.Integration/Data/OrganizationProvisioningSqlServerTests.cs` | Existing serializable/audit/lost-commit tests plus save-vs-confirm, concurrent creates/reuse/bind, stale new versus legacy clients, tenant first-subgroup race and rollback. Its `Skip.IfNot(fixture.Available)` is not a passing SQL acceptance result. |
| `tests/Ato.Copilot.Tests.Integration/Tenancy/WorkspaceOperationsAuthorizationTests.cs` | All new draft/summary paths ordinary CSP-only, provider-ID spoof/foreign-provider rejection, denied support/ordinary tenant/foreign targets, absent ambient-context failure, no public reference projection, existing current-operation ordering, same-key replay, nonmember handoff; page bounds/order/total and admin outside first page still influences availability/conflict. |
| `tests/Ato.Copilot.Tests.Integration/Tenancy/WorkspaceMembershipTests.cs` | Preserve compound identity, local Person, explicit grants, revocation, last-admin protection. Add guest resource-pair versus home/resource mismatch and actual token-claim-shaped fixtures; these synthetic cases are not real Entra sign-in evidence. |
| `tests/Ato.Copilot.Tests.Unit/Directory/EntraDirectoryTests.cs`, `tests/Ato.Copilot.Tests.Integration/Tenancy/EntraDirectoryHttpTests.cs` | Existing cloud/escaping/top20/consent/authority cases; no/ambiguous results, stale identity selection, cancellation, malformed IDs, wrong actor directory, no invitations/grants. |
| `tests/Ato.Copilot.Tests.Integration/Tenancy/TenantOnboardingWizardTests.cs` | Add real workspace pipeline scenario without fixture bypass reproducing the POST policy problem before changing metadata. Test all six hydrated slices, partial save not activation, legacy POST/new draft conflicts, ID-based subgroup reuse, both deployment modes, support/member denial, admin ordinary success, final cache invalidation. |
| `Dash/__tests__/workspaces/OrganizationSetupFlow.test.tsx` | All six organization mock states; create/reuse/defer/manual choices, explicit Save/failed save/refresh, same/different admin, interrupted read-only recovery, denied customer handoff. Extend with isolated TenantWizard component cases for every step remount/hydration/navigation/active reentry until a focused suite is approved. |
| `Dash/__tests__/workspaces/EntraUserPicker.test.tsx` | All directory error branches, multiple explicit matches, no auto-selection/grant, stale search ignored, manual selection remains unverified. |
| `Dash/__tests__/api/organizationSetup.test.ts` | New DTO routes and revision, original key preservation, expected 404 versus transport/auth failures, no GET writes. |
| `Dash/features/workspace-operations/OrganizationPages.test.tsx`, `Dash/__tests__/workspaces/WorkspaceOperations.test.tsx` | Live versus historical summary, count unavailable, exact-operation links, existing setup-success regressions, handoff availability. |
| `Dash/__tests__/workspaces/WorkspaceBoundary.test.tsx`, `Dash/__tests__/routing/WorkspaceResolvers.test.tsx` | Existing deep links, tab scope, old keys/routes, ordinary versus support, delayed responses, organization activation gate. |

User-local acceptance, after implementation approval:

1. Use deterministic synthetic provider-only admin, organization admin, ordinary
   member and foreign-directory fixtures, never real PII. Save a partial
   organization draft, refresh/restart browser, reopen same ID; show no Tenant,
   Person or grant before confirmation.
2. Confirm once, interrupt enrollment response, read/recover the same operation.
   Prove retained Person/membership are not duplicated; compare same-current-admin
   success with a different requested administrator still needing review.
3. Exercise all directory fallback choices. Real guest sign-in is a separate
   consented environment test; record emitted identity pair and access outcome
   without logging tokens. No claim of invitation or redemption coverage.
4. Sign in ordinarily as enrolled org admin, hydrate all tenant values, change
   each slice, navigate away/back, explicitly save and refresh. Two tabs must
   conflict rather than overwrite; invalid timezone/classification must not
   silently become a default. Activate only with applied required facts.
5. Provider-only actor must remain in provider handoff; ordinary member cannot
   administer or activate; active tenant reentry and SingleTenant compatibility
   must be demonstrated. Test every touched screen at 1440/390/intermediate
   widths and keyboard-only, then let the user review locally before acceptance.

Phase 0 stop condition: this contract is available for review with exact
source-backed dispositions and proposed routes/schema. Full HTTP reproduction,
test execution, live directory/guest validation, schema deployment, UI rendering
and manual acceptance remain **not performed**.

## 10. Authorized local implementation checkpoint

Following explicit authorization on September 30, Phase 3 source changes were
made without commits, deployment or external writes. This supersedes the Phase 0
“not performed” statement only for the checks listed here:

- Frontend failing-first run: four intended failures, 25 retained passing cases
  (missing saved-exit actions and lost tenant field/classification hydration).
- Parent-owned backend RED: five HTTP cases, four intended failures for absent
  draft/summary routes and hydration fields; ordinary member denial retained.
  Sequential host logs also showed the ordinary-admin step POST returning 403.
- Current local frontend regression: **153 tests passing across seven files**;
  strict `npx tsc -b --pretty false` passes.
- No .NET command was run by this subagent. New backend source, private-filter
  tests and additive migration tests await the parent's serialized GREEN pass.
  No real Graph guest sign-in, SQL Server validation, deployment or local user
  visual acceptance is asserted.
- Browser follow-up: **16 Chromium scenarios pass** across the new
  `e2e/tests/onboarding-organization-consolidation.spec.ts` and retained
  `organization-setup-flow.spec.ts`. These use `installWorkspaceFixture` plus
  explicit canonical API response fixtures against the real local SPA at
  `http://127.0.0.1:5179`. All six organization mock states were captured at
  1440px, 900px and 390px; tenant save/reload was captured at 1440px and 390px.
  Checks include lost-response reconciliation, forbidden summary, no provider-only
  customer handoff, and retained partial enrollment. Screenshots reside under
  `src/Ato.Copilot.Dashboard/test-results/organization-onboarding/`.
  Desktop details and mobile recovery captures were inspected. This is
  **synthetic UI evidence, not live backend authentication/cloud evidence**.
  The existing HTML report was not used or overwritten (`--reporter=list`).

Concrete integration points are
`OrganizationOnboardingModelConfiguration.Configure(modelBuilder)` before
query-filter installation;
`OrganizationOnboardingSchemaAdditions.ApplyAsync(db,logger,ct)` immediately
after the tenancy schema module and before any Tenant-reading backfill;
and `WorkspaceOperationsEndpoints.MapOrganizationOnboardingEndpoints(app)`.
Existing service DI remains sufficient; new entities use `Set<T>()`.

Implementation refinements to the proposed wire/storage details:

- Draft actor attribution uses canonical `directory/object` strings, not separate
  columns. `ValuesJson` becomes immutable at confirmation and is the retained
  confirmed intent, with `ConfirmedIntentHash` and `ConfirmedRevision`;
  no duplicate confirmed JSON column. `UpdatedAtTicks` supports database-ordered,
  bounded SQLite/SQL Server pagination without client-loading all drafts.
- `CreationKey` stays immutable. Optional `ProvisioningKey` retains an older
  reused operation's key without violating the provider/draft uniqueness index.
  `resumeUrl` carries that canonical key and exact operation ID.
- `OrganizationSetupSummary.reconciliation` is top-level alongside
  `requestedOperation`, so live access can be described even without an
  operation. The administrator collection is a bounded `PagedResult`.
- `TenantOnboardingProgress` includes additive `submittedValues`, `draft`,
  `draftRevision` and `missingRequiredFields`. Domain policy is enforced by the
  tenant endpoint filter before any workspace write, with the coarse endpoint
  marker only admitting requests to that policy. No identity grant is created.
- All organization and tenant states use the shared `SetupFrame`; existing
  canonical creation/recovery APIs remain supported. Pre-confirmation
  Save & finish later uses the private draft; confirmation of a retained draft
  uses the draft confirm endpoint, while an unsaved new creation retains the
  existing immutable-key canonical create command. Both converge on the same
  provisioning service and explicit membership stages.
- Saved exit goes to the parent's `/setup/resume` workspace route rather than
  the legacy `/onboarding` wizard. Tenant activation's guard allows only that
  setup entry/resume surface in addition to its own route; server activation
  gates remain unchanged. An existing live Administrator with no historical
  enrollment operation gets a ready-access projection, not a manufactured
  enrollment request. Existing-organization confirmation opens this projection.
- New client exports live in
  `Dash/features/workspace-operations/organizationOnboardingApi.ts`:
  `OrganizationOnboardingDraft`, `OrganizationDraftPage`,
  `listOrganizationDrafts(page,pageSize,signal)`, `getOrganizationDraft`,
  `saveOrganizationDraft`, `confirmOrganizationDraft`, and
  `getOrganizationSetupSummary`.

The remaining backend validation selectors include
`FullyQualifiedName~OrganizationCreationFlowTests`,
`FullyQualifiedName~TenantOnboardingWizardTests`,
`FullyQualifiedName~WorkspaceOperationsSchemaTests`, and
`FullyQualifiedName~TenantScopedQueryGuardGlobalReferenceTests`.
Do not call the complete journey verified until those gates and local user
review have been completed.

## 11. Retirement inventory and compatibility boundary

The post-replacement caller trace searches Dashboard source/e2e and extension
TypeScript, and server/tests for the exact exported symbols. This is a repository
caller audit, not a statement about unknown external library consumers.

| Surface | Caller evidence and disposition |
|---|---|
| Old TenantWizard inline full-screen shell, local per-step state, tenant skip/AO-job adapters | Replaced by shared `SetupFrame` plus tenant-owned controlled context. The old code is removed from `TenantWizard/index.tsx`; seven existing step components remain live imports and are retained as controlled domain fields, not a second editor. |
| `OrganizationSetupPresentation.SetupSteps` | Removed the helper and its unused Check icon import: no caller remains after Add/provision pages use `SetupFrame`. |
| `features/onboarding/components/SkipStepModal` and `JobStatusPanel` | Deleted both unused UI files after confirming no source, e2e or extension imports. Generic onboarding/job services, retained jobs, and the separately live `BackgroundJobProgress` component are untouched. |
| `OrgsTable.CreateOrgModal`, `createOpen` and its submission/form state | Deleted the duplicate modal/editor. Its sole callsite now navigates to canonical `/workspaces/csp/organizations/new`. Unrelated status/lifecycle rendering and tests remain. No current application importer of OrgsTable was found, but retaining its domain-distinct status surface is not retaining a second onboarding engine. |
| `csp-dashboard/api.createCspDashboardTenant` and its private request/response interfaces | Removed the unreferenced client writer and DTOs. Its only runtime caller was the retired modal; its request omitted the canonical idempotency header. Actual HTTP `POST /api/csp/dashboard/tenants` and canonical `workspace-operations/api.createOrganization` remain. |
| `ICspDashboardService.CreateTenantAsync` / `CspDashboardService.CreateTenantAsync` | Removed the unused duplicate writer after verifying no production/test callers. The HTTP endpoint already calls `IWorkspaceOperationsService.CreateOrganizationAsync`; dashboard read service and supported route remain. |
| `POST /api/tenants` and `ITenantProvisioningService` | Retain: `TenantsEndpoints.CreateTenantAsync` still calls directory-aware administrative preprovisioning. It is not the onboarding draft editor and is a distinct compatibility/bootstrap contract. |
| AddOrganizationPage, OrganizationProvisioningPage, AdministratorInputs, SetupInfo, SetupSummary, SetupField, EntraUserPicker | Retained live callers through WorkspaceOperationsPage and its two routes. New/legacy creation, keyed recovery and bound-person reconciliation still converge on the existing canonical commands. |
| General onboarding engine, tenant activation service, membership/role services | Retain distinct responsibilities and required legacy API/deep links. No historical receipts, memberships, role assignments, audit records or authorization records are removed. |

The legacy-create navigation regression failed first (zero canonical navigation
calls, ten status tests passing), then passed after deleting its editor.
Obsolete modal-client mocks were removed with the retired writer; status-action
tests remain. Strict types and **153 focused tests** pass after retirement.
An exact-symbol post-deletion source/e2e/extensions scan finds no remaining
references to the removed UI symbols. The system contract's earlier reference
to TenantWizard calling SkipStepModal/JobStatusPanel is superseded by this
verified post-replacement trace and has been communicated to its owner.

### Backend GREEN follow-up findings

The parent's first working combined run passed 43/46 tests. Three failures are
being corrected without relaxing assertions or authorization:

- Same-key concurrent create could read “no operation,” then observe the other
  request's committed Tenant name and wrongly return name-conflict 422.
  Recheck the **same key and exact intent hash** when the name precheck detects
  a collision; different keys remain conflicts. Existing uniqueness guards stay.
- Live administrator summary applied `Distinct().CountAsync()` to a positional
  record projection. A direct service diagnostic reproduced an EF relational
  query-shaper exception at that Count, before SQL execution. Use a translatable
  member-initialized row projection and page in SQL before DTO construction.
  The join contains unique membership/assignment IDs, so removing redundant
  Distinct does not collapse or duplicate different records.
- The historical tenant wizard test's default fixture identity contains only
  `NameIdentifier=multi-tenant-test-user`, no Administrator claim or explicit
  membership. Replace that inaccurate admin fixture assumption with the real
  workspace test host and an explicitly seeded tenant-local Person, membership,
  and Administrator role. Keep production administrator checks intact.

All three corrections are now implemented and validated in the explicitly
granted serialized .NET slot:

- 25 organization/tenant integration tests pass, including the formerly failing
  concurrent create, live summary and all seven activation steps under real
  workspace resolution without test-context bypass.
- 11 private query-guard/additive schema tests pass.
- 55 retained workspace operation service/contract tests pass.
- The parent's original combined selectors now pass **46/46** integration tests
  (organization/tenant/provider/system setup), with zero skipped tests.

The summary failure was reproduced directly to obtain its EF exception stack;
that temporary diagnostic call was removed after correcting the query. Creation
test diagnostics retain response bodies on failure, without weakening its
same-key success assertion. Existing build warnings elsewhere in the repository
were emitted; no warning suppression or auth/fixture bypass was introduced.
This supersedes the earlier checkpoint's “no .NET run” statement only for this
authorized validation slot; no live deployment, real guest Graph login, or SQL
Server claim is made.

Coverage expansion found an additional UI-only review defect: draft-versus-
submitted comparison used insertion-order JSON serialization, so semantically
identical profile properties arriving in another order could disable activation.
The failing active-reentry/activation tests now require canonical key ordering
before comparison; this does not relax the server's final draft check.

The parent subsequently reproduced a lost review step at 900px. Its reported
Playwright output was no longer present when this agent attempted to read it,
so a deterministic component regression was added instead: after initial draft
hydration, edit the organization and reach Review, refresh the workspace
navigation context with the same provider identity, then release a delayed old
draft response. This failed by replacing Review with the saved Details step.
The hydration effect depended on the navigation callback identity, which changes
when the workspace context object refreshes. Hydration must be owned by the
scalar draft identity, not that callback; the locally saved URL transition must
also retain ownership after the write's pending flag clears. Cancellation and
exact draft-response checks remain required on actual draft identity changes.

Review follow-up identifies two additional concurrency obligations. Existing-
organization confirmation must increment the reused operation revision whenever
its unbound administrator intent changes, with updated timestamp and actor audit;
an old-tab enrollment PATCH must then fail `STALE_REVISION` without any grant.
The HTTP regression is queued for the .NET slot owner before backend changes.
Separately, a deterministic frontend test failed because “Check saved draft”
installed another tab's newer revision while retaining stale full-form values.
Conflict reconciliation must keep the old revision/edits unwritable until the
user explicitly chooses the saved server version, hydrating those values and
revision together. Only a confirmed same-request uncertain save may reconcile
without that explicit replacement; its request identity, revision and canonical
values must match.

The parent captured the reused-operation revision regression failing with
revision 0 after confirmation. The production correction now compares the stored
unbound intent JSON with the confirmed normalized intent. Every actual change
updates that operation's revision and timestamp and stages a target-tenant,
actor-attributed `OrganizationOnboarding.IntentUpdated` audit in the confirmation
transaction. The audit includes only draft/operation IDs and revision, not raw
identity/contact JSON. Same-intent confirmation/replay changes neither operation
revision nor this audit. The regression now also verifies a single attributed
audit and exact replay revision. Source is ready for the parent's combined
GREEN run; this paragraph does not claim that pending test run passed.
