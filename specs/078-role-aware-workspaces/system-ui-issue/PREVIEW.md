# [UI] Complete system-level Security Capabilities with unified views, applicability and responsibility review

## Outcome

Replace the separate system sidebar Capabilities and Components entries with one **Security Capabilities** destination. Within the selected system, users can browse capabilities or components, inspect implementation and coverage, review evidence/narratives, add library capabilities with system placement, and remove system applicability safely.

The top-navigation Security Capabilities destination remains the reusable organization library. The system sidebar destination shows applicability to the selected system. Reuse the same underlying records and services, with explicit system scope throughout.

Parent: #1002. Attach this issue as an actual GitHub sub-issue. Coordinate with #1025, #1033, #1034 and #1035; this issue covers the remaining system-level integration and expanded mock views, not a second organization catalog or setup engine.

## Required mock references

Publish the following four image boards and their README with this issue. Each repository path must become an accessible immutable GitHub file link before posting. Embed the four PNGs in the issue so reviewers can see the designs, and retain file links for Copilot to inspect/download them.

1. `docs/design/system-security-capability-mocks/01-overview-and-implementation.png` — views 01–02: system capability list and implementation detail.
2. `docs/design/system-security-capability-mocks/02-components-and-library.png` — views 03–04: component view/drawer and library selection.
3. `docs/design/system-security-capability-mocks/03-coverage-evidence-and-removal.png` — views 05–06: coverage/duties, evidence/narratives and removal confirmation.
4. `docs/design/system-security-capability-mocks/04-applicability-and-confirmation.png` — views 07–08: applicability, final review, success and partial failure.
5. `docs/design/system-security-capability-mocks/README.md` — interpretation rules and screen index.

These are proposed designs with illustrative names, counts, dates, versions and control mappings. Persisted data and server permissions are authoritative. Match the SPIN shell, hierarchy, panels, typography and interactions; do not copy the PNGs into the app as its implementation. Required reviews must disable confirmation until valid, even where a static mock uses a primary-button color.

## Current code audit

Source inspected September 23, 2026 in the working tree of `feature/1002-workspace-ui-1025-1035`. This branch includes uncommitted work; recheck the named methods before implementation. These findings are source observations, not proof of deployed behavior or a passing end-to-end test.

- `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`: `SYSTEM_NAV_GROUPS` still has `capability-coverage` / Capabilities and `components` / Components.
- `src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx`: legacy component and capability routes remain; `systems/:id/security-capabilities/*` already mounts `WorkspaceOperationsPage` outside the existing nested `SystemLayout` block. The route is not missing; finish its system-shell integration.
- `src/Ato.Copilot.Dashboard/src/features/workspace-operations/WorkspaceOperationsPage.tsx`: already dispatches system routes to `OrganizationLibrary`, `CapabilityDetail` and `SetupWizard` with `routeSystemId`. The setup form locks the target system, supports prepared operations, and renders per-write outcomes/retry. Preserve these protections.
- `src/Ato.Copilot.Dashboard/src/features/workspace-operations/api.ts` and `types.ts`: provide paged organization catalog/detail, component grouping, supporting components, coverage, responsibility/narrative summaries, and prepare/get/complete setup wrappers. `CatalogQuery` has system/source/component filters but no explicit boundary/type filter. Setup input represents one source/record with component IDs and a subscription choice, not the mock's multi-capability selection plus placements.
- `src/Ato.Copilot.Mcp/Endpoints/Workspaces/WorkspaceOperationsEndpoints.cs`: resolves readable systems and gates prepare/complete through `CanManageSystem`; narrative proposal review uses `CanReviewNarratives` and checks proposal source/system identity. Do not replace these with a UI persona check or silently grant ISSO broader setup authority.
- `src/Ato.Copilot.Core/Services/Workspaces/WorkspaceOperationsService.cs`: `ListOrganizationCapabilitiesAsync` already supports component/capability grouping and system filters; the inspected component branch limits provider rows through active system subscriptions. `PrepareSetupAsync` / `CompleteSetupAsync` already persist operation identity, execution claims and outcomes. Extend them where needed instead of inventing browser-only retry state.
- `src/Ato.Copilot.Dashboard/src/api/components.ts`: existing system component reads and local component assignments include boundary identifiers. Preserve assignment semantics; do not assume these local component APIs already accept provider-owned component IDs.
- Existing responsibility APIs in `src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs` and `src/Ato.Copilot.Dashboard/src/api/capabilityResponsibilities.ts` are the integration point for subscriptions, per-control review and concurrency handling. Existing narrative proposal/evidence services remain authoritative.

## 1. Navigation and shared shell

- Mount the unified system experience under `/workspaces/organizations/{tenantId}/systems/{systemId}/security-capabilities` inside the system layout. Avoid nested duplicate PageLayouts, headers or conflicting scroll containers.
- Retain selected organization, system, effective roles, system sidebar and existing right-hand context panel. Do not use a provider/support identity as an ordinary organization assignment.
- Replace the two sidebar items with Security Capabilities. Keep Roles & Permissions, Boundaries, Control Inheritance and Narratives as their existing destinations.
- Provide compatible redirects for `capability-coverage`, `capabilities` and `components`, preserving system, selected record and the appropriate capability/component view. Old inventory creation/import/discovery actions must remain reachable through an appropriate component action; consolidation must not silently remove existing functionality.
- Store view, search, source, type, boundary, page and selected detail tab in the route/query. Refresh/back/forward and independent organization/system tabs must retain their own state. Resolve authorization on every request; cancel pending loads and clear stale data when scope changes.

## 2. System list: By capability

- Show only capabilities actually linked/subscribed to this system, with name, source identity, contributor count, distinct mapped-control count, current review summary and View details.
- Provide Add from library as an explicit separate catalog-browse flow. A released provider capability merely being available in the organization catalog must not make it appear applied.
- Search/filter/sort/page on the server with matching totals. Show available source/review data honestly; unavailable is not zero. A mixed set of per-control allocations must display Mixed or Review required, not a falsely uniform Shared/Inherited label.
- Include system follow-up counts derived from distinct persisted affected controls/capabilities. Keep unrelated approvals and AO decisions unchanged.

## 3. By component and component drawer

- Show Person, Place, Thing and compatible Policy records with subtype, source, delivered capabilities and actual system/boundary placements. A component can deliver multiple capabilities and a capability can have multiple contributors.
- Include authorized local system assignments, contributors from subscribed provider sources, and organization contributors through supporting local capabilities, without duplicates. Preserve directly assigned components with no capability link and explain that state.
- Provider component details are read-only. Manage system placement edits applicability/assignment records, not provider authorship. Open source in library opens the correct record with clear organization-library scope.
- Boundary filters and the drawer display actual component placements, including unassigned/system-wide/multiple placements when supported. Do not store a single capability-level boundary string as a replacement for component-boundary relationships.

## 4. Capability detail: Implementation

- Header identifies selected system, source owner, source revision and local/provider edit authority. Provider metadata is read-only; authorized local edits use existing forms/services.
- Contributor cards show source, classification, subtype and contribution; distinguish provider contributors from supporting organization capabilities. Linking local support must not mutate the provider's contributor list.
- Show applicable boundaries and links to the existing boundary experience. Removing a relationship must not delete a shared component or organization capability.

## 5. Coverage & duties

- Show each control's provider coverage, remaining organization duty, persisted allocation, review state, confirmed source revision and available source revision. Missing/conflicting/outside-baseline/preserved-override states remain visible and unresolved where appropriate.
- Selecting a control opens source comparison and the existing revision-bound confirmation flow. Required checks/notes and permission must be valid before enabling confirmation. A subsequent source/baseline change invalidates stale confirmation and requires reload/review.
- Reuse assigned ISSM/ISSO authorization for responsibility review. Membership, MissionOwner or CSP Admin/support status alone cannot confirm customer responsibilities.
- Link to the full system responsibility page; both surfaces must report the same persisted result. Do not add a second global inheritance dropdown or infer allocation from provider ownership, component type or mapping role.

## 6. Evidence & narratives

- Show authorized evidence references with real owner/source/state and protected open/view actions. Link evidence only with the existing applicable permission. Reference availability does not prove implementation; never expose secret storage paths or bearer URLs.
- Show policy and technical approved content, freshness and proposal state independently. Use existing generation/review services; persist system/control/capability/source provenance.
- Preserve approved content until authorized proposal acceptance. Gate generation/review using actual dependency policies and explain blocked states; do not assume every pending control blocks every narrative operation indiscriminately.
- Verify proposal ID, tenant, system and originating capability before accepting/rejecting. A stale proposal/revision or lost access must not be accepted through this detail route.

## 7. Add from library: complete three-step flow

1. **Select:** browse authorized eligible local/provider library records with search/source filters and pagination. Indicate Already applied; disable duplicate selection. Keep selection across pages using source-qualified stable IDs.
2. **Applicability:** lock the target system to the route. Review each selected source/revision, choose authorized existing component/boundary placements, and link appropriate supporting organization capabilities. Provider source stays read-only. Validate all identifiers and placement compatibility server-side.
3. **Review and add:** preview exact writes, target/source revisions, already-existing relationships and unresolved follow-up. On confirmation persist via the existing setup operation model. Show saved and pending outcomes, not simulated success.

Extend the single-record operation contract for multi-selection and placements, or introduce a durable parent operation composed of existing single-record operations. Choose/document one approach before implementation. Require request-payload binding, actor/scope authorization on resume, stable idempotency keys, execution concurrency protection and per-item outcomes. A timeout/partial failure retry applies only incomplete writes; it must not duplicate subscriptions, assignments or inline-created records. Cancel does not delete shared or already-saved records. Specify draft/operation retention and recovery after refresh.

## 8. Remove from this system

- Show an explicit confirmation with target system, source record and impact preview. Distinguish local capability unlink from provider unsubscribe.
- Revalidate preview/source/relationship revision at execution; reject stale impact. Use existing unsubscribe/reconciliation for provider records and existing local relationship services where applicable.
- Retain shared library records, unrelated contributors and approved historical narratives. Mark applicable responsibilities/narrative dependencies for review through existing services; never change an AO decision.
- Explain boundary placements separately. Do not automatically remove component placements that another capability/system still needs. Expose recoverable failure and a stable retry result.

## API and model work

Extend existing routes where possible; the following are required contracts, not claims that new endpoints already exist:

| Surface | Reuse | Required delta |
|---|---|---|
| System catalog/detail | Organization capability list/detail with explicit system ID | Explicit applied-versus-available query semantics; boundary/type filters; stable source + recordType + ID keys; accurate review counts; component placement projection; scoped action permissions |
| Component applicability | System component reads/local assignments | Provider-source applicability adapter if needed; support multiple placements; authorize target boundary and preserve local/provider ownership |
| Coverage/review | Existing subscription/responsibility service | Include/link revision-bound per-control coverage, duties and change comparison in system detail; no parallel allocation store |
| Evidence/narratives | Existing protected artifact and proposal services | Capability/system-scoped projections and traceability; action permissions; independent policy/technical freshness |
| Add/setup | Existing prepare/get/complete operations | Multi-selection and placement plan; durable request-bound outcomes and safe resume; planned writes must match final execution |
| Removal | Existing unsubscribe/local link operations | Authorized impact preview and stale-request handling; source-qualified local unlink/provider unsubscribe response; explicit retained relationships |

Document exact request/response schemas, route choices, validation limits, pagination and cancellation in the feature contracts before coding. Prefer additive DTO changes. If new applicability relationships are needed, add tenant-scoped migrations/indexes and a compatibility/backfill plan; avoid copying provider records into tenant-editable ownership. Do not use display names as keys. Use existing structured error envelopes and preserve machine-readable 400/401/403/404/409 outcomes in the frontend.

## Implementation sequence

1. Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md`, applicable contract documents and `docs/architecture/workspaces.md`. Resolve setup-versus-review permissions and placement/batch consistency first.
2. Write failing regression tests for system-shell navigation, applied-only data, legacy redirects and cross-system rejection.
3. Add the required server projections/contracts and authorization tests, then integrate the unified list and component drawer.
4. Reuse detail, responsibility, evidence/narrative components/services with system context; split the growing WorkspaceOperationsPage into focused reusable views rather than duplicate implementations.
5. Extend setup/removal persistence and failure recovery, then implement the three-step flow and confirmation dialogs.
6. Verify both roles/scopes, old entry points, responsive themes and local user acceptance before closing. Preserve compatibility routes until verified.

## Acceptance checklist

- [ ] Eight numbered mock views plus component drawer, removal dialog and success/partial-failure states are implemented with real data and correct permissions.
- [ ] Global library and system-applied scope are visibly different; one system sidebar item replaces two without losing inventory functions.
- [ ] Old routes, direct detail links, refresh, back/forward and two simultaneous system tabs preserve authorized scope.
- [ ] An organization-only system works without any CSP subscription or provider baseline selection.
- [ ] Two systems using one provider capability can have different contributors/placements/review results without leaking or overwriting each other.
- [ ] Type/source/boundary filters, pagination and totals agree; components with no capability and capabilities with multiple components remain visible appropriately.
- [ ] Provider metadata cannot be edited through customer controls; system placement changes cannot rewrite shared library ownership.
- [ ] Responsibility detail and existing review page agree; overlapping coverage, removal, missing baseline, conflicts and stale revisions are exercised.
- [ ] Evidence access and proposal review reject foreign tenant/system/capability IDs and preserve approved content.
- [ ] Setup and removal handle timeout, concurrent retry, permission loss and partial failure without duplicates or unrelated deletions.
- [ ] Keyboard tabs/dialog focus, accessible status labels, narrow layouts and light/dark themes match the mock intent.
- [ ] Automated validation is recorded and the user can manually test the full local flow. Screenshots alone do not close the issue.

## Validation and local acceptance

Use synthetic data: two organizations, two systems in one organization, local-only and provider-backed capabilities, all four component classifications, multiple boundaries, one unapplied provider offering and one pending source change. Test CSP oversight/support, organization Administrator, ISSM, ISSO, SCA, AO, MissionOwner and a member without system assignment. Assert each permission against the current server policy; do not make every pictured action available to every role.

Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root; run `npx tsc --noEmit` and `npm test` in `src/Ato.Copilot.Dashboard`, plus relevant Playwright system/workspace flows. Expected: clean types/build and passing relevant tests. Record any full-suite baseline failures separately with logs rather than reporting a failing suite as green.

Manual path: select an ordinary organization system → open Security Capabilities → switch both list views → inspect a component → inspect all three capability-detail tabs → add one provider and one local capability with different placements → review exact writes → recover one failed write → confirm one control as ISSM/ISSO → review a narrative proposal through the authorized workflow → remove only the system applicability → verify shared records and other systems remain intact.
