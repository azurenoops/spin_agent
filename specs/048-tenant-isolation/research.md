# Phase 0 Research: Tenant- & Organization-Scoped Data Isolation

**Feature**: `048-tenant-isolation` | **Date**: 2026-05-07 | **Spec**: [spec.md](spec.md)

This file resolves every open technical question identified during planning. Each entry records the **decision**, the **rationale**, and the **alternatives considered**. Spec clarifications recorded in `spec.md` § Clarifications are referenced rather than re-litigated.

---

## 1. EF Core query-filter registration strategy

**Decision** — Use a `[TenantScoped]` / `[GlobalReference]` attribute pair on entity classes, and register `HasQueryFilter` reflectively in `AtoCopilotContext.OnModelCreating` for every entity carrying `[TenantScoped]`. The filter expression is built dynamically: `e => e.TenantId == _tenantContext.EffectiveTenantId || _tenantContext.IsCspAdmin && _tenantContext.ImpersonatedTenantId == null`.

**Rationale** — There are ~60 entities to retrofit; per-entity `modelBuilder.Entity<T>().HasQueryFilter(...)` calls are 60 lines of copy-paste plus 60 lines whenever a developer adds a new entity. An attribute-driven approach (a) makes the *intent* explicit on the entity itself, (b) cannot be forgotten when a new tenant-scoped entity is added — the developer marks the class once, (c) enables a startup self-check that asserts every non-`[GlobalReference]` entity has either `[TenantScoped]` or an explicit `[ExemptFromTenantScoping]` justification.

**Alternatives considered**
- **Per-entity manual calls.** Rejected: 60 sites of drift.
- **Convention based purely on the presence of a `TenantId` property.** Rejected: ambiguous when an entity has a `TenantId` but is intentionally global (e.g., system-tenant rows).
- **`IModelCustomizer` instead of `OnModelCreating`.** Rejected: harder for new contributors to discover; `OnModelCreating` is the canonical spot.

**Reference** — Spec FR-020, FR-022, FR-024.

---

## 2. SQL Server Row-Level Security predicate shape

**Decision** — Install one `SECURITY POLICY` per tenant-scoped table with two predicates:

```sql
CREATE SECURITY POLICY [Tenancy].[<Table>_RLS]
ADD FILTER PREDICATE [Tenancy].[fn_TenantPredicate]([TenantId]) ON [dbo].[<Table>],
ADD BLOCK PREDICATE [Tenancy].[fn_TenantPredicate]([TenantId]) ON [dbo].[<Table>] AFTER INSERT,
ADD BLOCK PREDICATE [Tenancy].[fn_TenantPredicate]([TenantId]) ON [dbo].[<Table>] AFTER UPDATE
WITH (STATE = ON);
```

…where `fn_TenantPredicate` is an inline TVF returning `1` when:

```sql
@TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
   OR CAST(SESSION_CONTEXT(N'IsCspAdminAllTenants') AS bit) = 1
   OR @TenantId = '00000000-0000-0000-0000-000000000000'  -- system tenant for [GlobalReference] rows
```

**Rationale** — A single predicate function lets us amend the cross-tenant bypass logic in one place. Including the system-tenant short-circuit avoids requiring CSP-Admin context to read NIST controls. `BLOCK PREDICATE AFTER INSERT/UPDATE` (no `BEFORE`) keeps the table writeable by ETL scripts that target the correct tenant; using `AFTER` rather than `BEFORE` matches the documented Microsoft pattern for catch-the-mistake semantics.

**Alternatives considered**
- **Inline literal predicate per table.** Rejected: cannot amend bypass policy in one place.
- **`BEFORE` block predicate.** Rejected: subtle interaction with EF Core temporal/optimistic concurrency.
- **No `BLOCK PREDICATE`.** Rejected: defeats US5.

**Reference** — Spec FR-030, FR-031, FR-032, US5.

---

## 3. Setting `SESSION_CONTEXT` per pooled connection

**Decision** — Implement a `SqlServerSessionContextConnectionInterceptor : DbConnectionInterceptor` (EF Core 9 `IDbConnectionInterceptor`) that, on `ConnectionOpenedAsync`, executes `EXEC sp_set_session_context` for `TenantId` and (when applicable) `IsCspAdminAllTenants`. The interceptor reads `ITenantContextAccessor.Current` (resolved via DI from the same `IServiceProvider` that built the `DbContext`).

**Rationale** — Connection-pool reuse means the same physical connection serves consecutive requests for different tenants; `SESSION_CONTEXT` is per-connection state and must be reset on every "logical open". `IDbConnectionInterceptor.ConnectionOpenedAsync` is the EF-supported hook that runs before any command on a freshly checked-out connection. Setting `read_only = 0` (default) lets the tenant value be overridden by the next connection borrower without `RESET CONNECTION`.

**Alternatives considered**
- **Set on every command via `IDbCommandInterceptor`.** Rejected: 2× the chatter; redundant if the connection is reused mid-request.
- **A custom `DbConnection` wrapper.** Rejected: heavier, fights EF's pooling.
- **`ApplicationIntent=ReadOnly` style connection-string segregation per tenant.** Rejected: explodes the connection pool count.

**Reference** — Spec FR-031.

---

## 4. SQLite (dev) vs SQL Server (prod) parity

**Decision** — At startup, detect the EF provider; if SQLite, log:

```
WRN  Tenant isolation: SQL Server Row-Level Security is unavailable on this provider. EF query filters are the sole protection. Do not use SQLite for production tenants.
```

…and skip the connection interceptor and migration step that installs RLS. EF query filters are identical across providers and remain in force.

**Rationale** — SQLite has no equivalent of Row-Level Security or `SESSION_CONTEXT`. The dev experience must continue to work for self-host upgrade testing (US3). A loud warning at boot makes the lower assurance level obvious; production deployments target SQL Server.

**Alternatives considered**
- **Emulate `SESSION_CONTEXT` via SQLite session vars.** Rejected: fragile, complex, no real BLOCK semantics.
- **Refuse to start with SQLite when `ATO_DEPLOYMENT__MODE=MultiTenant`.** Rejected: blocks dev workflows where SQLite is the only practical local choice.

**Reference** — Spec FR-033.

---

## 5. CSP-Admin role mapping (group GUIDs → role claim)

**Decision** — Extend `CacAuthenticationMiddleware` to read a new `Auth:RoleClaimMappings` config dictionary (`Dictionary<string, Guid[]>` — role-name → list of Entra group object-IDs). For every `groups` claim on the inbound principal, look up the group GUID; for every match, append a `ClaimTypes.Role` claim with the configured role name. Log only the role-name (never the group-id) to honor Government least-privilege logging.

**Rationale** — The user explicitly chose Entra Security Group mapping (Spec § Clarifications Q1). Multiple groups can map to the same role; multiple roles can share a group. Mapping happens in middleware so downstream `[Authorize(Roles = "CSP.Admin")]` works without change. Token-refresh-driven revocation (FR-050) is automatic since each token is re-evaluated.

**Alternatives considered**
- **App Roles in Entra app registration manifest.** Rejected per clarification.
- **Custom `IClaimsTransformation`.** Considered but `IClaimsTransformation` runs after `Authentication` and does not see the `groups` claim if the upstream middleware filters it. Middleware is more reliable.

**Reference** — Spec FR-050.

---

## 6. New `Ato.Copilot.Cli` project for `ato-cli`

**Decision** — Add a new console project `src/Ato.Copilot.Cli/Ato.Copilot.Cli.csproj` published as a dotnet-tool (`<PackAsTool>true</PackAsTool>`). It uses `System.CommandLine` 2.0, references `Ato.Copilot.Core` for `AtoCopilotContext`, and shares the migration logic with `MultiTenantMigrationService` so the admin endpoint and the CLI execute identical SQL.

**Rationale** — Spec § Clarifications Q5 chose Option C (hybrid: built-in admin endpoint + CLI). A dedicated project is preferable to bolting CLI commands onto `Ato.Copilot.Mcp` because (a) the CLI must run *without* the ASP.NET host and HTTP stack, (b) packaging as a dotnet-tool gives operators a one-line `dotnet tool install --global Ato.Copilot.Cli` install path, (c) air-gapped DBs need a small offline binary.

**Alternatives considered**
- **Add commands to existing tool projects.** Rejected: violates SRP (these are not MCP tools).
- **Standalone shell script.** Rejected: would re-implement DbContext logic, drift inevitable.
- **Single-file deployment without dotnet-tool.** Considered as fallback for fully offline environments; documented in Quickstart §6.

**Reference** — Spec FR-073, FR-074, FR-075, FR-076.

---

## 7. Impersonation transport: cookie vs header vs claim

**Decision** — Issue a short-lived (1-hour) **HTTP-only, Secure, SameSite=Strict cookie** named `ato-impersonate` whose value is a signed JWT containing `sub` (impersonator OID), `actor_tid` (impersonator's home tenant), `eff_tid` (impersonated tenant), `iat`, `exp`. `TenantResolutionMiddleware` verifies the cookie's signature and the impersonator's still-current `CSP.Admin` role on every request. Clients can also set `X-Impersonated-Tenant: {tenantId}` once per request (the dashboard does this via Axios interceptor); the middleware rejects the header unless `CSP.Admin` is present.

**Rationale** — A signed cookie survives full-page reloads (so the dashboard does not need to re-impersonate after a hard refresh) without exposing the impersonation token to JavaScript (XSS hardening). Re-evaluating the role on every request implements FR-050's "membership changes take effect on next token refresh" — even sooner: on next request. The `X-Impersonated-Tenant` header path supports server-to-server scripted operations.

**Alternatives considered**
- **Server-side session table.** Rejected: adds DB writes on every login.
- **Mint a new access token via Entra On-Behalf-Of.** Rejected: requires registering a separate app + admin consent in every customer Entra tenant; massive operational burden.
- **Embed `eff_tid` directly in the user's claims via `IClaimsTransformation`.** Rejected: claims are immutable per request; we need switchable scope.

**Reference** — Spec FR-051, FR-052.

---

## 8. Backfill strategy for the 60+ existing un-scoped tables

**Decision** — Two-phase additive deployment.

- **Phase A (one release ahead, optional but recommended)**: `EnsureSchemaAdditionsAsync` adds `TenantId UNIQUEIDENTIFIER NULL` and `OrganizationId UNIQUEIDENTIFIER NULL` columns + supporting indexes to every retrofitted table. No data movement. The application keeps running.
- **Phase B (this feature)**: At startup, if `ATO_DEPLOYMENT__MODE = SingleTenant`, run `MultiTenantMigrationService.BackfillAsync(defaultTenantId)`; if `MultiTenant`, fail fast and instruct operators to use the admin endpoint or `ato-cli tenant migrate`. Once `TenantId` is non-null on every row, an `ALTER COLUMN … NOT NULL` statement is issued (idempotent, safe to re-run). RLS policies are installed only after `NOT NULL` succeeds.

**Rationale** — Adding non-nullable columns with a default and then backfilling would lock huge tables on SQL Server; the additive nullable-then-tighten pattern matches the existing `EnsureSchemaAdditionsAsync` discipline. Splitting backfill from schema-add lets CSPs run the backfill during a maintenance window with the existing app still serving reads.

**Alternatives considered**
- **Single migration with default value.** Rejected: long table locks on hot tables (`ComplianceFinding`, `EvidenceArtifact`).
- **Triggers to populate on the fly.** Rejected: opaque, hard to debug, conflict with EF interceptors.

**Reference** — Spec FR-003, FR-070, FR-071, FR-073.

---

## 9. `[GlobalReference]` catalog — which entities qualify

**Decision** — The following are `[GlobalReference]` (live in the system tenant `00000000-0000-0000-0000-000000000000`, readable by all):

- `NistControl`, `FrameworkControl`, `ComplianceFramework`, `ControlBaseline` (when seeded from NIST 800-53 Rev 5 catalog)
- `InformationType` (when sourced from NIST 800-60)
- `SecurityCapability` (when seeded from CSP-published catalog)
- `Tenants` (the table that defines tenants is itself global)
- All published `EvidenceArtifact` / `ControlNarrative` rows produced by `POST /api/global-baselines/publish` (FR-081)

Everything else carries `[TenantScoped]` and a `TenantId` column.

**Rationale** — Reference data must be readable by every tenant or onboarding fails. CSP-published baselines are, by definition, content the CSP intends to share across all tenants. The `Tenants` table itself cannot be tenant-scoped (chicken-and-egg).

**Alternatives considered**
- **Per-tenant copies of the NIST catalog.** Rejected: 800+ controls × 200 tenants = 160k duplicated rows for no semantic benefit.
- **Treat all reference data as `[TenantScoped]` with system tenant.** Same outcome, but `[GlobalReference]` is more self-documenting.

**Reference** — Spec FR-022, FR-080, FR-081.

---

## 10. Channels & MCP-tool propagation of `ITenantContext`

**Decision** — `Ato.Copilot.Channels` (`StreamContext`) gains `TenantId` and `EffectiveTenantId` fields populated by the host (`Ato.Copilot.Mcp`) at the moment the channel hands control to the agent runner. Tools that already take `IUserContext` via constructor DI gain a second parameter for `ITenantContext`; tools that read `IHttpContextAccessor` directly are migrated to the typed interface as part of this feature (constitution Principle II).

**Rationale** — Tool authors should not have to "know" about the claim shape; a typed interface is the smallest possible API. Channels propagation is essential because tools invoked from VS Code or Teams do not have an `HttpContext`.

**Alternatives considered**
- **AsyncLocal-based ambient context.** Rejected: hostile to test fixtures; surprising to debug.
- **Pass `TenantId` as a parameter to every tool.** Rejected: 130 tools × every method.

**Reference** — Spec edge case "MCP tool invoked from VS Code extension or Teams bot"; Constitution Principle II, VI.

---

## 11. Audit log schema extension

**Decision** — Add three nullable `Guid?` columns to `AuditLogEntry`: `ActorTenantId`, `EffectiveTenantId`, `ImpersonatedTenantId`. Existing rows remain valid (NULL). `AuditLoggingMiddleware` populates them from `ITenantContext`. `GET /api/audit` (CSP-Admin only) accepts query params `tenantId`, `actorTenantId`, `actorOid`, `action`, `from`, `to`, `page`, `pageSize` and is paginated with a default page size of 50 (Constitution Principle VIII).

**Rationale** — Backwards-compatible (old rows simply have NULL tenant fields). The dual `ActorTenantId` + `EffectiveTenantId` capture impersonation in a single row without a join, which is required for FR-052/FR-060.

**Alternatives considered**
- **Add a new `AuditLogTenancy` table joined by `AuditLogId`.** Rejected: doubles writes on the hottest table.
- **Embed in the existing JSON `Details` blob.** Rejected: makes querying impossible without `JSON_VALUE()` per row.

**Reference** — Spec FR-060, FR-061.

---

## 12. Tenant lifecycle status enforcement point

**Decision** — `TenantResolutionMiddleware` is the single enforcement point. After resolving the effective tenant, it loads the `Tenants` row (cached in `IMemoryCache` for 30 s with a sliding expiration) and consults `Status`:

| `Status` | HTTP method | Behavior |
|----------|-------------|----------|
| `Active` | any | Pass through. |
| `Suspended` | `GET`/`HEAD`/`OPTIONS` | Pass through. |
| `Suspended` | `POST`/`PUT`/`PATCH`/`DELETE` | Short-circuit with `423 TENANT_SUSPENDED` (envelope schema). |
| `Disabled` | any | Short-circuit with `401 TENANT_DISABLED`. |
| `Disabled` | any (CSP-Admin impersonation active) | Pass through (read-only behavior enforced by impersonation). |

**Rationale** — Single enforcement point keeps the matrix tractable and testable. 30-second cache balances responsiveness on status flip with avoiding a DB lookup on every request (Constitution VIII). MCP tool calls share the same middleware via the HTTP stack; tools invoked through Channels consult the same `ITenantContext` which carries the cached `Status`.

**Alternatives considered**
- **Per-endpoint `[RequireActiveTenant]` attribute.** Rejected: 250+ endpoints to annotate, easy to forget.
- **DB trigger blocking writes for suspended tenants.** Rejected: ugly; cannot return a meaningful HTTP code.

**Reference** — Spec FR-057, FR-058, FR-059.

---

## 13. Onboarding wizard re-entrancy

**Decision** — Reuse the existing `TenantOnboardingState` + `OnboardingStepCompletion` entities (introduced by Feature 047). Add a new step prefix `Tenant.*` (e.g., `Tenant.LegalEntity`, `Tenant.HqAddress`, `Tenant.Classification`, `Tenant.Ao`) ahead of the existing `Org.*` steps. The wizard router consults `OnboardingState` (`Pending` / `InWizard` / `Active`) and the most recent `OnboardingStepCompletion` to resume.

**Rationale** — Feature 047 already solved re-entrancy for organization onboarding; extending its step list is cheaper and more consistent than building a parallel state machine. Tenant-level steps must precede org-level steps because the org row's `TenantId` FK depends on the tenant existing.

**Alternatives considered**
- **A dedicated `TenantOnboardingFlow` aggregate.** Rejected: duplicates Feature 047 infrastructure.
- **Single-page form (no resumability).** Rejected: violates spec FR-056.

**Reference** — Spec FR-053, FR-054, FR-055, FR-056; Feature 047 spec.

---

## 14. Performance: composite indexes

**Decision** — Every retrofitted table receives a composite index `IX_<Table>_TenantId_<NaturalKey>` (e.g., `IX_RegisteredSystems_TenantId_Name`, `IX_ComplianceFindings_TenantId_AssessmentId`). Where a table already has a hot index on `<NaturalKey>`, replace it with the composite (the leading-`TenantId` form supports both patterns). For tables that join tenant-scoped to global-reference data (e.g., `ControlImplementation` → `NistControl`), the index has `TenantId` first, then the FK to the global reference.

**Rationale** — RLS adds a `WHERE TenantId = …` to every query; without a leading-`TenantId` index, SQL Server falls back to scans + filter, which is the documented worst case for RLS performance. Composite indexes pay for themselves immediately: page-level locking shrinks to per-tenant ranges, multi-tenant deployments avoid lock contention between tenants.

**Alternatives considered**
- **Filtered indexes per tenant.** Rejected: 200 tenants × 60 tables = 12k indexes; index-bloat catastrophe.
- **Rely on the existing single-column indexes.** Rejected: known RLS regression on Microsoft docs.

**Reference** — Performance Goals in plan.md; Constitution VIII.

---

## 15. Open items deferred to implementation

The following were **explicitly chosen to defer**:

| Topic | Why deferred | Where it surfaces |
|-------|--------------|-------------------|
| Audit retention period in years (FedRAMP requires 1; DoD CC SRG often expects 6) | Operational policy, not application code; the audit table already supports indefinite retention. | `docs/operations/audit-retention.md` (separate ADR). |
| Full per-table p95 latency budgets under RLS | Will be benchmarked against the seeded multi-tenant dataset during implementation; budgets adjusted in `tasks.md`. | Phase 2 (`/speckit.tasks`). |
| Scale ceiling per deployment (max tenants × max rows per tenant) | Depends on customer hardware; documented as an operational guideline only. | `docs/operations/multi-tenant-migration.md`. |

These do not block planning or testing.

---

## Decisions index (for quick reference)

| # | Topic | Decision summary |
|---|-------|------------------|
| 1 | Query-filter strategy | `[TenantScoped]` attribute + reflective `OnModelCreating` |
| 2 | RLS predicate | Single inline TVF with FILTER + 2× BLOCK |
| 3 | Setting `SESSION_CONTEXT` | `IDbConnectionInterceptor` on `ConnectionOpenedAsync` |
| 4 | SQLite parity | Skip RLS, log warning, EF filter only |
| 5 | CSP-Admin mapping | Middleware reads `Auth:RoleClaimMappings` group GUIDs |
| 6 | `ato-cli` packaging | New `Ato.Copilot.Cli` dotnet-tool project |
| 7 | Impersonation transport | Signed cookie + optional header; role re-checked per request |
| 8 | Backfill strategy | Two-phase: nullable column → backfill → NOT NULL → RLS |
| 9 | `[GlobalReference]` catalog | NIST/framework data + published baselines + `Tenants` |
| 10 | Channels propagation | `StreamContext.TenantId` + DI for tools |
| 11 | Audit schema | Three nullable Guid columns, paginated query endpoint |
| 12 | Lifecycle enforcement | `TenantResolutionMiddleware` with 30-s `IMemoryCache` |
| 13 | Wizard re-entrancy | Extend Feature 047 state machine with `Tenant.*` steps |
| 14 | Indexing | Composite `(TenantId, …)` indexes on all retrofitted tables |
| 15 | Deferred | Retention years, exact budgets, scale ceiling |
