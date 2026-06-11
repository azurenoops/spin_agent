# MCP Tool Contracts: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Date**: 2026-06-11  
**Pattern**: All tools follow the existing `BaseTool` envelope with `{ result, isError }` wrapping  
**RBAC**: Enforced at tool level; tool returns `isError: true` with code `FORBIDDEN` if role insufficient  
**File**: `src/Ato.Copilot.Agents/Compliance/Tools/EmassWorkflowTools.cs`

---

## emass_get_workflow_status

Returns the ISSO's eMASS export workflow status for a system as a formatted markdown response.

### Metadata

| Field | Value |
|-------|-------|
| Tool name | `emass_get_workflow_status` |
| Source file | `EmassWorkflowTools.cs` |
| Roles | ISSO, ISSM, AO |
| Calls | `IEmassWorkflowStatusService.GetStatusAsync` |
| Surface | Dashboard Chat, Teams, VS Code |

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | `string` | ✓ | The GUID or slug of the registered system |

### Example Invocation (natural language → tool call)

```
User: "What is my eMASS export status for System X?"
→ Tool: emass_get_workflow_status({ "system_id": "abc123" })
```

### Success Response

```json
{
  "result": "## eMASS Workflow Status — My System\n\n| Category | Exported | Pending |\n|----------|----------|---------|\n| Controls | 325 | 12 |\n| POA&M Items | 48 | 5 |\n| Artifacts | 6 | 0 |\n\n**Last Exported**: May 15, 2026  \n**Last Synced**: June 1, 2026  \n**Unresolved Conflicts**: 3 ⚠️  \n**Readiness**: ❌ Not Ready (1 blocking gap)\n\n> 3 sync conflicts are awaiting your review. Navigate to the [eMASS Status page](/systems/abc123/emass/status) to resolve them.",
  "isError": false
}
```

### Error Response — System Not Found

```json
{
  "result": "System 'abc123' was not found or you do not have access to it.",
  "isError": true
}
```

### Formatting Rules

- Status table: markdown table with Category / Exported / Pending columns
- `HasConflicts`: show ⚠️ and conflict count with link to status page
- `NeverExported`: show "Never exported" with a call-to-action to run readiness check
- `UpToDate`: show ✅ confirmation
- `PendingExport`: show pending counts and suggest running an export

---

## emass_check_export_readiness

Checks whether a system has all required fields populated for a successful eMASS export/import. Returns a formatted readiness card.

### Metadata

| Field | Value |
|-------|-------|
| Tool name | `emass_check_export_readiness` |
| Source file | `EmassWorkflowTools.cs` |
| Roles | ISSO, ISSM, AO |
| Calls | `IEmassExportReadinessService.CheckReadinessAsync` |
| Surface | Dashboard Chat, Teams, VS Code |

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | `string` | ✓ | The GUID or slug of the registered system |

### Example Invocations

```
User: "Is System X ready for eMASS export?"
→ Tool: emass_check_export_readiness({ "system_id": "abc123" })

User: "Check eMASS readiness for my system"
→ Tool: emass_check_export_readiness({ "system_id": "<system from context>" })
```

### Success Response — Not Ready

```json
{
  "result": "## eMASS Export Readiness — My System\n\n❌ **Not Ready** — 1 blocking gap must be resolved before export.\n\n### Blocking Gaps\n\n| Field | Issue | Fix |\n|-------|-------|-----|\n| **DitprId** | DITPR System ID is required for eMASS import matching. | [Set DitprId](/systems/abc123/settings#identifiers) |\n\n### Advisory Gaps (export may proceed with acknowledgment)\n\n| Field | Issue |\n|-------|-------|\n| Ssp.ApprovedSections | At least one SSP section should be Approved before export. |\n| PoamItems.ScheduledDates | 3 POA&M items are missing scheduled completion dates. |",
  "isError": false
}
```

### Success Response — Ready

```json
{
  "result": "## eMASS Export Readiness — My System\n\n✅ **Ready for eMASS export.** All required fields are populated.\n\nYou can now generate an authorization package or use the eMASS export tools.",
  "isError": false
}
```

### Formatting Rules

- Blocking gaps: shown in bold in the table, section header is `### Blocking Gaps`
- Advisory gaps: separate section `### Advisory Gaps (export may proceed with acknowledgment)`
- If zero gaps of either type, omit that section
- Include fix URLs as markdown links when available
- Suggest next step: "Run `emass_get_workflow_status` to check your overall export state."

---

## Existing Tools — No Changes Required

The following existing tools in `EmassExportTools.cs` are **not modified** by Feature 071:

| Tool | Status | Notes |
|------|--------|-------|
| `emass_export_controls` | Unchanged | Excel export; Feature 071 adds DitprId/EmassId to output (T009) |
| `emass_export_poam` | Unchanged | Excel POA&M export |
| `emass_export_oscal_poam` | **OSCAL version upgraded** (T001) | Now emits 1.1.2 — same tool name, same parameters |
| `emass_export_oscal_ar` | **OSCAL version upgraded** (T002) | Now emits 1.1.2 — same tool name, same parameters |

The OSCAL version upgrade (Phase 1) is transparent to tool callers — same parameters, same tool names, upgraded output format.

---

## Tool Registration

Add both new tools to the MCP tool registry. Pattern from `EmassExportTools.cs`:

```csharp
public class EmassWorkflowTools : BaseTool
{
    [McpTool("emass_get_workflow_status")]
    [McpDescription("Returns the ISSO's eMASS export workflow status including export counts, sync conflicts, and readiness summary.")]
    [McpRole(Roles.Isso, Roles.Issm, Roles.Ao)]
    public async Task<ToolResult> GetWorkflowStatusAsync(
        [McpParameter("system_id", "The system GUID or slug.")] string systemId,
        CancellationToken ct = default)
    { ... }

    [McpTool("emass_check_export_readiness")]
    [McpDescription("Checks whether a system has all required fields for a successful eMASS export. Returns blocking gaps and advisory gaps.")]
    [McpRole(Roles.Isso, Roles.Issm, Roles.Ao)]
    public async Task<ToolResult> CheckExportReadinessAsync(
        [McpParameter("system_id", "The system GUID or slug.")] string systemId,
        CancellationToken ct = default)
    { ... }
}
```

---

## Agent Tool Catalog Update

After implementing, add entries to `docs/architecture/agent-tool-catalog.md`:

```markdown
### emass_get_workflow_status
- **Description**: Returns eMASS export workflow status for a system.
- **Parameters**: `system_id` (string, required)
- **Roles**: ISSO, ISSM, AO
- **Example**: "What is my eMASS status for System X?"

### emass_check_export_readiness
- **Description**: Validates all required eMASS fields before export.
- **Parameters**: `system_id` (string, required)
- **Roles**: ISSO, ISSM, AO
- **Example**: "Is System X ready for eMASS export?"
```
