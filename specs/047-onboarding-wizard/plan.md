# Implementation Plan: Tenant Onboarding Wizard

**Branch**: `047-onboarding-wizard` | **Date**: 2026-05-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from [`/specs/047-onboarding-wizard/spec.md`](./spec.md)

## Summary

Deliver a guided **tenant-level onboarding wizard** that an organization's first administrator runs once (and any in-app **Administrator** can re-run later) to bring an empty ATO Copilot tenant from "fresh install" to "ready to author and assess." The wizard guides the admin through seven steps:

1. **Organization & branch context** (org name, DoD branch / civil agency / industry partner, sub-org, classification posture, POC) — feeds every downstream cover page, narrative, and export header.
2. **RMF role assignment** (ISSM / ISSO / Administrator / Assessor) — establishes organization-level defaults inherited by every system created afterward (Feature 042 / 044).
3. **eMASS bulk system import** (Excel + package archive) — reuses Feature 015 / Feature 041 importers; runs as a background job with SignalR progress; per-system Merge / Skip / Overwrite conflict resolution; per-system commit so partial failures don't roll back successful systems.
4. **SSP PDF ingestion** — best-effort text extraction of system identification, categorization, boundary, components, and NIST 800-53 narratives, with confidence flags for low-confidence fields and full provenance tagging.
5. **Azure subscription scope selection** — enumerates subscriptions visible to the **signed-in user's delegated ARM token** (Microsoft.Identity.Web with incremental ARM-scope consent), persists the selection as the tenant's Azure authorization scope, and feeds Features 001 / 005 / 008 / 029 query paths automatically.
6. **Custom document templates** for SSP, SAR, SAP, CRM, and H/W/S/W list — uploaded via the existing `IFileStorageProvider` (local FS dev / Azure Blob prod), one default per type, structurally sanity-checked (placeholders / column headers); consumed by Features 037 / 018 / 041 / 043 / 025 export pipelines without per-system re-upload.
7. **Narrative seed documents** — uploaded via the Feature 038 evidence repository, indexed for retrieval-augmented narrative authoring (Feature 024).

In addition to the seven steps, the feature ships an **"Imported Documents" management view** that lists every wizard-uploaded artifact, lets an authorized admin rename / replace / mark-default / delete, and **cascades staleness flags** to dependent downstream artifacts (exports, imported systems, narrative suggestions) so that replacing a source automatically surfaces a re-run path through the long-running-job pipeline. The first authenticated user in a fresh tenant is auto-granted the in-app **Administrator** RMF role to bootstrap; all wizard endpoints enforce that role on every entry point thereafter.

The technical approach reuses every available pattern in the codebase rather than inventing parallel infrastructure: existing `AtoCopilotContext` for metadata; existing `IFileStorageProvider` for binaries; existing `IEvidenceArtifactService` / Feature 038 evidence repo for narrative seeds; existing SignalR notifier + hub pattern (mirroring `ISspExportNotifier` / `NotificationHub`) for live progress; Microsoft.Identity.Web with incremental consent for ARM tokens; and the existing dashboard React 19 + Vite SPA for the wizard UI.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript 5.7 / React 19 (frontend)
**Primary Dependencies**: ASP.NET Core 9.0, EF Core 9.0.0 (SQL Server + SQLite), Microsoft.AspNetCore.SignalR, Microsoft.Identity.Web 3.5+, Microsoft.Graph 5.70+, Azure.Identity 1.13.2, Azure.ResourceManager 1.13.2, Serilog 4.2.0, DocumentFormat.OpenXml, ClosedXML 0.104.2, PdfPig (new), Microsoft.Extensions.AI 9.4-preview, React 19, React Router 7, Vite 6, Tailwind CSS 3, Axios 1.7, @microsoft/signalr 8.x
**Storage**: EF Core dual-provider (SQL Server prod / SQLite dev) via `AtoCopilotContext` with 13 new DbSets; binaries via existing `IFileStorageProvider` (`LocalFileStorageProvider` dev / `AzureBlobStorageProvider` prod) for templates / eMASS / SSP PDFs; narrative seed documents via Feature 038 evidence repository
**Testing**: xUnit 2.9.3 + FluentAssertions 7.0.0 + Moq 4.20.72 + EF Core InMemory + `WebApplicationFactory` integration tests
**Target Platform**: Linux containers in Azure Container Apps (Azure Government regions `usgovvirginia` / `usgovarizona` / `usgovtexas`) for backend; modern evergreen browsers (Chromium / Edge / Firefox / Safari latest) for the dashboard SPA at `/onboarding`
**Project Type**: web

### Primary Dependencies — detail

- **Backend**: ASP.NET Core 9.0 Minimal APIs, Entity Framework Core 9.0.0 (SQL Server + SQLite), Microsoft.AspNetCore.SignalR (existing), Microsoft.Identity.Web 3.5+ (existing — extended for incremental ARM scope consent), Microsoft.Graph 5.70+ (existing — used for Entra ID person lookup), Azure.Identity 1.13.2 (existing), Azure.ResourceManager 1.13.2 (existing — subscription enumeration via the user's delegated token), Serilog 4.2.0 (existing), QuestPDF 2025.7.0 (existing — used here only to validate uploaded SSP DOCX templates render through the same pipeline), DocumentFormat.OpenXml (existing — DOCX placeholder sanity check), ClosedXML 0.104.2 (existing — XLSX column-header sanity check for CRM and H/W/S/W templates), **PdfPig (new — MIT, digital-PDF text extraction; research §R3)**, Microsoft.Extensions.AI 9.4-preview (existing, optional AI-backed SSP field recognition), `System.Text.Json` 9.0.5
- **Frontend (Dashboard)**: React 19, React Router 7, Vite 6, Tailwind CSS 3, Axios 1.7, `@microsoft/signalr` 8.x (existing in dashboard for progress streams)
- **Testing**: xUnit 2.9.3, FluentAssertions 7.0.0, Moq 4.20.72, EF Core InMemory 9.0.0, Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory`)

### Storage — detail

- **EF Core dual-provider**: SQL Server (Docker / production) + SQLite (development) via the existing `AtoCopilotContext`. New DbSets: `TenantOnboardingStates`, `OrganizationContexts`, `OrganizationRoleAssignments`, `Persons`, `EmassImportSessions`, `SspPdfImportSessions`, `NarrativeSeedDocuments`, `AzureSubscriptionRegistrations`, `OrganizationDocumentTemplates`, `WizardArtifactDependencies`, `WizardJobStatuses`, `WizardAuditEntries`, `OnboardingStepCompletions`. Schema additions follow the existing `EnsureCreatedAsync()` / additive-migration pattern used elsewhere in the repo.
- **Binary file storage**: Single `IFileStorageProvider` interface with two impls already in production — `LocalFileStorageProvider` (dev / on-prem) and `AzureBlobStorageProvider` (prod). Used for custom templates, eMASS export uploads, and SSP PDF uploads. Narrative seed documents continue through the **Feature 038 evidence repository** (`IEvidenceArtifactService` + the same `IFileStorageProvider` under it) so they appear in standard evidence views.
- **Storage container layout (new keys, no new containers)**:
  - `wizard/templates/{tenantId}/{templateId}/{filename}` — custom org templates
  - `wizard/imports/emass/{tenantId}/{sessionId}/{filename}` — eMASS export originals (retained per FR-091 to support FR-065 retries and FR-095 cascade re-runs)
  - `wizard/imports/ssp-pdf/{tenantId}/{sessionId}/{filename}` — SSP PDF originals (same retention rationale)

### Testing — detail

- Unit (`tests/Ato.Copilot.Tests.Unit/`): xUnit + FluentAssertions + Moq with EF Core InMemory; required by Constitution Principle III for every public service method (positive + negative).
- Integration (`tests/Ato.Copilot.Tests.Integration/`): `WebApplicationFactory<Program>` against an in-memory SQLite `AtoCopilotContext`; covers every onboarding endpoint (happy + at least one error path), the SignalR progress hub, the management view, and the cascade re-run flow.
- Manual (`tests/Ato.Copilot.Tests.Manual/` if present, else `docs/persona-test-cases/`): one fresh-tenant walk-through and one re-run-after-replace walk-through, executed via the Dashboard SPA.

**Performance Goals** (derived from spec Success Criteria + Constitution VIII):

- Minimum onboarding (Steps 1 + 2) end-to-end in **< 5 minutes** (SC-001).
- eMASS import of 5-system export end-to-end in **< 15 minutes** (SC-002).
- SSP PDF extraction confidence: ≥ 80% high/medium on system identification, ≥ 60% high/medium on control narratives (SC-003).
- Five-template upload session in **< 5 minutes** total for ≤ 10 MB templates (SC-012).
- Locate-and-replace any wizard artifact from the management view in **< 2 minutes** (SC-014).
- Wizard endpoint p95 latency **< 200 ms** for non-upload, non-import operations (Constitution VIII); upload acceptance and metadata edits **< 1 s p95**; long-running parse / extract / index / re-render execute as background jobs and MUST NOT count against synchronous endpoint latency.
- Subscription enumeration list **< 5 s p95** for users with up to ~200 visible subscriptions (Constitution VIII MCP simple-query budget).

**Constraints**:

- **Compliance / Sovereignty (Constitution IV)**: Azure interactions use `DefaultAzureCredential` for service-side calls and **delegated user tokens** (Microsoft.Identity.Web + incremental ARM scope consent) for the subscription-enumeration step. NO tenant-wide service principal is created or required by onboarding. Secrets in Key Vault. Data residency: US Gov regions only.
- **Authorization (FR-001 / FR-002 / FR-009)**: First authenticated user in a fresh tenant is auto-granted the in-app `Administrator` role. After onboarding completes, every wizard endpoint, including deep-link entry points and management-view actions, enforces `Administrator` and audits forbidden attempts. Backend enforces with an `OnboardingAuthorizationFilter` / endpoint policy; frontend hides the wizard entry but does not rely on UI hiding for security.
- **Long-running operations (FR-064 / FR-065 / FR-066)**: eMASS parse, SSP PDF extract, narrative-seed indexing, template structural validation > inline threshold, and replace-cascade re-runs all execute via the existing background-job pattern with SignalR progress and a polling-status fallback. SignalR is progress-only — disconnects MUST NOT mark a successful job as failed.
- **Storage routing (FR-090)**: Narrative seeds → Feature 038 evidence repo. Custom templates / eMASS exports / SSP PDFs → `IFileStorageProvider` only. NO parallel storage backend introduced.
- **Cancellation / Memory (Constitution VIII)**: Every async path accepts `CancellationToken`. Steady-state memory < 512 MB; bulk operations (eMASS parse, multi-PDF extraction) < 1 GB; pagination on every collection endpoint with default page size 50.

**Scale/Scope**:

- **Scale**: Targets a single tenant per onboarding execution. Realistic upper bounds — eMASS exports up to 50 systems, ~5 000 control implementations, ~500 POA&M items per export; up to 25 SSP PDFs in one batch; up to 50 narrative seed documents per tenant; up to 10 custom templates per type (one default + nine archived versions); up to 50 selected Azure subscriptions per tenant.
- **Scope (this feature)**: 13 new EF Core entities + DbSets, ~30 new HTTP endpoints (under `/api/onboarding/...` and `/api/wizard/imports/...`), 1 new SignalR notifier + hub group (reuses `NotificationHub`), 1 new `OnboardingAuthorizationPolicy`, ~7 new React route components inside the existing Dashboard SPA plus a shared `WizardLayout` and `ImportedDocumentsView`, and ~5 new background-job handlers (eMASS parse, eMASS commit, SSP PDF extract, narrative-seed index, template structural validation).
- **Scope (touch but do not own)**: Existing eMASS importers (Features 015 / 041), SSP PDF parsing utilities (if any from Feature 022 / 037 — otherwise introduce a new service in `Ato.Copilot.Agents.Compliance.Services`), evidence repo (Feature 038), template-rendering pipelines (Features 037 / 018 / 041 / 043 / 025).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | **PASS** | Every wizard step cites the existing feature spec it builds on (015 / 022 / 024 / 025 / 037 / 038 / 041 / 042 / 043 / 044). New ADR will be opened only if a downstream review finds an undocumented architecture choice; none anticipated. |
| II | BaseAgent / BaseTool Architecture | **PASS** | This feature ships HTTP endpoints + EF entities + React UI + background-job handlers. It does NOT add new MCP tools or AI agents; it consumes existing services. If a future increment exposes "open onboarding wizard step" as an MCP tool, it will extend `BaseTool`. |
| III | Testing Standards (NON-NEGOTIABLE) | **PASS** | Plan mandates xUnit + FluentAssertions + Moq unit tests for every public service method (positive + negative), `WebApplicationFactory` integration tests for every endpoint (happy + ≥ 1 error), boundary tests on size limits / role-assignment cardinality / cascade dependency counts, and regression tests for every fix. Targets 80%+ coverage; flaky-test policy applies. |
| IV | Azure Government & Compliance First | **PASS** | Subscription step uses delegated user tokens (no tenant-wide SP); secrets via Key Vault; deployment targets US Gov regions; supports `AzureUSGovernment` cloud env; ARM scope acquisition uses Microsoft.Identity.Web incremental consent. |
| V | Observability & Structured Logging | **PASS** | Every onboarding action (FR-060) and every replace / update / delete (FR-097) emits a structured Serilog event with actor, tenant, step, action, dependency-effect snapshot. Background-job handlers log queued / progress / success / failure with correlation IDs. |
| VI | Code Quality & Maintainability | **PASS** | New services inject all dependencies via constructor; methods kept ≤ 50 logical lines; XML doc on every public type; no magic strings (size limits, container prefixes, role names extracted to `OnboardingConstants`); naming follows .NET guidelines (`McpServer`-style 3+ char acronyms). |
| VII | User Experience Consistency | **PASS** | All endpoint responses use the existing `{ status, data, metadata }` envelope; errors include `errorCode` + `suggestion` (e.g., `WIZARD_TEMPLATE_WRONG_FORMAT`, `WIZARD_ARM_CONSENT_REQUIRED`); progress > 2 s streams via SignalR; every long-running operation reports compliance context (artifact type, framework, scope) where applicable. |
| VIII | Performance Requirements | **PASS** | Synchronous endpoints budgeted < 200 ms p95 (non-upload) / < 1 s (upload accept); long-running work pushed to background jobs; pagination on management-view endpoints (default 50); `CancellationToken` flowed end-to-end; startup unaffected (lazy registration). |

**Initial Constitution Check: PASS — no violations to justify in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
specs/047-onboarding-wizard/
├── spec.md                         # Authoritative feature specification (existing)
├── plan.md                         # This file (/speckit.plan output)
├── research.md                     # Phase 0 — resolves remaining unknowns (this run)
├── data-model.md                   # Phase 1 — entities, DbContext additions, FK rules
├── quickstart.md                   # Phase 1 — local dev walkthrough + manual verification
├── contracts/                      # Phase 1 — HTTP contract OpenAPI fragments + SignalR events
│   ├── onboarding-api.yaml         #   — wizard step CRUD + management view
│   ├── azure-subscriptions-api.yaml#   — subscription enumeration + persistence
│   ├── imports-api.yaml            #   — eMASS + SSP PDF upload + status + commit
│   ├── templates-api.yaml          #   — custom template upload + management
│   └── progress-events.md          #   — SignalR group / event schema for live progress
├── checklists/
│   └── requirements.md             # Existing spec-quality checklist
└── tasks.md                        # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

The feature ships into the existing solution. **No new top-level projects** are introduced.

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Onboarding/                          # NEW — entity types (matches existing Models/&lt;group&gt;/ convention)
│   │   ├── TenantOnboardingState.cs
│   │   ├── OrganizationContext.cs
│   │   ├── OrganizationRoleAssignment.cs
│   │   ├── Person.cs
│   │   ├── EmassImportSession.cs
│   │   ├── SspPdfImportSession.cs
│   │   ├── NarrativeSeedDocument.cs                # metadata only — bytes live in evidence repo
│   │   ├── AzureSubscriptionRegistration.cs
│   │   ├── OrganizationDocumentTemplate.cs
│   │   ├── WizardArtifactDependency.cs
│   │   └── WizardJobStatus.cs
│   ├── Interfaces/Onboarding/                      # NEW — contracts
│   │   ├── IOnboardingService.cs
│   │   ├── IOrganizationContextService.cs
│   │   ├── IPersonDirectoryService.cs              # FR-022 person resolution (Q5 — NEEDS CLARIFICATION; default to hybrid Entra+local)
│   │   ├── IEmassImportOrchestrator.cs
│   │   ├── ISspPdfImportOrchestrator.cs
│   │   ├── IAzureSubscriptionEnumerationService.cs
│   │   ├── IOrganizationTemplateService.cs
│   │   ├── INarrativeSeedDocumentService.cs
│   │   ├── IWizardArtifactDependencyService.cs
│   │   ├── IWizardJobOrchestrator.cs
│   │   └── IOnboardingAuditTrail.cs
│   └── Data/AtoCopilotContext.cs                   # MODIFIED — add 13 DbSets + EnsureSchemaAdditions
│
├── Ato.Copilot.Agents/
│   └── Onboarding/                                 # NEW — implementations (matches tasks.md path convention)
│       ├── OnboardingService.cs
│       ├── OrganizationContextService.cs
│       ├── PersonDirectoryService.cs
│       ├── EmassImportOrchestrator.cs              # delegates to existing Feature 015 / 041 importers
│       ├── SspPdfImportOrchestrator.cs             # delegates to PDF extraction
│       ├── AzureSubscriptionEnumerationService.cs  # uses delegated ARM token (Microsoft.Identity.Web)
│       ├── OrganizationTemplateService.cs          # uses IFileStorageProvider directly
│       ├── NarrativeSeedDocumentService.cs         # uses IEvidenceArtifactService (Feature 038)
│       ├── WizardArtifactDependencyService.cs      # cascade flagging (FR-094)
│       ├── WizardJobOrchestrator.cs                # background queue + SignalR notifier
│       ├── OnboardingAuthorizationFilter.cs        # FR-009 enforcement
│       └── BackgroundJobs/
│           ├── EmassParseJobHandler.cs
│           ├── EmassCommitJobHandler.cs
│           ├── SspPdfExtractJobHandler.cs
│           ├── NarrativeSeedIndexJobHandler.cs
│           └── TemplateStructuralValidationJobHandler.cs
│
├── Ato.Copilot.Mcp/
│   ├── Endpoints/
│   │   └── OnboardingEndpoints.cs                  # NEW — wizard + imports + templates + management view
│   ├── Hubs/
│   │   └── NotificationHub.cs                      # MODIFIED — adds `wizard-{tenantId}` group + `SubscribeToWizardJob` method (research §R2; no new hub)
│   ├── Services/
│   │   ├── SignalRWizardProgressNotifier.cs        # NEW — mirrors SignalRSspExportNotifier
│   │   └── OnboardingAuditTrail.cs                 # NEW — Serilog-backed
│   └── Program.cs                                  # MODIFIED — DI registration + endpoint mapping
│
├── Ato.Copilot.Dashboard/
│   └── src/
│       ├── routes/
│       │   ├── OnboardingWizard.tsx                # NEW — top-level route, gates on Administrator role
│       │   ├── steps/
│       │   │   ├── Step1OrganizationContext.tsx
│       │   │   ├── Step2RoleAssignment.tsx
│       │   │   ├── Step3EmassImport.tsx
│       │   │   ├── Step4SspPdfImport.tsx
│       │   │   ├── Step5AzureSubscriptions.tsx
│       │   │   ├── Step6CustomTemplates.tsx
│       │   │   └── Step7NarrativeSeedDocuments.tsx
│       │   └── ImportedDocumentsView.tsx           # NEW — management view (FR-092..FR-097)
│       ├── components/wizard/
│       │   ├── WizardLayout.tsx
│       │   ├── ProgressIndicator.tsx
│       │   ├── BackgroundJobProgress.tsx           # subscribes to SignalR + falls back to polling
│       │   └── ArtifactDependencyBadge.tsx         # surfaces "stale" / "re-run available"
│       └── api/onboarding.ts                       # NEW — Axios client
│
└── Ato.Copilot.Channels/                           # UNCHANGED for this feature

tests/
├── Ato.Copilot.Tests.Unit/
│   └── Onboarding/                                 # NEW — one folder per service + entity
└── Ato.Copilot.Tests.Integration/
    └── Onboarding/                                 # NEW — endpoint + hub + cascade tests
```

**Structure Decision**: Web application layered into the existing multi-project .NET solution and the existing React Dashboard SPA. The feature does **not** create any new top-level project; it adds new sub-folders inside `Ato.Copilot.Core` (entities + interfaces), `Ato.Copilot.Agents` (services + background-job handlers), `Ato.Copilot.Mcp` (HTTP endpoints + SignalR notifier + auth filter), and `Ato.Copilot.Dashboard` (React routes for the wizard and the Imported Documents management view). All binary uploads except narrative seeds flow through the existing `IFileStorageProvider`; narrative seeds flow through the Feature 038 evidence repository. Long-running work runs as background jobs and reports progress through the existing SignalR `NotificationHub` using a tenant-scoped wizard group.

### Discovery binding (T047a — canonical integration points)

Resolved by T047a so downstream wiring tasks (T054, T068, T103, T114) target a precise file rather than "or the equivalent":

| # | Wiring task | FR(s) | Canonical file (verified to exist) | Notes |
|---|-------------|-------|-------------------------------------|-------|
| 1 | T054 — SSP cover-page renderer reads `OrganizationContext` | FR-014 | [src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs) | The single SSP export-render service (also see [OscalSspExportService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs) for OSCAL output). Inject `IOrganizationContextService` and inject organization name + branch into the cover page; fall back to a placeholder when no `OrganizationContext` row exists for the tenant. |
| 2 | T068 — system-creation pipeline inherits org-level role assignments | FR-024 | [src/Ato.Copilot.Agents/Compliance/Services/RmfLifecycleService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/RmfLifecycleService.cs) (method `RegisterSystemAsync`) | Single canonical entry point used by `RmfRegistrationTools`, `DashboardEndpoints`, and `ComplianceMcpTools`. Copy `OrganizationRoleAssignment` rows for the tenant onto the new system at create-time (FR-024). |
| 3 | T103 — Azure scope resolver consumes `AzureSubscriptionRegistration` | FR-072 / FR-076 | [src/Ato.Copilot.Agents/Compliance/Services/SystemSubscriptionResolver.cs](../../src/Ato.Copilot.Agents/Compliance/Services/SystemSubscriptionResolver.cs) (`ISystemSubscriptionResolver.ResolveAsync`) | Used by `ComplianceWatchService` and `ScanImportService`. Update so the resolver consults `AzureSubscriptionRegistration` rows for the tenant (status = `Registered`) before falling back to the existing system→subscription map. |
| 4 | T114 — export renderers resolve org default templates | FR-085 | (a) [src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs); (b) [src/Ato.Copilot.Mcp/Services/CrmExportService.cs](../../src/Ato.Copilot.Mcp/Services/CrmExportService.cs); (c) [src/Ato.Copilot.Agents/Compliance/Services/OscalSapExportService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/OscalSapExportService.cs); (d) [src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs) (eMASS package — covers SAR/SAP/H-W-S-W package members) | At each render entry point, call `IOrganizationTemplateService.GetActiveDefaultAsync(tenantId, type)` and use the resolved template if present (FR-085); fall back to the built-in template otherwise. |

## Complexity Tracking

> No Constitution violations identified — this section intentionally left empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _(none)_  | _(n/a)_    | _(n/a)_                             |

## Post-Design Constitution Re-Check (after Phase 1)

Re-evaluation after producing `research.md`, `data-model.md`, `contracts/*`, and `quickstart.md`. Principle names match [.specify/memory/constitution.md](../../.specify/memory/constitution.md) v1.1.0 verbatim.

| # | Principle | Status | Notes after design |
|---|-----------|--------|---------------------|
| I | Documentation as Source of Truth | **PASS** | Spec was clarified (4 questions answered, 1 deferred → resolved in [research §R1](./research.md)); `plan.md`, `research.md`, `data-model.md`, `contracts/*`, `quickstart.md` are produced, internally consistent, and cross-reference each upstream Feature (015 / 022 / 024 / 025 / 037 / 038 / 041 / 042 / 043 / 044). No undocumented architecture choice surfaced; no ADR required. |
| II | BaseAgent / BaseTool Architecture | **PASS** | Phase 1 adds HTTP endpoints + EF entities + React UI + background-job handlers consuming existing services (Feature 015/041 importers, Feature 038 evidence repo, existing `NotificationHub`). No new MCP tool or AI agent introduced; if a future increment exposes "open onboarding wizard step" as an MCP tool, it will extend `BaseTool`. |
| III | Testing Standards (NON-NEGOTIABLE) | **PASS** | [Quickstart §"Automated test coverage to validate"](./quickstart.md) and [tasks.md](./tasks.md) enumerate xUnit + FluentAssertions + Moq unit tests for every public service method (positive + negative), `WebApplicationFactory` integration tests for every endpoint (happy + ≥ 1 error), boundary tests on size limits / role-assignment cardinality / cascade dependency counts. Tests are required before implementation per the Phase plan. |
| IV | Azure Government & Compliance First | **PASS** | Subscription enumeration uses delegated user tokens (Microsoft.Identity.Web + incremental ARM scope consent); no tenant-wide service principal introduced. Secrets via Key Vault. All storage providers already authorized for Azure Government regions. `Environment` enum on `AzureSubscriptionRegistration` distinguishes `AzureCloud` / `AzureUSGovernment`. |
| V | Observability & Structured Logging | **PASS** | Two-track audit ([research §R12](./research.md) + [data-model §13 `WizardAuditEntry`](./data-model.md)): Serilog `WizardAudit` enricher emits structured events; persisted `WizardAuditEntry` rows record before/after JSON snapshots and dependency-effect lists. Background-job handlers log queued / progress / success / failure with correlation IDs (FR-060, FR-097). Every entity is tenant-scoped (`TenantId`); every SignalR group is tenant-scoped (`wizard-{tenantId}`). |
| VI | Code Quality & Maintainability | **PASS** | Constructor-injected dependencies; methods bounded ≤ 50 logical lines; XML doc on every public type; magic strings extracted to `OnboardingConstants` / `WizardErrorCodes` / `WizardStorageKeys`; 3+ char acronym naming. Path conventions in [tasks.md "Path Conventions"](./tasks.md) match the canonical structure declared in this plan's Project Structure section. |
| VII | User Experience Consistency | **PASS** | All endpoint responses use the existing `{ status, data, metadata }` envelope; every error includes `errorCode` + `message` + `suggestion` ([contracts/progress-events.md](./contracts/progress-events.md) catalogs all 21 wizard error codes; OpenAPI specs reference the `ProblemEnvelope` schema). Long-running operations stream live progress via SignalR with a polling fallback (FR-064 / FR-066). |
| VIII | Performance Requirements | **PASS** | Synchronous endpoints budgeted < 200 ms p95 (non-upload) / < 1 s (upload accept); long-running work pushed to background jobs (`IWizardJobRunner`) and tracked in persisted `WizardJobStatus` rows. Pagination on management-view endpoints (default 50). `CancellationToken` flowed end-to-end. Subscription enumeration < 5 s p95 for ~200 visible subs. SC-001 / SC-002 / SC-003 / SC-012 / SC-014 are codified in the spec and validated by performance spot-checks ([tasks.md T141 / T142](./tasks.md)). |

**Post-Design Constitution Check: PASS — no new violations introduced by the Phase 1 design.**

## Open Clarifications Carried Into Phase 0 — RESOLVED

The clarification loop covered four of five queued questions. The remaining open question was resolved in `research.md` before tasks generation:

- **Person identity model (Q5)** — RESOLVED in [research.md §R1](./research.md): **Hybrid (Entra ID preferred, free-text local Person fallback, with a one-way `PromoteToDirectoryAsync` path)**. The `Person` UUID is stable across promotion; directory→free-text downgrade is not supported.
