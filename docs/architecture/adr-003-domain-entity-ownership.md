# ADR-003 — Domain Entity Ownership: One File per Domain Type

**Status:** Accepted  
**Date:** 2026-06-09  
**Author:** Batman (Chief Systems Architect)  
**Relates to:** Issue [#367](https://github.com/azurenoops/spin_agent/issues/367)  
**Files affected:**  
- `src/Ato.Copilot.Dashboard/src/api/packages.ts`  
- `src/Ato.Copilot.Dashboard/src/api/package.ts`

---

## Context

Two API files in the `api/` directory both export an interface named `PackageDetail` with **incompatible shapes**:

| File | `PackageDetail` fields |
|------|----------------------|
| `api/packages.ts` | `packageId`, `systemId`, `status`, `evidenceMode`, `fileSize?`, `generatedAt?`, `completedAt?`, `expiresAt?`, `failureReason?` — minimal job-status shape |
| `api/package.ts` | Full shape — adds `artifacts: PackageArtifact[]`, `validation: PackageValidation \| null`, `failedArtifactType`, `generatedBy`, plus stricter nullability (`fileSize: number \| null` vs `fileSize?: number`) |

TypeScript does **not** catch cross-module name collisions. A component that imports `PackageDetail` from the wrong module receives a structurally incomplete object at runtime, causing silent `undefined` field access — no build error, no type error, no warning.

### Root Cause Pattern

This is a symptom of a broader file organization anti-pattern: `packages.ts` (plural) grew alongside `package.ts` (singular) without a canonical ownership rule. The identical export name was introduced as both files iterated independently.

---

## Decision

### 1. One canonical type file per domain entity

**The rule:** For any domain entity in `api/`, exactly one file owns the authoritative type definition. All other files that reference that entity MUST import from the canonical owner — never re-declare or re-define.

**For the `Package` domain:**

| File | Purpose |
|------|---------|
| `api/package.ts` | **Canonical owner** — full `PackageDetail`, `PackageSummary`, `PackageArtifact`, `PackageValidation`, `ValidationFinding`, `PackageListResponse`, `GeneratePackageResponse`, all package-domain types |
| `api/packages.ts` | **Operations file** — `enqueuePackage`, `getPackageStatus`, `getPackageDownloadUrl`, `enqueueEmassExport`; imports `PackageDetail` from `api/package.ts` — does NOT redeclare it |

### 2. Immediate consolidation steps

1. **Remove `PackageDetail` from `api/packages.ts`** — delete the interface declaration
2. **Add import** to `api/packages.ts`: `import type { PackageDetail } from './package';`
3. **Audit all consumers** — search for `import.*PackageDetail.*from.*packages` and update to `./package`
4. **Verify build passes** — `tsc --noEmit` must be green

The minimal `PackageDetail` shape in `packages.ts` was a polling-convenience subset. `getPackageStatus()` already returns `Promise<PackageDetail>` — it should return the full canonical shape. If the server's polling endpoint only returns a subset of fields, that is a server contract issue to resolve in the OpenAPI spec — not a reason to maintain two incompatible client types.

### 3. Naming convention

To prevent recurrence:

| Pattern | Rule |
|---------|------|
| `api/<entity>.ts` | Canonical: defines the entity's types + the primary CRUD API functions |
| `api/<entities>.ts` (plural) | **Discouraged.** If it exists, it MUST only contain list-query helpers and import types from the singular file. No type declarations. |
| `api/<feature>Api.ts` | Feature-scoped client (e.g., `onboardingApi.ts`) — may define feature-local types, but MUST NOT shadow types from domain entity files |

### 4. TypeScript path alias (recommended, not required)

Consider adding a barrel export in `api/index.ts` that re-exports all canonical entity types. This makes the import path unambiguous and allows `eslint-plugin-import` rules to enforce the convention:

```ts
// api/index.ts
export type { PackageDetail, PackageSummary, PackageArtifact, PackageValidation } from './package';
export type { PackageJob } from './packages';
```

Consumers then import from `'../api'` — one canonical path.

---

## Consequences

**Positive:**
- Eliminates the silent runtime `undefined` field access risk
- Single source of truth for `PackageDetail` — all consumers see the same shape
- Establishes a naming rule that prevents the same issue across other domain entities

**Negative / Trade-offs:**
- Requires an audit of all `PackageDetail` import sites — small but necessary refactor
- If `getPackageStatus()` truly only receives a partial shape from the API, the OpenAPI spec and server response must be updated rather than maintaining a thin client type

---

## Alternatives Considered

**A. Rename the `packages.ts` type to `PackageStatusDetail`** — Considered. Rejected because it papering over the root problem: parallel type definitions for the same concept with no ownership rule. The fix should eliminate the duplicate, not rename it.

**B. Use TypeScript `Partial<PackageDetail>` in `packages.ts`** — Rejected. `Partial<>` loses nullability and required field semantics. The server contract should be the definition, not a client-side weakening.

**C. `@ts-nocheck` or type casting** — Rejected immediately. Masks the problem.

---

## Enforcement

- PR review: any new file in `api/` that declares a type matching an existing entity name MUST be rejected — add import, do not redeclare
- `eslint-plugin-import/no-duplicates` rule should be confirmed active
- `tsc --noEmit` is a required CI check (it already is per the build pipeline — this ADR reinforces it must stay)
