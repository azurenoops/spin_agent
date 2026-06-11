# MCP Tools Contract — 074: Policy + Technical Narrative Split

All tools follow the existing ATO Copilot `BaseTool` response envelope:

```json
{
  "status": "success" | "error",
  "data": <T>,
  "metadata": { "executionTimeMs": number, "timestamp": "ISO-8601", "tool": "<tool-name>" },
  "error": { "errorCode": string, "message": string, "suggestion": string } | null
}
```

Three new tools are added in this feature. All are registered in
`Ato.Copilot.Agents.Compliance` alongside existing narrative tools.

---

## Tool 1 — `narrative_set_policy`

**File:** `src/Ato.Copilot.Agents/Compliance/Tools/NarrativePolicyTool.cs`
**Class:** `NarrativePolicyTool`

### Description

Author or update the **Policy/Procedural** narrative half for a NIST control. Captures
org-level governance: who owns the control, what the written policy says, review frequency,
role assignments, and policy document citations. The Technical half is left unchanged.

ISSO/ISSM roles only. PlatformEngineer and SCA callers will receive an error.

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | ✅ | System GUID, name, or acronym |
| `control_id` | string | ✅ | NIST 800-53 control ID (e.g., `AC-2`, `IA-5(1)`) |
| `policy_narrative` | string | ✅ | Full text of the Policy narrative (max 8 000 chars) |

### Success Response

```json
{
  "status": "success",
  "data": {
    "systemId": "aaaaaaaa-0000-0000-0000-000000000001",
    "controlId": "AC-2",
    "policyNarrative": "The Account Management Policy (AMP-001)..."
  },
  "metadata": { "executionTimeMs": 45, "timestamp": "2026-06-11T00:00:00Z", "tool": "narrative_set_policy" }
}
```

### Error Responses

| `errorCode` | Condition |
|-------------|-----------|
| `INVALID_INPUT` | `system_id`, `control_id`, or `policy_narrative` missing or blank |
| `NOT_FOUND` | System or control not found in caller's tenant |
| `FORBIDDEN` | Caller role is not permitted to write Policy narratives |
| `VALIDATION_ERROR` | `policy_narrative` exceeds 8 000 characters |

---

## Tool 2 — `narrative_set_technical`

**File:** `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeTechnicalTool.cs`
**Class:** `NarrativeTechnicalTool`

### Description

Author or update the **Technical/Implementation** narrative half for a NIST control.
Captures system-level configuration: cloud config, automated controls, scan output,
Conditional Access rules, RBAC assignments, CI/CD gates. The Policy half is left unchanged.

Available to ISSO, ISSM, SCA, and PlatformEngineer roles.

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | ✅ | System GUID, name, or acronym |
| `control_id` | string | ✅ | NIST 800-53 control ID |
| `technical_narrative` | string | ✅ | Full text of the Technical narrative (max 8 000 chars) |

### Success Response

```json
{
  "status": "success",
  "data": {
    "systemId": "aaaaaaaa-0000-0000-0000-000000000001",
    "controlId": "AC-2",
    "technicalNarrative": "Azure AD Conditional Access policy 'Require MFA'..."
  },
  "metadata": { "executionTimeMs": 38, "timestamp": "2026-06-11T00:00:00Z", "tool": "narrative_set_technical" }
}
```

### Error Responses

| `errorCode` | Condition |
|-------------|-----------|
| `INVALID_INPUT` | `system_id`, `control_id`, or `technical_narrative` missing or blank |
| `NOT_FOUND` | System or control not found in caller's tenant |
| `VALIDATION_ERROR` | `technical_narrative` exceeds 8 000 characters |

---

## Tool 3 — `evidence_classify`

**File:** `src/Ato.Copilot.Agents/Compliance/Tools/EvidenceClassifyTool.cs`
**Class:** `EvidenceClassifyTool`

### Description

Manually classify an existing `EvidenceArtifact` as supporting the Policy half, Technical
half, both (Combined), or mark as Unclassified. Overwrites any prior auto-tag. Records the
caller as `ManuallyTaggedBy` and clears the classifier's `AutoTagRationale`.

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `evidence_artifact_id` | string | ✅ | Evidence artifact GUID |
| `narrative_type` | string | ✅ | One of: `"Policy"`, `"Technical"`, `"Combined"`, `"Unclassified"` |
| `rationale` | string | ❌ | Optional human-readable explanation stored in `AutoTagRationale` |

### Parameter Notes

- `narrative_type` is case-insensitive. `"policy"`, `"POLICY"`, and `"Policy"` all map to `EvidenceNarrativeType.Policy`.
- Sending `"Unclassified"` effectively un-classifies the artifact and removes the prior
  manual tag (sets `ManuallyTaggedBy = null`).

### Success Response

```json
{
  "status": "success",
  "data": {
    "evidenceArtifactId": "cccccccc-0000-0000-0000-000000000001",
    "fileName": "Account_Management_Policy_v3.0.pdf",
    "narrativeType": "Policy",
    "autoTagRationale": "Manual: confirmed this is the signed policy document.",
    "manuallyTaggedBy": "john.doe@contoso.com",
    "classifiedAt": "2026-06-11T00:00:00Z"
  },
  "metadata": { "executionTimeMs": 22, "timestamp": "2026-06-11T00:00:00Z", "tool": "evidence_classify" }
}
```

### Error Responses

| `errorCode` | Condition |
|-------------|-----------|
| `INVALID_INPUT` | `evidence_artifact_id` or `narrative_type` missing; `narrative_type` not a recognized value |
| `NOT_FOUND` | Artifact does not exist in caller's tenant |

---

## Tool Registration

Register all three tools in the agent DI setup (find existing narrative tool registrations
by searching for `NarrativeHistoryTool` in the MCP startup/DI file):

```csharp
// Add alongside NarrativeHistoryTool, NarrativeGovernanceTool, etc.
services.AddScoped<NarrativePolicyTool>();
services.AddScoped<NarrativeTechnicalTool>();
services.AddScoped<EvidenceClassifyTool>();
```

And add to the tool list exposed by the agent:

```csharp
// In the tool manifest builder / agent tool factory
tools.Add(provider.GetRequiredService<NarrativePolicyTool>());
tools.Add(provider.GetRequiredService<NarrativeTechnicalTool>());
tools.Add(provider.GetRequiredService<EvidenceClassifyTool>());
```
