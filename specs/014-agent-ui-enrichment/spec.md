# Feature Specification: Agent-to-UI Response Enrichment

**Feature Branch**: `014-agent-ui-enrichment`  
**Created**: 2026-02-26  
**Status**: Draft  
**Input**: User description: "Enrich the agent-to-UI response pipeline so that MCP server responses carry intent classification, structured data, tool execution details, follow-up prompts, and suggestions for Compliance, KnowledgeBase, and Configuration agents. Fix field name mismatches between server and clients. Remove all M365 cards not related to Compliance, KnowledgeBase, or Configuration (cost, deployment, infrastructure, resource). Enrich VS Code analysis panel with full compliance context. Align response envelope with Constitution VII."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Enriched MCP Server Responses (Priority: P1)

As a frontend consumer (VS Code extension, M365 extension, or Chat app), I need the MCP server to return structured, typed responses with intent classification, tool execution details, and suggestions so that I can render rich, context-appropriate UI instead of bare text.

Currently the MCP server only maps 5 of 11 available `McpChatResponse` fields when building the success response. `ToolsExecuted`, `Suggestions`, `RequiresFollowUp`, and `Metadata` are silently dropped even though the underlying `AgentResponse` carries `ToolsExecuted` data. The field `AgentName` on the server serializes as `agentName` in JSON but TS clients read `agentUsed`, causing agent attribution to silently fail. No `intentType` is ever populated, making all intent-based card routing in M365 unreachable dead code.

**Why this priority**: This is the root cause of all downstream UI rendering failures. Without enriched server responses, no amount of client-side improvement can produce rich cards. Every other user story depends on this data being available.

**Independent Test**: Can be tested by sending a POST to `/mcp/chat` with a compliance question (e.g., "Run a FedRAMP assessment") and verifying the JSON response includes `intentType: "compliance"`, a populated `toolsExecuted` array, `agentUsed` (not `agentName`), and a `data` object with structured compliance results. Can also be verified by sending a configuration question and confirming `intentType: "configuration"`.

**Acceptance Scenarios**:

1. **Given** a user sends a compliance-related message, **When** the MCP server processes it via ComplianceAgent, **Then** the response includes `intentType: "compliance"`, `agentUsed: "Compliance Agent"`, and `toolsExecuted` with at least one entry showing tool name, success status, and execution time.
2. **Given** a user sends a knowledge base question, **When** the MCP server processes it via KnowledgeBaseAgent, **Then** the response includes `intentType: "knowledgebase"` and `agentUsed: "KnowledgeBase Agent"`.
3. **Given** a user sends a configuration request, **When** the MCP server processes it via ConfigurationAgent, **Then** the response includes `intentType: "configuration"` and `agentUsed: "Configuration Agent"`.
4. **Given** the agent response includes tool execution results, **When** the MCP server builds the response, **Then** each tool execution is mapped to the `toolsExecuted` array with `toolName`, `success`, and `executionTimeMs`.
5. **Given** the agent response text contains structured data (e.g., compliance scores, control counts), **When** the MCP server builds the response, **Then** it extracts structured fields into a `data` object appropriate for the intent type.
6. **Given** the MCP server encounters an error, **When** building the error response, **Then** each error entry includes a machine-readable `errorCode` and a human-readable `suggestion` for corrective action, per Constitution VII.
7. **Given** any successful response, **When** serialized to JSON, **Then** the agent name field is serialized as `agentUsed` (not `agentName`) to match client expectations.

---

### User Story 2 — Rich M365 Adaptive Cards for Compliance, KnowledgeBase, and Configuration (Priority: P2)

As a compliance officer using Microsoft Teams, I want intent-specific Adaptive Cards that display compliance scores, knowledge base answers, and configuration status with action buttons, so that I can take immediate action without switching tools.

Currently, the M365 extension contains 8 card builders but only the generic card and error card ever render because the server never sends `intentType` or structured `data`. Additionally, 4 card builders (cost, deployment, infrastructure, resource) serve intents that no existing agent supports. These should be removed to reduce dead code.

**Why this priority**: M365 integration is the primary channel for non-developer stakeholders. Rich cards directly improve compliance officer productivity and decision-making quality.

**Independent Test**: Can be tested by sending a POST to `/api/messages` with `{ "text": "Run compliance assessment", "conversation": { "id": "test-1" }, "from": { "id": "user-1" } }` and verifying the response contains an Adaptive Card with compliance score, color-coded percentage, passed/warning/failed counts, and action buttons instead of a bare generic text card.

**Acceptance Scenarios**:

1. **Given** the MCP server returns `intentType: "compliance"` with structured compliance data, **When** the M365 extension builds the response, **Then** a compliance Adaptive Card renders with overall score (color-coded: green ≥80%, orange ≥60%, red <60%), passed/warning/failed control counts, and action buttons.
2. **Given** the MCP server returns `intentType: "knowledgebase"`, **When** the M365 extension builds the response, **Then** a knowledge base Adaptive Card renders with the answer text, source references, and a "Learn More" action.
3. **Given** the MCP server returns `intentType: "configuration"`, **When** the M365 extension builds the response, **Then** a configuration Adaptive Card renders with current settings (framework, baseline, subscription, environment) and "Update Setting" actions.
4. **Given** the MCP server returns `requiresFollowUp: true` with `followUpPrompt` and `missingFields`, **When** the M365 extension builds the response, **Then** a follow-up Adaptive Card renders with the prompt, numbered missing fields, and quick-reply buttons.
5. **Given** a response with `agentUsed` populated, **When** any card renders, **Then** an "agent attribution" footer shows the agent name.
6. **Given** the M365 extension codebase, **When** reviewing card builders, **Then** the cost, deployment, infrastructure, and resource card builders have been removed and no longer exist in the source.
7. **Given** the M365 extension processes a long-running request, **When** waiting for the MCP server response, **Then** a typing indicator or "processing" status is shown to the user in Teams.

---

### User Story 3 — Enriched VS Code Compliance Analysis Panel (Priority: P2)

As a DevSecOps engineer using VS Code, I want the compliance analysis panel to display full NIST 800-53 context including control family, framework reference, assessment scope, 5-level severity, resource context, and remediation actions so that I can assess and act on findings without leaving the editor.

Currently the VS Code analysis panel renders only 5 finding fields (controlId, title, severity with 3 levels, description, recommendation). It is missing control family grouping, framework references (e.g., "NIST 800-53 Rev 5"), assessment scope indicators, Critical and Informational severity levels, resource identifiers, and auto-remediation indicators. Constitution VII mandates all compliance output include control family identifier, framework reference, and assessment scope.

**Why this priority**: VS Code is the primary developer interface. Engineers need full compliance context to prioritize and remediate findings during coding.

**Independent Test**: Can be tested by running "Security Posture Intelligence Navigator: Analyze Current File for Compliance" on a `.bicep` file and verifying the webview panel shows findings with 5 severity levels (Critical/High/Medium/Low/Informational), control family groupings, a "NIST 800-53 Rev 5" framework badge, resource identifiers, and auto-remediation indicators.

**Acceptance Scenarios**:

1. **Given** compliance analysis results, **When** the webview panel renders, **Then** findings are grouped by control family (e.g., AC — Access Control, AU — Audit and Accountability) with collapsible sections.
2. **Given** compliance findings, **When** severity badges render, **Then** five severity levels are displayed: Critical (purple), High (red), Medium (orange), Low (yellow), Informational (blue).
3. **Given** any compliance analysis, **When** the panel header renders, **Then** it displays the framework reference ("NIST 800-53 Rev 5") and assessment scope (file name or workspace name).
4. **Given** a finding with resource context, **When** the finding card renders, **Then** it shows `resourceId`, `resourceType`, and `controlFamily` alongside the existing controlId and title.
5. **Given** a finding marked as auto-remediable, **When** the finding card renders, **Then** an "Auto-Remediate" indicator/badge is visible.
6. **Given** enriched response data from the MCP server, **When** the analysis panel renders tool execution details, **Then** a summary section shows which tools ran, their execution times, and success/failure status.

---

### User Story 4 — Enriched VS Code Chat Participant Responses (Priority: P3)

As an engineer chatting with `@ato` in VS Code Copilot Chat, I want to see agent attribution, tool execution summaries, contextual follow-up suggestions, and compliance score highlights in the chat response so that I get actionable, rich feedback instead of bare text.

Currently the VS Code participant renders only raw Markdown text. Agent attribution fails silently due to the `agentUsed` vs `agentName` field mismatch. Template rendering is dead code because the server never sends templates. Follow-up prompts and suggestions are not rendered.

**Why this priority**: Chat is the most frequent interaction mode for developers. Enriching it improves the daily workflow but depends on server-side fixes (US1) being in place first.

**Independent Test**: Can be tested by typing `@ato /compliance How do I comply with AC-2?` in Copilot Chat and verifying the response shows agent attribution ("Processed by: Compliance Agent"), a tool execution summary, and follow-up suggestions.

**Acceptance Scenarios**:

1. **Given** the MCP server returns `agentUsed`, **When** the chat response renders, **Then** an attribution footer "Processed by: {agentUsed}" appears.
2. **Given** the MCP server returns `toolsExecuted`, **When** the chat response renders, **Then** a collapsible "Tools Used" summary shows each tool name and execution time.
3. **Given** the MCP server returns `suggestions`, **When** the chat response renders, **Then** each suggestion appears as a clickable follow-up button that re-submits the suggestion as a new message.
4. **Given** the MCP server returns `requiresFollowUp: true` with `followUpPrompt`, **When** the chat response renders, **Then** the follow-up prompt appears with a "Reply" button.
5. **Given** a response with `intentType: "compliance"` and structured `data`, **When** the chat response renders, **Then** a compliance summary block shows the score and pass/warn/fail counts in a formatted Markdown table before the main response text.

---

### Edge Cases

- What happens when the MCP server returns a response with no `intentType` (e.g., from a future unknown agent)? → Falls through to the generic card (M365) or plain Markdown (VS Code). No error.
- What happens when `toolsExecuted` is empty? → Tool execution section is omitted from the UI. No empty sections shown.
- What happens when `data` is null or empty for a known intent type? → Cards render with available text response and omit missing structured fields gracefully. No crash.
- What happens when `suggestions` is empty? → Follow-up suggestion buttons are not rendered. No empty button area shown.
- What happens when the M365 extension receives a response with a removed intent type (cost, deployment, etc.)? → Falls through to generic card. The removed intent code paths no longer exist.
- What happens when `followUpPrompt` is set but `missingFields` is empty? → The follow-up card shows the prompt text without a numbered list. Quick-reply buttons are omitted.
- What happens when the agent response has `ToolsExecuted` entries but some have failed? → Failed tools are shown with a failure indicator. The overall response is still rendered if `Success` is true.
- What happens when `errorCode` is missing from an error response? → Client falls back to displaying only the error message text. `suggestion` defaults to "Please try again or contact support."
- What happens when an action button sends an `action` request but the target agent tool is unavailable or unauthorized? → Server returns a structured error with `errorCode: "ACTION_UNAVAILABLE"` and a `suggestion` guiding the user. The card/panel shows the error inline without navigation.
- What happens when a drill-down for a controlId returns no data? → Client shows "No additional details available for {controlId}" inline. No error card or empty panel.
- What happens when M365 conversation history exceeds memory limits? → History is truncated to the most recent 20 exchanges per conversation ID. Oldest entries are dropped silently.
- What happens when the SSE stream connection drops mid-scan? → Extensions retry once with exponential backoff, then fall back to synchronous `/mcp/chat`. Any partial text already received is preserved and the final synchronous response replaces it.
- What happens when a `toolProgress` event reports 100% but no `toolComplete` follows? → Extensions treat 100% progress as visually complete for that tool and wait for the next event. If no event arrives within 30 seconds, the tool is marked as timed out in the UI.
- What happens when the IaC scanning tool receives a non-IaC file? → Returns an empty findings array with a `response` message explaining the file type is not supported for compliance scanning. No error.
- What happens when a user clicks "Apply Fix" but CAC authentication fails or is unavailable? → The remediation is blocked. VS Code shows an error notification: "CAC authentication required for remediation actions." M365 shows an error card with `errorCode: "CAC_AUTH_REQUIRED"` and a suggestion to verify smart card reader connectivity.
- What happens when a user has CAC auth but no active PIM role assignment? → The action is blocked. Extensions display a PIM activation prompt with the required role name and scope. The user must activate PIM before retrying. No partial execution occurs.
- What happens when a PIM role expires mid-operation (e.g., during a long remediation)? → The server returns `errorCode: "PIM_EXPIRED"` with a suggestion to re-activate PIM and retry. The finding status is not changed. The audit log records the failure reason.
- What happens when a remediation script execution fails after user confirmation? → The finding status remains `Open` (not changed to `Remediated`). The audit log records the failure. The UI shows an error with the failure reason and suggests manual remediation.

## Requirements *(mandatory)*

### Functional Requirements

#### MCP Server Response Enrichment

- **FR-001**: MCP server MUST derive `intentType` from the selected agent's `AgentId` property in `ProcessChatRequestAsync`: `"compliance-agent"` → `"compliance"`, `"knowledgebase-agent"` → `"knowledgebase"`, `"configuration-agent"` → `"configuration"`. Unknown agent IDs MUST default to `"general"`. No changes to agents are required — the server performs this mapping using the `targetAgent` variable already in scope.
- **FR-002**: MCP server MUST map `ToolsExecuted` from the `AgentResponse` to the `McpChatResponse`, copying `ToolName`, `Success`, and `ExecutionTimeMs` for each tool execution result.
- **FR-003**: MCP server MUST serialize the agent name field as `agentUsed` in the JSON response to match TypeScript client expectations. The internal C# property name may remain `AgentName` but MUST use a JSON serialization attribute to output `agentUsed`.
- **FR-004**: MCP server MUST populate `Suggestions` from the `AgentResponse.Suggestions` property when present. Agents dynamically populate this list based on actual results to indicate contextually relevant follow-up actions (e.g., after a failing compliance scan: "Generate remediation plan", "View detailed findings"; after a passing scan: "Export compliance report", "View trend"). Suggestions MUST NOT be hardcoded per intent type.
- **FR-005**: MCP server MUST populate `RequiresFollowUp`, `followUpPrompt`, and `missingFields` from the corresponding optional properties on `AgentResponse` when the agent determines that additional user input is needed to complete the request.
- **FR-005a**: The `AgentResponse` model MUST be extended with optional properties: `Suggestions` (list of strings), `RequiresFollowUp` (bool), `FollowUpPrompt` (string), `MissingFields` (list of strings), and `ResponseData` (dictionary of string to object). All properties are optional and default to empty/false/null so existing agent code is unaffected.
- **FR-006**: MCP server MUST include a structured `data` object in the response, populated directly from the optional `AgentResponse.ResponseData` dictionary when present. The server passes this dictionary through as the `data` field without transformation. Agents populate intent-specific fields (e.g., `complianceScore`, `passedControls` for compliance; `answer`, `sources` for knowledge base).
- **FR-007**: The `McpChatResponse.Errors` property MUST be changed from `List<string>` to `List<ErrorDetail>`. Each `ErrorDetail` MUST include `errorCode` (machine-readable string, e.g., `"AGENT_TIMEOUT"`, `"UNAUTHORIZED"`, `"TOOL_FAILURE"`), `message` (human-readable description), and `suggestion` (corrective guidance) per Constitution VII. All existing code that populates `Errors` MUST be updated to use the new `ErrorDetail` type.

#### Agent Response Data Population

- **FR-007a**: `ComplianceAgent` MUST populate `AgentResponse.ResponseData` with a typed dictionary after compliance assessments: `{ "type": "assessment", "complianceScore": <double>, "passedControls": <int>, "warningControls": <int>, "failedControls": <int>, "findings": <int>, "framework": <string>, "assessmentScope": <string> }`. For finding-detail responses: `{ "type": "finding", "finding": <ComplianceFinding> }`. For remediation plans: `{ "type": "remediationPlan", "phases": [...], "findings": [...], "riskReduction": <double> }`. For kanban board views: `{ "type": "kanban", "board": <KanbanBoard> }`.
- **FR-007b**: `KnowledgeBaseAgent` MUST populate `AgentResponse.ResponseData` with: `{ "type": "answer", "answer": <string>, "sources": [<string>], "controlId": <string?>, "controlFamily": <string?> }`. For NIST control lookups: `{ "type": "control", "controlId": <string>, "controlFamily": <string>, "title": <string>, "statement": <string> }`.
- **FR-007c**: `ConfigurationAgent` MUST populate `AgentResponse.ResponseData` with: `{ "type": "configuration", "subscriptionId": <string>, "framework": <string>, "baseline": <string>, "cloudEnvironment": <string> }`.
- **FR-007d**: All three agents MUST populate `AgentResponse.Suggestions` with contextually relevant follow-up actions based on results. Examples: after a failing assessment → `["Generate remediation plan", "View detailed findings", "Show kanban board"]`; after a passing scan → `["Export compliance report", "View trend"]`; after a KB query → `["Show related controls", "View STIG mapping"]`.

#### M365 Extension Card Changes

- **FR-008**: M365 extension MUST remove the cost card builder, deployment card builder, infrastructure card builder, and resource card builder from the codebase. These files and their imports MUST be deleted entirely.
- **FR-009**: M365 extension MUST add a knowledge base Adaptive Card builder that displays the answer text, source references (if available in `data`), and a "Learn More" action button.
- **FR-010**: M365 extension MUST add a configuration Adaptive Card builder that displays current settings (framework, baseline, subscription ID, cloud environment) and "Update Setting" action buttons for each configurable field.
- **FR-010a**: M365 extension MUST add the following additional compliance-domain Adaptive Card builders:
  - **Finding detail card** — severity badge (5-level), resource context (`resourceId`, `resourceType`), remediation guidance, "Apply Fix" action button for auto-remediable findings, link to Azure portal.
  - **Remediation plan card** — prioritized finding list, 5-phase timeline, risk reduction projection, "Start Remediation" action set.
  - **Alert lifecycle card** — alert detail with severity, affected resources, SLA countdown timer, "Acknowledge" / "Dismiss" / "Escalate" action buttons.
  - **Compliance trend card** — sparkline-style visualization of score over time, significant events, trend direction indicator.
  - **Evidence collection card** — completeness meter, evidence items with verification hashes, "Collect More" action.
  - **NIST control card** — control statement, implementation guidance, related STIGs, FedRAMP baseline applicability.
  - **Multi-turn clarification card** — when the agent needs more info, show missing fields with input dropdowns (subscription, framework, baseline).
  - **Remediation kanban board card** — shows remediation tasks organized by status columns (To Do, In Progress, Done/Verified) with finding title, severity badge, assigned resource, and "Move to In Progress" / "Mark Complete" action buttons. Uses existing `KanbanBoardShowTool` data via `data.type === "kanban"`. Each task links to its finding detail card.
- **FR-011**: M365 extension MUST update the intent-based card routing to handle: `"compliance"` → compliance card (with sub-routing to finding detail, remediation plan, alert lifecycle, compliance trend, evidence collection, kanban board cards based on `data` type), `"knowledgebase"` → knowledge base card (with sub-routing to NIST control card), `"configuration"` → configuration card, `"clarification"` → multi-turn clarification card, default → generic card. Removed intent types (cost, deployment, infrastructure, resource) MUST NOT appear in the routing logic.
- **FR-012**: M365 extension MUST update the `McpResponse` TypeScript interface to align with the enriched server response, using `agentUsed` (matching server output), `intentType`, `data`, `toolsExecuted`, `suggestions`, `requiresFollowUp`, `followUpPrompt`, and `missingFields`. (See also FR-026 for cross-cutting interface alignment.)
- **FR-013**: All M365 Adaptive Cards MUST display an agent attribution footer showing `agentUsed` when present.
- **FR-014**: M365 extension MUST display a typing indicator or "Security Posture Intelligence Navigator is processing..." message while waiting for the MCP server response to provide progress feedback for long-running operations per Constitution VII.

#### Drill-Down Navigation & Action Buttons

- **FR-014a**: The MCP chat request model MUST be extended with an optional `action` field (string, e.g., `"remediate"`, `"collectEvidence"`, `"acknowledgeAlert"`, `"drillDown"`) and an optional `actionContext` dictionary (string-to-string, carrying parameters like `findingId`, `controlId`, `alertId`). When `action` is present, the server routes the request to the appropriate agent tool based on the action type and context, bypassing normal chat flow.
- **FR-014b**: M365 Adaptive Card action buttons ("Apply Fix", "Collect More", "Acknowledge", "Dismiss", "Escalate", "Start Remediation", "View Details") MUST submit structured payloads to `/mcp/chat` with the `action` field and relevant `actionContext`. The response is rendered as an updated card replacing the original.
- **FR-014c**: VS Code analysis panel MUST make control IDs and finding IDs clickable. Clicking triggers a new `/mcp/chat` request with `action: "drillDown"` and `actionContext: { "controlId": "<id>" }`, rendering the detail response in a side panel or inline expansion.
- **FR-014d**: M365 extension MUST maintain conversation history per conversation ID (currently hardcoded empty array). Each action and response MUST be appended to the history to support multi-turn state.

#### VS Code Analysis Panel Enrichment

- **FR-015**: VS Code analysis panel MUST display findings with 5 severity levels: Critical (purple badge), High (red badge), Medium (orange badge), Low (yellow badge), Informational (blue badge).
- **FR-016**: VS Code analysis panel MUST group findings by control family (e.g., "AC — Access Control", "AU — Audit and Accountability") with collapsible section headers showing the count of findings per family.
- **FR-017**: VS Code analysis panel MUST display a framework reference badge ("NIST 800-53 Rev 5") and assessment scope (file name or workspace name) in the panel header.
- **FR-018**: VS Code analysis panel MUST display `resourceId`, `resourceType`, `controlFamily`, and `riskLevel` for each finding when available, in addition to the existing `controlId`, `title`, `severity`, `description`, and `recommendation`.
- **FR-018a**: VS Code analysis panel MUST display `autoRemediable` flag with an "Apply Fix" action button. When clicked, open a read-only diff preview of the `remediationScript` showing proposed changes against the current file state. The preview MUST also display: affected resource (`resourceId`, `resourceType`), risk level, control ID, and finding severity. A separate "Confirm & Apply" button executes the remediation only after explicit user confirmation.
- **FR-018b**: VS Code analysis panel MUST track finding status lifecycle: `Open` → `Acknowledged` → `Remediated` → `Verified`. Status changes are persisted via action calls to `/mcp/chat` with `action: "updateFindingStatus"`.
- **FR-018c**: Any action that modifies infrastructure (remediation execution via "Confirm & Apply" or "Apply Fix", evidence collection that writes to storage, alert acknowledgment/escalation, configuration changes, finding status updates) MUST require both:
  1. **CAC (Common Access Card) authentication** — the user's CAC-derived identity (certificate subject DN or UPN) MUST be passed in the action request as `authenticatedUser`. The server MUST validate the CAC identity against the compliance authorization middleware before executing.
  2. **PIM (Privileged Identity Management) elevation** — the user MUST have an active PIM role assignment for the target scope (subscription/resource group). The server MUST verify the PIM-elevated role is active (not expired) before executing. If PIM elevation is not active, the extension MUST prompt the user to activate their eligible PIM role and provide a deep link to the Azure PIM activation page.
- **FR-018c-i**: VS Code extension MUST check for active PIM elevation before submitting infrastructure-modifying actions by sending an action request to `/mcp/chat` with `action: "checkPimStatus"` and `actionContext: { "scope": "..." }`. The server delegates to the existing `PimListActiveTool` (already registered as an MCP tool in `ComplianceMcpTools`). If no active role covers the target scope, display a notification: "Privileged access required. Activate your PIM role for {scope} before proceeding." with an "Activate PIM" button that sends `action: "activatePim"` with `actionContext: { "roleName": "...", "scope": "...", "justification": "..." }` (routed to existing `PimActivateRoleTool`). If the role requires approval (`PendingApproval`), show "Approval pending from security lead" status.
- **FR-018c-ii**: M365 extension MUST check for active PIM elevation before submitting infrastructure-modifying actions using the same action flow (`action: "checkPimStatus"`). If no active role covers the target scope, display a card with: "Privileged access required", the required role, target scope, eligible roles (fetched via `action: "listEligiblePimRoles"` routed to existing `PimListEligibleTool`), and an "Activate PIM" Action.Submit button that sends `action: "activatePim"` with justification and scope.
- **FR-018c-iii**: No new HTTP endpoints or APIs are needed for PIM operations. The existing MCP tools (`PimListActiveTool`, `PimActivateRoleTool`, `PimListEligibleTool`, `PimExtendRoleTool`, `PimDeactivateRoleTool`, `PimApproveRequestTool`, `PimDenyRequestTool`, `PimHistoryTool`) are already registered in `ComplianceMcpTools` and accessible via the `/mcp/chat` action routing (FR-014a) or the `/mcp/tools` endpoint.
- **FR-018d**: All infrastructure-modifying executions MUST be audit-logged with: user identity (CAC-derived), PIM role used, finding ID, control ID, remediation script hash (SHA-256), timestamp, execution result (success/failure), and target resource ID. Audit entries flow through the existing `AuditLoggingMiddleware`.
- **FR-018e**: Both extensions MUST display a confirmation view before executing any remediation:
  - **M365**: Display a confirmation Adaptive Card showing the remediation script preview, affected resource (`resourceId`, `resourceType`), risk level, control ID, finding severity, and "Confirm Remediation" / "Cancel" action buttons.
  - **VS Code**: Display a confirmation panel/dialog showing the same information: remediation script diff preview, affected resource (`resourceId`, `resourceType`), risk level, control ID, finding severity, and "Confirm & Apply" / "Cancel" buttons.
  - No remediation executes without the confirmation step in either extension.
- **FR-019**: VS Code analysis panel MUST display an "Auto-Remediate" badge on findings where `autoRemediable` is true.
- **FR-020**: VS Code analysis panel MUST display a tool execution summary section showing which tools ran, their execution times, and success/failure status when `toolsExecuted` data is present in the response.

#### VS Code Chat Participant Enrichment

- **FR-021**: VS Code chat participant MUST display agent attribution ("Processed by: {agentUsed}") when the response includes a non-empty `agentUsed` field, using the corrected field name from the server.
- **FR-022**: VS Code chat participant MUST render a "Tools Used" summary showing tool names and execution times when `toolsExecuted` is present and non-empty.
- **FR-023**: VS Code chat participant MUST render `suggestions` as clickable follow-up buttons that re-submit the suggestion text as a new `@ato` message.
- **FR-023a**: M365 extension MUST render `suggestions` as Action.Submit buttons on every card. Clicking a suggestion sends it as a new message to `/mcp/chat` with the current `conversationId`, maintaining multi-turn context.
- **FR-023b**: After a compliance assessment, typical contextual suggestions include: "Generate remediation plan", "Export SSP", "View compliance trend", "Collect evidence for {framework}", "Show kanban board". After a knowledge base query: "Show related controls", "View STIG mapping". After a configuration change: "Run dry-run assessment", "View current configuration". These are examples — agents determine the actual suggestions dynamically based on results.
- **FR-024**: VS Code chat participant MUST render the `followUpPrompt` with a "Reply" action button when `requiresFollowUp` is true.
- **FR-025**: VS Code chat participant MUST render a compliance summary (score, pass/warn/fail counts) as a formatted Markdown table when `intentType` is `"compliance"` and structured `data` is present.

#### Cross-Cutting TypeScript Interface Updates

- **FR-026**: Both VS Code `McpChatResponse` and M365 `McpResponse` TypeScript interfaces MUST be updated to align with the enriched server response: `agentUsed`, `intentType`, `toolsExecuted`, `suggestions`, `requiresFollowUp`, `followUpPrompt`, `data`, and structured `errors` (`ErrorDetail[]`). The interfaces in both extensions MUST be identical in shape. (Satisfies US3 and US4.)

#### Streaming & Real-Time Progress

- **FR-029a**: Both VS Code and M365 extensions MUST connect to the `/mcp/chat/stream` SSE endpoint for long-running operations (compliance scans, evidence collection, remediation). The SSE stream MUST emit the following typed events in order:
  - `agentRouted` — emitted when the server selects the target agent; payload includes `agentId` and `agentName`.
  - `thinking` — emitted when the agent begins processing; payload includes `status: "processing"`.
  - `toolStart` — emitted when a tool invocation begins; payload includes `toolName` and `toolIndex`.
  - `toolProgress` — emitted periodically during long tool executions; payload includes `toolName`, `percentComplete` (0–100), and optional `statusMessage`.
  - `toolComplete` — emitted when a tool finishes; payload includes `toolName`, `success`, `executionTimeMs`, and optional `resultSummary`.
  - `partial` — emitted for incremental response text chunks; payload includes `text` (appended to the response).
  - `validating` — emitted when the agent performs post-processing validation; payload includes `status: "validating"`.
  - `complete` — emitted once with the final full `McpChatResponse` payload (same schema as `/mcp/chat` response).
- **FR-029b**: VS Code chat participant MUST show real-time progress during streaming: display "Routing to {agentName}..." on `agentRouted`, show a progress indicator with tool name on `toolStart`/`toolProgress`, append text on `partial` events, and render the final enriched response on `complete`.
- **FR-029c**: VS Code analysis panel MUST show a progress bar during streaming scans, updating tool-by-tool progress using `toolStart`, `toolProgress`, and `toolComplete` events.
- **FR-029d**: M365 extension MUST show a typing indicator on `agentRouted`/`thinking` and update the card with intermediate status on `toolStart`/`toolComplete` events. On `complete`, the final Adaptive Card replaces any intermediate status card.
- **FR-029e**: Both extensions MUST handle SSE connection errors (timeout, disconnection) gracefully: retry once with exponential backoff, then fall back to the synchronous `/mcp/chat` endpoint and display a "Streaming unavailable, using synchronous mode" notice.

#### IaC File Analysis

- **FR-029f**: A dedicated MCP tool MUST be exposed for IaC file scanning (Bicep, Terraform, ARM templates) that accepts a file path or content and returns structured JSON findings (not Markdown). The response MUST conform to the `ComplianceFinding` entity schema with all enriched fields (`controlId`, `controlFamily`, `severity`, `resourceId`, `resourceType`, `autoRemediable`, `remediationScript`, `riskLevel`).

#### Observability

- **FR-027**: TypeScript extensions (VS Code and M365) MUST log all MCP server API calls at info level including: request URL, conversation ID, message length, response time, and intent type returned.
- **FR-028**: TypeScript extensions MUST log all errors at error level including: error code (from server error response or HTTP status), conversation ID, and the suggestion text for troubleshooting.
- **FR-029**: TypeScript extensions MUST log compliance-relevant actions at info level including: agent used, tools executed, intent type, and timestamp, to support audit trail requirements.

### Key Entities

- **McpChatResponse (enriched)**: The server-to-client response payload — includes `success`, `response` (text), `conversationId`, `agentUsed` (renamed from `agentName`), `intentType` (derived from selected agent), `processingTimeMs`, `toolsExecuted` (list of tool executions), `errors` (list of structured error objects), `suggestions` (list of follow-up prompts), `requiresFollowUp`, `data` (intent-specific structured payload), `followUpPrompt`, `missingFields`.
- **ToolExecution**: A record of a single tool invocation — `toolName`, `success`, `executionTimeMs`.
- **ErrorDetail**: A structured error entry — `errorCode` (machine-readable), `message` (human-readable), `suggestion` (corrective guidance).
- **ComplianceData**: Structured payload for compliance intent — `complianceScore`, `passedControls`, `warningControls`, `failedControls`, `findings` (count), `framework`, `assessmentScope`.
- **KnowledgeBaseData**: Structured payload for knowledge base intent — `answer`, `sources` (list of reference citations), `controlId`, `controlFamily`.
- **ConfigurationData**: Structured payload for configuration intent — `subscriptionId`, `framework`, `baseline`, `cloudEnvironment`, `dryRunDefault`, `defaultScanType`, `region`.
- **ComplianceFinding (enriched)**: An individual compliance finding — `controlId`, `controlFamily`, `title`, `severity` (Critical/High/Medium/Low/Informational), `description`, `recommendation`, `resourceId`, `resourceType`, `frameworkReference`, `autoRemediable`, `remediationScript`, `riskLevel`, `findingStatus` (Open/Acknowledged/Remediated/Verified).

## Assumptions

- The MCP server already exposes `/mcp/chat`, `/mcp/chat/stream`, `/health`, and `/mcp/tools` endpoints. This feature modifies the response mapping in `ProcessChatRequestAsync` but does not add new endpoints.
- Agent responses are already generated with meaningful text content. This feature enriches the envelope around that content. The `AgentResponse` model is extended with optional properties (`Suggestions`, `RequiresFollowUp`, `FollowUpPrompt`, `MissingFields`) so agents can explicitly signal follow-up needs. Existing agents are not required to populate these fields — they default to empty/false.
- The `AgentResponse.ToolsExecuted` field is already populated by agents that execute tools (ComplianceAgent via `RouteToToolAsync`, ConfigurationAgent via `ConfigurationTool`). This feature copies that data to the MCP response instead of discarding it.
- Intent classification is deterministic: it maps from the concrete agent type selected by the orchestrator. No AI-based intent classification is needed.
- The structured `data` object is populated from `AgentResponse.ResponseData` when agents explicitly provide it. No text parsing or extraction is performed. If `ResponseData` is null/empty, the `data` field in the response is null and clients degrade gracefully to text-only rendering.
- Existing M365 card tests for removed cards (cost, deployment, infrastructure, resource) will be deleted along with the card builders. New tests will cover the added card builders (knowledgebase, configuration) and updated routing.
- The VS Code finding interface expansion (adding `controlFamily`, `resourceId`, etc.) depends on the MCP server or analysis command providing those fields in the response. Fields not present in the response are rendered as absent (not shown) — no placeholder text.
- The 013-copilot-everywhere feature is fully implemented and all 2462 .NET tests and 93 TypeScript tests pass. This feature builds on top of that baseline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of MCP server chat responses include a non-empty `intentType` field correctly classifying the responding agent.
- **SC-002**: 100% of MCP server chat responses include `agentUsed` (not `agentName`) in the JSON output, and VS Code and M365 extensions successfully display agent attribution for every response.
- **SC-003**: Tool execution details are present in the response whenever the agent executed one or more tools, with zero data loss between `AgentResponse.ToolsExecuted` and `McpChatResponse.toolsExecuted`.
- **SC-004**: M365 compliance card renders with color-coded score, control counts, and action buttons for 100% of compliance-intent responses, replacing the current generic text card.
- **SC-005**: M365 codebase contains zero references to cost, deployment, infrastructure, or resource card builders after cleanup.
- **SC-006**: VS Code analysis panel displays 5 severity levels (Critical, High, Medium, Low, Informational) with correct color coding for all findings.
- **SC-007**: VS Code analysis panel displays framework reference ("NIST 800-53 Rev 5") and assessment scope in the panel header for every analysis run.
- **SC-008**: All error responses from the MCP server include a structured `errorCode` and `suggestion` field, meeting Constitution VII requirements.
- **SC-009**: All existing tests continue to pass (zero regressions) and new tests cover all enriched response paths with 80%+ coverage on modified files.
- **SC-010**: M365 extension provides visible progress feedback (typing indicator) within 1 second of sending a request, before the MCP server response arrives.

## Clarifications

### Session 2026-02-26

- Q: How should the MCP server obtain suggestions, follow-up prompts, and missing fields given that the current AgentResponse model has no fields for this data? → A: Extend AgentResponse with optional properties (`Suggestions`, `RequiresFollowUp`, `FollowUpPrompt`, `MissingFields`). Agents explicitly populate these. No heuristic text parsing.
- Q: How should the MCP server obtain structured data (compliance scores, KB answers, config settings) for the `data` response field? → A: Extend AgentResponse with an optional `ResponseData` dictionary. Agents populate structured fields explicitly. MCP server passes through without transformation. No response text parsing.
- Q: Renaming the JSON field from `agentName` to `agentUsed` could break existing .NET consumers. How should backward compatibility be handled? → A: JSON attribute only — add `[JsonPropertyName("agentUsed")]` to the existing C# `AgentName` property. C# code still uses `.AgentName`; only JSON serialization output changes. No breaking change for .NET consumers.
- Q: McpChatResponse.Errors is currently `List<string>`. Changing to structured objects is a breaking change. How should the error model evolve? → A: Replace `List<string>` with `List<ErrorDetail>` (ErrorCode, Message, Suggestion). The field is only set in one catch block in McpServer today — clean break with minimal migration.
- Q: Should intent type mapping live in the server or should agents declare their own intent type? → A: Server-side mapping via AgentId. Server maps `targetAgent.AgentId` to intent string (e.g., `"compliance-agent"` → `"compliance"`). No agent changes needed — agents don't need to know about presentation-layer concepts.
- Q: User's expanded requirements list 9 M365 card types including alert lifecycle and kanban board. Should both be in scope? → A: Alert lifecycle card is compliance-scoped (include). Kanban board card is included in this feature for UI rendering (FR-010a); remediation workflow logic remains in feature 002-remediation-kanban.
- Q: How should drill-down navigation and action buttons (Remediate, Collect Evidence, Acknowledge Alert) communicate with the server? → A: Reuse `/mcp/chat` with a new optional `action` field and `actionContext` dictionary in the request body. Server routes action requests to the appropriate agent tool based on action type. No new endpoints needed.
- Q: What granularity of SSE streaming events should both extensions expect from `/mcp/chat/stream`? → A: Full granularity — `agentRouted`, `thinking`, `toolStart`, `toolProgress` (percentage), `toolComplete`, `partial`, `validating`, `complete`. Maximum detail for rich progress UIs.
- Q: How should remediation script execution be secured, given that agent-generated scripts modify real infrastructure? → A: Preview + explicit confirmation. Scripts are shown as read-only diff previews first. Applying requires explicit user confirmation (VS Code: diff preview + "Confirm & Apply"; M365: confirmation card). All infrastructure-modifying actions require both CAC (Common Access Card) authentication AND active PIM (Privileged Identity Management) role elevation. CAC-derived identity is validated by compliance authorization middleware. PIM role must be active for the target scope. All executions are audit-logged with user identity, script hash, finding ID, PIM role, and result.
- Q: Should contextual follow-up prompts ("Generate remediation plan", "Export SSP", "View trend") be hardcoded per intent type or dynamically generated by agents? → A: Dynamic from agents. The `Suggestions` list on `AgentResponse` is populated by each agent based on actual results (e.g., failing assessment suggests remediation; passing assessment suggests export). Avoids stale/irrelevant suggestions.
