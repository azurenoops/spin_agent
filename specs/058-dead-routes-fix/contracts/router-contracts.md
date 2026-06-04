# Router Contracts: Fix Dead Routes

**Last updated**: 2026-06-03
**Source of truth**: `App.tsx` in `src/Ato.Copilot.Dashboard/src/`

## Complete Route Table (post-fix)

| Path | Component | Auth | Notes |
|------|-----------|------|-------|
| `/login` | `LoginPage` | Public | |
| `/login/callback` | `LoginCallbackPage` | Public | OAuth callback |
| `/login/error` | Login error view | Public | |
| `/login/select-tenant` | `TenantPickerPage` | RequireAuth | |
| `/` | `PortfolioRoute` | RequireAuth | CSP-Admins see CSP surface |
| `/systems` | `SystemsRoute` | RequireAuth | |
| `/systems/:id` | `SystemLayout` | RequireAuth | Nested routes below |
| `/systems/:id/` (index) | `SystemDetail` | RequireAuth | |
| `/systems/:id/roadmap` | `Roadmap` | RequireAuth | |
| `/systems/:id/boundaries` | `BoundaryManagement` | RequireAuth | |
| `/systems/:id/legal` | `LegalRegulatory` | RequireAuth | |
| `/systems/:id/documents` | `Documents` | RequireAuth | |
| `/systems/:id/conmon` | `ConMon` | RequireAuth | |
| `/systems/:id/narratives` | `Narratives` | RequireAuth | |
| `/systems/:id/deviations` | `DeviationsPage` | RequireAuth | |
| `/systems/:id/assessments` | `Assessments` | RequireAuth | |
| `/systems/:id/remediation` | `Remediation` | RequireAuth | |
| `/systems/:id/evidence` | `EvidenceRepository` | RequireAuth | |
| `/systems/:id/components` | `ComponentInventory` | RequireAuth | |
| `/systems/:id/poam` | `PoamManagement` | RequireAuth | |
| `/systems/:id/capability-coverage` | `CapabilityCoverage` | RequireAuth | |
| `/systems/:id/inheritance` | `ControlInheritance` | RequireAuth | |
| `/systems/:id/baseline` | `BaselineManagement` | RequireAuth | |
| `/systems/:id/profile/:sectionType` | `SystemProfile` | RequireAuth | |
| `/systems/:id/gaps` | `GapsPage` | RequireAuth | **NEW — US3** |
| `/capabilities` | `CapabilitiesRoute` | RequireAuth | |
| `/components` | `ComponentsRoute` | RequireAuth | |
| `/onboarding` | `OnboardingShell` | RequireAuth | |
| `/onboarding/tenant` | `TenantWizard` | RequireAuth | |
| `/onboarding/csp` | `CspWizard` | RequireAuth | |
| `/csp/inherited-components` | `CspInheritedComponentsPage` | RequireAuth | Current CSP-Admin surface |
| `/admin/imported-documents` | `ImportedDocumentsView` | RequireAuth | |
| `/controls` | `ControlsRoute` | RequireAuth | |
| `/csp-dashboard` | `Navigate` → `/csp/inherited-components` | — | **NEW — US2 (retired route redirect)** |
| `*` | `NotFoundPage` | RequireAuth | **NEW — US1 (catch-all, MUST be last)** |

## Redirects

| From | To | Type | Reason |
|------|----|------|--------|
| `/csp-dashboard` | `/csp/inherited-components` | `replace` (no history entry) | Route retired in Feature 048 |

## Retired Routes

| Path | Retired in | Replacement |
|------|------------|-------------|
| `/csp-dashboard` | Feature 048 | `/csp/inherited-components` |

## Chat Context Mappings (useChatContext.ts)

| URL pattern (pathname.includes) | Chat page key | Suggestions source |
|----------------------------------|--------------|-------------------|
| `/gaps` | `gap-analysis` | `phasePageSuggestions.ts:265` |
| _(others unchanged)_ | | |

## 404 Page Contract

- Component: `NotFoundPage`
- Renders: within authenticated shell (RequireAuth)
- Content: "Page not found" `<h1>`, brief message, `<Link to="/">Go to Dashboard</Link>`
- Unauthenticated access: `RequireAuth` redirects to `/login` before `NotFoundPage` renders

## CSP-Admin Navigation (TenantPickerPage)

After fix:
```tsx
// Line 88 — CSP-Admin post-login navigation
navigate('/csp/inherited-components', { replace: true });
```
