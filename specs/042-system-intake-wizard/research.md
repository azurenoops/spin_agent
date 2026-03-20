# Research: System Intake Wizard

**Feature**: 042-system-intake-wizard  
**Date**: 2026-03-20

## R1: Multi-Step Wizard Modal Pattern in React

**Decision**: Build a custom wizard component using React state management (useState/useReducer) inside a full-screen modal overlay. No wizard library needed.

**Rationale**: The dashboard already uses Tailwind CSS with custom modal patterns (see existing Add System dialog in `PortfolioDashboard.tsx`). The existing `RmfPhaseProgress.tsx` stepper component provides a proven visual pattern for step indicators. A custom implementation avoids adding new dependencies and stays consistent with the existing codebase style.

**Alternatives considered**:
- **react-step-wizard**: Adds a dependency for relatively simple step management. Rejected because the wizard's step logic (conditional save on Next, Skip semantics) is custom enough that a library wouldn't simplify much.
- **Headless UI stepper**: Headless UI is not currently in the project's dependencies. Adding it for one component is unnecessary.
- **Routed wizard (separate pages)**: Spec explicitly states modal overlay staying on `/systems` route.

## R2: Wizard State Management Approach

**Decision**: Use a `useReducer` hook to manage wizard state (current step, per-step data, validation errors, completion status). Each step's data is held in the reducer until the user clicks Next, at which point an API call persists it.

**Rationale**: The wizard has 7 steps with interdependent data (e.g., Step 4 references components from Step 3). A reducer provides predictable state transitions and makes backward/forward navigation straightforward. The "persist on Next" pattern aligns with FR-006 (data survives navigation and abandonment) while avoiding unnecessary API calls (NFR-004 batch saves).

**Alternatives considered**:
- **useState per step**: More fragmented state management. Harder to coordinate cross-step concerns like the system ID flowing from Step 1 to all subsequent steps.
- **React Context / Zustand store**: Overkill for a single modal's state. The wizard is self-contained and doesn't need global state.
- **Form library (React Hook Form)**: Could be used within individual steps for field validation, but the wizard-level orchestration still needs custom logic. Per-step forms with `React Hook Form` is a possible optimization but adds a dependency not currently in the project.

## R3: "Setup Incomplete" Badge Tracking

**Decision**: Compute the "Setup Incomplete" status dynamically from existing data relationships rather than adding a new column. A system is "setup complete" when it has: (1) at least one boundary definition, (2) at least one RMF role assignment, and (3) a SecurityCategorization record.

**Rationale**: The spec says the badge should be removed "once all required setup steps (roles assigned, boundary defined, categorization set) are completed, whether through the wizard or individual management pages." A computed approach avoids state synchronization issues where the badge could become stale if data is modified outside the wizard. The portfolio query already joins these tables for other metrics.

**Alternatives considered**:
- **Boolean `IsSetupComplete` column on RegisteredSystem**: Requires updating the flag from multiple code paths (wizard, individual pages, MCP tools). Risk of stale data. Rejected.
- **`SetupProgress` JSON column**: Store which wizard steps were completed. Over-engineered — the spec's completion criteria are relationship-based, not wizard-step-based.

## R4: Capability Linking in Step 2

**Decision**: Reuse the existing capability search API (`GET /api/dashboard/capabilities?search=&category=&status=`) and create a new endpoint (`POST /api/dashboard/systems/{id}/capabilities`) to link capabilities to a system. This follows the existing `ComponentSystemAssignment` pattern.

**Rationale**: The capability library API already supports search, filtering by category and status (FR-019). The linking mechanism needs a many-to-many relationship between `RegisteredSystem` and `SecurityCapability`. The existing `CapabilityControlMapping` entity links capabilities to controls per system, but the wizard needs a simpler system-level link. A new join entity `SystemCapabilityAssignment` or reuse of existing patterns is needed.

**Alternatives considered**:
- **Reuse CapabilityControlMapping**: This table maps capabilities to specific controls, not to systems directly. Using it would require creating placeholder control mappings, which is semantically wrong.
- **ComponentCapabilityLink**: Links capabilities to components, not systems. Different purpose.

## R5: SP 800-60 Information Types Reference Data

**Decision**: Serve SP 800-60 information types from a static JSON file bundled with the frontend, loaded on demand when Step 7 opens. The reference data is already available at `src/Ato.Copilot.Agents/Compliance/Resources/sp800-60-information-types.json` with 90+ types including categories and provisional C/I/A levels.

**Rationale**: SP 800-60 data is static reference data that rarely changes. Loading it client-side avoids a round trip and enables instant search/filtering (NFR-003: 300ms filter). The existing JSON file is well-structured with id, name, category, and C/I/A defaults.

**Alternatives considered**:
- **Server-side API endpoint**: Adds unnecessary latency for data that doesn't change. The JSON file is ~15KB — trivial to bundle.
- **Database-seeded reference table**: Over-engineered for read-only reference data. Adds migration complexity.

## R6: Person Component Selection for RMF Roles (Step 5)

**Decision**: Query org-wide components filtered by `componentType=Person` using the existing `GET /api/dashboard/components?type=Person` endpoint (org-wide library). Present as a searchable dropdown per role.

**Rationale**: The spec explicitly states roles must be assigned from "existing org-wide Person components (SystemComponent entries of type Person)." The org-wide component library API already supports type filtering and search. This avoids coupling role assignment to system-scoped components.

**Alternatives considered**:
- **Free-text user entry**: Explicitly prohibited by FR-013.
- **Azure AD / Entra ID user picker**: Not in scope — the spec says to use existing Person components, which are already managed in the component library.

## R7: Wizard Performance Strategy

**Decision**: Implement performance through: (1) lazy loading step content (only mount the current step's component), (2) debounced search inputs (300ms), (3) virtualized lists for large datasets (capabilities, information types), and (4) batch saves on step transition only.

**Rationale**: NFR-001 requires 2-second initial load, NFR-002 requires 1-second transitions, NFR-003 requires 300ms filtering. Lazy loading step content keeps the initial render fast. Debounced search prevents excessive filtering. Virtualized lists (using CSS-based virtual scrolling or a lightweight approach) handle the ~90 information types and potentially hundreds of capabilities.

**Alternatives considered**:
- **Eager-load all steps**: Increases initial render time, especially when fetching data for Steps 2-7 upfront.
- **React.lazy for step components**: Code splitting adds complexity but would reduce initial bundle size. Worth considering if Step 7's SP 800-60 data significantly impacts load time, but the data is small (~15KB).

## R8: Backend API Changes Required

**Decision**: Minimal backend changes needed. The wizard primarily orchestrates existing API endpoints. New endpoints/changes required:
1. **No new endpoints for Step 1**: `POST /api/dashboard/systems` already exists.
2. **New endpoint: System capability links** (`POST/GET /api/dashboard/systems/{id}/capability-links`): Link capabilities to a system.
3. **Steps 3-7**: All use existing endpoints (`POST components`, `POST boundary-definitions`, `POST roles`, `POST categorization`).
4. **Portfolio query update**: Add setup completion status to the `PortfolioSystemSummary` DTO.

**Rationale**: The spec assumes "existing backend endpoints are sufficient" and the wizard orchestrates existing operations. Only the capability-to-system linking and the "Setup Incomplete" badge calculation require backend changes.

**Alternatives considered**:
- **New wizard-specific batch endpoint**: A single `POST /systems/{id}/intake` that accepts all wizard data at once. Rejected because the spec requires step-by-step persistence (FR-006), and individual endpoints enable the wizard steps to work independently.

## R9: Documentation Update Strategy

**Decision**: Create a new `docs/guides/system-intake-wizard.md` guide documenting all 7 wizard steps with screenshots placeholders. Update the ISSM, ISSO, and System Owner getting-started guides to reference the wizard as the primary registration method.

**Rationale**: FR-021 and FR-022 require documentation updates. A dedicated guide page allows the documentation site search to return results for "system intake," "register system," and "add system" queries (SC-007). Persona-specific guides already exist at `docs/getting-started/{issm,isso,engineer}.md`.

**Alternatives considered**:
- **Inline documentation in existing pages only**: Would scatter wizard documentation across multiple pages, making it harder to find via search.
- **Video tutorial**: Not in scope for this feature.
