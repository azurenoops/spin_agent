# Research: Implementation Roadmap (031)

**Date**: 2026-03-15

## R1: AI-Driven Phase Clustering Strategy

**Decision**: Use Azure OpenAI (GPT-4o via existing `IChatClient`) to cluster controls into phases based on control relationships, complexity, severity, and dependency chains.

**Rationale**: The spec requires AI-driven clustering (Clarification Q2). The existing `TaskEnrichmentService` already uses `IChatClient` for AI-driven remediation script generation (Feature 012). The same pattern applies here — send the gap list with control metadata to the LLM and receive a structured phase grouping. CAT I/critical gaps are weighted toward early phases via the prompt, not hardcoded logic.

**Alternatives Considered**:
- Deterministic severity-first grouping (rejected — user specifically chose AI-driven clustering for more nuanced groupings)
- Manual-only assignment (rejected — defeats the automation purpose)

**Implementation Approach**: Create a `RoadmapGenerationPrompt` that receives gap analysis data (control IDs, families, gap types) and returns a structured JSON response with phase assignments. Use `System.Text.Json` deserialization of the AI response. Fall back to severity-first deterministic grouping if AI call fails.

---

## R2: Effort Estimation Without Historical Data

**Decision**: AI estimates from control complexity and NIST SP 800-53 guidance as primary source. Historical Kanban task completion data refines estimates when available.

**Rationale**: Clarification Q3 confirmed AI-first estimation. The existing `ITaskEnrichmentService.EnrichTaskAsync` generates remediation scripts and validation criteria — effort estimation is a natural extension. The AI prompt can analyze control description, enhancement count, and typical implementation complexity to produce person-day estimates.

**Alternatives Considered**:
- Fixed defaults per family (rejected — too coarse, doesn't account for control complexity differences within a family)
- Require manual estimates (rejected — defeats day-one usability)

**Implementation Approach**: Include effort estimation in the same AI prompt as phase clustering (R1). The prompt provides control descriptions from NIST 800-53 and asks for effort estimates alongside phase assignments. When historical data exists (completed Kanban tasks with the same control ID from any system), use the median completion time as input to refine the AI estimate.

Historical data query: `SELECT ControlId, AVG(DATEDIFF(day, CreatedAt, UpdatedAt)) FROM RemediationTasks WHERE Status = 'Done' GROUP BY ControlId`.

---

## R3: Weighted Severity Risk Reduction Formula

**Decision**: Each gap contributes weighted points: CAT I = 10, CAT II = 5, CAT III = 1. Phase risk reduction = (sum of points for items in phase) / (total points across all items) × 100%.

**Rationale**: Clarification Q4 confirmed this formula. It produces intuitive, explainable results suitable for AO briefings. A system with 5 CAT I gaps (50 points) and 45 CAT III gaps (45 points) would show closing the 5 CAT I items reduces 52.6% of risk — correctly reflecting their outsized impact.

**Alternatives Considered**:
- Simple gap count ratio (rejected — treats a CAT III gap as equal to a CAT I gap)
- CVSS-based scoring (rejected — over-engineered for controls that don't have CVSS scores)

**Implementation Approach**: Implement as a pure function `CalculateRiskReduction(items, totalPoints)` in the `RoadmapService`. The function is deterministic and trivially testable. Severity is determined from the control's existing categorization in the baseline or from the gap analysis data.

**Severity Mapping**: Controls without an explicit finding severity default to CAT III (1 point). Controls with associated findings use the finding's severity. Controls with multiple findings use the highest severity.

---

## R4: Control Dependency Data Source

**Decision**: Derive dependency ordering from NIST SP 800-53 Rev 5 control family ordering and well-known prerequisite relationships. Do not maintain a custom dependency graph.

**Rationale**: The spec's Assumptions section states dependencies come from "NIST SP 800-53 control relationships and family ordering — not from a custom-maintained dependency graph." A small, curated set of well-known dependencies suffices:

| Prerequisite | Dependent | Reason |
|-------------|-----------|--------|
| AC-2 (Account Mgmt) | AU-6 (Audit Review) | Can't review audits without accounts to audit |
| AC-2 (Account Mgmt) | AC-3 (Access Enforcement) | Enforcement requires managed accounts |
| IA-2 (Identification & Auth) | AC-2 (Account Mgmt) | Account management requires authentication |
| CM-6 (Config Settings) | CM-7 (Least Functionality) | Must configure before restricting |
| RA-3 (Risk Assessment) | CA-5 (POA&M) | POA&M requires identified risks |
| PL-2 (System Security Plan) | CA-2 (Assessment) | Assessment requires an SSP |

**Implementation Approach**: Embed these as a static `Dictionary<string, string[]>` in the `RoadmapService`. During phase generation, the AI prompt includes these dependencies as constraints. Post-AI validation ensures no dependency violations exist in the returned phases.

---

## R5: Bi-Directional Kanban Sync

**Decision**: Implement sync via event-based status propagation — when a Kanban task status changes, update the linked roadmap item, and vice versa.

**Rationale**: FR-011 requires bi-directional sync. The existing `KanbanService.MoveTaskAsync` method is the control point for task status changes. Adding a post-move hook that updates the linked `RoadmapItem` is the cleanest approach.

**Implementation Approach**:
1. Add a nullable `RoadmapItemId` FK to `RemediationTask` to link tasks to roadmap items.
2. In `RoadmapService`, when creating a board from a roadmap (FR-010), set `RoadmapItemId` on each task and set `LinkedKanbanTaskId` on each roadmap item.
3. Add a `SyncRoadmapItemStatusAsync(string taskId, TaskStatus newStatus)` method to `IRoadmapService` that maps TaskStatus → RoadmapItemStatus and recalculates phase progress.
4. Call `SyncRoadmapItemStatusAsync` from `KanbanService.MoveTaskAsync` after the status change is persisted.
5. For roadmap-to-Kanban direction: when `RoadmapService.UpdateRoadmapItemAsync` changes an item's role/effort, update the linked Kanban task.

---

## R6: PDF Export Approach

**Decision**: Generate PDF server-side using QuestPDF (MIT-licensed .NET library) for programmatic document generation.

**Rationale**: Clarification Q5 confirmed PDF export with timeline, phase tables, and risk curve. Server-side generation ensures consistent output regardless of client. The dashboard's React components cannot directly render to PDF from the backend, so we generate an HTML template and convert to PDF.

**Implementation Approach**: Use `QuestPDF` (MIT-licensed .NET library) to generate PDFs programmatically. Render:
1. Header with system name, roadmap status, generation date
2. Summary metrics (total gaps, effort, risk reduction, target timeline)
3. Phase timeline as a simple horizontal bar chart (drawn with QuestPDF primitives)
4. Phase detail tables (Control ID, Gap Type, Effort, Role, Status)
5. Risk reduction curve (rendered as a simple line chart using QuestPDF drawing primitives)

Exposed via MCP tool `compliance_export_roadmap_pdf` and dashboard endpoint `GET /api/dashboard/systems/{systemId}/roadmap/export`.

---

## R7: Adaptive Card Design for Roadmap

**Decision**: Create a new `roadmapCard.ts` builder following the existing card builder pattern. Route via `dataType === "roadmap"` in `cardRouter.ts`.

**Rationale**: The existing `remediationPlanCard.ts` is the closest analog — it shows phases, findings, risk reduction, and action buttons. The roadmap card extends this with per-phase effort totals and progress indicators.

**Implementation Approach**: Two card variants:
1. **Summary card** (`roadmap` data type): Shows total gaps, phases count, total effort, risk reduction, per-phase summary rows. Action buttons: "Create Kanban Board", "Export PDF", "Show Phase Details".
2. **Phase detail card** (`roadmapPhaseDetail` data type): Shows individual controls in a phase with effort, role, gap type, dependencies, status. Action button: "Back to Roadmap".

Both follow the `buildAgentAttribution` + `buildSuggestionButtons` shared component pattern.

---

## R8: RBAC Enforcement Pattern

**Decision**: Follow the existing PIM tier enforcement pattern in `BaseTool` for MCP tools. For dashboard endpoints, add role-based authorization checks.

**Rationale**: FR-017 requires ISSM-only for write operations, read-only for other roles. The existing `BaseTool` has PIM tier enforcement. Dashboard endpoints currently don't enforce RBAC (they're anonymous), but the MCP tools do.

**Implementation Approach**:
- MCP tools: Set `RequiredPimTier = "Compliance.SecurityLead"` for generate/update/delete tools. Set no tier requirement for get/list tools (any authenticated role can read).
- Dashboard endpoints: Since the dashboard is currently unauthenticated (runs behind nginx in Docker), RBAC enforcement lives in the MCP tool layer. The dashboard shows read-only views by default. Write operations (generate, update) are initiated through Teams/VS Code → MCP tools, not directly through dashboard endpoints.
