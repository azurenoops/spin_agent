# Provider onboarding consolidation — Phase 0 contract amendment

**Status:** Phase 0 evidence retained; approved local implementation in progress
(see §10 for implementation/verification boundaries).
**Evidence baseline:** branch `078-onboarding-consolidation`, commit `052120a1`,
inspected September 30, 2026. The initial Phase 0 assignment performed only
investigation/documentation, without application changes or test execution.
The user subsequently authorized local application implementation; the
source-evidence sections below remain labeled against their original baseline.
No deployment, commit or external write is authorized by this document.

The approved session plan, sections 1, 6, 8 and Phase 2, selects the
[onboarding mock](../../../docs/design/onboarding-mock/index.html), explicitly
allows **unassociated canonical receipts**, and requires explicit server-side
**Save & finish later**, not continuous autosave. This document supplements
[package-imports.md](package-imports.md) and
[provider-authorizations.md](provider-authorizations.md); their existing review,
source provenance, publication and compatibility requirements remain binding.
Organization creation/enrollment and system intake are outside this amendment.

## 1. Evidence and current execution trace

Paths below are repository-relative. Line anchors identify the inspected
baseline, not a guarantee that later edits retain line numbers. “Current” means
verified by sequential source/test inspection, **not runtime reproduction**.

### 1.1 Provider entry, identity, support, classification and completion

| Stage | Verified implementation and evidence |
|---|---|
| Entry | `src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx:124` mounts `CspWizard` under `RequireAuth` at `/onboarding/csp`. `ApplicationFrame.tsx:25-26` installs `CspOnboardingGuard` for the provider context. `features/csp-onboarding/CspOnboardingGuard.tsx:27-75` probes once on mount; inactive profiles redirect, unavailable responses render children. That client behavior is not server authority. |
| Current screen sequence | `features/csp-onboarding/CspWizard.tsx:38-169`: Identity → SupportContact → Classification → optional AtoDocuments → Review. AtoDocuments is **UI-only**; server `Review` implies that UI step is checked off even without a source choice. This is not the approved seven-screen mock journey. |
| State read | `features/csp-onboarding/api.ts:25-72,128-153` defines the legacy DTO and `GET /api/csp/onboarding/state`. `src/Ato.Copilot.Mcp/Endpoints/Csp/CspOnboardingEndpoints.cs:35-76,246-277` returns profile slices only after their completion timestamps. No partial unsaved-field draft, source deferral, security contact or upload-attempt identity is in that DTO. |
| Identity | `CspWizard.tsx:228-243` → `postCspOnboardingIdentity` → `POST /identity` (`CspOnboardingEndpoints.cs:78-111`) → `ICspProfileService.UpdateIdentityAsync` → `src/Ato.Copilot.Core/Services/Tenancy/CspProfileService.cs:119-138`. Required legal entity 2–256 and display name 1–64; optional absolute logo URL. Saves the profile slice and first completion time. |
| Support | `CspWizard.tsx:245-260` → `POST /support` → `UpdateSupportAsync` (`CspProfileService.cs:140-158`): required email 3–254 containing `@`, optional phone ≤40. There is no named service/security contact in this profile contract. |
| Classification | `CspWizard.tsx:262-279` → `POST /classification` → enum parse → `UpdateClassificationAsync` (`CspOnboardingEndpoints.cs:147-181`; `CspProfileService.cs:160-172`). Only a defined `ClassificationLevel` is required. No trusted upload-handling policy is consulted on this path. |
| Canonical model | `src/Ato.Copilot.Core/Models/Tenancy/CspProfile.cs:19-104`: globally readable singleton profile, identity/support fields, `DefaultClassificationFloor`, `Pending/InWizard/Active`, completion/audit timestamps, rowversion. It is **not** a safe place to expose private draft contacts or upload recovery keys. |
| Resume | `CspProfileService.ComputeCurrentStep` (`:70-83`) derives the next step from completion timestamps; `CspWizard.toWizardStep` resumes server Review/Complete at Review. `EnsureCreatedAsync` (`:85-117`) creates provisional `Pending` identity text and `InWizard`, not an authorized offering/boundary. Server-saved profile slices survive restart; browser field edits do not thereby become durable. |
| Submit | `CspWizard.tsx:281-299` → `POST /submit` → `CspProfileService.SubmitAsync` (`:174-208`). It requires three completion timestamps, sets Active/completion/audit times and invalidates the 30-second profile cache. It does **not** require an offering, receipt, source review, approval or publication. A second legacy submit returns `409 CSP_ALREADY_ONBOARDED`. |
| Post-submit/reentry | The wizard refreshes state and navigates to `/workspaces/csp/authorizations`. Active entry redirects unless `?reentry=...` is present (`CspWizard.tsx:171-214`). Reentry does not enable editing: the same profile service rejects writes after activation (`CspProfileService.ApplyAsync:211-258`). Do not advertise reentry as an existing post-activation edit contract. |

`IdentityStep`, `SupportContactStep`, `ClassificationStep` and `ReviewStep` are
reusable field/validation sources, not permission to retain the old layout.
The current classification copy is internally different: the wizard calls it
the “highest classification … authorized for,” while
`steps/ClassificationStep.tsx:12-64` describes a **minimum tenant floor**.
Neither wording establishes a deployment upload ceiling. This inspection does
not verify the floor's purported enforcement in tenant onboarding; that domain
was deliberately not traced.

Other direct consumers of the legacy state read are
`components/layout/useCspBranding.ts:27-66` (Active display name/logo) and the
provider-probe branch of `features/onboarding/OnboardingGate.tsx:49-69`
(ordinary-provider suppression). Preserve their state shape; their surrounding
organization workflow is not part of this trace. DI at `Program.cs:536-541`
binds `ICspProfileService` to **Core.Services.Tenancy.CspProfileService**.
The same-named `Mcp.Services.CspProfileService` is a JSON inheritance-profile
loader, not the onboarding singleton service; do not merge these concepts.

### 1.2 Source selection → retained receipt → association

1. **Current wizard path.** `steps/AtoDocumentsStep.tsx:15-40` embeds
   `OfferingIntake` and `PackageReceipts`. The wrapper blocks Continue/Back
   while files are selected or the nested operation is pending. The wizard
   disables sidebar navigation with `sourcePending`. No persisted “deferred”
   choice is written by Continue.
2. **Associated intake.** `features/provider-authorizations/OfferingIntake.tsx`
   exports `OfferingCreate`, `OfferingPicker`, `BoundaryEditor`,
   `PersistedReceipt` and `OfferingIntake`. At `:104-179`, the intake requires
   offering, exact boundary and package name before rendering `PackageUpload`.
   It can create an offering and immutable boundary first. An empty resource
   list is explicitly not universal coverage. A boundary name and actual scope
   statement are required; inventing either to bypass this UI is prohibited.
3. **Associated API.** `features/provider-authorizations/api.ts:64-91` submits
   multipart `name`, `boundaryRevisionId`, `expectedOfferingRevision`,
   optional series/predecessor and repeated files, with `Idempotency-Key`.
   `ProviderAuthorizationEndpoints.cs:70-83,137-165` maps
   `POST /api/csp/offerings/{id}/package-versions`, limits 1–1000 files/50 MiB,
   returns 202 `{package, packageVersion}` and package `Location`.
   `ProviderAuthorizationService.Packages.cs:13-23` delegates to
   `ICspPackageService.ReceiveForOfferingAsync`, then reads the exact version.
4. **Already-supported unassociated API.**
   `CspPackageImportEndpoints.cs:65-105` has two ingress adapters:
   `/api/csp/onboarding/atos/upload` before activation, and
   `/api/csp/inherited-components/import` requiring Active. Both call
   `CspPackageService.ReceiveAsync` with **no offering context**. The onboarding
   route rejects an already Active profile; the active route rejects an
   incomplete profile. `Prefer: respond-async` returns durable 202/Location;
   legacy callers without it process synchronously and receive a tally, still
   without publication.
5. **Current callers of unassociated ingress.**
   `features/package-imports/api.ts:57-71` exposes `receivePackage(files,key,
   onboarding=false)` and validates receipt/operation IDs.
   `features/csp-onboarding/api.ts` retains `postCspOnboardingAtosUpload`,
   but the current AtoDocumentsStep instead uses associated OfferingIntake.
   `features/provider-authorizations/FileFirstImport.tsx:13-31` uses the
   **active** unassociated ingress, then navigates to an import URL with
   `packageId`. Even its selected-offering variant explicitly says the upload
   does not associate the package.
   The legacy `GET /api/csp/onboarding/atos/state` remains mapped
   (`CspOnboardingEndpoints.cs:341-412`): it tallies non-package legacy
   components plus package originals/staged candidates. It is not a per-request
   receipt reconciliation read. Its `aiMappingAvailable` tally is not a trusted
   handling/analysis health fact. Keep compatibility but use canonical package
   statuses for the new journey.
6. **Single canonical receipt service.**
   `CspPackageService.cs:87-183` reads/bounds bytes, hashes exact package name,
   ordered file name/media type/content hashes and, for associated intake, the
   entire offering context including expected revision. The service authorizes,
   verifies the current provider, checks unique provider/key intent, stores
   original artifacts through `IFileStorageProvider`, writes entries/audit,
   and acknowledges only after persistence. Concurrent same-intent receipt
   winners are recovered; changed content conflicts. Associated receipt and
   version use a Serializable transaction. Storage is not a database
   transaction; orphan cleanup after arbitrary storage/commit failures is
   **not** proven by the race-specific cleanup at `:160-174`.
7. **Models and DTOs.** `Models/PackageImports/CspPackage.cs:6-28` retains
   provider, nullable association triple, key/hash, processing/publication,
   human `Revision`, writer `Version`, lease/checkpoint and audit identity.
   Original/expanded entries, private candidates, exact approvals and audits
   remain in its existing child tables (`:31-102`).
   `Interfaces/PackageImports/ICspPackageService.cs:29-84` defines
   `PackageStatus`, `PackageOfferingContext`, `PackageOfferingAssociation`,
   entries, selection/decision/recovery/publication DTOs. The current
   `operationId` is the package ID (`CspPackageService.Status:229-234`), not a
   separately created onboarding operation.
8. **Explicit later association.**
   `POST /api/csp/package-imports/{packageId}/association` already exists.
   `ProviderAuthorizationContracts.cs:70-77` defines
   `AssociateProviderPackageRequest(expectedPackageRevision, offeringId,
   expectedOfferingRevision, boundaryRevisionId, seriesId?, previousVersionId?)`.
   `ProviderAuthorizationService.Packages.cs:25-44` checks package revision,
   rejects Processing, increments package revisions and invalidates unpublished
   approvals/Approved candidates. `CspPackageService.Association.cs:12-58`
   validates same-provider offering/boundary, rejects reassignment, requires the
   original manifest and current series predecessor, creates one immutable
   `ProviderPackageVersion`, updates association and offering revision, and
   invalidates offering impact. `ProviderAuthorizationStore.WriteAsync:
   151-204` supplies scoped idempotency/intent replay and transaction handling.
   No source upload or duplicate package is required.
9. **Destination callers.** `AuthorizationsPage.tsx:24-77` uses OfferingIntake
   for successors, resolves legacy package links through stored association,
   preserves query/hash, and renders `PackagePreparation` for unassociated
   receipts. `FileFirstImport.tsx:33-87` polls/reads claims, proposes editable
   scope and delegates explicit association back to OfferingIntake. Keep these
   components and paths; do not create a second receipt/review portal.

### 1.3 Exact browser durability and reconciliation limits

`PackageUpload.tsx:14-61` keeps `File[]`, upload key and sorted prepared files in
React state/refs. `uploadIdentity.ts:17-28` hashes file bytes; sorts fingerprints
of `[name,type,size,sha256]`; sends the files in that order; and returns
`source-sha256-<64 lowercase hex>`. Reselecting identical bytes/name/type/size
recomputes that **base** key after remount. No source bytes, source manifest,
attempt outcome, package name or full offering request are saved by this code
to localStorage, IndexedDB or a server setup draft.

`OfferingIntake.tsx:119,164-177` holds the full request in `useRef` and appends
offering/boundary/predecessor IDs to the base key. Two independently verified
limitations must not be described as a tested runtime failure:

* The base key is 78 characters; appending two UUIDs produces **152** characters
  (189 with predecessor). `ProviderAuthorizationHttp.Key:31-36` and
  `CspPackageService.ValidateKey:178-181` permit at most **100**. This is a
  source-proven incompatible request construction. Add a real-hashing/full-HTTP
  regression before changing code; current mocked transport acceptance is not
  that regression.
* On remount, the user must re-enter name/context. Even identical files can
  conflict if the name or offering expected revision changes, because those
  participate in the backend fingerprint but are not recoverably retained by
  the UI. A deterministic base key is not a durable full request intent.

`MutationForm` (`provider-authorizations/forms.tsx:58-100`) also keeps its UUID
and submission closure only in refs. It freezes uncertain operations within the
mounted page but does not survive a browser restart.

There is **no upload-by-request-key reconciliation endpoint in the inspected
package/offerings endpoint maps or their client APIs/interfaces**. Existing GET
by package ID, package list and `/{id}/review-state` require discovery/knowledge
of the receipt and are not an exact lost-upload-response lookup. The server
can replay an upload when the same key and entire request are resent.
`usePublicationIntent.ts:4-25` stores a *publication preview ID* in
sessionStorage; this is unrelated to upload intent and is not guaranteed to
survive closing the browser. `CspPackageService.Recovery.cs:9-46` independently
recovers saved preview and publication from the database. Reuse that authority.

### 1.4 Worker, review and publication

* `CspPackageWorker.cs:10-37` is registered at `Program.cs:517`; it polls the
  database every five seconds, constructs the trusted processor from scoped
  factory/storage/analyzer, and logs poll failures. No browser timer or
  in-memory onboarding queue owns processing.
* `CspPackageProcessor.cs:18-114` claims Received/expired Processing with a
  15-minute optimistic lease and one-minute heartbeat. It loads original
  entries, runs analysis/resume, fences writes and retains recoverable failure.
  `CspPackageProcessor.Resume.cs:10-94` verifies stored length/hash and
  checkpoint identity; previously processed legacy packages without a
  checkpoint require explicit new submission, not fictitious unfinished-only
  replay.
* `CheckpointAsync` (`CspPackageProcessor.cs:130-285`) stores expanded artifacts,
  stable candidate/entry identities, citations and explicit exclusions;
  preserves processed/excluded entries and existing reviewed candidates;
  stores generated checkpoints without bytes; invalidates unpublished
  approvals; and transitions to Received for bounded continuation,
  NeedsAttention or ReadyForReview. Zero candidates is not itself failure.
* **Worker authority limitation:** this background processor does not consume
  the requesting user's HTTP identity. Its inspected claim/input/checkpoint
  methods operate by ledger ID/lease and check byte/checkpoint integrity, not
  a trusted deployment handling policy or current provider/offering ownership
  relation. Do not claim the proposed revalidation already exists.
* `CspPackageService.Review.cs` owns candidate edits/exclusions, exact
  previews and approval. `CspPackageService.Publication.cs:15-122` requires
  Active provider, current approved selection, citations/dependencies and
  a relational transaction; materializes only selected records and invokes
  existing `WorkspaceOperationsService` working revision → preview → approve
  → release methods. Persisted publication key/result replay and
  PartiallyPublished remain authoritative. Setup never invokes those commands.
* `ProviderPublicationGuard.BindAsync:125-169` validates associated provenance
  and accepted fresh impact for affected offerings. A completely unlinked
  legacy package with no offering-linked graph can currently receive an empty
  binding; therefore **all unassociated publication is not already forbidden**.
  New onboarding-intent publication requirements below must be enforced
  explicitly without relabeling or rewriting legacy releases.

### 1.5 Current authorization and handling-limit boundary

`Program.cs:602-625` orders CAC/authentication context, tenant resolution,
compliance authorization, audit and standard endpoint authorization.
`TenantResolutionMiddleware.cs:97-104,148-168` preserves the incomplete-provider
gate with narrow onboarding/package/auth/deployment/health exceptions and
`ProviderOnboardingPreparation` metadata. At `:210-239,327-338` it derives
provider authority from authenticated `CSP.Admin` or configured group claims
and resolves directory/home context; fixture bypass is explicit test-only.

`CspOnboardingEndpoints` checks MultiTenant, `IsCspAdmin` and no impersonation;
`CspPackageImportEndpoints.ExecuteAsync:118-166` also checks authenticated
identity, mode and ordinary provider context. Offerings use
`RequireAuthorization`, `WorkspaceAuthorizedEndpoint`, narrow preparation
metadata and `ProviderAuthorizationStore` provider ownership predicates.
The metadata exemption is not itself permission. The package service repeats
ordinary-provider authorization and ownership checks for direct callers.
`ComplianceAuthorizationMiddleware.cs:269-277` allows ordinary CSP `/api/csp`
writes through its coarse workspace gate but delegates final operation scope
to the endpoint/service. `CacAuthenticationMiddleware.cs:92-170` confines
configured simulated identities to Development; simulated/mock roles are not
production identity evidence.

`features/workspaces/workspaceTransport.ts:10-59` selects ordinary CSP context
for the exact wizard route (including trailing slash), removes stale workspace
headers and cancels responses after workspace change. Keep this transport and
existing auth interceptors; never treat a selected workspace or supplied key as
authority. New endpoints require the same checks before looking up private
drafts, filenames, intent keys, receipts or protected sources.

`DeploymentOptions.cs:19-61` contains mode/default tenant/self-onboarding
settings, not a permitted-document classification policy.
`DeploymentEndpoints.cs:28-61` anonymously returns only mode/default tenant.
The inspected profile/upload/processor paths do not supply an authoritative
handling-limit read or enforcement contract. Trusted production policy and
actual authorization of a deployed environment have **not been verified**.
Do not use a name such as “Azure IL5,” `DefaultClassificationFloor`,
candidate `Classification`, or an offering's declared impact as that authority.

## 2. Named disposition inventory

| ID / surface | Disposition for implementation |
|---|---|
| P01 CspWizard route/guard | Retain route/entry protection; replace its screen composition with the approved journey, not a parallel wizard engine. |
| P02 IdentityStep + SupportContactStep | Combine reusable fields/validation under p-details; add named contacts privately; retain legacy profile APIs and completed identities. |
| P03 ClassificationStep | Remove as a standalone required visual step. Present trusted handling read-only; place legacy classification-default confirmation in secondary deployment-default details, clearly not a ceiling. Keep legacy command compatibility. |
| P04 AtoDocumentsStep | Replace mandatory OfferingIntake nesting with shared upload/receipt controls and persistent source choice; detailed boundary review remains a portal action. |
| P05 OfferingCreate/Picker/BoundaryEditor | Reuse create/select fields and canonical services. BoundaryEditor remains optional exact-context/portal work, never required to invent context at setup. |
| P06 PackageUpload/uploadIdentity | Extend with server-held exact intent and restart reconciliation; retain limits/hashing/file UI; fix oversized composed keys under regression tests. |
| P07 ReceiveAsync/ReceiveForOfferingAsync | Retain as sole receipt persistence paths. Add optional registered intent binding; no new canonical receipt table or upload worker. |
| P08 FileFirstImport/PackagePreparation | Share uploader/preparation with setup; parameterize preactivation ingress instead of copying the active-only adapter. |
| P09 Association endpoint/version | Retain exact revision/manifest/ownership/predecessor checks. Unconfirmed offering hint is not association. |
| P10 Worker/analyzer/checkpoints | Retain durable polling/leases/budgets; add durable context/handling revalidation, never a wizard-owned queue. |
| P11 Review/approval/publication | Retain exact canonical workflow and recovery; no setup-triggered review/publication. New setup receipts require explicit association before publishing. |
| P12 PackageReceipts/ReceiptCard | Reuse paged/polling status and exceptions; augment minimum review queue with draft deferral/unknown facts, not duplicated package states. |
| P13 ProviderAuthorizationOperation | Reuse offering creation/association idempotency. It requires an offering FK; do not create a dummy offering to store setup-only metadata. |
| P14 Setup draft + command persistence | Add provider-private, domain-specific persistence below. Explicit saves only; no generic workflow/task engine. |
| P15 Access/security contact | Read actual caller authority; save contact-only or explicit deferral. Discovery/role assignment is a separate authorized workflow. No self-grant/enrollment. |
| P16 First-offering metadata | Add descriptive fields that mirror mock selections; keep technical Azure scope distinct from declared service environment/impact. |
| P17 Deployment handling | New trusted configuration/read/ingress guard proposal; unknown policy blocks upload, not partial draft saving. Do not represent current UI enum as security enforcement. |
| P18 Active legacy profiles | Do not reopen first-run setup, erase receipts or backfill deferrals/contacts/authorization from missing data. |
| P19 Legacy source/review links | Preserve stored association lookup, query/hash, protected source access and receipt identity. |
| P20 Optional work | Persist offering/source/contact deferrals and unknown attempts as facts. Project named next actions from those facts and canonical receipts; do not create a task database. |

## 3. Proposed additive HTTP and DTO contract

Everything in this section is **proposed**, not an existing route or DTO.
Existing routes remain. New responses use the existing
`{status,data,metadata}` / `{status:"error",error:{errorCode,message,suggestion},
metadata}` envelopes and `Cache-Control: private, no-store`.

All setup writes take a UUID `Idempotency-Key` (36 characters), expected
integer revisions, and authenticated server actor identity. Actor/provider
ownership is never accepted from body fields. Same provider/operation/key and
normalized intent replays the immutable committed **outcome IDs/revisions**,
not a historical `ProviderSetupState` containing actor/access/action availability;
changed intent is 409. The client persists command identity in the explicit saved draft **before**
starting a consequential operation. Replaying successful commands cannot rerun
their side effects. New draft revisions never silently overwrite stale fields.
After current request authorization and outcome lookup, project current state,
actor, access and available actions afresh. Keep historical commit identity/
facts distinctly labeled; they never establish the replaying actor's authority.

### 3.1 Routes

| Method/path | Input → outcome |
|---|---|
| `GET /api/csp/onboarding/setup` | Authorized provider singleton lookup, no creation. Returns `ProviderSetupState`; `draft:null` if absent; existing Active remains Active. |
| `PUT /api/csp/onboarding/setup/draft` | `SaveProviderSetupDraft {expectedRevision:long (0 for absent), draft:ProviderSetupDraftInput}` + key. Saves partial fields/deferrals/cursor and selected-file manifest metadata atomically, not canonical profile/offerings, receipt bytes, roles or imports. 200 `ProviderSetupCommandResult` with committed revision/time and separate fresh projection. |
| `POST /api/csp/onboarding/setup/commits` | `{expectedRevision, section:"Details"|"Contacts"|"FirstOffering", expectedProfileRevision, expectedOfferingRevision?:long}` + key. Explicit p-details/access/offering Save & continue commits the saved slice using existing canonical validators/services; records committed IDs. No boundary creation by this command. Returns `ProviderSetupCommandResult`. |
| `POST /api/csp/onboarding/setup/completion` | `{expectedRevision, expectedProfileRevision, confirmed:true, acknowledgedUnresolvedIntentIds:UUID[]}` + key. Rechecks required profile facts, actual caller access and exact source disposition, calls canonical activation, stores completion snapshot. Returns `ProviderSetupCommandResult`; exact replay succeeds without a second activation and refreshes actor/access facts separately. |
| `POST /api/csp/onboarding/setup/commands/reconcile` | Read-only lookup body `{operation:"SaveDraft"|"CommitDetails"|"CommitContacts"|"CommitFirstOffering"|"Complete", requestKey}`. Returns `ProviderSetupCommandResult`; NotFound means no committed result observed, not proof a concurrent request cannot finish. Current actor/access facts are projected separately from the historical commit. |
| `POST /api/csp/package-imports/upload-intents` | `{expectedSetupRevision, intent:PackageUploadIntentInput}` + key equal to `intentId`. Metadata-only preparation; 201 `PackageUploadIntentState`. Used by explicit Upload, never continuously autosaved on field edits; atomically binds the intent to the draft and increments its revision. |
| `GET /api/csp/package-imports/upload-intents/{intentId}` | Authorized metadata/recovery state. Resolves the existing provider/key receipt; no bytes required. Never creates a package or executes a retry. |
| `POST /api/csp/package-imports/receipt-reconciliation` | Read-only private body `{requestKey, intentHash}` → `ReceiptReconciliation`. Exact provider/key lookup for registered intents and explicit legacy recovery. Avoid keys/filenames in URL/query logs. |
| `GET /api/csp/onboarding/setup/actions?page=1&pageSize=25` | `PagedResult<ProviderSetupAction>` composed from saved provider setup facts and canonical receipt/review state. No task writes and no independent completion calculator. |
| `GET /api/csp/onboarding/handling-policy` | Authorized `DeploymentHandlingPolicy` projection below; no operator secrets and no public mutation route. |

All setup routes are ordinary-provider only and 404 in SingleTenant. Reuse the
existing narrow onboarding gate allowance. Do not grant general provider
mutation access before activation. Read-only recovery remains reachable during
setup and after activation, including the same uncertain onboarding upload.

`ProviderSetupCommandResult`:

```text
outcome: Committed|NotFound
replayed: boolean
committedOutcome: {
  commandId, providerId, draftId, operation, committedAt,
  committedDraftRevision, committedProfileRevision?:long,
  committedOfferingId?:UUID, committedOfferingRevision?:long
}|null
historicalCommitSnapshot: {
  committedBy:{directoryTenantId,objectId}, factReferences,
  acknowledgedUnresolvedIntentIds
}|null
current: {
  projectionState: Available|Unavailable, evaluatedAt,
  actor:{directoryTenantId,objectId,displayName}|null,
  access: current authorized access projection|null,
  state: ProviderSetupState|null,
  error:{errorCode,message,suggestion}|null
}
```

Only `committedOutcome` and `historicalCommitSnapshot` are journaled. `current`
is a pure read projection, never persisted as replay authority. Another currently
authorized provider administrator sees their own current actor/access, not the
original submitter's. Denied/revoked authority returns 401/403 before disclosure;
no stored success bypasses that check. Mutation authorization for a **new**
execution and authorization to read an already committed outcome are separate
checks; replay does not grant permission to repeat the mutation.

If the committed outcome is known but its fresh state/action projection fails,
return 200 with `outcome:Committed`, `current.projectionState:Unavailable`,
null unavailable facts and `SETUP_PROJECTION_UNAVAILABLE` corrective guidance.
Do not substitute the old projection, label the command uncommitted, execute
it again, or require a new key. The UI shows “Saved; current status unavailable”
and retries the pure projection/reconciliation read. If the journal itself
cannot be read, return 503/unknown—not NotFound. Historical actor/facts may be
shown only under a clearly historical commit/audit heading.

### 3.2 Setup ownership and field definitions

There is one private setup aggregate per existing provider, **shared by current
ordinary provider administrators**, not per browser/device or former creator.
Creation uses the existing provisional profile creation primitive when necessary,
under a serialized singleton check; no second provider is allocated. Current
permission is checked on every read/write/replay. A revoked creator cannot
recover it; another currently authorized provider administrator can continue
the same provider-owned work, with its own actor audit.
For an already Active profile, reuse it without calling provisional creation.
Source recovery/private draft updates do not reopen onboarding. Legacy
post-activation profile edits remain rejected; this amendment does not silently
turn the new Details commit into an unrestricted profile-edit endpoint. The
UI must distinguish immutable committed profile facts from remaining private
contact/offering/source preparation.

`ProviderSetupState`:

```text
providerId: UUID|null
profile: existing CspOnboardingStateDto
profileRevision: positive integer|null
draft: {
  draftId: UUID, revision: positive integer, schemaVersion: 1,
  currentScreen: one of the seven p-* IDs, savedAt: ISO instant,
  savedBy: authorized actor display reference, fields: ProviderSetupDraftInput,
  committedOfferingId: UUID|null,
  completion: {completedAt, profileRevision, draftRevision}|null
}|null
access: {state:"Authorized"|"Denied"|"Unknown", checkedAt,
         actor:{directoryTenantId,objectId,displayName}, scope:"Provider"}
handling: DeploymentHandlingPolicy
uploadIntents: PackageUploadIntentState[]  // paged separately beyond 25
facts: SetupFact[]
```

Do not return savedBy/contact details to subscriber/global profile reads.
Unreadable projection dependencies return Unknown/503, not fabricated empty
arrays or “complete.” Lists use the existing page/pageSize limits (1–100).

`ProviderSetupDraftInput`:

* `currentScreen`: `p-details|p-access|p-offering|p-sources|p-uncertain|
  p-review|p-ready` (cursor, never proof of completion).
* `details`: nullable partial `displayName` (64), `legalEntityName` (256),
  `logoUrl` (2048), `serviceContactName` (256), `serviceContactEmail` (254),
  `supportPhone` (40), `legacyClassificationDefault`
  (`Unclassified|CUI|Secret|null`), `confirmLegacyClassificationDefault:boolean`.
  Partial save allows missing required values; commit uses canonical validation.
  Display/operator/contact map explicitly to existing display/legal/support
  fields; new contact names remain provider-private metadata.
* `securityContact`: `{choice:"Unspecified"|"ContactOnly"|"ExistingIdentity"|
  "Deferred", displayName?:string(256), email?:string(254),
  directoryTenantId?:UUID, objectId?:UUID, deferral?:Deferral}`.
  ExistingIdentity requires a verifiable compound identity from the authorized
  discovery workflow; ContactOnly is explicitly unverified and grants no access.
  Directory unavailable leaves the fact Unknown; no fabricated match or group
  membership. This amendment does not invent a provider directory/enrollment API.
* `firstOffering`: `{choice:"Unspecified"|"Existing"|"New"|"Deferred",
  offeringId?:UUID, expectedRevision?:long, name?:string(256),
  description?:string(8000), serviceDescription?:ServiceDescription,
  environments?:("AzureCloud"|"AzureUSGovernment"|"Microsoft365DoD"|"ManualService")[],
  serviceModel?:string, managementArrangement?:string,
  serviceOwner?:string(256), securityContact?:string(256), deferral?:Deferral}`.
  Existing validates same-provider identity. Deferred stores no fake offering.
* `sources`: `{choice:"Unspecified"|"Selected"|"Deferred"|"Intents",
  deferral?:Deferral, intentIds:UUID[], selection?:SelectedSourceMetadata}`.
  `SelectedSourceMetadata` has `packageName`, ordered `files` in the §4 manifest
  shape, nullable `offeringHintId` and nullable proposed exact `context`.
  It is saved atomically with the draft, remains editable before a submission
  intent is registered, and is not a receipt or frozen upload intent. No bytes
  are transmitted by Save & finish later. A registered unresolved/received
  intent cannot be removed or converted to Deferred by changing this choice.
  Selected alone does not satisfy completion: explicitly defer or proceed
  through registered intent preparation first.

`Deferral = {reason:string(1..2000), ownerRole:"CSP.Admin",
ownerActor?:{directoryTenantId,objectId}}`. Server adds stable identity,
recordedBy/time and source revision. Offered reasons such as “Source documents
will be added later” are explicit user choices, not silent defaults.

`SetupFact = {key, state:"Satisfied"|"Open"|"Deferred"|"Unknown"|
"NotApplicable", sourceKind, sourceId, sourceRevision, observedAt,
reasonCode, message}`. Offering/source optionality makes a deliberate Deferred
fact compatible with activation; absence/unknown is not Completed. Receipt,
analysis, human review, publication and setup are separate facts.

### 3.3 Offering description versus technical scope

**Effective rebase amendment, September 30, 2026, upstream `a4d43d7c`:**
the original proposal to add `serviceDescription` to canonical offering DTOs
and `ServiceDescriptionJson` to `ProviderOffering` is superseded. Canonical
create/update/response DTOs and schema retain upstream `ServiceModel`,
`ManagementArrangement`, `ServiceOwner` and `SecurityContact`, without a second
offering descriptor column or API. The onboarding-only descriptor below remains
in `ProviderSetupDraft.DraftJson.firstOffering.serviceDescription`; committing
an offering retains that draft metadata and records the canonical offering ID.

```text
environmentKind: AzureCommercial|AzureGovernment|AwsGovCloud|
                 Microsoft365DoD|Other
environmentLabel: string <=256|null      // required for Other
serviceModel: InfrastructureShared|Platform|Software|BrokeredHosting|null
managedBy: Provider|SharedOperations|MissionOwner|null
intendedUse: string <=2000|null
declaredImpactLevel: IL2|IL4|IL5|IL6|Other|null
declaredImpactText: string <=256|null     // required for Other
```

This is provider-declared descriptive metadata, not a source-reviewed decision,
technical boundary, Azure connection, workload coverage or deployment handling
authority. Do not parse IL5 out of the offering name. No extra mandatory impact
step is added: the mock primarily asks for service identity; declared impact
can remain null or appear in progressive details.

Onboarding maps descriptor models `InfrastructureShared`, `Platform`, `Software`
and `BrokeredHosting` to upstream `InfrastructureSharedServices`,
`PlatformService`, `SoftwareAsAService` and `BrokeredCloudSpace`, respectively.
`Provider` and `MissionOwner` map to `ProviderManaged` and
`MissionOwnerManaged`; `SharedOperations` is unchanged. Contradictory canonical
and declared model/management values are rejected.

Descriptor requests may retain an empty input `environments` array for AWS
GovCloud/M365/Other, but canonical persistence uses `ManualService` for
AWS GovCloud/Other and `Microsoft365DoD` for M365 DoD. These manual environments
require an explicit service model under upstream validation. Azure choices
require their exact matching technical cloud. A contradictory supplied
environment is rejected, never rewritten into a fictional Azure connection.
Requests without a descriptor use the canonical identity/environment fields
directly. Upstream scope/boundary validation is unchanged. Saving identity
creates no boundary, hosting scope, package version, connection or authorization.
No environment, impact, model or management values are inferred during migration.

## 4. Exact upload intent, restart and receipt reconciliation

### 4.1 Intent metadata, not another receipt

`PackageUploadIntentInput`:

```text
intentId: client-generated UUID            // same 36-char Idempotency-Key
schemaVersion: 1
packageName: string 1..256
entryPoint: Onboarding|ActivePortal
associationMode: Unassociated|ExactBoundary
offeringHintId: UUID|null                   // non-authoritative; may be null
context: PackageOfferingContext|null        // required only for ExactBoundary
files: ordered array[1..1000] of {
  ordinal: integer 0..999, fileName: string 1..512, mediaType: string <=256,
  byteLength: integer >=0, sha256: 64 hex
}
handlingPolicyVersion: string <=100
declaredContent: {classification:Unclassified|CUI|Secret,
                 markings:string[], containsOnlySyntheticData:boolean}
```

Total bytes ≤50 MiB; no `File`, base64, absolute path, browser path or source body
is persisted in intent metadata. Duplicate file descriptors remain distinct
ordered occurrences. Server normalizes hashes/canonical JSON and computes
`intentHash` over **all** fields except `intentId`. Identity includes name,
mode, exact expected offering revision, boundary, series/predecessor, ordered
manifest and handling declaration. Store the normalized full payload, not only
its hash. A hint is provider-owned context for later selection; it never sets
`CspPackage.OfferingId`, `PackageVersionId` or `BoundaryRevisionId`.

`PackageUploadIntentState` returns saved input/hash/revision/time plus
`receipt:PackageStatus|null`, `lastSubmission:{startedAt,finishedAt?,errorCode?}|
null`, `reconciliation:ReceiptReconciliation`, and action availability.
Receipt status is derived from `CspPackage`, **not** copied processing/review/
publication state on the intent.

Add optional multipart `uploadIntentId` to both legacy ingress routes and the
associated package-version route. With it, ingress requires the matching key,
provider ownership, stored exact context/hash, current handling permission and
file manifest match. Extend the canonical receive implementation; do not
implement a separate uploader. Do not append IDs to the key.

The accepted package stores nullable `UploadIntentId`, nullable immutable
`HandlingDeclarationJson`/`HandlingPolicyVersion`, and
`RequiresOfferingAssociation=true` for this new onboarding path. Legacy
receipts default to false/null. The upload intent itself never represents
“received” before the existing receipt transaction commits and original bytes
are retained.

### 4.2 Reconciliation algorithm and races

`ReceiptReconciliation = {outcome:"Confirmed"|"NotObserved"|"InFlight"|
"Rejected"|"Conflict"|"Unknown", observedAt, intentId?:UUID,
intentHash, receipt?:PackageStatus, errorCode?:string, nextAction:
"OpenReceipt"|"WaitAndCheck"|"ReselectSameFiles"|"CorrectRejectedIntent"|
"RestoreAccess"}`.

1. Recheck ordinary provider authorization **before** lookup; lookup is bounded
   by actual provider, not a body provider ID. A guessed key is not a bearer
   credential. Unauthorized/missing records follow existing private disclosure
   rules; do not reveal another provider's filenames or receipt existence.
2. Query the existing unique `(ProviderId,IdempotencyKey)` package first. Verify
   the persisted input binding, original manifest and association when present.
   Return Confirmed with that exact receipt even if the browser never received
   the 202. If package and intent linkage repair is needed after an interrupted
   metadata update, derive it from the exact key/content binding, never filename
   similarity. Conflicting binding returns 409, not an arbitrary winner.
3. If no package is observed and a submission holds the intent's active
   time-bounded lease, return InFlight. On expired/absent lease return
   NotObserved with ReselectSameFiles; this is **not proof of non-receipt**.
   A storage/database outage returns 503/Unknown. Persisted definite server
   rejection is Rejected only when the canonical receipt query confirms no
   committed receipt for the exact intent.
4. Across browser restart, load the server draft/intents, reconcile first, and
   show p-uncertain for unresolved submitted intent. `File` objects do not
   survive. If required, ask for re-selection and hash every file; compare the
   full stored ordered manifest before sending. Different bytes/name/media
   type/count are not the same attempt. The original name/context/revision are
   restored from the server, not re-derived from today's offering.
5. Exact retries retain the UUID and normalized intent. A delayed old request
   racing a retry is still bounded by the canonical package unique key and
   content fingerprint. Intent submission leases use optimistic revision
   fencing; no lease expiry creates a new package key. A changed payload gets
   a **new explicit attempt only after** the prior attempt is reconciled to
   confirmed or definitely rejected/cancelled-before-submit, with history kept.
6. Upload completion versus profile activation is handled explicitly: registered
   onboarding intents may be reconciled/replayed after activation. A new
   unregistered upload to the legacy onboarding endpoint still returns its
   existing 409. A previously prepared but never submitted intent requires
   explicit continuation using the same registered intent and canonical
   receipt service, not a silent replacement via a different route.
7. Save & finish later with selected files computes/saves `selection` manifest
   metadata and partial fields but sends **no bytes** and does not register a
   submission. Missing handling configuration does not prevent this partial
   save; it still prevents Upload. If saving fails, remain on screen and
   mark unsaved; do not navigate with a “saved” claim. In-page Back/Next retains
   edits in one parent draft state. Explicit Upload is a separate user-authorized
   operation that registers the exact intent before transmitting bytes.

Legacy recovery without a registered intent may query by retained key plus the
exact legacy request hash; it cannot recover an unknown key/full request that
was never persisted. Do not fabricate lost pre-upgrade browser state. Offer the
authorized receipt list and manual confirmation; uncertain changed-context
re-upload remains blocked until the old receipt/intent is established.

## 5. Deployment handling, worker scope and publication policy

### Trusted handling policy proposal

Add operator-controlled configuration `Deployment:DataHandling` with
`PolicyId`, `Version`, `EnvironmentLabel`, `ApprovalReference`,
`AllowedClassifications`, `AllowedMarkings`, `SyntheticOnly`, `ValidUntil`,
`UploadsEnabled`, and `AnalysisEnabled`. Approved values must come from the
deployment owner; **no application default grants CUI/Secret handling** and no
onboarding mutation updates it. Connection cloud/region and an impact label
are not substitutes for approval. This proposes a concrete adapter/configuration
contract; actual approval evidence and production settings remain unverified.

`DeploymentHandlingPolicy = {state:"Known"|"Unknown"|"Expired",
policyId?:string, version?:string, environmentLabel?:string,
approvalReference?:string, validUntil?:ISO instant,
allowedClassifications:ClassificationLevel[], allowedMarkings:string[],
syntheticOnly:boolean, uploadsPermitted:boolean,
analysisPermitted:boolean, checkedAt, reasonCode?:string}`.

Missing/invalid/expired policy projects Unknown/Expired with upload/analysis
denied, not Unclassified by inference. The read-only p-details panel shows this
honestly. Draft/profile work can continue; sources can be explicitly deferred.
Existing critical deployment gates remain; policy upload deferral does not
assert deployment operational readiness.

Ingress checks policy **before reading/storing source bytes**, rechecks version
at receipt commit, and audits actor, policy version, decision and receipt IDs
without source text. Declaration is not a content-classification detector;
accepted bytes mean retention allowed under the submitted declaration, not
verified absence of sensitive content. Document supported inspection limitations.
Old clients without declarations must receive explicit
`422 HANDLING_DECLARATION_REQUIRED` when the new guard is enabled; never silently
assume safe data. This is an intentional safety-gate compatibility change,
requiring rollout notice and adapter/tests, not an unnoticed breaking change.

### Worker and publication

Before source hydration and again before committing generated results, the
existing processor must validate durable provider existence, package/original
entry ownership, association consistency when present, retained manifest
integrity and current configured processing permission. Pre-activation
processing remains allowed when permitted; **Active is not a new analysis
prerequisite**. Background processing runs as the authorized service against
the committed provider receipt, not a cached browser identity. Current human
authorization is independently rechecked on all user reads/retries/reviews.
Policy revocation halts further analysis into explicit NeedsAttention with
`HANDLING_POLICY_CHANGED`/`HANDLING_POLICY_UNKNOWN`; it does not delete retained
evidence or publish partial results. Recheck associated context after long
analysis before checkpoint; preserve lease fencing.

For **new setup receipts only**, `RequiresOfferingAssociation` adds an explicit
blocker `OFFERING_ASSOCIATION_REQUIRED` at preview/approval/publication until the
existing association operation succeeds. Enforce it in the canonical service/
publication guard, not only by hiding UI. A setup hint alone cannot satisfy it.
Legacy unlinked receipts/releases retain existing compatibility and audit
history; migration must not silently apply a new authorization or association.

## 6. Persistence, concurrency and migration design

Use the existing `AtoCopilotContext`, private query filters, restrictive FKs,
SQLite/SQL Server additive schema additions and execution-strategy patterns.
Do not use global profile JSON for private data. No new canonical receipt,
source artifact, candidate, approval, release or task engine is proposed.

Mark each new private draft/command/upload-intent entity `[ProviderScoped]`
with server-assigned immutable `Guid ProviderId`, never `[GlobalReference]`.
The existing installer at `AtoCopilotContext.cs:3530-3538` applies the
`TenantFilterDisabled || TenantFilterCspAdminAll` private filter, and the
model self-check at `:4035-4051` requires that ProviderId and filter. The filter
is an ordinary-provider privacy gate, **not a particular-provider predicate**:
all operations must additionally resolve the current provider and filter
`ProviderId` explicitly, as `CspPackageService.cs:27-47` does.
Use provider-qualified draft/command/intent references and restrictive FKs;
do not inherit `ProviderOwnedRow` merely for its mapping, because it requires
an offering and these records can precede offering creation. Do not use
`IgnoreQueryFilters` or request a filter-disabled context for HTTP recovery.

| Proposed storage | Exact responsibility and constraints |
|---|---|
| `ProviderSetupDrafts` | `Id UUID PK`, `ProviderId UUID UNIQUE FK CspProfiles RESTRICT`, `SchemaVersion int=1`, `Revision bigint concurrency=1`, `DraftJson`, `CommittedMetadataJson`, nullable `CompletionSnapshotJson`, `CreatedAt/By`, `UpdatedAt/By`. Draft schema is §3.2. Committed metadata holds `{serviceContactName, securityContact, committedOfferingId, committedSectionRevisions}` using the same bounded field types; it does not copy canonical profile/receipt state. Completion snapshot holds `{completedAt, profileRevision, draftRevision, factReferences, acknowledgedUnresolvedIntentIds}`. No bytes. Provider-private query filter and explicit ProviderId predicate. |
| `ProviderSetupCommands` | `Id UUID PK`, `ProviderId`, `DraftId FK RESTRICT`, `Operation varchar(32)`, `IdempotencyKey varchar(100)`, `IntentHash char(64)`, `RequestJson`, `OutcomeJson`, `HistoricalCommitSnapshotJson`, `CommittedDraftRevision bigint`, `CreatedAt/By`; unique `(ProviderId,Operation,IdempotencyKey)`. Outcome/snapshot use §3.1, never the whole `ProviderSetupState` or current authority/action projection. Narrow transactional replay journal for draft/profile commands, not a scheduler or generic job engine. |
| `CspPackageUploadIntents` | `Id UUID PK` (the intent UUID), `ProviderId FK CspProfiles RESTRICT`, nullable provider-qualified `DraftId FK RESTRICT`, `EntryPoint varchar(32)` (legacy default Onboarding), nullable `OfferingHintId`, `IdempotencyKey varchar(100)`, `IntentHash char(64)`, `IntentJson`, `Revision bigint concurrency`, `CreatedAt/By`; unique `(ProviderId,IdempotencyKey)` and provider/entry-point/hint/ID paging index. Onboarding has a draft; ActivePortal requires Active profile and has no draft reference/side effect. No package processing/publication state. Prepared metadata persists uncertainty; receipt is joined from canonical package. See §10 for the deliberate no-independent-submission-lease implementation. |
| `CspPackages` additive columns | nullable `UploadIntentId UUID`, `HandlingPolicyVersion varchar(100)`, `HandlingDeclarationJson`; `RequiresOfferingAssociation bool NOT NULL DEFAULT false`. Unique non-null UploadIntentId; ownership-qualified link checked in the transaction. Keep existing provider/key uniqueness, revision, lease and child tables. |
| `ProviderOfferings` canonical identity | Upstream ServiceModel, ManagementArrangement, ServiceOwner and SecurityContact columns and validators; no onboarding-specific descriptor column. The original ServiceDescriptionJson proposal is superseded by §3.3. Declared metadata remains in the private setup draft. |
| `CspProfiles` additive column | `SetupRevision bigint NOT NULL DEFAULT 1`, concurrency token updated by all profile mutators, including legacy endpoints. Retain rowversion; explicit integer revision supplies portable expected-revision semantics. |

Normalize new command JSON server-side with versioned deterministic property/
enum/array rules and compute SHA-256, retaining original validated semantic
fields. File order remains significant. Case normalization is explicit only
for hashes/UUIDs; do not rename files or reinterpret media types.

Profile commits/activation must share the same DbContext/transaction as command
outcome/snapshot and draft revision changes. The fresh projection is read only
after that commit and cannot roll it back. Refactor the existing profile mutator
internals to participate in a caller-owned transaction; retain validators,
cache invalidation and audit semantics. Do not sequentially call independent
committing services and claim atomicity. First-offering creation similarly
reuses `ProviderAuthorizationStore`/service primitives and its operation
replay, with the committed ID bound into setup in the same transaction.
No second copy of offering creation rules is acceptable.

When a v1 setup exists, the canonical submit prerequisite check includes its
source disposition. Both the new completion route and legacy `/submit` must
honor that check, preventing a direct API bypass. No-source Unspecified requires
an explicit defer/add decision; prepared/submitting/unknown source work must
remain recorded and be explicitly acknowledged for later recovery. A received
package may still be Processing/NeedsAttention. Complete stores the exact fact
snapshot but future status is always read from canonical records.

On stale draft/profile/offering revision return `409 SETUP_REVISION_CONFLICT`
with current revision and reload suggestion, preserving browser edits for
explicit reconciliation. On duplicate key/different intent return
`409 SETUP_INTENT_CONFLICT`. New registered-upload manifest mismatch:
`409 UPLOAD_INTENT_MISMATCH`; stale handling version:
`409 HANDLING_POLICY_CHANGED`; forbidden handling content:
`422 HANDLING_NOT_PERMITTED`; unreadable policy/storage:
503 with stable code. Existing endpoints retain their documented status/code
mapping otherwise. Unauthorized 401/403, absent/private 404 and size-limit 413
must not be converted into success/empty data.

### Migration and rollback

1. Add a `ProviderSetupSchemaAdditions` module **after** existing profile,
   package and provider-authorization schema additions (current startup:
   `Program.cs:1333-1336`). Update EF mappings and both SQL dialects; test
   migration baseline, not just EnsureCreated. Existing examples:
   `CspPackageSchemaAdditions.cs:10-46` and
   `ProviderAuthorizationModelConfiguration.cs:14-40`.
2. Initialize SetupRevision only; do not auto-create a saved draft, deferral,
   upload intent, first offering or completion snapshot for legacy profiles.
   Active stays Active. Incomplete profiles hydrate only existing committed
   fields; absent new facts are Unknown/Unspecified.
3. Preserve package IDs/source hashes/bytes/approval/release versions and null
   associations. Additive receipt flags default false. No boundary/source
   ownership backfill from name similarity or earliest offering.
4. Singleton creation must be serialized and detect multiple existing profile
   rows; report IDs/counts for authorized repair rather than selecting/deleting
   a “winner.” Do not add a destructive singleton uniqueness repair.
5. Migrate with restartable idempotent DDL and transactional write/replay
   concurrency on both supported databases. Verify unique key races, same
   provider FKs, stale revisions and interrupted commits.
6. Rollback retains all new tables/columns/artifacts. **Do not simply deploy an
   old writer** that ignores SetupRevision, new association restrictions or
   handling policy. Disable setup writes/uploads/publication or retain the
   compatibility guards during rollback; keep receipts readable under current
   permissions. Any later data deletion needs separate retention approval.

## 7. Persistent next work and completion meaning

The minimum provider review queue is an extension of existing Authorizations/
PackageReceipts, not another canonical task system. A provider-scoped generic
task write contract has not been established by this bounded trace; organization
and system task stores were not inspected. Store the **actual deferral/unknown
facts** in the domain draft/intents and derive next-action cards from those and
canonical package states. Do not claim this is a general task assignment engine.

`ProviderSetupAction = {actionId, label, state:"Open"|"Deferred"|"Unknown"|
"Blocked"|"Satisfied", ownerRole:"CSP.Admin", ownerActor?,
reasonCode, source:{kind,id,revision}, destination:{path,label},
contribution, evaluatedAt}`.

Stable identities are `provider:{providerId}:setup:offering`,
`:setup:sources`, `:setup:security-contact`,
`upload-intent:{intentId}:reconcile`, and
`package:{packageId}:association|analysis|review|publication`.
They do not change with retry or page refresh. There is no “mark done” endpoint:
fulfillment follows the authoritative operation; deferral remains historical
and visible with its reason/owner/source revision. Unknown storage/access does
not satisfy an action.

Named destinations reuse existing routes:

* unassociated receipt: `/workspaces/csp/authorizations/import?packageId={id}`;
* associated receipt:
  `/workspaces/csp/authorizations/offerings/{offeringId}/packages/{packageId}`;
* scope: existing offering `boundary`/`inherited-coverage` sections;
* publication: existing offering `impact` plus exact package review flow;
* unresolved attempt: `/onboarding/csp?intentId={intentId}&reentry=resume`
  (proposed parameter; server receipt lookup, not URL authority);
* no offering: existing Authorizations create/select destination.

Completing provider setup means required profile facts committed, current
provider access checked, and optional work explicitly retained. It does **not**
mean trusted deployment approval, completed analysis, reviewed scope,
capability publication, customer access, inheritance, eMASS submission or an AO
decision. Unknown submitted attempts may coexist with an Active profile only
with explicit acknowledgement and a visible recovery action; p-ready must say
“Profile ready — source receipt unresolved,” never “sources not added.”
Active deployments do not re-enter first run because review/publication is open.

Provider identity/source provenance supports later service documentation and
reviewed reusable implementation records. This amendment does not assert a
verified downstream SSP/eMASS export connection; the later source → reviewed
artifact acceptance remains required.

## 8. Seven-screen fidelity and test mapping

Primary source:
`docs/design/onboarding-mock/app.js:26-55`,
`screens/index.json:14-54`, the corresponding `p-*.png` and
`p-sources-mobile.png`. Preserve guided-setup header/Save & finish later,
numbered navigation, eyebrow/title/help hierarchy, panels/supporting guidance,
status banners/cards, purple selection/action treatment and Back/primary
footer. Validate 1440px, 390px and an intermediate width. No old five-step
layout substitution; no prototype selector/gallery links, synthetic people,
pre-confirmed receipts or simulated success.
There are **six numbered provider stages and seven provider screen states**:
p-uncertain is receipt recovery within source work, not a seventh mandatory
stage. The inspected desktop p-details image has two-column fields and a
separate guidance column; the inspected 390px p-sources image stacks panels,
guidance and footer while retaining Save & finish later. Preserve that reflow.

| Screen | Required production behavior | Existing tests read; required additions |
|---|---|---|
| **p-details** Identify your provider | Display/operator/contact grouping; reuse existing identity; read-only trusted handling panel; partial explicit save/reload. Secondary legacy default confirmation is not a new screen or handling authority. | `CspOnboardingReentrancyTests.GetState_AfterPartialProgress_ReportsLastIncompleteStep` reads committed identity/support in a fresh client. Add real browser close/reopen for partial fields, failed-save no navigation, stale-admin edit conflict, Unknown/Expired handling, display/operator/contact mapping and no duplicate profile. |
| **p-access** Confirm provider access | Actual authenticated administrator/scope, separate security review contact, discovery link only if authorized; contact/manual-unverified/deferral states remain honest. | `ProviderAuthorizationHttpTests.ProviderApis_RejectOrdinaryTenantAndSupport` and private-read tests cover fixture-based denial. Add real middleware, anonymous/subscriber/support/revoked/different-provider negatives for every new read/write/replay; contact save must not add grants. Directory failure cannot fabricate an identity. |
| **p-offering** Add your first service offering | Name/environment/model/management/intended-use fields, existing offering reuse and explicit add-later; no boundary prerequisite. | `ProviderAuthorizationHttpTests.OfferingCreate_ReplaysExactIntent_AndRejectsChangedIntent` and boundary successor test assert replay/conflict/version retention. Add non-Azure descriptive metadata, same-name not identity, changed key/intent, restart/lost creation response, deferred offering persistence, stale existing offering and no authorization/connection from declared impact. |
| **p-sources** Add source material | Optional source cards, explicit skip with retained deferral, shared drop zone, confirmed receipt safe to leave during processing. Unknown boundary uses **unassociated** receipt with hint only. | `OnboardingHandoff.test.tsx` currently asserts offering/boundary upload and continuation during Processing; amend the prerequisite assertion without losing no-publication coverage. `CspPackageImportContractTests.OnboardingReceipt_CanBePolledBeforeActivation_AndSubmitLeavesDraftsUnpublished` already exercises unassociated preactivation receipt. Add no-boundary first receipt, no fake offering, full real-key transport, definite rejection correction, policy denial before bytes, multi-file manifest and skip persistence. |
| **p-uncertain** Check the package receipt | Dedicated unknown banner, actual manifest/context, Check existing receipt and Save later. No alternate-key automatic upload/skip; after restart files must be reselected if receipt cannot be confirmed. | `PackageUpload.test.tsx` proves in-mount exact retry and remount/reselection base-key equality, **not browser-server durable intent**. Add server intent recovery, browser restart with empty storage/File objects, timeout after receipt commit, same-key/wrong-manifest, changed offering revision, stale lease, simultaneous retry, no receipt observed versus unavailable DB, post-activation replay, revoked access and second-admin recovery. |
| **p-review** Review provider setup | Actual saved profile/access/offering/source state; explicit confirmation; label Deferred/Unknown accurately, not the prototype fallback “Not added.” No source review/approval prerequisite for activation. | `OnboardingHandoff.test.tsx` checks submit separation/no publication; `CspOnboardingContractTests` asserts Active then legacy second-submit 409. Add acknowledgement of each unresolved intent, direct legacy-submit bypass rejection for v1 drafts, exact completion replay, three-way stale draft/profile/offer, failed activation preservation and saved-unsaved distinction. |
| **p-ready** Your provider workspace is ready | Provider review-queue handoff with separate source-analysis, scope/responsibility and reviewed-release actions. Retain unknown-receipt recovery; never claim actual authorization. | `OnboardingHandoff.test.tsx` verifies Authorizations destination; `Association.test.tsx` covers retained association/query/hash, foreign selected offering and denied read. Add actual new queue projection, stable action IDs/deferred owner/reason across restart, processing exceptions, reentry without first-run loop and links that open the correct retained receipt/version. |

Existing backend tests **inspected, not run**:

* `tests/Ato.Copilot.Tests.Unit/PackageImports/CspPackageServiceTests.cs:
  431-524,578-710`: stale-worker fencing, publication rollback/replay,
  expired-lease recovery, one concurrent analyzer, durable failure/retry,
  retained receipt/manifest, changed-content conflict and private reads.
* `tests/Ato.Copilot.Tests.Integration/Tenancy/CspPackageImportContractTests.cs:
  34-145`: retained bytes before receipt, preactivation reads and activation
  without publication, fixture-based private endpoint denial.
* `tests/Ato.Copilot.Tests.Integration/Tenancy/ProviderAuthorizationHttpTests.cs:
  22-79`: offering idempotency, immutable successor and ownership context denial.
* `src/Ato.Copilot.Dashboard/src/__tests__/provider-authorizations/
  Association.test.tsx:17-75`: explicit unassociated handling and bookmark
  preservation; no auto-selection or denied-read-to-empty fallback.

Do not overstate the baseline: the existing
`CspOnboardingContractTests.Post_Submit_Without_Required_Steps_Returns422`
at `:145-158` asserts only “not 500 / less than 500,” allowing fixture state
contamination; it does **not** prove strict missing-step rejection. Existing
HTTP fixtures inject tenant/admin context; they are not evidence of complete
ordinary authentication/tenant-resolution coverage.

Additional implementation gates: SQLite and SQL Server baseline/repeated-start
migration preservation; real production-shaped key/manifest requests; processor
restart with policy/context changes; deferred/unavailable facts; stale preview/
impact after association; unassociated new-setup publication denied server-side;
legacy unlinked receipt compatibility; exact canonical transaction rollback;
cross-admin replay returns the current actor without reexecution; revoked replay
is denied; changed permissions/context refresh action availability; a known
commit survives a failed pure projection with no fabricated NotFound or retry
mutation; private provider filters and ownership-qualified references;
source confidentiality; keyboard/focus/error announcements; responsive
screenshots; actual reviewed provenance in downstream document acceptance.
Every new executable test follows AAA and red-before-production TDD.

## 9. Review packet and boundaries of completion

At the Phase 0 checkpoint, this document was the only authorized deliverable.
The following review decisions governed the subsequently authorized local
implementation. Phase 0 did not run builds/tests/browser automation; §10
records later validation separately.

Implementation review must explicitly approve:

1. Provider-private draft/command/intent persistence and serialized profile
   concurrency rather than global profile storage or dummy offering rows.
2. Registered unassociated receipt path, durable full intent and ≤100-character
   key contract; explicit publication association requirement for new setup
   receipts while retaining legacy records.
3. Trusted handling-policy source/approval and rollout behavior for missing
   legacy declarations; declaration does not certify content classification.
4. Descriptive non-Azure offerings without fictional Azure scope; optional
   declared impact never changes deployment handling authority.
5. Exact seven-screen mock fidelity, persistent deferral/Unknown facts and
   safe completed-profile/unfinished-source coexistence.

For local manual acceptance after implementation: use synthetic files only;
save incomplete provider fields and restart the browser; defer offering/sources
and inspect retained actions; receive an unassociated package, finish while it
processes and reopen the same receipt; interrupt the response, restart without
File objects, reconcile and retry the same intent only; explicitly associate a
reviewed exact boundary; inspect exceptions and approved-set publication
separately. Repeat with revoked/support access, changed handling policy and stale
revisions. Let the user perform these checks before calling the provider flow
complete. Actual deployment approval, directory enrollment, production migration,
document export and eMASS acceptance remain unverified/outside this Phase 0 work.

## 10. Local implementation checkpoint

Following explicit implementation authorization, the local provider slice adds
private provider setup drafts/command outcomes, registered source metadata,
authorized recovery reads, explicit deferrals and action projections. It reuses
the existing receipt ledger, association/review/publication services and worker.
`CspWizard` now adapts the shared `SetupFrame` to the seven provider screens;
legacy routes remain separate. The existing associated intake key construction
is reduced to a deterministic SHA-256 context key within the server's 100-character
limit rather than concatenating GUIDs.

Implementation-specific bounds/clarifications:

* Setup retains at most 25 upload intents. The initial registered-intent
  projection emits **Confirmed** or **NotObserved**, not an invented InFlight,
  failed/no-receipt or definite rejection. Package uniqueness/content binding
  handles concurrent receipt retries. No separate intent-processing lease or
  second receipt worker was added. A policy/version change can block retry;
  no changed payload or fresh key is silently substituted.
* The draft stores source selection metadata before submission; files must be
  reselected after browser restart. Explicit Upload registers the exact request
  before bytes. Partial saves never send bytes or create an offering.
* Registered uploads enforce the configured operator handling policy. Once
  a provider has a v1 setup draft, or a handling policy is configured, old
  unregistered ingress receives corrective declaration-required rejection
  rather than bypassing the new boundary. Pre-upgrade provider flows without
  either retain their prior compatibility; this is not a handling-approval claim.
* Security reviewers are contact-only or explicitly deferred. Provider directory
  discovery is visibly unavailable in this adapter; it does not manufacture
  directory identities, membership or roles.
* Private command journals retain committed IDs/revisions and historical
  snapshots; replay freshly projects the current actor/access. Known commit
  plus unavailable projection does not trigger mutation reexecution.

Validation recorded during implementation:

* Initial scoped frontend RED: 10 intended failures / 6 existing passes.
  Real WebCrypto upload identity reached the assertion **152 > 100**.
  A test-only jsdom/Node ArrayBuffer adapter was needed to reach that assertion.
* Parent reported combined initial provider HTTP RED: seven intended 404
  failures for missing routes, with compilation successful.
* Scoped frontend GREEN: 16 tests passed; subsequent related provider/package
  regression run: **395 tests passed across 37 files**. That run retained
  existing ImpactPanel React `act(...)` warnings and Browserslist age warning;
  it was not warning-free.
* Added browser-component remount recovery/wrong-manifest tests passed
  (11 tests with the seven-screen/draft suite). These are **component tests**,
  not a claim of a real browser process restart or pixel-diff acceptance.
* Final related frontend run with bounded parallelism: **397 passed across
  38 files** (`--maxWorkers=2`); dashboard `tsc --noEmit` passed. An intervening
  run exposed an incorrect offering-list mock in the new screen test; the
  fixture now returns route-appropriate data. An unchanged ImpactPanel test
  also failed once and passed its isolated rerun; its production source was
  not changed. Existing React/Browserslist warnings remain recorded above.
* A subsequent focused RED reproduced a second draft write after a known commit
  whose fresh projection was unavailable. The UI now requires a successful
  state reload before further writes. The setup/recovery suite then passed
  **12 tests**, and `tsc --noEmit` passed; the earlier 397-test count does not
  include this added case.
* Added `e2e/tests/onboarding-provider-consolidation.spec.ts` using
  `installWorkspaceFixture` and canonical synthetic provider DTOs. Against the
  parent's responsive local Vite preview, the latest Chromium run passed
  **7 real-SPA scenarios**: all seven screen states at 1440/390, explicit
  save/reload without activation, interrupted receipt reconciliation without
  duplicate bytes/boundary/publication, failed save, forbidden read and unknown
  handling. That run's screenshots were subsequently removed by another
  runner's default output cleanup; use the surviving regenerated captures
  recorded below, not that historical directory.
  Command: `PLAYWRIGHT_BASE_URL=http://127.0.0.1:5179 npx playwright test
  e2e/tests/onboarding-provider-consolidation.spec.ts --reporter=list
  --output=test-results/provider-onboarding`.
  One intervening run observed same-URL navigation interrupting the upload
  click; its cause was not verified. The isolated recovery scenario and
  subsequent full seven-scenario run passed. No container/server restart or
  production behavior workaround was used.
  These are synthetic HTTP fixtures through the real SPA, **not** live
  authentication, database, Azure policy validation or pixel-diff acceptance.
  The latest dashboard `tsc --noEmit` passed after concurrent shared-file edits
  settled.
* Parent exclusively owns .NET commands for this shared checkout. New provider
  integration selectors are `FullyQualifiedName~ProviderSetup`; the added
  publication guard regression is
  `CspPackageServiceTests.NewSetupReceipt_RequiresExplicitOfferingAssociation_WithoutChangingLegacyPublication`.
  Final backend, migration, concurrency and manual visual acceptance must be
  recorded from actual results before claiming this domain end-to-end complete.

### Phase 6 provider retirement ledger

After the replacement component/browser tests passed, a bounded import/caller
trace across Dashboard source, tests, E2E and extensions found:

| Surface | Retirement decision and evidence |
|---|---|
| `csp-onboarding/steps/IdentityStep.tsx`, `SupportContactStep.tsx`, `ClassificationStep.tsx` | Retire. The replacement CspWizard imports shared fields/SetupFrame and the canonical setup adapter, not these step-local editing components. No production caller remains. Server identity/support/default validation and legacy routes remain. |
| `csp-onboarding/steps/AtoDocumentsStep.tsx`, `ReviewStep.tsx` | Retire. Only old wrapper-focused Vitest tests reference them. Their offering-before-receipt/legacy-submit UI behavior is superseded by explicit source dispositions, registered unassociated receipts, reviewed completion and p-ready handoff. |
| `csp-onboarding/steps/ComponentExtractionPreview.tsx` | Retire. No caller remains; the obsolete extraction tally cannot substitute for retained receipt/review state. |
| Legacy `postCspOnboardingIdentity/Support/Classification/Submit/AtosUpload`, `getCspOnboardingAtosState` client functions and their unused request/step-tally types | Retire the unused Dashboard adapter functions, not the HTTP endpoints. Runtime callers use the new setup adapter. The legacy state **read** remains live in CspOnboardingGuard, OnboardingGate’s provider probe and useCspBranding. |
| `AtoSourceFormat`, `AtoUploadFileResult`, `AtoUploadResponse` transport types and `importCspInheritedComponents` client function | Retire after following the apparent type consumer: `features/csp-inherited-components/api.ts` imports the response type only for its unused synchronous upload function; that function has no production invocation and only a negative test spy. The component-library button already navigates to the durable portal. Preserve the real legacy HTTP endpoint and deep-link handoff, not an uncalled Dashboard upload adapter. |
| `OnboardingPackage.test.tsx` old wrapper assertions | Retire obsolete step-layout/submit tests. Move still-relevant unavailable-status retry/polling assertions to direct PackageReceipts tests. Replacement setup/recovery tests retain optional source, processing handoff, no-publication, exact-intent restart and wrong-file coverage. |
| Old five-step wizard cases in `OnboardingHandoff.test.tsx` and `e2e/tests/package-imports.spec.ts` | Replace/remove only superseded onboarding cases. Keep live canonical package review/approval/publication tests, transport scope checks and exact receipt recovery in the new provider browser suite. |
| PackageUpload, PackageReceipts, OfferingIntake/BoundaryEditor, PackagePreparation, canonical profile/receipt/association/worker/review/publication services | Retain actual runtime consumers in Authorizations and provider setup. Do not delete by naming similarity. |
| Tenant/general/bootstrap flows, legacy HTTP/deep links, persisted profiles/receipts/grants/reviews/releases | Retain domain-distinct behavior and historical records. No data deletion or migration reset is part of UI retirement. |

Retirement verification: the Dashboard/extensions caller scan reports no
references to the retired CSP steps, mutation/tally adapters, synchronous client
import helper or old five-step submit labels. `tsc --noEmit` passes.
The provider/package/legacy-entry Vitest slice passes **393 tests in 39 files**;
the lower count reflects removal of obsolete wrapper assertions, not removal
of canonical receipt/review coverage.

The final combined synthetic Chromium run passes **13 scenarios** at 1440/390:
the seven new setup scenarios plus six retained source-review, exact
approval/publication replay, reviewed-reference download and private-access
cases. Running the retained cases exposed stale fixtures missing required
association metadata and an obsolete “Review imports” entry label; fixtures
now follow the current Authorizations handoff and exact association response.
Production source review/publication behavior was not weakened to satisfy them.
Screenshots are in
`src/Ato.Copilot.Dashboard/test-results/provider-onboarding-and-review/`;
the retained tests now write screenshots only to test output, not design assets.
This remains synthetic real-SPA validation, not live backend/auth/cloud proof.

### Stored-field acceptance: descriptive services and handling separation

`ProviderSetupOfferingContractTests` adds parent-run HTTP/database assertions
for AWS GovCloud, Microsoft 365 DoD and manually described SaaS (`Other`,
`serviceDescription.serviceModel=Software`). Under the effective §3.3 rebase
amendment, successful first-offering commits retain the full descriptor in the
saved draft while storing canonical `SoftwareAsAService`/`ProviderManaged`
identity and `ManualService` or `Microsoft365DoD` environments, with no Azure
environment, boundary/hosting pointers or boundary, hosting-scope or package
version rows. The pre-rebase empty canonical environment expectation is
superseded. A non-Azure descriptor paired with invented `AzureCloud`, or
contradictory canonical model/management, is rejected without an offering row;
its draft input remains available for correction.

The same suite varies the actually stored legacy profile floor through
Unclassified/CUI/Secret while storing a declared IL6 service descriptor.
Even with upload/analysis enable flags and allowed classifications configured,
missing policy identity/version/approval/validity keeps handling **Unknown**:
no upload intent, receipt or bytes may be created. Another case persists
offering/source deferrals, activates an otherwise complete profile, and
asserts unchanged offering/receipt counts and a retained completion snapshot.

These tests are **READY, not yet claimed passed** by this agent; the parent owns
the serialized .NET slot. Selector:
`FullyQualifiedName~ProviderSetupOfferingContractTests` (also included by
`FullyQualifiedName~ProviderSetup`).

Parent integration checkpoint: after correcting a verified system-schema
startup ordering issue, the parent reported **43 passed / 3 failed** in the
combined 46-case HTTP run, with all three failures in organization/legacy-tenant
cases and the ProviderSetup/Receipt selectors passing under the actual wired
host. This agent did not run .NET. The exact stored-offering and package-unit
selector outcomes should be attached separately rather than inferred from the
aggregate count.

Coverage checkpoint requested before final acceptance: the first scoped V8
measurement (393 passing tests) reported **81.68% statements/lines overall**,
with `CspWizard` **87.87% lines / 63.63% branches**, `providerSetupApi`
**42.85% lines**, upload identity **100% lines**, and OfferingIntake **100%
lines**. The folder include also measured unchanged guard code at zero; this
aggregate is not a modified-line percentage and **does not meet the requested
100% modified-path target**. Additional transport and state-transition tests are
being added; backend coverage awaits the serialized .NET slot.

A focused new RED case demonstrated that an upload response containing IDs
could display “receipt confirmed” even when the subsequent authoritative saved
intent still had no matching receipt. The required correction is to retain the
uncertain state/files until the fresh intent projection confirms the exact
returned package ID, not merely a success-shaped response.

The upload-response and reconciliation-response mismatch cases both reached
RED before correction; the focused transport/setup/recovery suite then passed
**19 tests**, with TypeScript clean. Relational data tests are ready for the
exclusive .NET slot: concurrent first draft/profile (same and different intent),
offering/journal rollback plus exact retry, legacy SQLite column/index upgrade
and repeated preservation, known commit with unreadable current projection,
invalid input leaving no provider/draft/command writes, and processor handling
revocation before/during analysis preserving original bytes with no candidates
or publication. Neither these unrun tests nor line-coverage percentages imply
100% branch/modified-path coverage.

### Provider serialized .NET verification

With the explicitly granted exclusive slot, this agent ran:

* `dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj
  --filter FullyQualifiedName~ProviderSetupOfferingContractTests --no-restore`:
  **8/8 passed**, including actual persisted descriptor/environment/scope
  assertions and unknown handling despite stored floor/declared impact.
* Integration `--filter FullyQualifiedName~ProviderSetup --no-build --no-restore`:
  **20/20 passed**, no skips. This combines provider drafts/replay, source
  receipt/reconciliation/policy and stored-offering acceptance.
* Unit selector
  `FullyQualifiedName~CspPackageServiceTests|FullyQualifiedName~ProviderSetupCompatibilityTests|FullyQualifiedName~ProviderSetupRelationalTests`
  with `--no-restore`: **67/67 passed**, including concurrent first
  provider/draft writes, immutable outcome replay, transactional offering
  rollback, SQLite legacy-schema/repeated upgrade, retained source/worker
  policy revocation and canonical publication protections.

The integration compilation emitted zero compiler-warning lines. The unit
compilation emitted 65 warning lines in existing tests: CS0105 (1), CS8604 (4),
CS8602 (3), CS8620 (32), CS0618 (19), CS0219 (1), CS8625 (5). None named the
new ProviderSetup files or modified CspPackageServiceTests; the package-analyzer
transport obsolete SDK warning was retained. No warning suppression or assertion
weakening was used. The .NET slot was released after these green runs.

These results verify the exercised SQLite/HTTP/service paths, not SQL Server
runtime or 100% modified-path coverage. Manual user review and remaining
coverage/SQL Server acceptance are still explicitly outstanding.

Screenshot-output coordination: the earlier capture directory was verified
absent. A fresh isolated run using
`--output=test-results/provider-onboarding-and-review --reporter=list`
passed **13/13** and produced **41 PNG captures**, verified present after the
run. All future provider browser commands must specify a provider-specific
output directory so sibling/parent test captures are not deleted by Playwright.
The surviving provider path is
`src/Ato.Copilot.Dashboard/test-results/provider-onboarding-and-review/`.

### Active portal continuation correction

Review identified a functional blocker: counting all retained provider intents
against setup's 25-item bound permanently prevented the 26th intake, while the
suggested portal path lacked registered intents and was rejected by the handling
guard. A focused frontend RED confirmed that FileFirstImport sent source bytes
without first registering an ActivePortal intent.

Correct the ownership boundary, not the safety policy: upload intents gain
persisted `EntryPoint` and nullable `DraftId`. Onboarding intents retain the
bounded setup association; ActivePortal intents require an Active provider and
have no draft FK/value or setup-revision side effect. Historical receipts/intents
remain retained. A paged ordinary-provider-only
`GET /api/csp/package-imports/upload-intents?entryPoint=ActivePortal&page=1&pageSize=25`
returns pending attempts (optional exact `offeringHintId`) for restart recovery.
The existing explicit intent GET/reconciliation and canonical receive/association
services remain the authority; no second receipt engine or handling bypass.

FileFirstImport and OfferingIntake must share a policy-aware upload component
that reads the trusted handling projection, obtains explicit declaration,
registers the exact active-portal intent before bytes, and verifies/reconciles
the retained receipt. Unknown handling remains blocking. Original key/manifest/
name/context survive browser restart through paged server intents; listing is
not lifetime intake capacity. SQLite upgrade rebuilds only the new private
intent table when its prior DraftId column is non-nullable, copying every row
and preserving indexes/FKs; SQL Server makes that column nullable. Old rows
default to Onboarding rather than reclassifying existing setup history.

The shared portal component is `features/package-imports/ProviderSourceUpload`.
Both FileFirstImport and exact-context OfferingIntake use it; it wraps the
existing PackageUpload/manifest hashing controls and calls canonical APIs.
Registered intent UUIDs replace the intermediate context-key helper, which was
removed after its last runtime caller migrated. An `uploadIntentId` query
parameter preserves the precise recovery link after a lost response; it is a
locator only and every read remains authorized. Pending pages are database-paged
and provider/entry-point filtered, not a lifetime receipt counter.

Portal correction frontend verification: **401 Vitest tests in 41 files passed**,
`tsc --noEmit` passed, and **15 synthetic real-SPA browser scenarios passed**
including active portal receipt recovery with 25 historical sources, the seven
provider states, setup save/reload, and canonical source review/publication.
The isolated output
`src/Ato.Copilot.Dashboard/test-results/provider-activeportal-integration/`
contains 46 PNGs verified present after the run. This is not a backend
26th-receipt claim: HTTP data regressions and nullable-FK migration preservation
tests are ready for the next serialized .NET slot; no concurrent .NET command
was started for this correction.

The subsequently granted **short exclusive provider .NET slot** verified this
correction:

* The first build exposed a misplaced schema-upgrade block introduced by this
  change; it was corrected so additive columns precede nullable-table upgrade,
  followed by indexes. No fixture/auth bypass was used.
* To verify the exact reported lifetime-cap regression on current compilable
  schema, the former provider-wide `Count >= 25` guard was restored temporarily
  during the exclusive run. Both 26th-receipt cases failed **422 versus required
  201**, once after 25 Onboarding receipts and once after 25 ActivePortal receipts.
  That former guard was then removed; only setup-associated intents remain
  bounded. No prior behavior was left restored in final source.
* Current `FullyQualifiedName~ProviderSetup` HTTP run: **23/23 passed**.
  Actual data assertions verify 26 retained intents/receipts/original manifests,
  unchanged setup fields/revision for ActivePortal, and portal preparation with
  **zero setup-draft rows**.
* Package service + ProviderSetup relational/compatibility selectors:
  **68/68 passed**, including prior non-null DraftId schema upgrade preserving
  old intent/hash/revision and permitting a new null-draft ActivePortal intent.
* Final integration compilation emitted **0 warning lines**. The unit/shared
  rebuild emitted **89 warning lines**, none in ProviderSetup/ProviderSource
  files; its two EF1002 warnings name
  `WorkspaceOperationsSchemaAdditions.cs:221,300`, not the provider migration.
  No suppressions or weakened assertions were introduced.

The slot was released to the parent after GREEN. SQL Server runtime and 100%
modified-path coverage are still not claimed. The browser results above are
synthetic, while these HTTP tests exercise the wired local SQLite host.

### Standalone base-schema ownership correction

The failure counts and removed-column diagnosis below are **pre-rebase
historical evidence**, not validation of the effective upstream amendment.

Parent's full unit run supplied RED evidence: **9 failures / 7244 passes**.
Sequential failure logs showed standalone package schema callers could not
insert mapped `HandlingDeclarationJson`; standalone profile callers lacked
`SetupRevision`; exact provider offering CREATE/model assertions lacked
`ServiceDescriptionJson`. The earlier focused new-module tests did not prove
standalone owner compatibility.

The original schema owners now include their current model columns on fresh
CREATE **and** additive repeat-upgrade paths for SQLite/SQL Server:

* `CspPackageSchemaAdditions`: upload intent ID, policy version, declaration,
  association-required default and filtered unique upload-intent index.
* `TenantsAndOrganizationsSchemaAdditions`: profile SetupRevision with default 1.
* `ProviderAuthorizationSchemaAdditions`: the pre-rebase correction added
  ServiceDescriptionJson. That column/model is now superseded by upstream
  ServiceModel, ManagementArrangement, ServiceOwner and SecurityContact.

No exact type/nullability/index assertions were weakened. The original SQL
package schema test again runs its owner alone, rather than depending on
ProviderSetup initialization. New
`ProviderSetupBaseSchemaOwnershipTests.BaseSchemaOwners_UpgradeTheirCurrentColumnsWithoutProviderSetupModuleOrDataReset`
removes these fields from a retained baseline, reruns only the base owners,
and checks retained IDs/hash/defaults/index and absence of setup tables.
This correction is source-ready for parent validation; no .NET run was started
while the parent owns the final SQL/full lane.

### Rebase validation handoff

For upstream `a4d43d7c`, the standalone ownership test now removes and restores
the four canonical identity columns rather than the superseded descriptor
column. It still requires retained offering/provider/receipt identity, package
hash/defaults, repeat-upgrade idempotence, the filtered upload-intent index and
no setup tables. Offering contract tests assert canonical identity, persisted
draft descriptor retention, absence of invented Azure/context records and
rejection without partial writes for mismatched identity/environment input.
These updated tests have **not been executed** during conflict resolution;
the parent owns compilation and test execution after dashboard conflicts clear.
