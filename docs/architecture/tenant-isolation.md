# Tenant Isolation Architecture

> **Spec**: [`specs/048-tenant-isolation/spec.md`](../../specs/048-tenant-isolation/spec.md) · **Status**: live (Feature 048)

ATO Copilot is built so a single deployment can host one customer (SingleTenant)
or many customers (MultiTenant) without a code rebuild. This document is the
authoritative reference for how that isolation is enforced and where the
defense-in-depth boundaries sit.

## Table of Contents

1. [Deployment Modes](#deployment-modes)
2. [Three-Layer Defense in Depth](#three-layer-defense-in-depth)
3. [Tenant Context Propagation](#tenant-context-propagation)
4. [Scoping Attributes](#scoping-attributes)
5. [Cross-Tenant Sharing Model](#cross-tenant-sharing-model)
6. [Reuse-First Audit](#reuse-first-audit)
7. [Migration](#migration)
8. [Testing Strategy](#testing-strategy)

---

## Deployment Modes

The deployment mode is selected at startup from `Deployment:Mode` in
`appsettings.json` (or the `DEPLOYMENT__MODE` environment variable). Changing
the mode requires a process restart.

| Mode           | Tenant table | RLS enabled | CSP onboarding wizard | Tenant picker UI |
|----------------|--------------|-------------|-----------------------|------------------|
| `SingleTenant` | One row, well-known id | No | Disabled (404)        | Hidden           |
| `MultiTenant`  | Many rows    | Required on SQL Server | Required on first boot | Visible to CSP-Admin |

In `SingleTenant` mode the tenant filter is still active, but the well-known
tenant id is bound from configuration on every request, so callers never need
to authenticate against an Entra tenant claim. This keeps the codebase
single-pathed: every query goes through the tenant filter regardless of mode.

## Three-Layer Defense in Depth

Tenant isolation is enforced at **three independent layers**. A bug in any one
layer cannot, by itself, leak data across tenants.

```text
        Request                         App Layer
   ┌────────────┐               ┌────────────────────────┐
   │ HTTP /     │  ITenantCtx   │ EF Core query filter   │
   │ Channels   │ ─────────────▶│ + SaveChanges stamping │
   │ envelope   │               │ + interceptor FK guard │
   └────────────┘               └────────────────────────┘
                                          │
                                          ▼
                                   DB Layer (SQL Server only)
                                ┌──────────────────────────┐
                                │ Row-Level Security       │
                                │ predicate on TenantId    │
                                └──────────────────────────┘
                                          │
                                          ▼
                                  Operational Layer
                                ┌──────────────────────────┐
                                │ CSP-Admin impersonation  │
                                │ + AuditLogEntry on every │
                                │ tenant boundary cross    │
                                └──────────────────────────┘
```

### Application Layer — `ITenantContext` + Query Filters

- **`ITenantContext`** (scoped, per-request) carries `TenantId`,
  `OrganizationId?`, `IsCspAdmin`, `ImpersonatedTenantId?`, and
  `EffectiveTenantId = ImpersonatedTenantId ?? TenantId`.
- **`TenantResolutionMiddleware`** populates the scoped `ITenantContext` from
  the bearer-token claim (or the configured single-tenant id) on every request,
  reading impersonation state from a signed cookie. It also validates tenant
  status (`Active` / `Suspended` / `Disabled`) and short-circuits with an
  envelope error when the caller's tenant is unusable.
- **EF Core global query filters** on every `[TenantScoped]` entity rewrite
  every `IQueryable<T>` to `Where(e => filtersDisabled || cspAdminAll ||
  e.TenantId == effectiveTenantId)`. The filter is generated once per
  `OnModelCreating` and cannot be removed by application code.
- **`TenantStampingSaveChangesInterceptor`** (Feature 048 §T040) stamps
  `TenantId = EffectiveTenantId` on every Added entity that is `[TenantScoped]`
  and rejects Modified entities that try to change `TenantId`. It also walks
  every reference FK on each entity and **rejects** any FK that points across
  tenants, returning `409 CROSS_TENANT_REFERENCE_REJECTED` (FR-080). FKs that
  point at `[GlobalReference]` rows (see §5.2) are explicitly allowed.

### Database Layer — Row-Level Security (SQL Server)

In `MultiTenant` mode on SQL Server, every `[TenantScoped]` table carries an
RLS security predicate that compares `TenantId` to `SESSION_CONTEXT('TenantId')`.
The application sets the session context on every connection check-out via the
EF Core connection interceptor in `src/Ato.Copilot.Core/Data/Interceptors/`.
RLS is **disabled** in SQLite (development) because the engine doesn't support
it; isolation in dev relies on the application layer alone.

Why RLS in addition to query filters: ad-hoc reporting tools, accidental
`db.Database.ExecuteSqlRaw` calls, or future endpoints that bypass the EF Core
filter (e.g. raw `SqlConnection`) still cannot read across tenants. The DB
itself enforces the boundary.

### Operational Layer — CSP-Admin Impersonation & Audit

CSP-Admins (operators of the hosting CSP) can read and write any tenant's data
**only** through `POST /api/tenants/{id}/impersonate`, which sets a signed
cookie with the impersonated tenant id. Every cross-tenant action — start
impersonation, end impersonation, publish a global baseline, suspend a tenant,
run the migration tool — emits an `AuditLogEntry` with the actor's OID, the
target tenant id, the action verb, and a correlation id. The audit log is
itself `[GlobalReference]` so CSPs can query it across tenants.

## Tenant Context Propagation

### HTTP Requests

`TenantResolutionMiddleware` is the only path that mutates the scoped
`ITenantContext` for HTTP traffic. Every other layer reads from it.

### Channels Library (M365 Teams, VS Code Extension)

The Channels library has no project reference to `Ato.Copilot.Core`, so it
cannot use `ITenantContext` directly. Instead:

1. The transport (VS Code extension or M365 bot) attaches a
   `TenantContextEnvelope` to the inbound `IncomingMessage` carrying
   `TenantId`, `OrganizationId?`, `IsCspAdmin`, and `ImpersonatedTenantId?`.
2. `DefaultMessageHandler.HandleMessageAsync` calls
   `ITenantScopeBinder.Bind(message.TenantContext)` for the duration of agent
   invocation.
3. The composition root (currently `Ato.Copilot.Chat`) registers
   `AccessorTenantScopeBinder`, which calls `ITenantContextAccessor.Push` so
   any MCP tool invoked in-process during the message scope sees the same
   ambient `ITenantContext` as direct HTTP callers.

The default registration in the Channels library itself is
`NullTenantScopeBinder` (a no-op), so test consumers and other hosts that don't
want this bridge get a safe default.

### Background Workers & Hosted Services

Workers run outside any HTTP request, so they explicitly call
`ITenantContextAccessor.Push(ctx)` for the scope of work that needs to act as
a specific tenant. The compliance watch worker, conmon evaluator, and SSP
export retention service all follow this pattern.

## Scoping Attributes

| Attribute            | Meaning                                                             | Example entities |
|----------------------|---------------------------------------------------------------------|------------------|
| `[TenantScoped]`     | Row belongs to exactly one tenant; query filter + stamping applies. | `RegisteredSystem`, `Person`, `RemediationTask` |
| `[GlobalReference]`  | Row is shared across all tenants; query filter does NOT apply; FKs to it are allowed from any tenant. | `Tenant`, `AuditLogEntry`, `GlobalBaseline`, `CspProfile`, `NistControl` |
| _(neither)_          | Implicit: legacy/seed reference data treated as `[GlobalReference]`. New entities MUST declare one of the two. | — |

The interceptor and the model builder both reflect over these attributes; if
you add a new entity and forget to attribute it, the unit test
`[TenantAttributeCoverageTests]` fails and CI blocks the merge.

## Cross-Tenant Sharing Model

### Org Inheritance Defaults

Within a tenant, an `OrganizationInheritanceDefault` row lets a parent
organization push baseline narratives down to child orgs. This is purely
intra-tenant and uses normal `[TenantScoped]` semantics.

### Global Baselines

`GlobalBaseline` is `[GlobalReference]`. CSP-Admins call
`POST /api/global-baselines/publish { kind, sourceId, title?, notes? }` to
copy a tenant-local row (a control narrative, an evidence artifact, an org
inheritance default) into the system tenant for cross-tenant reuse.
`UnpublishAsync(id)` is a logical delete (`UnpublishedAt`/`UnpublishedBy`) so
inheritors retain a stable reference. Both verbs emit `AuditLogEntry` rows.

The interceptor's FK guard explicitly allows references **into**
`[GlobalReference]` entities from any tenant; the inverse — references **out
of** a global row into a tenant-scoped row — is rejected.

### CSP-Inherited Components & Capabilities

Components that the hosting CSP inherits to all customer tenants (Azure-managed
identity templates, baseline platform controls) are stored as `[GlobalReference]`
rows in the system tenant and surfaced by the dashboard with a `Source: Global
Baseline` label so operators always know provenance.

## Reuse-First Audit

Before any new feature introduces a `Service` or `Tool`, the spec-kit Reuse
Audit (Phases 15+16 of feature 048) requires the author to enumerate existing
services that already cover the use case. The intent is to keep the
tenant-aware service surface small and predictable. See
`specs/048-tenant-isolation/tasks.md` §T217 / §T218.

## Migration

See [`docs/operations/multi-tenant-migration.md`](../operations/multi-tenant-migration.md).

## Testing Strategy

Tenant isolation has the strictest test discipline in the codebase:

- **Unit** (`tests/Ato.Copilot.Tests.Unit/Tenancy/`): attribute-coverage checks,
  interceptor stamping/rejection logic, query-filter generation, scoping
  attribute conformance, GlobalBaseline service contract.
- **Integration** (`tests/Ato.Copilot.Tests.Integration/Tenancy/`): all tests
  share the `[Collection("Tenancy")]` collection so they serialize against the
  in-memory test factory. Coverage:
  cross-tenant `404`, save-time stamping, audit emission, audit query envelope,
  CSP-Admin RBAC, mode-switch behavior, single-tenant bootstrap, tenant picker
  visibility, impersonation flow + cookie HMAC, MCP tool tenant scope, tenant
  query-filter join behavior, tenants endpoint contract, onboarding wizard,
  wizard re-entrancy, admin-migration endpoints, CLI migration tool,
  `GlobalBaselineEndpointTests`, `ChannelsTenantContextPropagationTests`.
- **Manual** (quickstart.md sections 1–7): exercised on a clean dev machine
  before each release.

The Constitution Check gate (`.specify/memory/constitution.md` §VI) requires a
failing test before any production-code change touching this surface.

## Performance & Indexing

The query filter rewrites every `WHERE` clause to begin with
`Effective.TenantId = @tid`. To keep tenant-scoped reads fast on multi-tenant
SQL Server deployments, every `[TenantScoped]` table carries a
`IX_<table>_TenantId` index installed by
`TenantIdColumnAdditions.ApplyAsync` (Feature 048 §T056) and, where the
existing entity model declares them, composite indexes
`IX_<table>_TenantId_<naturalKey>` (e.g. `(TenantId, Status)`,
`(TenantId, SubscriptionId)`, `(TenantId, Timestamp)`) declared on the
`AtoCopilotContext` model snapshot.

To verify production queries use the indexes:

```sql
SET STATISTICS PROFILE ON;
EXEC sp_set_session_context 'TenantId', '<tenant-guid>';
SELECT * FROM RegisteredSystems WHERE Status = 'Active';
SELECT * FROM Findings WHERE Severity = 'High';
```

Inspect the execution plan for an `Index Seek` on
`IX_RegisteredSystems_TenantId_Status` / `IX_Findings_TenantId_*`. A
`Clustered Index Scan` or a `Key Lookup` on these tables is a regression — file
a perf bug. `RlsPolicyInstaller` does not change the plan shape (RLS adds the
predicate, the index still serves the scan).

p95 latency for `GET /api/dashboard/systems` and
`GET /api/dashboard/findings` against a seeded MultiTenant DB (3 tenants
× 250 systems × 10k findings, dev hardware) was within the per-feature
performance budget at the time of merge; rerun the smoke pack after any
schema change to confirm.

