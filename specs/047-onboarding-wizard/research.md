# Research: Tenant Onboarding Wizard (Feature 047)

**Date**: 2026-05-07
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)

## Purpose

Resolve every `NEEDS CLARIFICATION` and high-impact unknown identified during planning so that
Phase 1 (data model + contracts + quickstart) and Phase 2 (`/speckit.tasks`) can proceed without
ambiguity. Each section follows the **Decision / Rationale / Alternatives considered** format.

## Resolved during `/speckit.clarify` (recorded in `spec.md`)

These are not re-derived here; they are listed for cross-reference.

| ID | Topic | Decision (binding) |
|----|-------|--------------------|
| Q1 | Azure auth for subscription enumeration | **User-delegated OAuth via Microsoft.Identity.Web with incremental ARM scope consent.** No tenant-wide service principal. (FR-070, FR-070a, FR-075) |
| Q2 | Wizard authorization | **First authenticated user auto-granted `Administrator` to bootstrap; thereafter only `Administrator` may run/re-run the wizard.** Non-admins see read-only summary. "Never zero administrators" invariant enforced. (FR-001, FR-002, FR-009) |
| Q3 | Long-running operation UX | **Background job + SignalR progress stream + polling fallback.** Reuses the existing pipeline (mirroring `ISspExportNotifier` / `NotificationHub`). Threshold: ~10 s wall-clock. (FR-064, FR-065, FR-066) |
| Q4 | File storage routing + management UX | **Hybrid:** narrative seeds → Feature 038 evidence repo; templates / eMASS / SSP PDFs → existing `IFileStorageProvider` with metadata in `AtoCopilotContext`. New "Imported Documents" management view with rename / replace / mark-default / delete and **cascade staleness flagging + admin-initiated re-run** for dependents. (FR-090..FR-097, SC-013, SC-014) |

## R1. Person identity model for RMF role assignment (Q5 — open in spec)

**Decision**: **Hybrid — Entra ID lookup preferred, free-text local `Person` record as fallback,
with a one-way "promote to directory-linked" path.**

The wizard's Step 2 role-assignment UI MUST first invoke an Entra ID people-picker (via
`Microsoft.Graph` `/users` filtered by the user's home tenant). If the assignee is found, the
created `Person` record stores `EntraObjectId` and is flagged `IsLinkedToDirectory = true`. If the
assignee is not in Entra (common during initial onboarding when ISSOs haven't been provisioned
yet), the admin enters a name + email + optional phone, and the system creates a `Person` record
with `IsLinkedToDirectory = false` and `EntraObjectId = null`.

A new `PromoteToDirectory(personId, entraObjectId)` operation rewrites the local `Person` record
with the directory link **without** rewriting any `OrganizationRoleAssignment`, narrative
authorship reference, or audit-trail entry that points to that person. The promotion is one-way
(directory-linked → free-text downgrade is **not** supported; that would defeat the audit-trail
guarantees of Constitution V).

**Rationale**:

- Matches operational reality: real DoD / agency onboarding routinely begins before the appointed
  ISSO has an Entra account in the tenant, especially for industry-partner tenants where staffing
  predates IT provisioning.
- Preserves audit fidelity: every role assignment, including those made before directory linkage,
  has a stable `Person.Id` (UUID) and full Serilog event lineage. Promotion is a metadata update,
  not a re-key.
- Enables progressive enhancement: SSO-aware features (Feature 003 CAC + PIM, Feature 035
  deviation routing) can later require `IsLinkedToDirectory = true` for *new* role assignments
  while still honoring existing free-text assignments — without forcing an org-wide identity
  cleanup.
- Confines complexity: the alternative (Entra-only) blocks the most common bootstrap case;
  free-text-only loses every benefit of an SSO directory; the `SystemComponent type=Person`
  alternative couples organization-level identity to the per-system inventory model, which is
  semantically wrong.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **A — Entra ID users only** | Blocks the very common bootstrap scenario where the assignee isn't in Entra yet (e.g., new ISSO). Forces an external IT prerequisite before onboarding can complete. |
| **B — Free-text local Person records only** | Loses SSO mapping, no Graph-backed contact lookup, and downstream features (deviation routing, narrative governance) can't reliably reach the person. |
| **D — Reuse `SystemComponent` type=Person** | Couples organization-level identity to the per-system inventory; wrong semantic level for org-default role assignments; would require synthetic systemless components. |

**Implementation hooks**:

- `IPersonDirectoryService` — methods `SearchAsync(query, ct)` (Graph), `CreateLocalAsync(name, email, ct)`, `PromoteToDirectoryAsync(personId, entraObjectId, ct)`.
- `Person` entity columns: `Id` (Guid), `DisplayName`, `Email`, `PhoneNumber?`, `EntraObjectId?`, `IsLinkedToDirectory`, `CreatedAt`, `CreatedBy`, `LastPromotedAt?`.
- Audit (FR-060, Constitution V): every promote / create / link emits a structured Serilog event.

## R2. SignalR group / hub topology for wizard progress

**Decision**: **Reuse the existing `NotificationHub`** at `/hubs/notifications` and introduce a
**tenant-scoped group convention** `wizard-{tenantId}` (and per-job sub-group
`wizard-{tenantId}-job-{jobId}` when fine-grained subscriptions are needed). A new
`SignalRWizardProgressNotifier : IWizardProgressNotifier` mirrors the existing
`SignalRSspExportNotifier` shape.

**Rationale**:

- The repo already runs `NotificationHub` and `PackageHub`; introducing a *third* hub for one
  feature would violate "no new top-level infrastructure" (plan §Project Structure) and
  duplicate auth wiring.
- Group naming with the tenant ID is sufficient isolation because every connected client is
  already authenticated via Microsoft.Identity.Web before SignalR negotiation; the
  `OnConnectedAsync` override on `NotificationHub` will add the connecting client to
  `wizard-{tenantId}` only when the tenant claim is present and the user's role allows wizard
  access (FR-009).
- Per-job sub-groups let the React `BackgroundJobProgress` component subscribe narrowly when the
  wizard renders a single import / extract / re-render in isolation, reducing client-side noise.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **New `WizardProgressHub`** | Costs duplicate auth pipeline + connection negotiation; the codebase explicitly favors hub reuse with grouping. |
| **Per-job hub instance** | Operationally heavier with no UX benefit over per-job groups on a shared hub. |
| **Server-Sent Events instead of SignalR** | Existing dashboard already speaks SignalR (`@microsoft/signalr`); switching channels for one feature contradicts mode-parity (Constitution VII). |

**Event schema (recorded in `contracts/progress-events.md`)**:

```jsonc
// Event: "wizard-job-status"
{
  "jobId":   "uuid",
  "tenantId":"uuid",
  "jobType": "EmassParse | EmassCommit | SspPdfExtract | NarrativeSeedIndex | TemplateValidation | ExportRerender | ImportRerender",
  "status":  "Queued | InProgress | Succeeded | Failed",
  "percent": 0-100,        // null when not applicable
  "message": "string",      // human-readable, plain-language (Constitution VII)
  "errorCode": "string|null",
  "suggestion": "string|null",
  "timestamp": "ISO-8601"
}
```

## R3. SSP PDF text extraction strategy

**Decision**: Use a **dual-pass digital-text-only** extractor — `PdfPig` (MIT) as the primary
extractor for digital PDFs, with a `PdfTextExtractionService` that returns one document model per
PDF carrying `(pageNumber, blockId, text, boundingBox)` tuples. Image-only / encrypted PDFs are
rejected with an explicit error code (`SSP_PDF_NO_TEXT_LAYER` /
`SSP_PDF_PASSWORD_PROTECTED`) — no OCR is attempted (per spec **Out of Scope**).

A second pass — the `SspFieldRecognitionService` — runs heuristics + (optional, AOAI-backed)
prompt-based extraction over the text blocks to identify system identification fields,
categorization, boundary, components, and NIST 800-53 control narratives. Each extracted field
records a confidence band (high / medium / low) per FR-041.

**Rationale**:

- `PdfPig` is the de facto MIT-licensed digital-PDF text extractor for .NET; small dep surface;
  no native binaries required for Linux container deployment (Azure Government).
- Refusing OCR keeps scope bounded (spec **Out of Scope** is explicit).
- Splitting extraction (pure text) from recognition (heuristic / AI) lets us unit-test the
  deterministic path independently of any AI surface — matches Constitution III testing rigor.
- AI-backed recognition is **opt-in via existing `Microsoft.Extensions.AI` `IChatClient`** (when
  configured) and gracefully degrades to heuristics-only when AI is unavailable; this avoids
  hard-coupling the wizard to AOAI in dev environments and Azure Government where the AOAI
  resource may be project-scoped.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **iText 7** | AGPL/commercial licensing problematic for Government distribution. |
| **`Spire.PDF` / `PDFsharp`** | Either commercial or limited extraction quality vs. PdfPig. |
| **OCR (Tesseract / Azure AI Document Intelligence)** | Out of scope per spec; would inflate dependency surface and inference cost. |
| **AI-only extraction** | Non-deterministic; harder to unit-test; produces "confident but wrong" output on tabular SSP layouts. |

**Confidence-band rules** (codified in `SspFieldRecognitionService`):

- **High**: Field matches a labeled regex anchor with high specificity (e.g.,
  `"System Identification:"\s*\n([^\n]+)`) **and** appears in the expected section.
- **Medium**: Field matches a regex anchor but the text is not in the expected section, OR the
  AI extractor returned the field with the regex anchor missing.
- **Low**: AI extractor produced the value with no regex anchor, OR multiple candidates conflict.
  These are flagged for user review per FR-041.

## R4. eMASS export format coverage

**Decision**: Reuse existing importers from **Feature 015 (eMASS Excel import)** and **Feature
041 (eMASS Authorization Package)** unchanged. The wizard's Step 3 orchestrator
(`EmassImportOrchestrator`) detects the file type by signature (`.xlsx` / `.zip`) plus a
content-shape probe (workbook sheet names vs. package manifest), and dispatches to the matching
existing importer. No new eMASS file format is invented.

**Rationale**:

- Spec Assumption "eMASS file formats" already binds this; Feature 015 / 041 are the canonical
  importers.
- Conflict resolution (Merge / Skip / Overwrite) is implemented at the wizard orchestration
  layer, not duplicated inside the importers — the existing importers already expose
  per-system result objects that the orchestrator can consume.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Build a new wizard-specific importer** | Duplicates well-tested code (Constitution I "Documentation as source of truth" → existing importers documented). |
| **Force users to convert all uploads to one format** | Hostile UX; eMASS produces both formats. |

## R5. Template structural sanity checks

**Decision**: Per-type pluggable validators behind a single
`IOrganizationTemplateValidator` interface. Implementations:

- `SspDocxTemplateValidator` — uses `DocumentFormat.OpenXml` to enumerate content controls and
  `${...}` placeholders; checks for required SSP placeholders (cover-page, section-13, control
  block) and warns on missing ones (FR-084: accept with warning, fall back at render time).
- `SarDocxTemplateValidator` / `SapDocxTemplateValidator` — same pattern, different placeholder
  set.
- `CrmXlsxTemplateValidator` — uses `ClosedXML` to read header row of the first non-hidden sheet
  and verify required CRM columns (Control ID, Customer Responsibility, Provider Responsibility,
  Inheritance Type, Status).
- `HwSwXlsxTemplateValidator` — same pattern; required H/W/S/W columns derived from Feature 025.

Validators run **inline** for files ≤ 5 MB and as a **background job** above that threshold (per
FR-064 + Constitution VIII). Validators **never** reject a structurally non-compliant template;
they always accept it with a warning and store the warning list on
`OrganizationDocumentTemplate.ValidationWarnings`. (Format mismatch — wrong file extension /
content type — is rejected at upload, not at validation.)

**Rationale**:

- DocumentFormat.OpenXml and ClosedXML are already in the dependency graph (Features 037 / 041).
- "Accept with warning" matches FR-084 and avoids blocking admins on cosmetically imperfect
  templates while still surfacing the issue at render time.
- Single interface keeps the structure pluggable for future template types without changing
  call sites.

## R6. Cascade dependency model (FR-094)

**Decision**: Maintain a **`WizardArtifactDependency` table** that records, at the time of
derivation, the link from a wizard-uploaded source artifact (`SourceArtifactType` +
`SourceArtifactId` + `SourceVersionTag`) to the downstream entity it produced (`DependentType`
+ `DependentId`). When the source is replaced (FR-093), the dependency service walks the table
in O(n) per source and **flags** every dependent (`StaleSince`, `StaleReason`) without
re-processing — re-runs are admin-triggered (FR-095).

The five concrete dependency wirings:

| Source | Dependent | Wired by |
|--------|-----------|----------|
| `OrganizationDocumentTemplate` (default flag set) | Generated `SspExport` / `SarExport` / `SapExport` / `CrmExport` / `HwSwExport` | Each export pipeline records source template id + version on completion. |
| `EmassImportSession` | `RegisteredSystem`, `ControlImplementation`, `PoamItem` | `EmassImportOrchestrator` writes a dependency row per created entity. |
| `SspPdfImportSession` | `RegisteredSystem` (and the entity's PDF-sourced fields) | `SspPdfImportOrchestrator` writes a single dependency row per system; the PDF-sourced field map is stored in audit, not in the dependency row. |
| `NarrativeSeedDocument` | `NarrativeSuggestion` (citation) | Narrative-suggestion service writes a dependency row whenever a suggestion is emitted with that document as source. |

**Rationale**:

- A single normalized table keeps O(n) walks straightforward and avoids feature-specific tables.
- `SourceVersionTag` (a stable hash or content-checksum captured at derivation) lets the system
  decide on re-run whether the new upload meaningfully differs from the prior version (FR-094:
  "what changed").
- Admin-initiated re-runs (no automatic re-processing) prevent expensive cascades from running
  unannounced, satisfying Constitution VIII performance budgeting.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Per-source-type dependent tables** | Multiplies tables and migrations; harder to extend. |
| **Materialized "current dependencies" view via JOINs only** | Loses `SourceVersionTag` history; worse for audit replay. |
| **Auto-re-run on replace** | Violates "no silent re-processing" (FR-094) and risks unbounded resource use. |

## R7. Background job runner

**Decision**: **Reuse the existing background-job pattern already in the codebase** —
`Microsoft.Extensions.Hosting`-based hosted services with `System.Threading.Channels` queues,
the same shape used by `EvidenceVersionPurgeService` and the SSP / Package export pipelines. A
new `IWizardJobOrchestrator` exposes `EnqueueAsync(jobDescriptor)` /
`GetStatusAsync(jobId, ct)`; the implementation persists each job descriptor + status to a new
`WizardJobStatuses` DbSet so that the polling-fallback endpoint required by FR-066 has a
durable source of truth.

**Rationale**:

- Constitution I and AGENTS.md guidance: do not add new infrastructure when an established
  pattern exists.
- Persisted job statuses survive a server restart and let SignalR-disconnected clients recover
  via the polling endpoint (FR-066).
- `Channels<T>` provides backpressure and bounded queues for free, satisfying Constitution VIII
  memory budgeting.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Hangfire** | New dependency; overkill for the wizard's job set; persistence model would conflict with EF Core dual-provider. |
| **Azure Storage Queues** | Operational dependency on Azure even in local dev; existing pattern is in-process. |
| **Quartz.NET** | Same reasoning as Hangfire — net-new dep with no clear benefit. |

## R8. Microsoft.Identity.Web incremental ARM scope consent

**Decision**: Use Microsoft.Identity.Web's `MicrosoftIdentityWebApiAuthenticationBuilder.EnableTokenAcquisitionToCallDownstreamApi(...)` already configured for the dashboard, and call `ITokenAcquisition.GetAccessTokenForUserAsync(scopes: [https://management.azure.com/user_impersonation], tenantId: <user-home-tenant>)` only when the user reaches the subscription step. If `MsalUiRequiredException` is thrown, the API returns `403 + WWW-Authenticate: Bearer error="insufficient_claims", claims=...` and the React `Step5AzureSubscriptions` component invokes the existing front-channel `acquireTokenPopup` / `acquireTokenRedirect` flow with the same scope.

**Rationale**:

- The pattern is already in the Microsoft.Identity.Web docs and is the recommended way to layer
  on a downstream-API scope without re-prompting users at sign-in.
- Cleanly satisfies FR-070a's "incremental, on-demand consent."
- Declined consent maps to FR-073 / FR-075 by surfacing the
  `WIZARD_ARM_CONSENT_REQUIRED` error code with a `suggestion` to either grant consent or skip
  the step.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Request the ARM scope at initial sign-in** | Forces every dashboard user to consent up-front, even users who will never run onboarding (most users). |
| **Service-principal fallback when consent declined** | Violates FR-070's "no tenant-wide service principal." |

## R9. Subscription enumeration tier and filtering

**Decision**: Use `Azure.ResourceManager.ArmClient` constructed from a delegated-token
`TokenCredential` — call `GetSubscriptions().GetAllAsync()` and project to a DTO carrying
`{ subscriptionId, displayName, tenantId, environment }`. Environment label is derived from the
ARM endpoint base URL associated with the credential's authority. Results are not cached
server-side (FR-074 needs fresh visibility); the client-side React table caches the response
for the duration of the wizard step only.

**Rationale**:

- `Azure.ResourceManager` is already a project dependency; no new SDK introduced.
- Avoiding server-side caching is essential for FR-074 (a previously selected subscription
  becoming invisible must not persist as visible because of a cache).

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Resource Graph query** | Heavier dependency for what is essentially `subscriptions list`; worse latency. |
| **Cache for 5 minutes per user** | Conflicts with FR-074; the freshness benefit outweighs the small p95 latency saving. |

## R10. Wizard authorization enforcement surface

**Decision**: Implement a single ASP.NET Core authorization policy
`OnboardingAdministratorPolicy` registered with the existing JWT bearer scheme. Apply it via
`[Authorize(Policy = "OnboardingAdministrator")]` on every wizard endpoint and via
`MapHub<NotificationHub>(...).RequireAuthorization("OnboardingAdministrator")` for the
wizard-group connection path. The policy resolves the in-app `Administrator` RMF role from the
existing claims-augmentation pipeline (Feature 003); if absent, returns 403. The very first
authenticated user in a fresh tenant is auto-granted the role by `OnboardingService.StartAsync`
under a tenant-level lock, with the bootstrap event audited (FR-001).

**Rationale**:

- Centralizes enforcement (no per-endpoint `if`-check duplication, satisfies Constitution VII
  consistency).
- Reuses the existing policy infrastructure rather than building a custom filter stack.
- The tenant-level lock for the bootstrap grant prevents a race between two simultaneous first
  users.

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Per-endpoint role check in code** | Duplicates logic; easy to miss on a new endpoint; violates DRY (Constitution VI). |
| **Front-end-only gating** | Violates security best practice; also explicitly disallowed by spec ("backend MUST also enforce"). |

## R11. Quotas — defaults and configurability

**Decision**: Codify configurable defaults under a new `OnboardingOptions` configuration section
bound from `appsettings.json` (and Key Vault in production):

| Quota | Default | Configurable |
|-------|---------|--------------|
| eMASS upload max file size | **50 MB** | yes (`OnboardingOptions:Limits:EmassMaxBytes`) |
| SSP PDF upload max file size | **25 MB** | yes |
| SSP PDF batch max count | **25 PDFs** | yes |
| Custom template max file size | **25 MB** | yes |
| Templates per type max count (active) | **10** (1 default + 9 archived) | yes |
| Narrative seed doc max file size | **50 MB** | yes |
| Narrative seed per-tenant total budget | **5 GB** | yes |
| Onboarding job concurrency per tenant | **2** | yes |
| SignalR group fan-out cap | **50 connections per tenant** | yes |

Quota violations return `HTTP 413 Payload Too Large` (size) or
`HTTP 409 Conflict` (count / budget) with `errorCode` and `suggestion` per Constitution VII.

**Rationale**:

- Defaults chosen to comfortably accommodate "Scale/Scope" upper bounds (plan §Technical Context)
  while preventing accidental DoS via a malformed multi-GB upload.
- Configurability is required by spec FR-036, FR-054, FR-088 and by Government deployment
  variance (some agencies will tighten these limits further).

**Alternatives considered**:

| Option | Why rejected |
|--------|--------------|
| **Hard-code limits** | Violates FR-036 / FR-054 / FR-088 ("configurable"). |
| **Per-tenant override stored in DB** | Out of scope for v1; can be layered later behind the same `OnboardingOptions` shape. |

## R12. Audit logging shape

**Decision**: Reuse the existing `Serilog`-backed structured-log pipeline. Every onboarding event
emits with a stable property set:

```csharp
log.LogInformation(
    "Onboarding {Action} by {Actor} on {ResourceType} {ResourceId} (tenant {TenantId}, step {Step}); effects: {EffectsJson}",
    action,             // "OrganizationContextSaved" | "RoleAssigned" | "EmassImportCommitted" | ... | "ArtifactReplaced"
    actorUserId,
    resourceType,       // "Person" | "Template" | "EmassSession" | ...
    resourceId,
    tenantId,
    stepName,
    effectsJson         // JSON serialization of dependency effects (FR-097)
);
```

A persisted audit row is also written for every replace / update / delete on a wizard artifact
(per FR-097), via a new `WizardAuditEntries` DbSet — separate from the Serilog stream because
FR-097 requires structured query of "who changed what" without scraping logs.

**Rationale**:

- Two-track audit (logs + DbSet) is already used elsewhere in the codebase (e.g., evidence
  version purge); no new pattern.
- Keeping the dependency-effect snapshot in the DbSet column makes audit replay cheap.

## Outstanding `NEEDS CLARIFICATION` items remaining after Phase 0

**None.** Q1–Q4 were resolved via `/speckit.clarify` and recorded in `spec.md`. Q5 is resolved
above (R1) with the hybrid-Person model. Every other technical unknown surfaced during planning
(R2–R12) has a binding decision.

The plan is therefore ready to proceed to Phase 1 (data-model / contracts / quickstart) without
remaining ambiguity.
