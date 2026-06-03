# Data Model: Fix Dead Routes

## Overview

This epic introduces no database schema changes. All changes are in the React
frontend router configuration and component tree.

## Router Changes

### Current Route Tree (App.tsx, simplified)

```
<Routes>
  /login                          → LoginPage
  /login/callback                 → LoginCallbackPage
  /login/error                    → LoginErrorPage
  /login/select-tenant            → TenantPickerPage
  /                               → PortfolioRoute
  /systems                        → SystemsRoute
  /systems/:id                    → SystemLayout
    index                         → SystemDetail
    roadmap                       → Roadmap
    boundaries                    → BoundaryManagement
    legal                         → LegalRegulatory
    documents                     → Documents
    conmon                        → ConMon
    narratives                    → Narratives
    deviations                    → DeviationsPage
    assessments                   → Assessments
    remediation                   → Remediation
    evidence                      → EvidenceRepository
    components                    → ComponentInventory
    poam                          → PoamManagement
    capability-coverage           → CapabilityCoverage
    inheritance                   → ControlInheritance
    baseline                      → BaselineManagement
    profile/:sectionType          → SystemProfile
  /capabilities                   → CapabilitiesRoute
  /components                     → ComponentsRoute
  /onboarding                     → OnboardingShell
  /onboarding/tenant              → TenantWizard
  /onboarding/csp                 → CspWizard
  /csp/inherited-components       → CspInheritedComponentsPage
  /admin/imported-documents       → ImportedDocumentsView
  /controls                       → ControlsRoute
  [NO CATCH-ALL — blank screen]
</Routes>
```

### Target Route Tree (after this epic)

```
<Routes>
  ... (all existing routes unchanged) ...
  /systems/:id
    ... (existing nested routes) ...
    gaps                          → GapsPage              ← NEW (US3)
  /csp-dashboard                  → Navigate /csp/inherited-components  ← NEW (US2)
  /csp/inherited-components       → CspInheritedComponentsPage
  ... (other existing routes) ...
  *                               → NotFoundPage          ← NEW (US1, MUST BE LAST)
</Routes>
```

## New Components

### `NotFoundPage`

```
src/pages/NotFoundPage.tsx
  - Renders inside authenticated shell (RequireAuth wraps it in App.tsx)
  - Props: none
  - State: none
  - Links to: /
```

### `GapsPage`

```
src/features/gaps/GapsPage.tsx   (or src/pages/GapsPage.tsx)
  - Reads systemId from useParams()
  - Calls: GET /api/dashboard/systems/:systemId/gaps
  - Renders: list of control gaps with status/severity
  - Chat context: 'gap-analysis' key automatically mapped by useChatContext.ts
```

## Navigation Fix: TenantPickerPage

```
src/features/auth/TenantPickerPage.tsx
  Line 88: navigate('/csp/dashboard') → navigate('/csp/inherited-components')
  Line 20: update comment to reflect correct path
```

## Backend Endpoints Referenced (no changes)

| Endpoint | Method | Handler |
|----------|--------|---------|
| `/api/dashboard/systems/{systemId}/gaps` | GET | `GetGapAnalysis` (DashboardEndpoints.cs:404) |

Backend requires no changes.
