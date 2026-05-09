# Implementation Plan: Tenant- & Organization-Scoped Data Isolation

**Branch**: `048-tenant-isolation` | **Date**: 2026-05-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/048-tenant-isolation/spec.md`

## Summary

Introduce a first-class `Tenant` (root authorization boundary) → `Organization` (sub-grouping) → System hierarchy across the full ATO Copilot stack, retrofit `TenantId` (and where applicable `OrganizationId`) onto every tenant-scoped row in `AtoCopilotContext` (~115 DbSets, ~60 of them currently un-scoped), and enforce isolation in three layers of defense:

1. **Application** — a request-scoped `ITenantContext` resolved from CAC/Entra claims, an attribute-driven (`[TenantScoped]` / `[GlobalReference]`) `HasQueryFilter` registration in `OnModelCreating`, and a `TenantStampingSaveChangesInterceptor` that stamps `TenantId` on inserts and rejects cross-tenant FK references.
2. **Database** — SQL Server Row-Level Security policies driven by `SESSION_CONTEXT('TenantId')` set per pooled connection via a `DbConnectionInterceptor`; SQLite (dev) falls back to the EF query filter only with a startup warning.
3. **Operational** — CSP-Admin role granted via Entra Security Group → `Auth:RoleClaimMappings:CSP.Admin` mapping; `POST /api/tenants`, `POST /api/tenants/{id}/impersonate`, `PATCH /api/tenants/{id}/status`, `POST /api/admin/migrate-to-multitenant` (+ companion `ato-cli tenant` dotnet-tool), tenant-and-organization onboarding wizard, and audit log fields `ActorTenantId` / `EffectiveTenantId` / `ImpersonatedTenantId`.

In addition, two CSP-level user experiences are introduced (US7, US8): a singleton **CSP Onboarding Wizard** (`/onboarding/csp`, MultiTenant only) that captures the hosting CSP's own legal entity, branding, support contacts, and default classification floor on first CSP-Admin sign-in — gating all other tenant-scoped endpoints with `503 CSP_ONBOARDING_INCOMPLETE` until complete — and a **CSP Cross-Tenant Operational Dashboard** (`/api/csp/dashboard/*`, CSP-Admin only) that returns aggregate KPIs across all tenants in a single round trip plus a paginated tenants list with drill-down to impersonation. These add one new singleton entity (`CspProfile`, lives in the system tenant, marked `[GlobalReference]`) and two new endpoint groups but reuse the same `ITenantContext`, query-filter, and audit machinery as US1–US6.

A further CSP-inheritance experience (US9) extends the CSP Onboarding Wizard with an **ATO Documents** step that accepts uploads of the CSP's existing ATO artifacts (PDF SSP, DOCX, OSCAL JSON, FedRAMP/eMASS XLSX, eMASS ZIP, up to 50 MB each). The system reuses existing parsers — `PdfPig` (Feature 047), `DocumentFormat.OpenXml`, the OSCAL parser (Feature 022), `ClosedXML` — to extract candidate components, persists them as `CspInheritedComponent` rows in the system tenant marked `[GlobalReference]`, and invokes the existing `ICapabilityMappingService` (Feature 045 / Feature 008) to generate `CspInheritedCapability` rows mapped to NIST 800-53 controls. Capabilities below the configurable confidence threshold (`Csp:Inheritance:MappingConfidenceThreshold`, default `0.6`) or where the mapping service errors out are persisted with `Status = NeedsReview` and a non-empty `MappingFailureReason`, surfaced in the wizard's Review step as a `Mapped : NeedsReview` tally and resolvable later by a CSP-Admin via `PATCH /api/csp/inherited-components/{id}/capabilities/{capabilityId}/review`. CSP-inherited components are visible read-only to every hosted tenant and can be referenced from tenant-local inheritance defaults without violating FR-080's cross-tenant FK rejection (because they are `[GlobalReference]`). A post-onboarding `POST /api/csp/inherited-components/import` endpoint reuses the same parsing + mapping pipeline so additional ATOs can be ingested at any time.

A consumption-side counterpart (US10) wires CSP-inherited capabilities into tenants' SSPs by **reusing existing services** from Features 008 / 024 / 038 / 043 / 044 — no parallel implementations, no new endpoints. When a Mission Owner creates an `OrgInheritanceDefault` (or `ControlInheritanceMapping`) referencing a `CspInheritedCapability` (via a new nullable `SourceCspCapabilityId` FK), the existing `IOrgInheritanceDefaultService.SaveAsync` emits a `CspCapabilityConsumed` domain event consumed by `CspCapabilityConsumptionHandler`, which: (a) creates one `EvidenceArtifact` per mapped control of `Type = CspInheritedReference` via the existing `IEvidenceArtifactService` (Feature 038); (b) calls the existing `IControlNarrativeService` (Features 008 / 024) with a single new CSP-context prompt fragment so the narrative cites `<CspProfile.DisplayName>` and the source filename; (c) on AI failure, writes a deterministic stub narrative with `Status = NeedsReview` so the SSP is never blank. A reuse-first constraint (FR-110, FR-111) governs both US9 and US10: the [Reuse-First Audit](#reuse-first-audit-per-fr-110--fr-111) subsection below enumerates every existing code path that must be reused (and any redundant code to remove) before any net-new code in Phases 15 / 16 merges; a startup health check fails fatally if duplicate DI registrations of the named services appear.

The platform supports two deployment modes (`SingleTenant` default for self-host, `MultiTenant` for CSPs like Flankspeed and C-Army) governed by a `DeploymentOptions` config block. Existing onboarding entities already carry `TenantId`; the work here generalizes that pattern to the rest of the schema and adds the runtime + DB enforcement that today does not exist (zero `HasQueryFilter` calls, no `IInterceptor`, no RLS, no `ITenantContext`).

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (`net9.0`); TypeScript 5.7 / React 19 (dashboard frontend); TypeScript 5.x / Node 20 LTS (VS Code + M365 extensions)
**Primary Dependencies**: ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (`Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.EntityFrameworkCore.Sqlite`), Microsoft.Identity.Web 3.5+, Microsoft.AspNetCore.Authentication.JwtBearer 9.0, Azure.Identity 1.13.2, Serilog 4.2, FluentAssertions 7.0 / Moq 4.20 / xUnit 2.9.3 (tests), `System.CommandLine` 2.0 (new — for `ato-cli` migration tool), `PdfPig` (existing from Feature 047 — reused for US9 PDF SSP parsing), `DocumentFormat.OpenXml` (existing — DOCX), `ClosedXML` (existing — XLSX), the OSCAL parser from Feature 022 (existing — OSCAL JSON SSP), `ICapabilityMappingService` from Features 045 / 008 (existing — NIST control auto-mapping with confidence score), React Router 7, Tailwind CSS 3, Vite 6, Axios 1.7, `@microsoft/signalr` 8.x (frontend)
**Storage**: SQLite (dev) / SQL Server 2022 (prod) via EF Core dual-provider, accessed through `AtoCopilotContext` (existing) — adds 5 new DbSets (`Tenants`, `Organizations`, `CspProfile`, `CspInheritedComponents`, `CspInheritedCapabilities`) plus 2 schema-additive columns on existing entities (`OrgInheritanceDefault.SourceCspCapabilityId/SourceCspComponentId` and `ControlInheritanceMapping.SourceCspCapabilityId/SourceCspComponentId`), one new enum value (`EvidenceArtifactType.CspInheritedReference`), and `TenantId Guid NOT NULL` + `OrganizationId Guid?` columns to ~60 existing DbSets via `EnsureSchemaAdditionsAsync` (additive, idempotent) and a SQL-Server-only migration that installs Row-Level Security policies. **Schema extensions to existing entities** (no new DbSets): `OrgInheritanceDefault` (Feature 044) gains nullable `SourceCspCapabilityId Guid?` + `SourceCspComponentId Guid?` FKs; `ControlInheritanceMapping` (Feature 043) gains the same; Feature 038's `EvidenceArtifactType` enum gains a `CspInheritedReference` value with structured payload reusing the existing `EvidenceArtifact.Payload` JSON column. Test runs use `EnsureCreatedAsync` and SQLite.
**Testing**: xUnit 2.9.3 + FluentAssertions 7.0 + Moq 4.20 + WebApplicationFactory for integration; new test fixtures `MultiTenantWebApplicationFactory<TStartup>` (seeds 2 tenants × N rows) and `RlsIntegrationFixture` (SQL Server testcontainer) exercise raw SELECT/INSERT bypass scenarios. Constitution Principle III boundary tests cover null/empty/cross-tenant `TenantId` cases.
**Target Platform**: Linux containers on Azure Government Container Apps (primary) and AzureCloud (secondary); SQL Server 2022 on Azure SQL Managed Instance (Gov) or local Docker; SQLite for dev/test only.
**Project Type**: Multi-project .NET solution + React SPA + two TypeScript extensions (existing structure — no new top-level project except a new `Ato.Copilot.Cli` console project for `ato-cli`).
**Performance Goals**: Constitution Principle VIII applies. Targets: tenant-resolution middleware adds ≤ 5 ms p95 to request latency; SQL Server RLS adds ≤ 10 % to read p95 with the new `(TenantId, …)` composite indexes added on hot tables; `GET /api/tenants` returns ≤ 200 ms for 100 tenants; `POST /api/admin/migrate-to-multitenant` processes 100 k rows in ≤ 60 s on a single transaction in dev SQL Server.
**Constraints**: Cannot break existing single-tenant self-host upgrade path (US3) — the same Docker image + DB must boot in both modes. Cannot regress Constitution Principle IV (Government compliance) — all new endpoints must remain compatible with `AzureUSGovernment` and accept Managed Identity. Audit retention (existing 7-year / 2555-day policy) must be preserved for `Disabled` tenants.
**Scale/Scope**: ~115 DbSets touched (60 to retrofit + 55 already scoped or `[GlobalReference]`); 250+ HTTP endpoints; ~130 MCP tools; expected 1 – 200 tenants per CSP deployment; expected 1 – 50 organizations per tenant; expected ~50k – 5 M rows per tenant in compliance tables.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Initial Status | Notes |
|-----------|----------------|-------|
| **I. Documentation as Source of Truth** | PASS | Will add `docs/architecture/tenant-isolation.md` and `docs/operations/multi-tenant-migration.md` as part of this feature. All decisions cite spec FRs. |
| **II. BaseAgent/BaseTool Architecture** | PASS | Existing `BaseTool` contract is untouched. Tools resolve `ITenantContext` via constructor DI exactly as they do `IUserContext` today; no tool inheritance changes. |
| **III. Testing Standards (NON-NEGOTIABLE)** | PASS | Phase 1 contracts include unit + integration + RLS-bypass + manual-test scenarios. Cross-tenant attempts are explicit boundary cases; constitution-mandated. |
| **IV. Azure Government & Compliance First** | PASS | No new Azure SDKs introduced; existing `DefaultAzureCredential` chain reused. New `groupMembershipClaims` requirement on Entra app registration is documented for both `AzureUSGovernment` and `AzureCloud`. |
| **V. Observability & Structured Logging** | PASS | `TenantResolutionMiddleware`, the `SaveChanges` interceptor, the impersonation endpoint, and the migration utility all log structured events with correlation IDs and the tenant fields required by FR-060. |
| **VI. Code Quality & Maintainability** | PASS | Tenant scoping is implemented as a single attribute (`[TenantScoped]` / `[GlobalReference]`) + reflection-driven model-builder helper to avoid 60× copy-paste. Magic GUID `00000000-...-001` removed (FR-072). Public API surface gains XML docs. |
| **VII. UX Consistency** | PASS | Tenant errors use the standard envelope (`status`, `errorCode`, `suggestion`): `TENANT_NOT_PROVISIONED`, `TENANT_SUSPENDED`, `TENANT_DISABLED`, `MISSING_TENANT_CLAIM`, `FORBIDDEN_NOT_CSP_ADMIN`. Single-tenant mode hides all multi-tenant UI to preserve mode parity for self-host users. |
| **VIII. Performance Requirements** | PASS — with mitigation | RLS adds latency (see Performance Goals). Mitigation: composite `(TenantId, <natural-key>)` indexes added to all retrofitted tables; pagination unchanged; cancellation tokens propagated. Migration endpoint runs in a single transaction with progress logged to honor the 30-s complex-op budget for 100 k-row datasets. |

**Gate result**: PASS. No `Complexity Tracking` entries required.

**Post-Design Re-check (2026-05-07, after Phase 1)**: All Phase 0 / Phase 1 artifacts (research.md, data-model.md, 7× contracts, quickstart.md) reviewed against the same eight principles. No new violations surfaced:

- The attribute-driven query-filter approach (research §1) eliminates 60× per-entity boilerplate, reinforcing Principle VI.
- The `[GlobalReference]` catalog (data-model §5) and the `Ato.Copilot.System` tenant pattern keep the cross-tenant surface explicit and auditable, reinforcing Principle III.
- The `ato-cli` project (research §6) is the only new top-level deliverable; it introduces one new external dependency (`System.CommandLine` 2.0, MIT-licensed, used elsewhere in the .NET ecosystem) and emits the same audit events as the admin endpoint, preserving Principle V.
- All new HTTP responses use the standard error envelope with documented `errorCode` values (Principle VII).
- Performance gate stays PASS-with-mitigation: composite indexes are now enumerated per retrofitted table in data-model §6 and tested via the SQL Server testcontainer fixture in quickstart §5.

No `Complexity Tracking` entries added.

## Reuse-First Audit (per FR-110 / FR-111)

Phases 15 (US9 — CSP-inherited components) and 16 (US10 — capability consumption) introduce **zero** new mapping algorithms, **zero** new narrative prompt template families, **zero** new document parsers, **zero** new evidence persistence services, and **zero** new inheritance bookkeeping services. Every existing code path below MUST be reused; the surgical extension required for US9 / US10 is listed alongside, plus any redundant code identified for removal in the same PR.

> **Audit completion** — T217 produced [`research-reuse-audit.md`](research-reuse-audit.md) (the per-target inventory). Three of the named interfaces (`ICapabilityMappingService`, `IControlNarrativeService`, `IOrgInheritanceDefaultService` → real name `IOrgInheritanceService`) do not exist by those names today; T218 extracts them from existing concrete classes so the FR-110 health check can enforce uniqueness against real types. The audit found **zero duplicate registrations** to delete today; "None today" is the verified column-3 answer for every row.

| Capability | Existing service / file (reuse) | Surgical extension for US9 / US10 | Redundant code to remove (verified by T217) |
|---|---|---|---|
| Capability → NIST control mapping | New `ICapabilityMappingService` interface created by T218 over a new thin AI wrapper class (no pre-existing AI mapper at the path the spec referenced; `CapabilityImportService` does spreadsheet-driven mapping only) | Wrapped by `CspCapabilityMappingService` (T204) which only adds confidence-threshold + `NeedsReview` fallback logic; underlying mapping algorithm unchanged | None today (no parallel mapper exists). Health check enforces uniqueness going forward |
| AI control narrative generation | `Ato.Copilot.Core.Services.NarrativeTemplateService` (`src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs`); T218 extracts `IControlNarrativeService` over the existing class (singleton factory at `AtoCopilotMcpServiceExtensions.cs:81`) | Single new `CspContext` request field + one prompt-template fragment (T227) appended to existing `Prompts/NarrativeGeneration.prompt.txt`; no new service, no new template family | None today. Audit recorded that any future stub-narrative builder under `src/Ato.Copilot.Core/Services/Narratives/` MUST be removed in the PR that introduces it; the FR-109 deterministic stub is a string constant on `CspCapabilityConsumptionHandler`, not a service |
| PDF parsing | `Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf.SspPdfExtractionService` (`ISspPdfExtractionService`, registered `AddScoped` at `Mcp/Program.cs:369`) — corrected path from `src/Ato.Copilot.Agents/Services/Parsing/PdfDocumentParser.cs` | Reused as-is via `CspAtoDocumentParser` dispatcher (T202); no parser change | None |
| OSCAL JSON parsing | **Net-new** — Feature 022 is OSCAL **export-only**; no inbound SSP parser exists. New `OscalSspJsonParser` follows the `FrameworkImportService` parsing pattern and is reachable only via `CspAtoDocumentParser` (T202) | Net-new minimal class (T202); MUST be the only OSCAL-SSP parser in the codebase | None |
| DOCX parsing | `DocumentFormat.OpenXml` package (no shared text-walker class today) | `private static` text-walker helper inside `CspAtoDocumentParser` (T202); no new service class | None |
| XLSX parsing (FedRAMP / SAR / POAM workbooks) | `ClosedXML` package (no shared cell-walker class today) | `private static` cell-walker helper inside `CspAtoDocumentParser` (T202); no new service class | None |
| Evidence persistence | `IEvidenceArtifactService` (`src/Ato.Copilot.Core/Interfaces/Compliance/IEvidenceArtifactService.cs`, registered `TryAddSingleton` at `McpServiceExtensions.cs:72`) and `IEvidenceStorageService` (`AddSingleton` at `Agents/ServiceCollectionExtensions.cs:78`) | Add `EvidenceArtifactType.CspInheritedReference` enum value + structured payload schema (T224); reuse the existing service surface | None today. Audit recorded that any per-feature inline `EvidenceArtifact` constructions bypassing `IEvidenceArtifactService` MUST be removed if discovered during T218 |
| Inheritance bookkeeping | `IOrgInheritanceService` (`src/Ato.Copilot.Core/Interfaces/Compliance/IOrgInheritanceService.cs`, registered `TryAddSingleton` at `McpServiceExtensions.cs:48`) — corrected from spec name `IOrgInheritanceDefaultService`; entity is `ControlInheritance` (no "Mapping" suffix) at `RmfModels.cs:652` | T218 adds a new `SaveAsync` method to `IOrgInheritanceService` that emits the `CspCapabilityConsumed` event when `SourceCspCapabilityId.HasValue`; T223 adds the FK columns to `OrgInheritanceDefault` and `ControlInheritance`; existing 4 methods untouched | None today. Audit recorded that any future per-tenant manual "import shared component" affordance MUST go through `IOrgInheritanceService.SaveAsync` |
| Narrative governance status (`NeedsReview`, regen endpoint) | `POST /api/dashboard/systems/{systemId}/controls/{controlId}/regenerate-ai` (`DashboardEndpoints.cs:599`, calls `CapabilityService.RegenerateNarrativeWithAiAsync`) — confirmed existing endpoint, no new endpoint to be added | Reused unchanged for the FR-109 fallback path; T229's frontend button calls this exact route | None |
| Document parser dispatch | New thin `CspAtoDocumentParser` dispatcher (T202) | Net-new — only routes `contentType` → existing-parser; owns no parsing logic itself | n/a |
| Capability-consumption side-effects | New `CspCapabilityConsumptionHandler` (T226) | Net-new — composes `IEvidenceArtifactService` + `IControlNarrativeService` + the FR-109 deterministic stub string constant; contains zero parsing / mapping / narrative logic | n/a |

**Allow-list of permitted net-new classes under `src/Ato.Copilot.Core/Services/Tenancy/Csp*`**: `CspProfileService`, `CspAtoDocumentParser` (dispatcher), `CspComponentExtractionService`, `CspCapabilityMappingService` (wrapper), `CspInheritedComponentService`, `CspCapabilityConsumptionHandler`. Any other class added under that path MUST be rejected by the `CspInheritanceReuseAuditTests` reflection check (T222).

**Enforcement**:

- Task **T217** produces `specs/048-tenant-isolation/research-reuse-audit.md` listing every file path identified for reuse / refactor / removal, completing the TBD column above.
- Task **T218** applies the refactor (removes redundant code, consolidates duplicate DI registrations, wires the FR-110 startup health check) and **MUST land before any of T194–T216 (Phase 15 implementation) or T223–T230 (Phase 16 implementation) begin**.
- An `IHostedService` startup health check (T228) fails fatally if more than one implementation is registered for `ICapabilityMappingService`, `IControlNarrativeService`, `IEvidenceArtifactService`, `IEvidenceStorageService`, `IOrgInheritanceDefaultService`, or `ICspAtoDocumentParser` (FR-110).
- A unit test (`CspInheritanceReuseAuditTests`, T222) enforces the same property in CI and additionally verifies the allow-list above.

This audit reinforces Constitution Principle VI (Code Quality & Maintainability): a duplicate registration of any named service MUST fail the build.

## Project Structure

### Documentation (this feature)

```text
specs/048-tenant-isolation/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 — decisions + alternatives
├── data-model.md        # Phase 1 — Tenant, Organization, scoping rules, [GlobalReference] catalog
├── quickstart.md        # Phase 1 — local dev walkthrough (single-tenant + multi-tenant + impersonation)
├── contracts/
│   ├── tenants.openapi.yaml          # /api/tenants, /api/tenants/{id}, /api/tenants/{id}/impersonate, /api/tenants/{id}/status
│   ├── tenant-onboarding.openapi.yaml # /api/onboarding/tenant/* wizard endpoints
│   ├── csp-onboarding.openapi.yaml   # NEW — /api/csp/onboarding/* (US7)
│   ├── csp-dashboard.openapi.yaml    # NEW — /api/csp/dashboard/* (US8)
│   ├── csp-inherited-components.openapi.yaml # NEW — /api/csp/onboarding/atos/* and /api/csp/inherited-components/* (US9)
│   ├── admin-migration.openapi.yaml  # /api/admin/migrate-to-multitenant (+ /preview)
│   ├── audit.openapi.yaml            # /api/audit query endpoint
│   ├── global-baselines.openapi.yaml # /api/global-baselines (publish/unpublish)
│   ├── itenantcontext.cs.md          # Shape of ITenantContext + ITenantContextAccessor
│   └── ato-cli-tenant.md             # Surface of ato-cli tenant subcommand
├── checklists/
│   └── requirements.md  # Existing (created by /speckit.specify)
└── tasks.md             # Phase 2 — generated by /speckit.tasks (NOT created here)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/                          # Domain models + EF context
│   ├── Models/
│   │   ├── Tenancy/                           # NEW — Tenant.cs, Organization.cs, CspProfile.cs, CspInheritedComponent.cs, CspInheritedCapability.cs, TenantStatus.cs, OnboardingState.cs, ClassificationLevel.cs, CspInheritedComponentStatus.cs, CspInheritedCapabilityStatus.cs
│   │   ├── Tenancy/Attributes/                # NEW — TenantScopedAttribute.cs, GlobalReferenceAttribute.cs
│   │   ├── Tenancy/Migration/                 # NEW — MultiTenantMigrationReport.cs, TenantOverride.cs
│   │   └── … (existing 60 entities gain TenantId Guid + OrganizationId Guid? properties)
│   ├── Interfaces/
│   │   ├── Auth/IUserContext.cs               # EXISTING — unchanged
│   │   ├── Tenancy/ITenantContext.cs          # NEW
│   │   ├── Tenancy/ITenantContextAccessor.cs  # NEW (used outside HTTP scope, e.g., background services)
│   │   ├── Tenancy/ITenantProvisioningService.cs   # NEW
│   │   ├── Tenancy/ICspProfileService.cs           # NEW — singleton get/upsert/onboarding-state (US7)
│   │   ├── Tenancy/ICspAtoDocumentParser.cs        # NEW — dispatches PDF/DOCX/OSCAL/XLSX/ZIP to format-specific parsers (US9)
│   │   ├── Tenancy/ICspComponentExtractionService.cs # NEW — maps parser output → CspInheritedComponent rows (US9)
│   │   ├── Tenancy/ICspCapabilityMappingService.cs # NEW — wraps existing ICapabilityMappingService, applies confidence threshold + NeedsReview fallback (US9)
│   │   ├── Tenancy/ICspInheritedComponentService.cs # NEW — publish / archive / remap / capability review (US9)
│   │   └── Tenancy/IGlobalBaselineService.cs       # NEW
│   ├── Data/
│   │   ├── Context/AtoCopilotContext.cs       # MODIFIED — adds 2 DbSets, OnModelCreating reflection-driven query filters
│   │   ├── Interceptors/TenantStampingSaveChangesInterceptor.cs   # NEW
│   │   ├── Interceptors/SqlServerSessionContextConnectionInterceptor.cs # NEW (RLS)
│   │   └── Migrations/EnsureSchemaAdditions/                      # MODIFIED — append tenant columns + indexes
│   └── Services/Tenancy/
│       ├── TenantContext.cs                   # NEW — request-scoped impl
│       ├── TenantProvisioningService.cs       # NEW
│       ├── CspProfileService.cs               # NEW — singleton-row CRUD + state machine for US7
│       ├── CspAtoDocumentParser.cs            # NEW — dispatches by SourceFormat to PdfPig (047) / OpenXml / OSCAL (022) / ClosedXML / ZIP enumerator (US9)
│       ├── CspComponentExtractionService.cs   # NEW — produces CspInheritedComponent rows from parser output (US9)
│       ├── CspCapabilityMappingService.cs     # NEW — wraps ICapabilityMappingService, applies threshold, marks NeedsReview on failure (US9)
│       ├── CspInheritedComponentService.cs    # NEW — publish / archive / remap / capability review state machine (US9)
│       └── GlobalBaselineService.cs           # NEW
│
├── Ato.Copilot.Mcp/                           # ASP.NET host
│   ├── Middleware/
│   │   ├── CacAuthenticationMiddleware.cs     # MODIFIED — surface group GUIDs as roles via RoleClaimMappings
│   │   ├── TenantResolutionMiddleware.cs      # NEW — runs after auth, before authorization; also enforces CSP-onboarding gate (US7)
│   │   └── AuditLoggingMiddleware.cs          # MODIFIED — adds Actor/Effective/ImpersonatedTenantId fields
│   ├── Endpoints/
│   │   ├── TenantsEndpoints.cs                # NEW — /api/tenants, impersonation, status
│   │   ├── AdminMigrationEndpoints.cs         # NEW — /api/admin/migrate-to-multitenant + /preview
│   │   ├── AuditQueryEndpoints.cs             # NEW — /api/audit
│   │   ├── GlobalBaselineEndpoints.cs         # NEW
│   │   ├── Csp/CspOnboardingEndpoints.cs      # NEW — /api/csp/onboarding/* (US7) including /atos/upload (US9)
│   │   ├── Csp/CspDashboardEndpoints.cs       # NEW — /api/csp/dashboard/* (US8)
│   │   ├── Csp/CspInheritedComponentEndpoints.cs # NEW — /api/csp/inherited-components/* and /import + /remap + /capabilities/{id}/review (US9)
│   │   └── Onboarding/TenantOnboardingEndpoints.cs   # NEW — wizard step API
│   ├── Configuration/
│   │   ├── DeploymentOptions.cs               # NEW — Mode + DefaultTenantId + Tenants:AllowSelfOnboarding
│   │   └── RoleClaimMappingsOptions.cs        # NEW
│   └── Services/Tenancy/
│       ├── TenantImpersonationService.cs      # NEW — issues + validates impersonation cookies
│       └── MultiTenantMigrationService.cs     # NEW — shared by endpoint + ato-cli
│
├── Ato.Copilot.Cli/                           # NEW project — `ato-cli` dotnet-tool
│   ├── Commands/Tenant/
│   │   ├── TenantDefaultCommand.cs
│   │   ├── TenantAssignCommand.cs
│   │   └── TenantMigrateCommand.cs
│   └── Program.cs
│
├── Ato.Copilot.Agents/                        # MCP tool implementations
│   └── … (no per-tool changes; tools already get DI scope; ITenantContext is auto-resolved)
│
├── Ato.Copilot.Channels/ + Ato.Copilot.State/ # MODIFIED — propagate TenantId / EffectiveTenantId on stream context
│
└── Ato.Copilot.Dashboard/
    └── src/
        ├── features/tenancy/
        │   ├── TenantPicker.tsx               # NEW — header dropdown, hidden in SingleTenant mode
        │   ├── ImpersonationBanner.tsx        # NEW — visible while impersonation active
        │   └── api.ts                         # NEW — wraps /api/tenants
        ├── features/csp-onboarding/           # NEW — US7 wizard
        │   ├── CspWizard.tsx
        │   ├── steps/IdentityStep.tsx
        │   ├── steps/SupportContactStep.tsx
        │   ├── steps/ClassificationStep.tsx
        │   ├── steps/AtoDocumentsStep.tsx          # NEW — multi-file upload + extracted-component preview (US9)
        │   ├── steps/ReviewStep.tsx                # MODIFIED — surfaces Components / Capabilities Mapped / NeedsReview tally (US9)
        │   └── components/ComponentExtractionPreview.tsx # NEW — per-document drill-down with NeedsReview list (US9)
        ├── features/csp-inherited-components/  # NEW — post-onboarding management UI (US9)
        │   ├── CspInheritedComponentsPage.tsx
        │   ├── ComponentDetailDrawer.tsx
        │   ├── NeedsReviewQueue.tsx                 # CSP-Admin queue for resolving NeedsReview capabilities
        │   └── api.ts
        ├── features/csp-dashboard/            # NEW — US8 cross-tenant ops view
        │   ├── CspDashboardPage.tsx
        │   ├── widgets/SummaryCards.tsx
        │   ├── widgets/AtoStatusChart.tsx
        │   ├── widgets/FindingsBySeverityChart.tsx
        │   ├── TenantsTable.tsx
        │   └── api.ts
        ├── features/onboarding/
        │   └── TenantWizard/                  # NEW — multi-step form for FR-054
        └── routes.tsx                          # MODIFIED — guards on OnboardingState (Tenant + CspProfile)

extensions/
├── vscode/                                    # MODIFIED — surface impersonated tenant in status bar
└── m365/                                      # MODIFIED — Adaptive Card carries tenant indicator

tests/
├── Ato.Copilot.Tests.Unit/Tenancy/            # NEW — TenantContext tests, attribute discovery tests, interceptor tests
├── Ato.Copilot.Tests.Integration/Tenancy/     # NEW — middleware, query-filter, impersonation, migration endpoint
└── Ato.Copilot.Tests.Integration/Rls/         # NEW — SQL Server testcontainer-based BLOCK predicate tests

docs/
├── architecture/tenant-isolation.md           # NEW
└── operations/multi-tenant-migration.md       # NEW
```

**Structure Decision**: Multi-project .NET solution + React SPA + two TypeScript extensions, matching the existing repository layout (see [AGENTS.md](../../AGENTS.md)). One new project (`Ato.Copilot.Cli`) is added for the `ato-cli` dotnet-tool — rationale documented in [research.md](research.md) §6.

## Complexity Tracking

> Constitution Check passed without violations. No entries.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| _none_ | _n/a_ | _n/a_ |
