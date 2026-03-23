# Agent Tool Catalog — RMF Compliance Tools

> Feature 015: Persona-Driven RMF Workflows

This catalog documents the MCP tools introduced for RMF system registration, authorization boundary management, role assignment, RMF lifecycle step advancement, FIPS 199 security categorization, NIST 800-53 control baseline selection, SSP authoring & narrative management, assessment artifacts with CAT severity mapping, and authorization decisions with risk acceptance and POA&M management.

---

## Tools Overview

| Tool Name | MCP Method | Description |
|-----------|-----------|-------------|
| `compliance_register_system` | `RegisterSystemAsync` | Register a new system for RMF compliance tracking |
| `compliance_list_systems` | `ListSystemsAsync` | List registered systems with pagination and filtering |
| `compliance_get_system` | `GetSystemAsync` | Retrieve full system details including boundary, roles, categorization |
| `compliance_advance_rmf_step` | `AdvanceRmfStepAsync` | Advance (or regress) system through RMF lifecycle phases |
| `compliance_define_boundary` | `DefineBoundaryAsync` | Define or update authorization boundary with Azure resources |
| `compliance_exclude_from_boundary` | `ExcludeFromBoundaryAsync` | Exclude a resource from the authorization boundary |
| `compliance_assign_rmf_role` | `AssignRmfRoleAsync` | Assign an RMF role to a user for a system |
| `compliance_list_rmf_roles` | `ListRmfRolesAsync` | List all active RMF role assignments for a system |
| `compliance_categorize_system` | `CategorizeSystemAsync` | Perform FIPS 199 / SP 800-60 security categorization |
| `compliance_get_categorization` | `GetCategorizationAsync` | Retrieve security categorization for a system |
| `compliance_suggest_info_types` | `SuggestInfoTypesAsync` | Suggest SP 800-60 information types for a system |
| `compliance_select_baseline` | `SelectBaselineAsync` | Select NIST 800-53 control baseline with optional CNSSI 1253 overlay |
| `compliance_tailor_baseline` | `TailorBaselineAsync` | Add or remove controls from the selected baseline |
| `compliance_set_inheritance` | `SetInheritanceAsync` | Set inheritance type (inherited/shared/customer) for controls |
| `compliance_get_baseline` | `GetBaselineAsync` | Retrieve baseline details with optional tailoring/inheritance |
| `compliance_generate_crm` | `GenerateCrmAsync` | Generate Customer Responsibility Matrix grouped by family |
| `compliance_write_narrative` | `WriteNarrativeAsync` | Write or update a control implementation narrative |
| `compliance_suggest_narrative` | `SuggestNarrativeAsync` | Generate AI-suggested draft narrative with confidence score |
| `compliance_batch_populate_narratives` | `BatchPopulateNarrativesAsync` | Auto-populate inherited/shared control narratives |
| `compliance_narrative_progress` | `NarrativeProgressAsync` | Track SSP narrative completion status per-family |
| `compliance_generate_ssp` | `GenerateSspAsync` | Generate the System Security Plan document |
| `compliance_assess_control` | `AssessControlAsync` | Record per-control SCA effectiveness determination with CAT severity |
| `compliance_take_snapshot` | `TakeSnapshotAsync` | Create immutable SHA-256-hashed assessment snapshot |
| `compliance_compare_snapshots` | `CompareSnapshotsAsync` | Compare two snapshots side-by-side with score delta |
| `compliance_verify_evidence` | `VerifyEvidenceAsync` | Recompute hash and verify evidence integrity |
| `compliance_check_evidence_completeness` | `CheckEvidenceCompletenessAsync` | Report controls with/without verified evidence |
| `compliance_generate_sar` | `GenerateSarAsync` | Generate Security Assessment Report with CAT breakdown |
| `compliance_issue_authorization` | `IssueAuthorizationAsync` | Issue ATO/ATOwC/IATT/DATO authorization decision |
| `compliance_accept_risk` | `AcceptRiskAsync` | Accept risk on a specific finding and control |
| `compliance_show_risk_register` | `ShowRiskRegisterAsync` | View risk register with active/expired/revoked acceptances |
| `compliance_create_poam` | `CreatePoamAsync` | Create POA&M item with milestones |
| `compliance_list_poam` | `ListPoamAsync` | List POA&M items with filtering |
| `compliance_generate_rar` | `GenerateRarAsync` | Generate Risk Assessment Report |
| `compliance_bundle_authorization_package` | `BundleAuthorizationPackageAsync` | Bundle complete authorization package |
| `compliance_narrative_history` | `GetNarrativeHistoryAsync` | View version history for a control narrative |
| `compliance_narrative_diff` | `GetNarrativeDiffAsync` | Compare two narrative versions with unified diff |
| `compliance_rollback_narrative` | `RollbackNarrativeAsync` | Rollback to a previous narrative version |
| `compliance_submit_narrative` | `SubmitNarrativeAsync` | Submit a Draft narrative for ISSM review |
| `compliance_review_narrative` | `ReviewNarrativeAsync` | Approve or request revision of a narrative |
| `compliance_batch_review_narratives` | `BatchReviewNarrativesAsync` | Batch review narratives by family or control IDs |
| `compliance_narrative_approval_progress` | `GetNarrativeApprovalProgressAsync` | Aggregate approval status and progress dashboard |
| `compliance_batch_submit_narratives` | `BatchSubmitNarrativesAsync` | Batch submit Draft narratives for review |
| `compliance_generate_roadmap` | `GenerateRoadmapAsync` | Generate phased implementation roadmap from gap analysis |
| `compliance_get_roadmap` | `GetRoadmapAsync` | Get active implementation roadmap for a system |
| `compliance_get_roadmap_progress` | `GetRoadmapProgressAsync` | Get roadmap progress metrics with risk curve |
| `compliance_update_roadmap` | `UpdateRoadmapAsync` | Update roadmap items, merge/split phases |
| `compliance_create_board_from_roadmap` | `CreateBoardFromRoadmapAsync` | Create Kanban board from roadmap |
| `compliance_export_roadmap_pdf` | `ExportRoadmapPdfAsync` | Export roadmap as PDF document |

---

## Tool Reference

### `compliance_register_system`

Registers a new information system for RMF compliance tracking. Sets initial RMF phase to **Prepare**.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `name` | string | Yes | System name (human-readable) |
| `system_type` | enum | Yes | `MajorApplication`, `GeneralSupportSystem`, `Enclave`, `PlatformIt`, `CloudServiceOffering` |
| `mission_criticality` | enum | Yes | `MissionCritical`, `MissionEssential`, `MissionSupport` |
| `hosting_environment` | enum | Yes | `Commercial`, `Government`, `GovernmentAirGappedIl5`, `GovernmentAirGappedIl6` |
| `acronym` | string | No | System acronym |
| `description` | string | No | System description |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "name": "System Name",
    "acronym": "SN",
    "system_type": "MajorApplication",
    "current_rmf_step": "Prepare",
    "mission_criticality": "MissionCritical",
    "created_at": "2025-01-01T00:00:00Z"
  },
  "metadata": { "tool": "compliance_register_system", "timestamp": "..." }
}
```

---

### `compliance_list_systems`

Lists registered systems with optional pagination and active-only filtering.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `page` | int | No | Page number (default: 1) |
| `page_size` | int | No | Items per page (1–100, default: 20) |
| `active_only` | bool | No | Filter to active systems only (default: false) |

**Response:** Paginated list with `systems[]` and `pagination { total_count, page, page_size }`.

---

### `compliance_get_system`

Retrieves full system details including security categorization, control baseline, authorization boundary resources, and RMF role assignments.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID |

**Response:** Full system entity with nested `boundary_resource_count`, `role_assignment_count`, security categorization, and baseline details.

---

### `compliance_advance_rmf_step`

Advances (or regresses) a system through the RMF lifecycle: Prepare → Categorize → Select → Implement → Assess → Authorize → Monitor.

**Gate Conditions (forward movement):**

| Transition | Requirements |
|-----------|-------------|
| Prepare → Categorize | ≥1 active RMF role + ≥1 boundary resource |
| Categorize → Select | Security categorization + ≥1 information type |
| Select → Implement | Control baseline exists |
| Implement → Assess | Advisory only (full checks deferred) |
| Assess → Authorize | Advisory only |
| Authorize → Monitor | Advisory only |

Backward movement requires `force=true`. Force overrides also bypass failed forward gates (logged at Warning level).

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID |
| `target_step` | enum | Yes | `Prepare`, `Categorize`, `Select`, `Implement`, `Assess`, `Authorize`, `Monitor` |
| `force` | bool | No | Override gate failures or allow backward movement (default: false) |

**Response:** `previous_step`, `new_step`, `was_forced`, `gate_results[]` with pass/fail details.

---

### `compliance_define_boundary`

Defines or updates the authorization boundary for a system by adding Azure resource references.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID |
| `resources` | array | Yes | Array of `{ resource_id, resource_type, resource_name, inheritance_provider? }` |

Handles duplicates gracefully — re-includes previously excluded resources. Returns count of `resources_added`.

---

### `compliance_exclude_from_boundary`

Marks a resource as excluded from the authorization boundary with a rationale.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID |
| `resource_id` | string | Yes | Resource identifier to exclude |
| `rationale` | string | Yes | Justification for exclusion |

---

### `compliance_assign_rmf_role`

Assigns an RMF role to a user for a specific system. Idempotent — re-activates previously deactivated assignments.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID |
| `role` | enum | Yes | `AuthorizingOfficial`, `Issm`, `Isso`, `Sca`, `SystemOwner` |
| `user_id` | string | Yes | User identifier (e.g., email) |
| `user_display_name` | string | No | Human-readable user name |

---

### `compliance_list_rmf_roles`

Lists all active RMF role assignments for a system, ordered by role then user name.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID |

**Response:** Array of role assignments with `total_roles` count.

---

## US2: Security Categorization Tools

### `compliance_categorize_system`

Perform or update FIPS 199 / SP 800-60 security categorization for a registered system. Provide information types with C/I/A impact levels. Returns computed high-water mark, DoD Impact Level, and NIST baseline.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `information_types` | array | Yes | Array of info types with `sp800_60_id`, `name`, `confidentiality_impact`, `integrity_impact`, `availability_impact` (Low\|Moderate\|High) |
| `is_national_security_system` | boolean | No | Whether the system is designated NSS (affects IL derivation) |
| `justification` | string | No | Overall categorization rationale |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "system_id": "guid",
    "system_name": "System Name",
    "confidentiality_impact": "Moderate",
    "integrity_impact": "High",
    "availability_impact": "Moderate",
    "overall_categorization": "High",
    "fips_199_notation": "SC System = {(confidentiality, MODERATE), (integrity, HIGH), (availability, MODERATE)}",
    "dod_impact_level": "IL5",
    "nist_baseline": "High",
    "is_national_security_system": false,
    "information_type_count": 2,
    "information_types": [
      {
        "sp800_60_id": "C.3.5.8",
        "name": "Information Security",
        "confidentiality": "Moderate",
        "integrity": "High",
        "availability": "Moderate",
        "uses_provisional": true,
        "adjustment_justification": null
      }
    ]
  }
}
```

**High-Water Mark Computation:**
- The overall categorization is the **maximum** of C/I/A across all information types
- FIPS 199: SC = {(confidentiality, X), (integrity, Y), (availability, Z)}
- DoD IL derivation: Low→IL2, Moderate→IL4, High→IL5, NSS+classified→IL6

---

### `compliance_get_categorization`

Retrieve the FIPS 199 security categorization for a registered system, including all information types and computed fields.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |

**Response:** Same structure as `compliance_categorize_system`, or `data: null` with message if no categorization exists.

---

### `compliance_suggest_info_types`

Suggest SP 800-60 information types based on system description, type, and mission criticality. Returns a ranked list with confidence scores using heuristic keyword matching against a 16-entry SP 800-60 Vol. 2 catalog.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `description` | string | No | Additional context for better suggestions |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "system_name": "System Name",
    "suggestion_count": 5,
    "suggestions": [
      {
        "sp800_60_id": "C.3.5.8",
        "name": "Information Security",
        "category": "Management and Support",
        "confidence": 0.85,
        "rationale": "System description matches 'security'",
        "default_confidentiality_impact": "Moderate",
        "default_integrity_impact": "Moderate",
        "default_availability_impact": "Low"
      }
    ]
  }
}
```

---

### `compliance_select_baseline`

Selects the NIST SP 800-53 control baseline for a system based on its FIPS 199 categorization. Optionally applies the CNSSI 1253 overlay matching the DoD Impact Level.

**Prerequisite:** System must have a security categorization (run `compliance_categorize_system` first).

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `apply_overlay` | bool | No | Whether to apply the CNSSI 1253 overlay (default: true) |
| `overlay_name` | string | No | Override overlay name (e.g., "CNSSI 1253 IL5") |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "system_id": "guid",
    "baseline_level": "Moderate",
    "overlay_applied": "CNSSI 1253 IL4",
    "total_controls": 335,
    "customer_controls": 0,
    "inherited_controls": 0,
    "shared_controls": 0,
    "tailored_out_controls": 0,
    "tailored_in_controls": 0,
    "control_ids": ["AC-1", "AC-2", "..."],
    "created_by": "mcp-user",
    "created_at": "2025-01-01T00:00:00Z"
  }
}
```

**Baseline Levels:** Low (152 controls), Moderate (329 controls), High (400 controls). Overlay adds CNSSI 1253 enhancement controls.

---

### `compliance_tailor_baseline`

Add or remove controls from the selected baseline. Supports adding organization-specific controls and removing non-applicable controls with rationale.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `tailoring_actions` | array | Yes | Array of `{ control_id, action, rationale }` |

Each tailoring action:
- `control_id` — NIST control ID (e.g., "AC-99" or "ZZ-1")
- `action` — `"Added"` or `"Removed"`
- `rationale` — Justification for the tailoring decision

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "accepted_count": 3,
    "rejected_count": 0,
    "tailored_in": 2,
    "tailored_out": 1,
    "total_controls": 336,
    "accepted": [
      { "control_id": "ZZ-99", "action": "Added", "accepted": true },
      { "control_id": "AC-1", "action": "Removed", "accepted": true, "reason": "WARNING: Control is required by overlay..." }
    ]
  }
}
```

---

### `compliance_set_inheritance`

Set the inheritance type for controls in the baseline. Maps each control to an inheritance provider (e.g., FedRAMP-authorized cloud service).

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `inheritance_mappings` | array | Yes | Array of `{ control_id, inheritance_type, provider, customer_responsibility }` |

Each mapping:
- `control_id` — Control ID in the baseline
- `inheritance_type` — `"Inherited"`, `"Shared"`, or `"Customer"`
- `provider` — Provider or CSP name (e.g., "Azure Government (FedRAMP High)")
- `customer_responsibility` — (optional) Customer's responsibility for shared controls

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "controls_updated": 60,
    "inherited_count": 50,
    "shared_count": 10,
    "customer_count": 0,
    "skipped_controls": []
  }
}
```

---

### `compliance_get_baseline`

Retrieve the current control baseline for a system. Optionally includes tailoring and inheritance details, and can filter by control family.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `include_details` | bool | No | Include tailoring and inheritance records (default: false) |
| `family_filter` | string | No | Filter by control family prefix (e.g., "AC") |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "baseline_level": "Moderate",
    "total_controls": 336,
    "control_ids": ["AC-1", "AC-2", "..."],
    "tailorings": [
      { "control_id": "ZZ-99", "action": "Added", "rationale": "..." }
    ],
    "inheritances": [
      { "control_id": "AC-1", "inheritance_type": "Inherited", "provider": "Azure Government" }
    ]
  }
}
```

---

### `compliance_generate_crm`

Generate a Customer Responsibility Matrix (CRM) for the system. Groups controls by NIST 800-53 family and shows inheritance coverage.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_name": "My Application System",
    "baseline_level": "Moderate",
    "total_controls": 336,
    "inherited_controls": 50,
    "shared_controls": 10,
    "customer_controls": 0,
    "undesignated_controls": 276,
    "inheritance_percentage": 17.86,
    "family_groups": [
      {
        "family": "AC",
        "family_name": "Access Control",
        "total": 25,
        "inherited": 10,
        "shared": 2,
        "customer": 0,
        "controls": [
          { "control_id": "AC-1", "inheritance_type": "Inherited", "provider": "Azure Government" }
        ]
      }
    ]
  }
}
```

---

### Org-Level Inheritance Defaults (Feature 044)

Dashboard REST endpoints for managing org-wide inheritance defaults derived from the Security Capabilities Library. These are not MCP tools but REST API endpoints consumed by the Dashboard UI.

#### `GET /api/dashboard/inheritance/org-defaults`

List paginated org-level inheritance defaults.

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `family` | string | No | Filter by control family (e.g., "AC") |
| `inheritanceType` | string | No | Filter by inheritance type |
| `search` | string | No | Search by control ID or provider |
| `page` | int | No | Page number (default: 1) |
| `pageSize` | int | No | Items per page (default: 50) |

**Response:**
```json
{
  "items": [
    {
      "id": "guid",
      "controlId": "AC-2",
      "inheritanceType": 0,
      "provider": "Microsoft Entra ID",
      "sourceCapabilityIds": "guid1,guid2",
      "sourceCapabilityNames": "RBAC, MFA",
      "mappingRole": 0,
      "derivedAt": "2026-03-22T03:44:27Z"
    }
  ],
  "total": 24,
  "page": 1,
  "pageSize": 50
}
```

#### `POST /api/dashboard/inheritance/org-defaults/derive`

Derive org-level defaults from org-wide capability-control mappings and cascade to all system baselines.

**Request:** No body required.

**Response:**
```json
{
  "derivedCount": 24,
  "inheritedCount": 24,
  "sharedCount": 0,
  "removedCount": 0,
  "affectedSystems": 6,
  "derivedAt": "2026-03-22T03:44:24Z"
}
```

**Cascade behavior:**
- Creates `OrgDerived` designations on every system baseline for newly derived defaults
- Updates existing `OrgDerived` designations when inheritance type/provider changes
- Removes stale `OrgDerived` designations for controls no longer in org defaults
- Creates `OrgPropagation` audit entries per affected system

#### `POST /api/dashboard/systems/{systemId}/inheritance/revert-to-org-defaults`

Revert selected controls back to their org-level default designation.

**Request:**
```json
{
  "controlIds": ["AC-2", "AC-3"],
  "revertedBy": "dashboard-user"
}
```

**Response:**
```json
{
  "revertedCount": 2,
  "skipped": [
    { "controlId": "AC-99", "reason": "No org default exists" }
  ]
}
```

---

### Security Capabilities Hub (Feature 045)

Dashboard REST endpoints for the unified Capabilities Hub. CSP profile and CRM import have moved from the inheritance page to these capability-scoped endpoints.

#### `GET /api/dashboard/capabilities/coverage`

Compute coverage dashboard showing provider cards, KPI metrics, and gap controls.

**Response:**
```json
{
  "providers": [
    {
      "provider": "Azure Government",
      "controlledCount": 24,
      "totalControls": 339,
      "capabilities": 8,
      "components": 3
    }
  ],
  "totalCapabilities": 12,
  "totalControls": 339,
  "coveredControls": 42,
  "coveragePercent": 12.4,
  "gapControls": ["AC-4", "AC-5", "AC-6"]
}
```

#### `POST /api/dashboard/capabilities/import/csp-profile`

Import a CSP profile to create components, capabilities, and control mappings. Use `?dryRun=true` for preview.

**Request:**
```json
{
  "profileId": "azure-gov-high",
  "conflictResolution": "Skip"
}
```

**Response (dryRun=true):**
```json
{
  "profileName": "Azure Government — FedRAMP High",
  "newCapabilities": 8,
  "existingCapabilities": 2,
  "newMappings": 42,
  "conflicts": 3,
  "newComponents": 3
}
```

#### `POST /api/dashboard/capabilities/import/crm`

Import CRM spreadsheet (CSV/Excel) as `multipart/form-data`. Use `?dryRun=true` for preview.

**Form Fields:**
- `file` — CSV or Excel file
- `columnMapping` (optional) — JSON column mapping

**Response (dryRun=true):**
```json
{
  "totalRows": 120,
  "newCapabilities": 6,
  "existingCapabilities": 4,
  "newMappings": 35,
  "unmatchedControlIds": ["ZZ-99"],
  "detectedColumns": ["Control ID", "Inheritance", "Provider"],
  "sampleRows": [{"Control ID": "AC-2", "Inheritance": "Inherited"}]
}
```

#### `POST /api/dashboard/components/{componentId}/capabilities`

Bulk link capabilities to a component.

**Request:**
```json
{
  "capabilityIds": ["guid1", "guid2"]
}
```

#### `DELETE /api/dashboard/components/{componentId}/capabilities/{capabilityId}`

Unlink a capability from a component. Idempotent — returns 204 even if not linked.

---

## US5: SSP Authoring & Narrative Management Tools

### `compliance_write_narrative`

Write or update the implementation narrative for a NIST 800-53 control in a system's SSP. Creates a new narrative or updates an existing one (upsert behavior).

**RBAC:** Compliance.PlatformEngineer, Compliance.SecurityLead

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `control_id` | string | Yes | NIST 800-53 control ID (e.g., "AC-1") |
| `narrative` | string | Yes | Implementation narrative text |
| `status` | enum | No | `Implemented`, `PartiallyImplemented`, `Planned`, `NotApplicable` (default: `Implemented`) |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "system_id": "guid",
    "control_id": "AC-1",
    "implementation_status": "Implemented",
    "narrative": "Access control policies are enforced...",
    "is_auto_populated": false,
    "ai_suggested": false,
    "authored_by": "mcp-user",
    "authored_at": "2025-01-01T00:00:00Z",
    "modified_at": null
  },
  "metadata": { "tool": "compliance_write_narrative", "timestamp": "..." }
}
```

**Upsert Behavior:** If a narrative already exists for the (system_id, control_id) pair, the narrative and status are updated and `modified_at` is set.

---

### `compliance_suggest_narrative`

Generate an AI-suggested implementation narrative for a NIST 800-53 control based on system context, control requirements, and inheritance data. Returns a draft narrative with confidence score and reference sources.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `control_id` | string | Yes | NIST 800-53 control ID (e.g., "AC-2") |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "control_id": "AC-2",
    "suggested_narrative": "This control is fully inherited from Azure Government...",
    "confidence": 0.85,
    "references": [
      "NIST SP 800-53 Rev. 5",
      "FedRAMP High Baseline",
      "Azure Government FedRAMP High P-ATO"
    ]
  },
  "metadata": { "tool": "compliance_suggest_narrative", "timestamp": "..." }
}
```

**Confidence Levels:**
- **0.85** — Inherited controls (high confidence, mostly provider-documented)
- **0.75** — Shared controls (moderate-high, partial provider coverage)
- **0.55** — Customer controls (lower confidence, requires review)

---

### `compliance_batch_populate_narratives`

Auto-populate implementation narratives for inherited and/or shared controls using provider templates. Skips controls that already have narratives (idempotent). Significantly speeds up SSP authoring by pre-filling inherited control documentation.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `inheritance_type` | enum | No | Filter: `Inherited`, `Shared`, or omit for both |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "populated_count": 45,
    "skipped_count": 5,
    "populated_control_ids": ["AC-1", "AC-2", "..."],
    "skipped_control_ids": ["AC-3", "AC-4", "..."]
  },
  "metadata": { "tool": "compliance_batch_populate_narratives", "timestamp": "..." }
}
```

**Progress Reporting:** When called programmatically with `IProgress<string>`, reports progress every 10 controls processed.

---

### `compliance_narrative_progress`

Get SSP narrative completion status for a system. Shows per-family progress (total, completed, draft, missing controls) and overall completion percentage. Useful for tracking SSP readiness before assessment.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID (GUID) |
| `family_filter` | string | No | Filter by control family prefix (e.g., "AC") |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "total_controls": 335,
    "completed_narratives": 280,
    "draft_narratives": 30,
    "missing_narratives": 25,
    "overall_percentage": 92.54,
    "family_breakdowns": [
      {
        "family": "AC",
        "total": 25,
        "completed": 22,
        "draft": 2,
        "missing": 1
      }
    ]
  },
  "metadata": { "tool": "compliance_narrative_progress", "timestamp": "..." }
}
```

**Status Classification:**
- **Completed**: `Implemented` or `NotApplicable` status
- **Draft**: `PartiallyImplemented` or `Planned` status
- **Missing**: No narrative record exists

---

### `compliance_generate_ssp`

Generate the System Security Plan (SSP) document for a registered system. Produces a 13-section Markdown document following NIST 800-18 structure with YAML front-matter, completeness warnings, and per-control implementation narratives.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `format` | string | No | Output format: `markdown` (default) or `docx` |
| `sections` | string | No | Specific sections to include (comma-separated). Default: all 13 sections. See section key table below. |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "system_name": "Eagle Eye",
    "format": "markdown",
    "total_controls": 325,
    "controls_with_narratives": 310,
    "controls_missing_narratives": 15,
    "sections": ["system_identification", "categorization", "personnel", "system_type", "description", "environment", "interconnections", "laws_regulations", "minimum_controls", "control_implementations", "authorization_boundary", "personnel_security", "contingency_plan"],
    "warnings": [
      "Control AC-3 has no implementation narrative",
      "§12 (Personnel Security) has not been authored",
      "§13 (Contingency Plan) has not been authored"
    ],
    "content": "---\ntitle: System Security Plan\nsystem: Eagle Eye\nbaseline: Moderate\n---\n\n# System Security Plan (SSP)\n\n## §1 System Identification\n...",
    "generated_at": "2026-03-11T10:30:00Z"
  },
  "metadata": { "tool": "compliance_generate_ssp", "duration_ms": 2500, "timestamp": "..." }
}
```

**SSP Document Sections (NIST 800-18):**

| §  | New Section Key | Content | Auto/Authored |
|----|-----------------|---------|---------------|
| §1  | `system_identification` | System name, type, mission criticality, hosting environment, RMF status | Auto |
| §2  | `categorization` | FIPS 199 notation, C/I/A impacts, DoD IL, information types | Auto |
| §3  | `personnel` | RMF role assignments, contact information | Auto |
| §4  | `system_type` | Major/minor application, enclave classification | Auto |
| §5  | `description` | System purpose, functions, user base | Authored |
| §6  | `environment` | Deployment topology, hardware, software inventory | Authored |
| §7  | `interconnections` | System-to-system connections from Feature 021 data | Auto |
| §8  | `laws_regulations` | Applicable laws, Executive Orders, directives | Authored |
| §9  | `minimum_controls` | Baseline level, overlay, total controls, tailoring/inheritance summary | Auto |
| §10 | `control_implementations` | Per-family grouped controls with narratives, status, inheritance type | Auto |
| §11 | `authorization_boundary` | Authorization boundary description and diagram references | Auto |
| §12 | `personnel_security` | Personnel screening, access agreements, separation procedures | Authored |
| §13 | `contingency_plan` | Contingency plan overview, recovery objectives, testing schedule | Authored |

**Backward-Compatible Section Keys:**

| Old Key | Maps To | § |
|---------|---------|---|
| `system_information` | `system_identification` | §1 |
| `baseline` | `minimum_controls` | §9 |
| `controls` | `control_implementations` | §10 |

**Progress Reporting:** When called programmatically with `IProgress<string>`, reports progress per section and per control family.

> **Feature 022 Enhancement**: SSP generation now produces a 13-section NIST 800-18 document with YAML front-matter metadata. Sections §7 (Interconnections) pulls data from Feature 021 interconnection records. Authored sections (§5, §6, §8, §12, §13) are populated from `SspSection` entities written via `compliance_write_ssp_section`.

---

## Assessment Artifact Tools (US7)

### `compliance_assess_control`

Record an SCA's per-control effectiveness determination (Satisfied/OtherThanSatisfied) with DoD CAT severity mapping.

**RBAC:** `Compliance.Auditor` (SCA)

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `assessment_id` | string | Yes | ComplianceAssessment ID |
| `control_id` | string | Yes | NIST 800-53 control ID (e.g., "AC-2") |
| `determination` | enum | Yes | `Satisfied` or `OtherThanSatisfied` |
| `method` | string | No | Assessment method: `Test`, `Interview`, `Examine` |
| `evidence_ids` | string | No | Comma-separated or JSON array of evidence record IDs |
| `notes` | string | No | Assessor notes (max 4000 chars) |
| `cat_severity` | enum | No* | `CatI`, `CatII`, `CatIII` — **required if OtherThanSatisfied** |

**Response (success):**

```json
{
  "status": "success",
  "data": {
    "id": "...",
    "control_id": "AC-3",
    "determination": "OtherThanSatisfied",
    "cat_severity": "CatII",
    "assessment_method": "Test",
    "assessor_id": "mcp-user",
    "assessed_at": "2025-01-15T10:00:00Z"
  }
}
```

---

### `compliance_take_snapshot`

Create an immutable, SHA-256-hashed snapshot of the current assessment state for audit trail.

**RBAC:** `Compliance.Auditor`

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID |
| `assessment_id` | string | Yes | ComplianceAssessment ID |

**Response (success):**

```json
{
  "status": "success",
  "data": {
    "snapshot_id": "...",
    "captured_at": "2025-01-15T10:30:00Z",
    "compliance_score": 85.0,
    "total_controls": 20,
    "passed_controls": 17,
    "failed_controls": 3,
    "integrity_hash": "a1b2c3d4...64-char-hex",
    "is_immutable": true
  }
}
```

**Immutability:** Once created, snapshots cannot be updated or deleted. The integrity hash covers all ControlEffectiveness determinations, ComplianceFinding summaries, and ComplianceEvidence hashes in canonical JSON form.

---

### `compliance_compare_snapshots`

Compare two assessment snapshots side-by-side showing controls changed, score delta, and findings.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `snapshot_id_a` | string | Yes | First snapshot ID |
| `snapshot_id_b` | string | Yes | Second snapshot ID |

**Response (success):**

```json
{
  "status": "success",
  "data": {
    "score_delta": 15.0,
    "newly_satisfied": ["AC-3", "AU-6"],
    "newly_other_than_satisfied": [],
    "unchanged_count": 18,
    "new_findings": 0,
    "resolved_findings": 2,
    "evidence_added": 3,
    "evidence_removed": 0
  }
}
```

---

### `compliance_verify_evidence`

Recompute the SHA-256 hash of evidence content and verify it matches the stored hash. Updates `IntegrityVerifiedAt` on success.

**RBAC:** `Compliance.Auditor`

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `evidence_id` | string | Yes | ComplianceEvidence ID |

**Response (success):**

```json
{
  "status": "success",
  "data": {
    "evidence_id": "ev-1",
    "control_id": "AC-2",
    "original_hash": "a1b2c3...",
    "recomputed_hash": "a1b2c3...",
    "verification_status": "verified",
    "collector_identity": "sca@example.com",
    "collection_method": "automated_scan",
    "integrity_verified_at": "2025-01-15T10:35:00Z"
  }
}
```

**Tamper Detection:** If `verification_status` is `"tampered"`, the original and recomputed hashes will differ — this indicates evidence content was modified after collection.

---

### `compliance_check_evidence_completeness`

Report which controls have verified evidence vs. missing evidence with overall completeness percentage.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID |
| `assessment_id` | string | No | Filter to specific assessment |
| `family_filter` | string | No | Filter by family prefix (e.g., "AC") |

**Response (success):**

```json
{
  "status": "success",
  "data": {
    "completeness_percentage": 75.0,
    "total_controls": 8,
    "controls_with_evidence": 6,
    "controls_without_evidence": 2,
    "controls_with_unverified_evidence": 1,
    "control_statuses": [
      { "control_id": "AC-2", "status": "verified", "evidence_count": 2, "verified_count": 2 },
      { "control_id": "AC-3", "status": "unverified", "evidence_count": 1, "verified_count": 0 },
      { "control_id": "AC-4", "status": "missing", "evidence_count": 0, "verified_count": 0 }
    ]
  }
}
```

---

### `compliance_generate_sar`

Generate a Security Assessment Report (SAR) with executive summary, control-by-control results, risk summary, and CAT severity breakdown.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem ID |
| `assessment_id` | string | Yes | ComplianceAssessment ID |
| `format` | string | No | Output format: `markdown` (default) or `docx` |

**Response (success):**

```json
{
  "status": "success",
  "data": {
    "compliance_score": 87.5,
    "controls_assessed": 20,
    "controls_satisfied": 17,
    "controls_other_than_satisfied": 3,
    "cat_breakdown": { "cat_i": 0, "cat_ii": 2, "cat_iii": 1 },
    "family_results": [
      { "family": "AC", "assessed": 10, "satisfied": 8, "other_than_satisfied": 2 }
    ],
    "content": "# Security Assessment Report\n...",
    "generated_at": "2025-01-15T11:00:00Z"
  }
}
```

**SAR Sections:**

| Section | Content |
|---------|---------|
| Executive Summary | System name, assessor, overall score, finding counts |
| CAT Severity Breakdown | CAT I/II/III finding counts and risk level |
| Control Family Results | Per-family Satisfied/OtherThanSatisfied with CAT mapping |
| Risk Summary | Assessment-to-authorization risk posture |
| Detailed Findings | Per-control determination details |

---

## Phase 10 — Authorization Decisions & Risk Acceptance (US8)

### `compliance_issue_authorization`

Issues an authorization decision (ATO, ATOwC, IATT, DATO) for a registered system. Automatically supersedes any prior active decision and advances the system's RMF step to **Monitor**.

**RBAC:** `Compliance.AuthorizingOfficial` **only**

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `decision_type` | enum | Yes | `ATO`, `AtoWithConditions`, `IATT`, `DATO` |
| `expiration_date` | string | No | ISO-8601 expiration date (required for ATO/ATOwC/IATT) |
| `terms_and_conditions` | string | No | Authorization terms and conditions text |
| `residual_risk_level` | enum | Yes | `Low`, `Medium`, `High`, `Critical` |
| `residual_risk_justification` | string | No | Justification for the residual risk level |
| `risk_acceptances` | string | No | JSON array of inline risk acceptances: `[{finding_id, control_id, cat_severity, justification, compensating_control?, expiration_date}]` |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "system_id": "guid",
    "decision_type": "Ato",
    "decision_date": "2025-01-15T00:00:00Z",
    "expiration_date": "2028-01-15T00:00:00Z",
    "residual_risk_level": "Low",
    "residual_risk_justification": "All CAT I findings remediated",
    "compliance_score": 95.5,
    "terms_and_conditions": "Annual re-assessment required",
    "is_active": true,
    "issued_by": "mcp-user",
    "issued_by_name": "MCP User",
    "risk_acceptances_count": 2
  },
  "metadata": { "tool": "compliance_issue_authorization", "timestamp": "..." }
}
```

**Behavior:**
- Validates the system exists and is registered
- Calculates the compliance score from `ControlEffectiveness` records at decision time
- If a prior active authorization exists, it is deactivated (`IsActive=false`) and linked via `SupersededById`
- Creates `RiskAcceptance` records for any inline risk acceptances
- Advances the system's `CurrentRmfStep` to `Monitor`

---

### `compliance_accept_risk`

Accepts risk on a specific finding and control. Requires an active authorization decision to exist for the system.

**RBAC:** `Compliance.AuthorizingOfficial` **only**

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `finding_id` | string | Yes | ComplianceFinding ID |
| `control_id` | string | Yes | NIST control ID (e.g., `AC-2`) |
| `cat_severity` | enum | Yes | `CatI`, `CatII`, `CatIII` |
| `justification` | string | Yes | Risk acceptance rationale |
| `compensating_control` | string | No | Compensating control description |
| `expiration_date` | string | Yes | ISO-8601 expiration date for auto-expire |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "authorization_decision_id": "guid",
    "finding_id": "guid",
    "control_id": "AC-2",
    "cat_severity": "CatII",
    "justification": "Compensating controls in place",
    "compensating_control": "Network segmentation applied",
    "expiration_date": "2025-12-31T00:00:00Z",
    "accepted_by": "mcp-user",
    "accepted_at": "2025-01-15T12:00:00Z",
    "is_active": true
  },
  "metadata": { "tool": "compliance_accept_risk", "timestamp": "..." }
}
```

---

### `compliance_show_risk_register`

Views the risk register showing all risk acceptances for a system. Automatically expires past-due acceptances on query.

**RBAC:** All compliance roles

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `status_filter` | enum | No | `active`, `expired`, `revoked`, `all` (default: `active`) |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "total_acceptances": 5,
    "active_count": 3,
    "expired_count": 1,
    "revoked_count": 1,
    "acceptances": [
      {
        "id": "guid",
        "control_id": "AC-2",
        "cat_severity": "CatII",
        "justification": "...",
        "compensating_control": "...",
        "expiration_date": "2025-12-31T00:00:00Z",
        "accepted_at": "2025-01-15T12:00:00Z",
        "accepted_by": "ao-user",
        "status": "active",
        "finding_title": "Missing MFA enforcement"
      }
    ]
  },
  "metadata": { "tool": "compliance_show_risk_register", "timestamp": "..." }
}
```

---

### `compliance_create_poam`

Creates a formal Plan of Action & Milestones (POA&M) item with optional milestones. Links the weakness to a NIST control and DoD CAT severity.

**RBAC:** `Compliance.SecurityLead` (ISSM) or `Compliance.Administrator`

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `finding_id` | string | No | ComplianceFinding ID (optional link) |
| `weakness` | string | Yes | Weakness description (max 2000 chars) |
| `control_id` | string | Yes | NIST control ID (e.g., `AC-2`) |
| `cat_severity` | enum | Yes | `CatI`, `CatII`, `CatIII` |
| `poc` | string | Yes | Point of contact |
| `scheduled_completion` | string | Yes | ISO-8601 scheduled completion date |
| `resources_required` | string | No | Resources required description |
| `milestones` | string | No | JSON array: `[{description, target_date}]` |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "system_id": "guid",
    "finding_id": "guid",
    "weakness": "Insufficient access control logging",
    "weakness_source": "Assessment",
    "control_id": "AU-2",
    "cat_severity": "CatII",
    "poc": "John Smith",
    "resources_required": "SIEM integration (40 hours)",
    "scheduled_completion": "2025-06-30T00:00:00Z",
    "status": "Ongoing",
    "created_at": "2025-01-15T00:00:00Z",
    "milestones": [
      {
        "id": "guid",
        "description": "Configure SIEM connectors",
        "target_date": "2025-03-31T00:00:00Z",
        "completed_date": null,
        "sequence": 1,
        "is_overdue": false
      }
    ]
  },
  "metadata": { "tool": "compliance_create_poam", "timestamp": "..." }
}
```

---

### `compliance_list_poam`

Lists POA&M items for a system with optional status, severity, and overdue-only filters.

**RBAC:** All compliance roles

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `status_filter` | enum | No | `Ongoing`, `Completed`, `Delayed`, `RiskAccepted` |
| `severity_filter` | enum | No | `CatI`, `CatII`, `CatIII` |
| `overdue_only` | string | No | `true` to show only overdue items |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "total_items": 4,
    "ongoing_count": 2,
    "completed_count": 1,
    "delayed_count": 1,
    "overdue_count": 1,
    "items": [
      {
        "id": "guid",
        "weakness": "...",
        "control_id": "AC-2",
        "cat_severity": "CatII",
        "poc": "Jane Doe",
        "status": "Ongoing",
        "scheduled_completion": "2025-06-30T00:00:00Z",
        "actual_completion": null,
        "milestone_count": 3,
        "is_overdue": false
      }
    ]
  },
  "metadata": { "tool": "compliance_list_poam", "timestamp": "..." }
}
```

---

### `compliance_generate_rar`

Generates a Risk Assessment Report (RAR) with per-family risk analysis, CAT severity breakdown, and aggregate residual risk level.

**RBAC:** `Compliance.Auditor` or `Compliance.SecurityLead`

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `assessment_id` | string | Yes | ComplianceAssessment ID |
| `format` | string | No | Output format: `markdown` (default) |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "assessment_id": "guid",
    "generated_at": "2025-01-15T12:00:00Z",
    "format": "markdown",
    "executive_summary": "Risk Assessment Report for ...",
    "aggregate_risk_level": "Medium",
    "cat_breakdown": {
      "cat_i": 0,
      "cat_ii": 3,
      "cat_iii": 5,
      "total": 8
    },
    "family_risks": [
      {
        "family": "AC",
        "family_name": "Access Control",
        "total_findings": 4,
        "open_findings": 2,
        "accepted_findings": 1,
        "risk_level": "Medium"
      }
    ],
    "content": "# Risk Assessment Report\n..."
  },
  "metadata": { "tool": "compliance_generate_rar", "timestamp": "..." }
}
```

**RAR Sections:**

| Section | Content |
|---------|---------|
| Executive Summary | System name, assessment date, overall risk determination |
| CAT Severity Breakdown | CAT I/II/III finding counts by family |
| Control Family Analysis | Per-family risk with open/accepted/total findings |
| Risk Trending | Aggregate risk determination with justification |

---

### `compliance_bundle_authorization_package`

Bundles a complete authorization package containing SSP, SAR, RAR, POA&M, CRM, and ATO Letter. Reports document availability status for any missing documents.

**RBAC:** `Compliance.SecurityLead` or `Compliance.Administrator`

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | RegisteredSystem GUID |
| `format` | string | No | Output format: `markdown` (default) |
| `include_evidence` | string | No | `true` to include evidence documents |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "generated_at": "2025-01-15T12:00:00Z",
    "format": "markdown",
    "document_count": 6,
    "includes_evidence": false,
    "documents": [
      {
        "name": "System Security Plan",
        "file_name": "SSP.md",
        "document_type": "SSP",
        "status": "included",
        "content": "# System Security Plan\n..."
      },
      {
        "name": "Security Assessment Report",
        "file_name": "SAR.md",
        "document_type": "SAR",
        "status": "included",
        "content": "..."
      },
      {
        "name": "Risk Assessment Report",
        "file_name": "RAR.md",
        "document_type": "RAR",
        "status": "generated",
        "content": "# Risk Assessment Report\n..."
      },
      {
        "name": "Plan of Action & Milestones",
        "file_name": "POAM.md",
        "document_type": "POAM",
        "status": "generated",
        "content": "| # | Weakness | Control | CAT | POC | Scheduled | Status |\n..."
      },
      {
        "name": "Customer Responsibility Matrix",
        "file_name": "CRM.md",
        "document_type": "CRM",
        "status": "included",
        "content": "..."
      },
      {
        "name": "ATO Letter",
        "file_name": "ATO_Letter.md",
        "document_type": "ATO_Letter",
        "status": "included",
        "content": "..."
      }
    ]
  },
  "metadata": { "tool": "compliance_bundle_authorization_package", "timestamp": "..." }
}
```

**Document Status Values:**

| Status | Meaning |
|--------|---------|
| `included` | Document found in `ComplianceDocuments` table |
| `generated` | Document generated dynamically (RAR, POA&M table) |
| `not_found` | Document not yet created for this system |

---

## Phase 11 — Continuous Monitoring & Lifecycle (US9)

### `compliance_create_conmon_plan`

Create or update the continuous monitoring plan for a registered system (one plan per system — upsert).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `assessment_frequency` | string | yes | `Monthly` \| `Quarterly` \| `Annually` |
| `annual_review_date` | string | yes | ISO 8601 date (e.g., `2026-06-15`) |
| `report_distribution` | string[] | no | User IDs or role names for report distribution |
| `significant_change_triggers` | string[] | no | Custom trigger descriptions |

**Returns:** ConMonPlan record with plan ID, frequency, review date, and distribution list.

```json
{
  "status": "success",
  "data": {
    "plan_id": "...",
    "system_id": "...",
    "assessment_frequency": "Monthly",
    "annual_review_date": "2026-06-15",
    "report_distribution": ["ISSM", "AO"],
    "significant_change_triggers": ["New Interconnection"],
    "created_by": "system",
    "created_at": "2026-01-15T10:00:00Z"
  }
}
```

---

### `compliance_generate_conmon_report`

Generate a periodic continuous monitoring report with compliance score, delta from authorization baseline, findings, and POA&M status.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `report_type` | string | yes | `Monthly` \| `Quarterly` \| `Annual` |
| `period` | string | yes | Report period (e.g., `2026-02`, `2026-Q1`, `2026`) |

**Returns:** ConMonReport with compliance metrics and markdown report content.

```json
{
  "status": "success",
  "data": {
    "report_id": "...",
    "compliance_score": 92.5,
    "authorized_baseline_score": 95.0,
    "score_delta": -2.5,
    "new_findings": 3,
    "resolved_findings": 5,
    "open_poam_items": 2,
    "overdue_poam_items": 0,
    "report_content": "# Continuous Monitoring Report ..."
  }
}
```

---

### `compliance_report_significant_change`

Report a significant change that may trigger reauthorization review. Automatically classifies whether the change type requires reauthorization.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `change_type` | string | yes | Change category (e.g., `New Interconnection`, `Major Upgrade`) |
| `description` | string | yes | Detailed description of the change |

**Reauthorization trigger types:** New Interconnection, Major Upgrade, Data Type Change, Security Architecture Change, Operating Environment Change, New Threat, Security Incident, Boundary Change, Key Personnel Change, Compliance Framework Change.

```json
{
  "status": "success",
  "data": {
    "change_id": "...",
    "change_type": "New Interconnection",
    "requires_reauthorization": true,
    "reauthorization_triggered": false,
    "disposition": null
  }
}
```

---

### `compliance_track_ato_expiration`

Check ATO expiration status with graduated alerts at 90/60/30 days. DATO systems always return `None` alert level.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |

**Alert Levels:**

| Level | Days Remaining | Action |
|-------|---------------|--------|
| `None` | > 90 days or DATO | No action needed |
| `Info` | 60–90 days | Begin reauthorization planning |
| `Warning` | 30–60 days | Submit reauthorization package |
| `Urgent` | < 30 days | Escalate to AO immediately |
| `Expired` | ≤ 0 days | System operating without authorization |

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "system_name": "ACME Portal",
    "has_active_authorization": true,
    "decision_type": "Ato",
    "expiration_date": "2026-12-31",
    "days_until_expiration": 55,
    "alert_level": "Warning",
    "alert_message": "ATO expires in 55 days. Submit reauthorization package.",
    "is_expired": false
  }
}
```

---

### `compliance_multi_system_dashboard`

View all systems with name, impact level, RMF step, authorization status, compliance score, and alerts.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `active_only` | string | no | `true` (default) or `false` to include deactivated systems |

```json
{
  "status": "success",
  "data": {
    "total_systems": 3,
    "authorized_count": 2,
    "expiring_count": 1,
    "expired_count": 0,
    "systems": [
      {
        "system_id": "...",
        "name": "ACME Portal",
        "acronym": "ACP",
        "impact_level": "Moderate",
        "current_rmf_step": "Monitor",
        "authorization_status": "Authorized",
        "decision_type": "Ato",
        "expiration_date": "2026-12-31",
        "days_until_expiration": 340,
        "compliance_score": 95.2,
        "open_findings": 3,
        "open_poam_items": 1,
        "alert_count": 0
      }
    ]
  }
}
```

---

### `compliance_reauthorization_workflow`

Detect reauthorization triggers and optionally initiate the reauthorization workflow by regressing the RMF step to Assess.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `initiate` | string | no | `true` to initiate reauthorization (default: `false` — check only) |

**Trigger sources:** ATO expiration (< 30 days), unreviewed significant changes requiring reauthorization, compliance score drift (> 10% below baseline).

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "is_triggered": true,
    "triggers": ["ATO expiring in 25 days", "2 unreviewed significant changes require reauthorization"],
    "was_initiated": true,
    "previous_rmf_step": "Monitor",
    "new_rmf_step": "Assess",
    "unreviewed_change_count": 2
  }
}
```

---

### `compliance_send_notification`

Send continuous monitoring notifications (expiration alerts, significant change events) to configured recipients.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `notification_type` | string | yes | `expiration` \| `significant_change` \| `conmon_report` |

**Phase 17 Enhancement**: Notifications now route through the AlertManager → AlertNotificationService pipeline.
Expiration alerts are auto-created by `ConMonService.CheckExpirationAsync()` with graduated severity
(Low@90d, Medium@60d, High@30d, Critical@expired). Significant change alerts are auto-created when
`RequiresReauthorization = true`. The tool response includes channel `alert_pipeline` when connected.

```json
{
  "status": "success",
  "data": {
    "notification_type": "expiration",
    "system_id": "...",
    "alert_level": "Warning",
    "alert_message": "ATO expires in 55 days.",
    "delivered": true,
    "channels": ["mcp_response", "alert_pipeline"]
  }
}
```

---

## eMASS & OSCAL Interoperability Tools (US10)

### `compliance_export_emass`

Export system data in eMASS-compatible Excel (.xlsx) format with standard column
headers matching the eMASS controls and POA&M import templates.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `export_type` | string | yes | `controls`, `poam`, or `full` (both) |

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "export_type": "controls",
    "controls_exported": 325,
    "poam_exported": 0,
    "controls_file_size_bytes": 45321,
    "poam_file_size_bytes": 0,
    "controls_base64": "<base64-encoded .xlsx>",
    "poam_base64": null
  },
  "metadata": {
    "format": "xlsx",
    "emass_compatible": true
  }
}
```

---

### `compliance_import_emass`

Import system data from an eMASS-compatible Excel file with configurable
conflict resolution (skip, overwrite, merge) and dry-run preview.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `file_base64` | string | yes | Base64-encoded Excel file content |
| `conflict_strategy` | string | no | `skip` (default), `overwrite`, `merge` |
| `dry_run` | string | no | `true` (default) or `false` |

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "dry_run": true,
    "conflict_strategy": "skip",
    "total_rows": 10,
    "imported": 3,
    "skipped": 7,
    "conflicts": 7,
    "conflict_details": [
      {
        "control_id": "AC-1",
        "field": "ImplementationStatus",
        "existing_value": "Implemented",
        "imported_value": "Planned",
        "resolution": "Skipped"
      }
    ]
  },
  "metadata": {
    "applied": false
  }
}
```

---

### `compliance_export_oscal`

Export system data in OSCAL JSON format (v1.1.2). Supports SSP,
assessment-results, POA&M, and assessment-plan OSCAL models.

> **Feature 041 Enhancement**: Now supports `assessment-plan` model type via `OscalSapExportService`. OSCAL version updated to 1.1.2 for all models.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | yes | RegisteredSystem ID |
| `model` | string | yes | `ssp`, `assessment-results`, `poam`, or `assessment-plan` |

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "model": "ssp",
    "oscal_version": "1.0.6",
    "oscal_document": { "system-security-plan": { ... } }
  },
  "metadata": {
    "format": "json",
    "spec_version": "OSCAL 1.0.6"
  }
}
```

---

## Document Templates & PDF Export Tools (US11)

### `compliance_upload_template`

Upload a custom DOCX template for compliance document generation. Templates
contain `{{MergeField}}` placeholders that are validated against the document
type's merge-field schema (SSP, SAR, POA&M, RAR).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `template_name` | string | Yes | Friendly display name for the template |
| `document_type` | string | Yes | Document type: `ssp`, `sar`, `poam`, `rar` |
| `file_base64` | string | Yes | Base64-encoded DOCX file content |
| `uploaded_by` | string | Yes | User performing the upload |

- **RBAC**: ISSM, AO
- **RMF Step**: Authorize (Step 5)

**Example response:**

```json
{
  "status": "success",
  "data": {
    "template_id": "abc-123",
    "template_name": "Agency SSP Template v2",
    "document_type": "ssp",
    "is_valid": true,
    "merge_fields_found": ["SystemName", "SystemAcronym", "SecurityCategorization"],
    "merge_fields_missing": [],
    "warnings": []
  }
}
```

### `compliance_list_templates`

List available document templates, optionally filtered by document type.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `document_type` | string | No | Filter by type: `ssp`, `sar`, `poam`, `rar` |

- **RBAC**: Any authenticated user
- **RMF Step**: All steps

**Example response:**

```json
{
  "status": "success",
  "data": {
    "total": 2,
    "templates": [
      {
        "template_id": "abc-123",
        "template_name": "Agency SSP Template v2",
        "document_type": "ssp",
        "uploaded_by": "issm@agency.gov",
        "file_size_bytes": 45056,
        "is_default": false
      }
    ]
  }
}
```

### `compliance_update_template`

Update an existing template by replacing the DOCX file, renaming, or both.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `template_id` | string | Yes | ID of the template to update |
| `file_base64` | string | No | New base64-encoded DOCX file |
| `new_name` | string | No | New template name |
| `updated_by` | string | Yes | User performing the update |

- **RBAC**: ISSM, AO
- **RMF Step**: Authorize (Step 5)

### `compliance_delete_template`

Delete a document template by ID. This action cannot be undone.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `template_id` | string | Yes | ID of the template to delete |

- **RBAC**: ISSM, AO
- **RMF Step**: Authorize (Step 5)

### `compliance_generate_document` (enhanced)

The existing document generation tool now supports three output formats:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `document_type` | string | Yes | Document type: `ssp`, `poam`, `sar`, `rar` |
| `format` | string | No | Output format: `markdown` (default), `docx`, `pdf` |
| `system_id` | string | Conditional | Required for `docx`/`pdf` output |
| `template` | string | No | Custom template ID (DOCX/PDF only) |
| `subscription_id` | string | No | Azure subscription for evidence |
| `framework` | string | No | Compliance framework |
| `system_name` | string | No | System name for document |
| `board_id` | string | No | Kanban board ID for POA&M |

- **RBAC**: ISSM, SCA, AO
- **RMF Step**: Authorize (Step 5), Monitor (Step 6)
- **PDF Engine**: QuestPDF (Community Edition, MIT license)
- **Progress**: Reports streaming progress (0.0–1.0) during PDF generation

---

## SCAP/STIG Viewer Import & Export Tools (Feature 017)

### `compliance_import_ckl`

Import a DISA STIG Viewer CKL checklist file for a registered system. Creates
compliance findings, control effectiveness records, and evidence artifacts.
Accepts base64-encoded file content (max 5 MB after decoding).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `file_content` | string | Yes | Base64-encoded CKL file content (max 5 MB) |
| `file_name` | string | Yes | Original file name (e.g., `windows_server_2022.ckl`) |
| `conflict_resolution` | string | No | How to handle duplicates: `Skip` (default), `Overwrite`, `Merge` |
| `dry_run` | boolean | No | Preview results without persisting (default: `false`) |
| `assessment_id` | string | No | Optional assessment ID; auto-resolves or creates one if omitted |

```json
{
  "status": "success",
  "data": {
    "import_record_id": "...",
    "import_status": "Completed",
    "dry_run": false,
    "benchmark": "Windows_Server_2022_STIG",
    "benchmark_title": "Windows Server 2022 STIG",
    "total_entries": 287,
    "summary": {
      "open": 12,
      "pass": 260,
      "not_applicable": 10,
      "not_reviewed": 5,
      "error": 0,
      "skipped": 0,
      "unmatched": 3
    },
    "changes": {
      "findings_created": 12,
      "findings_updated": 0,
      "effectiveness_created": 8,
      "effectiveness_updated": 0,
      "nist_controls_affected": 8
    },
    "warnings": ["3 STIG rule(s) not found in curated library: V-99997, V-99998, V-99999"],
    "error_message": null
  }
}
```

- **RBAC**: ISSM, SCA
- **RMF Step**: Assess (Step 4)
- **Duplicate Detection**: SHA-256 file hash prevents re-importing identical files
- **Conflict Resolution**: `Skip` ignores existing findings, `Overwrite` replaces, `Merge` keeps higher severity

---

### `compliance_import_xccdf`

Import a SCAP Compliance Checker XCCDF results file for a registered system.
Creates compliance findings and control effectiveness records from automated
scan results. Supports XCCDF 1.1 and 1.2 namespace formats.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `file_content` | string | Yes | Base64-encoded XCCDF file content (max 5 MB) |
| `file_name` | string | Yes | Original file name (e.g., `scan_results.xccdf`) |
| `conflict_resolution` | string | No | How to handle duplicates: `Skip` (default), `Overwrite`, `Merge` |
| `dry_run` | boolean | No | Preview without persisting (default: `false`) |
| `assessment_id` | string | No | Optional assessment ID |

```json
{
  "status": "success",
  "data": {
    "import_record_id": "...",
    "import_status": "CompletedWithWarnings",
    "dry_run": false,
    "benchmark": "Windows_Server_2022_STIG",
    "total_entries": 300,
    "summary": {
      "open": 15,
      "pass": 270,
      "not_applicable": 8,
      "error": 2,
      "skipped": 0,
      "unmatched": 5
    },
    "changes": {
      "findings_created": 15,
      "findings_updated": 0,
      "effectiveness_created": 10,
      "effectiveness_updated": 0,
      "nist_controls_affected": 10
    }
  }
}
```

- **RBAC**: ISSM, SCA
- **RMF Step**: Assess (Step 4)
- **Collection Method**: Automated (vs. Manual for CKL)
- **Score Capture**: XCCDF benchmark score recorded in import record

---

### `compliance_export_ckl`

Export a CKL checklist file for a system and STIG benchmark. Returns
base64-encoded XML content compatible with DISA STIG Viewer and eMASS upload.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `benchmark_id` | string | Yes | STIG benchmark ID (e.g., `Windows_Server_2022_STIG`) |
| `assessment_id` | string | No | Optional assessment ID (uses latest if omitted) |

```json
{
  "status": "success",
  "data": {
    "file_content": "<base64-encoded CKL XML>",
    "file_name": "Windows_Server_2022_STIG_sys-001.ckl",
    "content_type": "application/xml",
    "benchmark_id": "Windows_Server_2022_STIG",
    "system_id": "sys-001"
  }
}
```

- **RBAC**: ISSM, SCA, Engineer
- **RMF Step**: Assess (Step 4), Monitor (Step 6)
- **Output Format**: DISA STIG Viewer CHECKLIST XML schema
- **Finding Status Mapping**: Open → `Open`, Remediated → `NotAFinding`, Accepted → `Not_Applicable`, null → `Not_Reviewed`

---

### `compliance_list_imports`

List import history for a registered system. Shows CKL and XCCDF imports with
summary statistics. Supports pagination and filtering.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `page` | integer | No | Page number, 1-based (default: 1) |
| `page_size` | integer | No | Items per page (default: 20, max: 100) |
| `benchmark_id` | string | No | Filter by benchmark ID |
| `include_dry_runs` | boolean | No | Include dry-run records (default: `false`) |

```json
{
  "status": "success",
  "data": {
    "total_count": 5,
    "page": 1,
    "page_size": 20,
    "imports": [
      {
        "id": "...",
        "file_name": "windows_2022.ckl",
        "import_type": "Ckl",
        "benchmark_id": "Windows_Server_2022_STIG",
        "benchmark_title": "Windows Server 2022 STIG",
        "status": "Completed",
        "imported_by": "mcp-user",
        "imported_at": "2026-03-15T10:30:00Z",
        "is_dry_run": false,
        "total_entries": 287,
        "open_count": 12,
        "pass_count": 260,
        "findings_created": 12,
        "findings_updated": 0
      }
    ]
  }
}
```

- **RBAC**: All compliance roles
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

### `compliance_get_import_summary`

Get detailed summary of a specific import operation, including per-finding
breakdown, unmatched rules, and import configuration.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `import_id` | string | Yes | Import record ID |

```json
{
  "status": "success",
  "data": {
    "id": "...",
    "file_name": "windows_2022.ckl",
    "file_hash": "abc123...",
    "import_type": "Ckl",
    "import_status": "CompletedWithWarnings",
    "benchmark_id": "Windows_Server_2022_STIG",
    "benchmark_title": "Windows Server 2022 STIG",
    "target_host": "DC01.agency.mil",
    "imported_by": "mcp-user",
    "imported_at": "2026-03-15T10:30:00Z",
    "is_dry_run": false,
    "conflict_resolution": "Skip",
    "counts": {
      "total": 287,
      "open": 12,
      "pass": 260,
      "not_applicable": 10,
      "not_reviewed": 5,
      "error": 0,
      "skipped": 0,
      "unmatched": 3,
      "findings_created": 12,
      "findings_updated": 0,
      "effectiveness_created": 8,
      "effectiveness_updated": 0,
      "nist_controls_affected": 8
    },
    "findings": [
      {
        "vuln_id": "V-12345",
        "rule_id": "SV-12345r1_rule",
        "raw_status": "Open",
        "mapped_severity": "CatII",
        "action": "Created",
        "resolved_stig_id": "V-12345"
      }
    ]
  }
}
```

- **RBAC**: All compliance roles
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

### `compliance_import_prisma_csv`

Import a Prisma Cloud CSPM compliance CSV export file. Supports multi-subscription auto-resolution, dry-run preview, and conflict handling.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `file_content` | string | Yes | Base64-encoded Prisma Cloud CSV file |
| `file_name` | string | Yes | Original file name |
| `system_id` | string | No | Registered system ID (if omitted, auto-resolves from Azure subscription IDs) |
| `conflict_resolution` | string | No | `"Skip"` (default), `"Overwrite"`, `"Merge"` |
| `dry_run` | boolean | No | Preview import without persisting (default: false) |
| `assessment_id` | string | No | Existing assessment ID (auto-resolved if omitted) |

```json
{
  "status": "success",
  "data": {
    "total_processed": 47,
    "total_skipped": 0,
    "duration_ms": 2340,
    "dry_run": false,
    "imports": [
      {
        "system_id": "...",
        "system_name": "ACME Portal",
        "import_record_id": "...",
        "import_status": "Completed",
        "total_alerts": 47,
        "summary": {
          "open": 32,
          "resolved": 12,
          "dismissed": 2,
          "snoozed": 1
        },
        "changes": {
          "findings_created": 32,
          "findings_updated": 0,
          "skipped": 0,
          "unmapped_policies": 3,
          "effectiveness_created": 22,
          "effectiveness_updated": 0,
          "nist_controls_affected": 22,
          "evidence_created": 32
        },
        "file_hash": "abc123...",
        "warnings": []
      }
    ],
    "unresolved_subscriptions": [],
    "skipped_non_azure": null
  }
}
```

- **RBAC**: `SecurityLead`, `Analyst`, `Administrator`
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

### `compliance_import_prisma_api`

Import Prisma Cloud API JSON (RQL alert response) with enhanced remediation context, CLI scripts, alert history, and policy metadata.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `file_content` | string | Yes | Base64-encoded JSON file |
| `file_name` | string | Yes | Original file name |
| `system_id` | string | No | Registered system ID |
| `conflict_resolution` | string | No | `"Skip"` (default), `"Overwrite"`, `"Merge"` |
| `dry_run` | boolean | No | Preview without persisting (default: false) |
| `assessment_id` | string | No | Existing assessment ID |

Returns the same format as CSV import, plus enhanced fields per import:

```json
{
  "imports": [
    {
      "enhanced": {
        "remediable_count": 28,
        "cli_scripts_extracted": 22,
        "policy_labels_found": ["CSPM", "Azure", "Storage"],
        "alerts_with_history": 47
      }
    }
  ]
}
```

- **RBAC**: `SecurityLead`, `Analyst`, `Administrator`
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

### `compliance_list_prisma_policies`

List unique Prisma Cloud policies observed across imports for a system, with NIST control mappings, finding status counts, and affected resource types.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |

```json
{
  "system_id": "...",
  "total_policies": 35,
  "policies": [
    {
      "policy_name": "Azure Storage account should use CMK encryption",
      "policy_type": "config",
      "severity": "high",
      "nist_control_ids": ["SC-28", "SC-12"],
      "open_count": 3,
      "resolved_count": 7,
      "dismissed_count": 1,
      "affected_resource_types": ["Microsoft.Storage/storageAccounts"],
      "last_seen_import_id": "...",
      "last_seen_at": "2026-03-05T10:30:00Z"
    }
  ]
}
```

- **RBAC**: `SecurityLead`, `Analyst`, `Assessor`, `Administrator`
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

### `compliance_prisma_trend`

Compare Prisma Cloud findings across scan imports to show remediation progress, new findings, and compliance drift. Supports optional breakdowns by resource type or NIST control.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `import_ids` | string | No | JSON array of specific import IDs to compare |
| `group_by` | string | No | `"resource_type"` or `"nist_control"` |

```json
{
  "system_id": "...",
  "imports": [
    { "import_id": "...", "imported_at": "2026-02-15T10:00:00Z", "total_alerts": 55 },
    { "import_id": "...", "imported_at": "2026-03-05T10:00:00Z", "total_alerts": 47 }
  ],
  "new_findings": 8,
  "resolved_findings": 16,
  "persistent_findings": 31,
  "remediation_rate": 34.04,
  "resource_type_breakdown": { "Microsoft.Storage/storageAccounts": 12 },
  "nist_control_breakdown": { "SC-28": 5, "AC-2": 3 }
}
```

- **RBAC**: `SecurityLead`, `Analyst`, `Assessor`, `Administrator`
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

## ACAS/Nessus Scan Import Tools (Feature 026)

### `compliance_import_nessus`

Import a Tenable Nessus/ACAS vulnerability scan file (.nessus XML format) for a
registered system. Parses plugin results per host, maps plugin families to NIST
800-53 controls via curated mapping table with heuristic fallback, creates
compliance findings, updates control effectiveness, and generates POA&M weakness
entries for Critical/High/Medium severity findings.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `file_content` | string | Yes | Base64-encoded .nessus XML file content (max 10 MB) |
| `file_name` | string | Yes | Original file name (e.g., `acas_scan_2025Q1.nessus`) |
| `conflict_resolution` | string | No | How to handle duplicates: `Skip` (default), `Overwrite`, `Merge` |
| `dry_run` | boolean | No | Preview results without persisting (default: `false`) |
| `assessment_id` | string | No | Optional assessment ID; auto-resolves or creates one if omitted |
| `user_role` | string | Yes | Caller's compliance role for RBAC enforcement |

```json
{
  "status": "success",
  "data": {
    "import_record_id": "...",
    "import_status": "Completed",
    "dry_run": false,
    "total_hosts": 5,
    "total_plugins": 287,
    "summary": {
      "critical": 3,
      "high": 12,
      "medium": 45,
      "low": 120,
      "informational": 107
    },
    "changes": {
      "findings_created": 60,
      "findings_updated": 0,
      "findings_skipped": 0,
      "effectiveness_created": 15,
      "effectiveness_updated": 0,
      "nist_controls_affected": 15,
      "poam_weaknesses_created": 12
    },
    "warnings": ["12 plugin(s) mapped via heuristic fallback (family → control)"],
    "error_message": null
  }
}
```

- **RBAC**: Analyst, SecurityLead, Administrator, PlatformEngineer
- **RMF Step**: Assess (Step 4)
- **Duplicate Detection**: SHA-256 file hash prevents re-importing identical files
- **Conflict Resolution**: `Skip` ignores existing findings, `Overwrite` replaces, `Merge` keeps higher severity
- **Control Mapping**: Curated plugin-family → NIST 800-53 mapping table with heuristic fallback
- **POA&M Integration**: Auto-creates POA&M weakness entries for Cat I/II/III findings

---

### `compliance_list_nessus_imports`

List Nessus/ACAS import history for a registered system. Filters to NessusXml
import type only. Supports date range filtering and pagination.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `from_date` | string | No | Start date filter (ISO 8601) |
| `to_date` | string | No | End date filter (ISO 8601) |
| `page_size` | integer | No | Items per page (default: 20, max: 50) |
| `user_role` | string | Yes | Caller's compliance role for RBAC enforcement |

```json
{
  "status": "success",
  "data": {
    "total_count": 3,
    "imports": [
      {
        "id": "...",
        "file_name": "acas_scan_2025Q1.nessus",
        "import_type": "NessusXml",
        "status": "Completed",
        "imported_by": "mcp-user",
        "imported_at": "2025-03-15T10:30:00Z",
        "total_findings": 287,
        "findings_created": 60
      }
    ]
  }
}
```

- **RBAC**: Analyst, SecurityLead, Administrator, PlatformEngineer, Auditor, AuthorizingOfficial, Viewer
- **RMF Step**: Assess (Step 4), Monitor (Step 6)

---

## SAP Generation Tools (Feature 018)

### `compliance_generate_sap`

Generate a Security Assessment Plan (SAP) for a registered system. Auto-populates from control baseline, OSCAL assessment objectives, STIG mappings, and evidence data. All optional parameters are auto-populated with sensible defaults — call with just the `system_id`. Produces a Markdown SAP document with 15 sections.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `assessment_id` | string | No | Optional assessment cycle ID to link SAP to |
| `schedule_start` | string | No | Assessment start date (ISO 8601) |
| `schedule_end` | string | No | Assessment end date (ISO 8601) |
| `team_members` | string | No | JSON array of `{ name, organization, role, contact_info? }` |
| `scope_notes` | string | No | SCA-provided assessment scope notes |
| `method_overrides` | string | No | JSON array of `{ control_id, methods[], rationale? }` |
| `rules_of_engagement` | string | No | Assessment constraints and availability windows |
| `format` | string | No | Output format: `markdown` (default), `docx`, or `pdf` |

```json
{
  "status": "success",
  "data": {
    "sap_id": "...",
    "system_id": "...",
    "assessment_id": "...",
    "title": "Security Assessment Plan — Eagle Eye",
    "status": "Draft",
    "format": "markdown",
    "baseline_level": "Moderate",
    "total_controls": 325,
    "customer_controls": 180,
    "inherited_controls": 100,
    "shared_controls": 45,
    "stig_benchmark_count": 3,
    "controls_with_objectives": 325,
    "evidence_gaps": 12,
    "family_summaries": [
      { "family": "AC", "control_count": 25, "customer_count": 15, "inherited_count": 7, "methods": ["Test", "Interview"] }
    ],
    "content": "# Security Assessment Plan\n...",
    "generated_at": "2026-03-11T10:00:00Z",
    "warnings": ["12 controls have no evidence artifacts"]
  },
  "metadata": { "tool": "compliance_generate_sap", "duration_ms": 1500, "timestamp": "..." }
}
```

- **RBAC**: `Analyst` (SCA), `SecurityLead` (ISSM)
- **RMF Step**: Assess (Step 4)
- **Auto-Population**: Schedule defaults to 30-day window, team populated from RMF role assignments, methods derived from OSCAL objectives

---

### `compliance_update_sap`

Update a Draft SAP's schedule, scope, team, assessment methods, or rules of engagement. Team replacement is atomic. Method overrides are additive (only specified controls updated). Finalized SAPs cannot be modified.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sap_id` | string | No | SAP ID to update (optional if `system_id` is provided) |
| `system_id` | string | No | System GUID, name, or acronym — looks up the latest Draft SAP |
| `schedule_start` | string | No | Updated assessment start date (ISO 8601) |
| `schedule_end` | string | No | Updated assessment end date (ISO 8601) |
| `scope_notes` | string | No | Updated scope notes |
| `rules_of_engagement` | string | No | Updated rules of engagement |
| `team_members` | string | No | JSON array of `{ name, organization, role, contact_info? }` — replaces entire team |
| `method_overrides` | string | No | JSON array of `{ control_id, methods[], rationale? }` — additive per-control overrides |

```json
{
  "status": "success",
  "data": {
    "sap_id": "...",
    "status": "Draft",
    "content": "# Security Assessment Plan\n...",
    "updated_at": "2026-03-11T11:00:00Z"
  },
  "metadata": { "tool": "compliance_update_sap", "duration_ms": 800, "timestamp": "..." }
}
```

- **RBAC**: `Analyst` (SCA), `SecurityLead` (ISSM)
- **RMF Step**: Assess (Step 4)
- **Lookup Behavior**: If only `system_id` is provided and no Draft SAP exists, auto-generates one first

---

### `compliance_finalize_sap`

Finalize a Draft SAP — locks it with SHA-256 content hash. Finalized SAPs are immutable: no updates, no re-finalization. Sets `FinalizedBy`, `FinalizedAt`, and `ContentHash` fields.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sap_id` | string | No | SAP ID to finalize (optional if `system_id` is provided) |
| `system_id` | string | No | System GUID, name, or acronym — looks up the latest SAP |

```json
{
  "status": "success",
  "data": {
    "sap_id": "...",
    "status": "Finalized",
    "content_hash": "sha256:a1b2c3d4...",
    "finalized_by": "mcp-user",
    "finalized_at": "2026-03-11T12:00:00Z",
    "total_controls": 325,
    "title": "Security Assessment Plan — Eagle Eye"
  },
  "metadata": { "tool": "compliance_finalize_sap", "duration_ms": 400, "timestamp": "..." }
}
```

- **RBAC**: `Analyst` (SCA)
- **RMF Step**: Assess (Step 4)
- **Integrity**: SHA-256 hash enables tamper detection for finalized assessment plans

---

### `compliance_get_sap`

Retrieve a specific SAP by its ID, or the latest SAP for a system. If both `sap_id` and `system_id` are provided, `sap_id` takes precedence. When retrieving by `system_id`, prefers Finalized SAPs over Drafts.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sap_id` | string | No | Specific SAP ID to retrieve |
| `system_id` | string | No | System GUID, name, or acronym — returns latest SAP (prefers Finalized) |

```json
{
  "status": "success",
  "data": {
    "sap_id": "...",
    "system_id": "...",
    "assessment_id": "...",
    "title": "Security Assessment Plan — Eagle Eye",
    "status": "Finalized",
    "format": "markdown",
    "baseline_level": "Moderate",
    "total_controls": 325,
    "customer_controls": 180,
    "inherited_controls": 100,
    "shared_controls": 45,
    "stig_benchmark_count": 3,
    "controls_with_objectives": 325,
    "content": "# Security Assessment Plan\n...",
    "content_hash": "sha256:a1b2c3d4...",
    "generated_at": "2026-03-11T10:00:00Z",
    "finalized_at": "2026-03-11T12:00:00Z",
    "family_summaries": [
      { "family": "AC", "control_count": 25, "customer_count": 15, "inherited_count": 7, "methods": ["Test", "Interview"] }
    ]
  },
  "metadata": { "tool": "compliance_get_sap", "duration_ms": 200, "timestamp": "..." }
}
```

- **RBAC**: All roles
- **RMF Step**: Assess (Step 4)

---

### `compliance_list_saps`

List all Security Assessment Plans (SAPs) for a system, including Draft and Finalized history. Returns status, dates, and scope summary per SAP. Content is omitted for brevity.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "sap_count": 2,
    "saps": [
      {
        "sap_id": "...",
        "title": "Security Assessment Plan — Eagle Eye",
        "status": "Finalized",
        "baseline_level": "Moderate",
        "total_controls": 325,
        "customer_controls": 180,
        "inherited_controls": 100,
        "shared_controls": 45,
        "generated_at": "2026-03-11T10:00:00Z",
        "finalized_at": "2026-03-11T12:00:00Z"
      }
    ]
  },
  "metadata": { "tool": "compliance_list_saps", "duration_ms": 150, "timestamp": "..." }
}
```

- **RBAC**: All roles
- **RMF Step**: Assess (Step 4)

### Troubleshooting — SAP Tools

**"Tool not found" error**: SAP tools require Feature 018 to be deployed and `ISapService` registered in DI. If you receive a tool-not-found response, verify the service is registered in `ServiceCollectionExtensions.cs` and the `Feature018_SapGeneration` migration has been applied.

```json
{
  "status": "error",
  "error": "Tool 'compliance_generate_sap' not found. Feature 018 (SAP Generation) may not be deployed in this environment.",
  "resolution": "Verify ISapService is registered and Feature018_SapGeneration migration is applied."
}
```

---

## Privacy & Interconnection Tools (Feature 021)

### `compliance_create_pta`

Conduct a Privacy Threshold Analysis (PTA) for a registered system. Auto-detects PII from categorized information types or accepts manual PII flags.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `manual_mode` | boolean | No | If true, use explicit PII flags instead of auto-detection |
| `collects_pii` | boolean | No | Manual mode: whether system collects PII |
| `maintains_pii` | boolean | No | Manual mode: whether system maintains PII |
| `disseminates_pii` | boolean | No | Manual mode: whether system disseminates PII |
| `pii_categories` | string | No | JSON array of PII categories |
| `estimated_record_count` | integer | No | Estimated number of PII records |
| `exemption_rationale` | string | No | Exemption justification if exempt |

```json
{
  "status": "success",
  "data": {
    "pta_id": "...",
    "determination": "PiaRequired",
    "collects_pii": true,
    "maintains_pii": true,
    "disseminates_pii": false,
    "pii_categories": ["SSN", "Name", "Email"],
    "pii_source_info_types": ["D.3.5.1 – Civilian Personnel"],
    "rationale": "System processes SSN for personnel records. PIA required per OMB M-03-22."
  }
}
```

- **RBAC**: `Analyst` (ISSO), `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)
- **Auto-Detection**: When `manual_mode` is false, scans SP 800-60 information types from system categorization for PII indicators

---

### `compliance_generate_pia`

Generate a Privacy Impact Assessment (PIA) with 8 OMB M-03-22 sections. Requires a completed PTA with `PiaRequired` determination.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

```json
{
  "status": "success",
  "data": {
    "pia_id": "...",
    "pia_status": "Draft",
    "version": 1,
    "total_sections": 8,
    "pre_populated_sections": 5,
    "sections": [
      {
        "section_id": "1",
        "title": "Authority to Collect",
        "question": "What legal authority allows the collection of this PII?",
        "answer": "10 USC §136, DoD Instruction 5400.11...",
        "is_pre_populated": true
      }
    ]
  }
}
```

- **RBAC**: `Analyst` (ISSO), `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)
- **Prerequisite**: PTA must exist with `PiaRequired` determination

---

### `compliance_review_pia`

Review a Privacy Impact Assessment — approve or request revision with deficiency notes.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `decision` | string | Yes | Review decision: `Approved` or `RequestRevision` |
| `reviewer_comments` | string | Yes | Reviewer notes and observations |
| `deficiencies` | string | No | JSON array of deficiency strings (required for `RequestRevision`) |

```json
{
  "status": "success",
  "data": {
    "pia_id": "...",
    "decision": "Approved",
    "new_status": "Approved",
    "reviewer_comments": "All sections adequately address PII handling and retention.",
    "deficiencies": [],
    "expiration_date": "2027-03-11T00:00:00Z"
  }
}
```

- **RBAC**: `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)
- **Expiration**: Approved PIAs expire annually and must be re-reviewed

---

### `compliance_check_privacy_compliance`

Get privacy compliance dashboard for a system — aggregates PTA, PIA, and gate status including interconnection agreement health.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

```json
{
  "status": "success",
  "data": {
    "system_id": "...",
    "system_name": "Eagle Eye",
    "pta_determination": "PiaRequired",
    "pia_status": "Approved",
    "privacy_gate_satisfied": true,
    "active_interconnections": 3,
    "interconnections_with_agreements": 3,
    "expired_agreements": 0,
    "expiring_within_90_days": 1,
    "interconnection_gate_satisfied": true,
    "has_no_external_interconnections": false,
    "overall_status": "Compliant"
  }
}
```

- **RBAC**: All roles
- **RMF Step**: Prepare (Step 1)

---

### `compliance_add_interconnection`

Register a system-to-system interconnection that crosses the authorization boundary. Clears `HasNoExternalInterconnections` flag if previously set.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `target_system_name` | string | Yes | Name of external system |
| `connection_type` | string | Yes | Connection type: `direct`, `vpn`, `api`, `federated`, `wireless`, `remote_access` |
| `data_flow_direction` | string | Yes | Data flow: `inbound`, `outbound`, `bidirectional` |
| `data_classification` | string | Yes | Data classification: `unclassified`, `cui`, `secret`, `top_secret` |
| `target_system_owner` | string | No | Organization/POC owning target system |
| `target_system_acronym` | string | No | Target system abbreviation |
| `data_description` | string | No | Description of data exchanged |
| `protocols` | string | No | JSON array of protocols used |
| `ports` | string | No | JSON array of ports used |
| `security_measures` | string | No | JSON array of security controls |
| `authentication_method` | string | No | How systems authenticate to each other |

```json
{
  "status": "success",
  "data": {
    "interconnectionId": "...",
    "targetSystemName": "JIRA Cloud",
    "interconnectionStatus": "Proposed",
    "hasAgreement": false
  }
}
```

- **RBAC**: `PlatformEngineer` (Eng), `Analyst` (ISSO), `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)

---

### `compliance_list_interconnections`

List all system interconnections with agreement status summaries. Optionally filter by status.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `status_filter` | string | No | Filter: `proposed`, `active`, `suspended`, `terminated` |

```json
{
  "status": "success",
  "data": {
    "systemId": "...",
    "totalInterconnections": 3,
    "interconnections": [
      {
        "id": "...",
        "targetSystemName": "JIRA Cloud",
        "interconnectionStatus": "Active",
        "hasAgreement": true
      }
    ]
  }
}
```

- **RBAC**: All roles
- **RMF Step**: Prepare (Step 1)

---

### `compliance_update_interconnection`

Update an existing interconnection's details or status. Requires `status_reason` when suspending or terminating.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `interconnection_id` | string | Yes | SystemInterconnection ID (GUID) |
| `status` | string | No | New status: `proposed`, `active`, `suspended`, `terminated` |
| `status_reason` | string | No | Reason for status change (required for `suspended`/`terminated`) |
| `connection_type` | string | No | Updated connection type |
| `data_classification` | string | No | Updated data classification |
| `protocols` | string | No | Updated protocols (JSON array) |
| `ports` | string | No | Updated ports (JSON array) |
| `security_measures` | string | No | Updated security controls (JSON array) |

```json
{
  "status": "success",
  "data": {
    "interconnectionId": "...",
    "targetSystemName": "JIRA Cloud",
    "interconnectionStatus": "Active",
    "hasAgreement": true
  }
}
```

- **RBAC**: `Analyst` (ISSO), `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)

---

### `compliance_generate_isa`

Generate an AI-drafted Interconnection Security Agreement (ISA) from interconnection data using NIST SP 800-47 structure.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `interconnection_id` | string | Yes | SystemInterconnection ID (GUID) |

```json
{
  "status": "success",
  "data": {
    "agreementId": "...",
    "title": "ISA — Eagle Eye ↔ JIRA Cloud",
    "agreementType": "Isa",
    "narrativeDocument": "# Interconnection Security Agreement\n..."
  }
}
```

- **RBAC**: `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)

---

### `compliance_register_agreement`

Register a pre-existing ISA, MOU, or SLA agreement for a system interconnection.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `interconnection_id` | string | Yes | SystemInterconnection ID |
| `agreement_type` | string | Yes | Agreement type: `isa`, `mou`, `sla` |
| `title` | string | Yes | Agreement title |
| `document_reference` | string | No | URL or path to agreement document |
| `status` | string | No | Initial status: `draft`, `pending_signature`, `signed` |
| `effective_date` | string | No | ISO 8601 effective date |
| `expiration_date` | string | No | ISO 8601 expiration date |
| `signed_by_local` | string | No | Local signatory name/title |
| `signed_by_remote` | string | No | Remote signatory name/title |

```json
{
  "status": "success",
  "data": {
    "agreementId": "...",
    "title": "ISA — Eagle Eye ↔ JIRA Cloud",
    "agreementType": "Isa",
    "agreementStatus": "Signed",
    "expirationDate": "2027-03-11T00:00:00Z"
  }
}
```

- **RBAC**: `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)

---

### `compliance_update_agreement`

Update an existing agreement's status, metadata, or signatories. Terminated agreements can only have `review_notes` updated.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `agreement_id` | string | Yes | InterconnectionAgreement ID (GUID) |
| `status` | string | No | New status: `draft`, `pending_signature`, `signed`, `expired`, `terminated` |
| `effective_date` | string | No | Updated ISO 8601 effective date |
| `expiration_date` | string | No | Updated ISO 8601 expiration date |
| `signed_by_local` | string | No | Updated local signatory |
| `signed_by_local_date` | string | No | Updated ISO 8601 local signature date |
| `signed_by_remote` | string | No | Updated remote signatory |
| `signed_by_remote_date` | string | No | Updated ISO 8601 remote signature date |
| `review_notes` | string | No | Review or renewal notes |

```json
{
  "status": "success",
  "data": {
    "agreementId": "...",
    "title": "ISA — Eagle Eye ↔ JIRA Cloud",
    "agreementType": "Isa",
    "agreementStatus": "Signed",
    "expirationDate": "2027-03-11T00:00:00Z"
  }
}
```

- **RBAC**: `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)

---

### `compliance_certify_no_interconnections`

Certify that a system has no external interconnections, satisfying Gate 4 without requiring interconnection records.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `certify` | boolean | Yes | `true` to certify no interconnections, `false` to revoke |

```json
{
  "status": "success",
  "data": {
    "systemId": "...",
    "hasNoExternalInterconnections": true,
    "interconnectionGateSatisfied": true
  }
}
```

- **RBAC**: `SecurityLead` (ISSM)
- **RMF Step**: Prepare (Step 1)

---

### `compliance_validate_agreements`

Validate that all active system interconnections have signed, current agreements. Supports `HasNoExternalInterconnections` bypass.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

```json
{
  "status": "success",
  "data": {
    "totalInterconnections": 3,
    "compliantCount": 2,
    "expiringWithin90DaysCount": 1,
    "missingAgreementCount": 0,
    "expiredAgreementCount": 0,
    "isFullyCompliant": true,
    "items": [
      {
        "interconnectionId": "...",
        "targetSystemName": "JIRA Cloud",
        "validationStatus": "Compliant",
        "agreementTitle": "ISA — Eagle Eye ↔ JIRA Cloud",
        "expirationDate": "2027-03-11T00:00:00Z",
        "notes": null
      }
    ]
  }
}
```

- **RBAC**: `Analyst` (ISSO), `SecurityLead` (ISSM), `Assessor` (SCA)
- **RMF Step**: Prepare (Step 1)

### Troubleshooting — Privacy & Interconnection Tools

**"Tool not found" error**: Privacy and interconnection tools require Feature 021 to be deployed with `IPrivacyService` and `IInterconnectionService` registered in DI. If you receive a tool-not-found response, verify the services are registered in `ServiceCollectionExtensions.cs` and the `Feature021_PrivacyInterconnection` migration has been applied.

```json
{
  "status": "error",
  "error": "Tool 'compliance_create_pta' not found. Feature 021 (Privacy & Interconnections) may not be deployed in this environment.",
  "resolution": "Verify IPrivacyService and IInterconnectionService are registered and Feature021_PrivacyInterconnection migration is applied."
}
```

---

## SSP Authoring & OSCAL Export Tools (Feature 022)

### `compliance_write_ssp_section`

Write or update an individual NIST SP 800-18 SSP section (§1–§13). Creates a new section on first write; subsequent writes increment the version and reset status to Draft. Auto-generated sections (§1,§2,§3,§4,§7,§9,§10,§11) regenerate from entity data; authored sections (§5,§6,§8,§12,§13) store user-provided markdown content. Use `submit_for_review=true` to transition from Draft to UnderReview.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `section_number` | integer | Yes | NIST 800-18 section number (1–13) |
| `content` | string | No | Section content in markdown (required for authored sections §5,§6,§8,§12,§13) |
| `authored_by` | string | Yes | Identity of the user authoring this section |
| `expected_version` | integer | No | Optimistic concurrency check — reject if stored version does not match |
| `submit_for_review` | boolean | No | If true, transitions from Draft to UnderReview after writing (default: `false`) |

```json
{
  "status": "success",
  "data": {
    "section_number": 5,
    "section_title": "System Environment",
    "status": "Draft",
    "version": 2,
    "word_count": 450,
    "is_auto_generated": false,
    "has_manual_override": true
  },
  "metadata": { "tool": "compliance_write_ssp_section", "duration_ms": 300, "timestamp": "..." }
}
```

- **RBAC**: `Analyst` (ISSO), `PlatformEngineer` (Eng)
- **RMF Step**: Implement (Step 3)
- **Concurrency**: `expected_version` provides optimistic locking to prevent lost updates

---

### `compliance_review_ssp_section`

Review an SSP section that is in UnderReview status. Approve to mark as Approved, or request revision to return to Draft with reviewer comments.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `section_number` | integer | Yes | NIST 800-18 section number (1–13) |
| `decision` | string | Yes | Review decision: `approve` or `request_revision` |
| `reviewer` | string | Yes | Identity of the reviewer |
| `comments` | string | No | Reviewer comments (required when requesting revision) |

```json
{
  "status": "success",
  "data": {
    "section_number": 5,
    "section_title": "System Environment",
    "status": "Approved",
    "reviewed_by": "jane.smith@agency.gov",
    "reviewed_at": "2026-03-11T14:00:00Z",
    "reviewer_comments": "Section accurately describes the deployment environment.",
    "version": 2
  },
  "metadata": { "tool": "compliance_review_ssp_section", "duration_ms": 200, "timestamp": "..." }
}
```

- **RBAC**: `SecurityLead` (ISSM)
- **RMF Step**: Implement (Step 3), Assess (Step 4)
- **State Machine**: Only sections in `UnderReview` status can be reviewed

---

### `compliance_ssp_completeness`

Check SSP section completeness status for a registered system. Returns per-section summary with status, word count, and version for all 13 NIST 800-18 sections. Includes overall readiness percentage and blocking issues list.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

```json
{
  "status": "success",
  "data": {
    "system_name": "Eagle Eye",
    "overall_readiness_percent": 85,
    "approved_count": 11,
    "total_sections": 13,
    "sections": [
      {
        "section_number": 1,
        "section_title": "System Identification",
        "status": "Approved",
        "is_auto_generated": true,
        "has_manual_override": false,
        "authored_by": "system",
        "authored_at": "2026-03-10T09:00:00Z",
        "word_count": 320,
        "version": 1
      }
    ],
    "blocking_issues": ["§12 (Personnel Security) is still in Draft", "§13 (Contingency Plan) has not been authored"]
  },
  "metadata": { "tool": "compliance_ssp_completeness", "duration_ms": 250, "timestamp": "..." }
}
```

- **RBAC**: All roles
- **RMF Step**: Implement (Step 3), Assess (Step 4)

---

### `compliance_export_oscal_ssp`

Export an OSCAL 1.1.2 System Security Plan as JSON for a registered system.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID (GUID) or name/acronym |
| `include_back_matter` | boolean | No | Include back-matter resources (default: `true`) |
| `pretty_print` | boolean | No | Pretty-print JSON output (default: `true`) |

```json
{
  "status": "success",
  "data": {
    "oscal_ssp_json": "{ \"system-security-plan\": { ... } }",
    "warnings": [],
    "statistics": {
      "control_count": 325,
      "component_count": 12,
      "inventory_item_count": 8,
      "user_count": 5,
      "back_matter_resource_count": 3
    }
  },
  "metadata": { "tool": "compliance_export_oscal_ssp", "duration_ms": 2000, "timestamp": "..." }
}
```

- **RBAC**: `SecurityLead` (ISSM), `Assessor` (SCA), `AuthorizingOfficial` (AO)
- **RMF Step**: Authorize (Step 5)
- **OSCAL Version**: 1.1.2 compliant JSON output

---

### `compliance_validate_oscal_ssp`

Generate OSCAL 1.1.2 SSP JSON for a system, then validate it for structural correctness.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID (GUID) or name/acronym |

```json
{
  "status": "success",
  "data": {
    "is_valid": true,
    "errors": [],
    "warnings": ["Back-matter has no document references"],
    "statistics": {
      "control_count": 325,
      "component_count": 12,
      "inventory_item_count": 8,
      "user_count": 5,
      "back_matter_resource_count": 0
    }
  },
  "metadata": { "tool": "compliance_validate_oscal_ssp", "duration_ms": 3000, "timestamp": "..." }
}
```

- **RBAC**: `SecurityLead` (ISSM), `Assessor` (SCA)
- **RMF Step**: Authorize (Step 5)

### Troubleshooting — SSP Authoring & OSCAL Tools

**"Tool not found" error**: SSP and OSCAL tools require Feature 022 to be deployed with `ISspService`, `IOscalSspExportService`, and `IOscalValidationService` registered in DI. If you receive a tool-not-found response, verify the services are registered in `ServiceCollectionExtensions.cs` and the `Feature022_SspOscal` migration has been applied.

```json
{
  "status": "error",
  "error": "Tool 'compliance_write_ssp_section' not found. Feature 022 (SSP Authoring & OSCAL) may not be deployed in this environment.",
  "resolution": "Verify ISspService, IOscalSspExportService, and IOscalValidationService are registered and Feature022_SspOscal migration is applied."
}
```

---

## Feature 024: Narrative Governance Tools

> Version Control + Approval Workflow for SSP control implementation narratives.

### `compliance_narrative_history`

View paginated version history for a control implementation narrative.

- **RBAC**: All compliance roles (read-only)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `control_id` (required), `page` (optional, default 1), `page_size` (optional, default 20)
- **Response**: Array of version records with `version_number`, `content`, `status`, `authored_by`, `authored_at`, `change_reason`

### `compliance_narrative_diff`

Compare two versions of a control narrative using unified diff format.

- **RBAC**: All compliance roles (read-only)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `control_id` (required), `from_version` (required), `to_version` (required)
- **Response**: `from_version`, `to_version`, `unified_diff` (text), `has_changes` (boolean)

### `compliance_rollback_narrative`

Rollback a control narrative to a previous version (creates a new version with the old content).

- **RBAC**: Compliance.Analyst (ISSO), Compliance.PlatformEngineer (Engineer)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `control_id` (required), `target_version` (required)
- **Response**: New `version_number`, `status`, `authored_by`, `authored_at`, `change_reason`
- **Error**: `UNDER_REVIEW` if narrative is currently in UnderReview status

### `compliance_submit_narrative`

Submit a Draft narrative for ISSM review. Transitions status from Draft/NeedsRevision to UnderReview.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.PlatformEngineer (Engineer)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `control_id` (required)
- **Response**: `version_number`, `previous_status`, `new_status`, `submitted_by`, `submitted_at`

### `compliance_review_narrative`

Approve or request revision of a narrative in UnderReview status.

- **RBAC**: Compliance.SecurityLead (ISSM)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `control_id` (required), `decision` (required: `approve`|`request_revision`), `comments` (optional, required for `request_revision`)
- **Response**: `decision`, `previous_status`, `new_status`, `reviewed_by`, `reviewed_at`, `comments`
- **Error**: `COMMENTS_REQUIRED` when decision is `request_revision` but no comments provided

### `compliance_batch_review_narratives`

Batch approve or request revision of narratives for a control family or specific control IDs.

- **RBAC**: Compliance.SecurityLead (ISSM)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `decision` (required), `comments` (optional), `family_filter` (optional), `control_ids` (optional, comma-separated)
- **Response**: `reviewed_count`, `skipped_count`, `reviewed_controls`, `skipped_controls`
- **Error**: `MUTUALLY_EXCLUSIVE_FILTERS` if both `family_filter` and `control_ids` provided

### `compliance_narrative_approval_progress`

Aggregate approval status counts, overall approval percentage, and per-family breakdown.

- **RBAC**: All compliance roles (read-only)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `family_filter` (optional)
- **Response**: `overall` (total/approved/draft/in_review/needs_revision/missing/approval_percentage), `families` array, `review_queue`, `staleness_warnings`

### `compliance_batch_submit_narratives`

Submit all Draft narratives for a control family (or all families) for ISSM review.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.PlatformEngineer (Engineer)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `family_filter` (optional)
- **Response**: `submitted_count`, `skipped_count`, `submitted_controls`, `skipped_controls`
- **Error**: `NO_DRAFT_NARRATIVES` if no Draft narratives found matching the filter

### Enhanced: `compliance_write_narrative`

> Feature 024 Enhancement: Now creates a `NarrativeVersion` record on every write, increments `CurrentVersion`, and supports optimistic concurrency control.

- **New Parameters**: `expected_version` (optional int, concurrency check), `change_reason` (optional string)
- **Enhanced Response**: Adds `version_number`, `approval_status`, `previous_version` fields
- **New Error Codes**: `CONCURRENCY_CONFLICT` (expected_version mismatch), `UNDER_REVIEW` (narrative is in review)

### Troubleshooting — Narrative Governance Tools

**"Tool not found" error**: Narrative governance tools require Feature 024 to be deployed with `INarrativeGovernanceService` registered in DI. Verify the service is registered in `ServiceCollectionExtensions.cs` and the `Feature024_NarrativeGovernance` migration has been applied.

```json
{
  "status": "error",
  "error": "Tool 'compliance_narrative_history' not found. Feature 024 (Narrative Governance) may not be deployed in this environment.",
  "resolution": "Verify INarrativeGovernanceService is registered and Feature024_NarrativeGovernance migration is applied."
}
```

---

## HW/SW Inventory Tools (Feature 025)

> **Feature 025**: HW/SW Inventory Management — Register, manage, import/export, and assess completeness of hardware and software inventory items.

### `inventory_add_item`

Register a hardware or software inventory item with function-based field validation.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.SecurityLead (ISSM), Compliance.PlatformEngineer (Engineer)
- **RMF Step**: Implement (Phase 3)
- **Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | ✓ | System GUID, name, or acronym |
| `item_name` | string | ✓ | Display name |
| `type` | string | ✓ | `hardware` or `software` |
| `function` | string | ✓ | HW: Server, Workstation, NetworkDevice, Storage, Other; SW: OperatingSystem, Database, Middleware, Application, SecurityTool, Other |
| `manufacturer` | string | | Required for hardware |
| `model` | string | | Hardware model |
| `serial_number` | string | | Serial number |
| `ip_address` | string | | Required for Server/NetworkDevice |
| `mac_address` | string | | MAC address |
| `location` | string | | Physical location |
| `vendor` | string | | Required for software |
| `version` | string | | Required for software |
| `patch_level` | string | | Patch level |
| `license_type` | string | | License type |
| `parent_hardware_id` | string | | Parent HW GUID (for SW items) |

- **Response**: Created item with `id`, `item_name`, `type`, `status`
- **Errors**: `SYSTEM_NOT_FOUND`, `VALIDATION_FAILED`, `DUPLICATE_IP`, `INVALID_INPUT`

### `inventory_update_item`

Update fields on an existing inventory item (partial update — null fields unchanged).

- **RBAC**: Compliance.Analyst (ISSO), Compliance.SecurityLead (ISSM), Compliance.PlatformEngineer (Engineer)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `item_id` (required), plus optional `item_name`, `manufacturer`, `model`, `serial_number`, `ip_address`, `mac_address`, `location`, `vendor`, `version`, `patch_level`, `license_type`
- **Response**: Updated item
- **Errors**: `ITEM_NOT_FOUND`, `DUPLICATE_IP`, `INVALID_INPUT`

### `inventory_decommission_item`

Soft-delete an inventory item. Cascades to child software items.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.SecurityLead (ISSM)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `item_id` (required), `rationale` (required)
- **Response**: Decommissioned item with cascaded child count in metadata
- **Errors**: `ITEM_NOT_FOUND`, `ALREADY_DECOMMISSIONED`, `INVALID_INPUT`

### `inventory_list`

List and filter inventory items with pagination.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.Auditor (SCA), Compliance.PlatformEngineer (Engineer)
- **RMF Step**: Implement (Phase 3), Assess (Phase 5)
- **Parameters**: `system_id` (required), optional `type`, `function`, `vendor`, `status`, `search`, `page_size`, `page`
- **Response**: `count`, `page`, `page_size`, `items[]`
- **Errors**: `SYSTEM_NOT_FOUND`, `INVALID_INPUT`

### `inventory_get`

Retrieve a single inventory item with installed software children.

- **RBAC**: All compliance roles
- **RMF Step**: Any
- **Parameters**: `item_id` (required)
- **Response**: Item detail with `installed_software[]` for hardware items
- **Errors**: `ITEM_NOT_FOUND`, `INVALID_INPUT`

### `inventory_export`

Export inventory to an eMASS-compatible Excel workbook with Hardware and Software worksheets.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.SecurityLead (ISSM)
- **RMF Step**: Implement (Phase 3), Authorize (Phase 6)
- **Parameters**: `system_id` (required), optional `export_type` (`all`/`hardware`/`software`), `include_decommissioned`
- **Response**: `file_base64` (base64-encoded .xlsx)
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_INVENTORY_DATA`

### `inventory_import`

Import inventory from an eMASS-format Excel workbook with dry-run support.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.SecurityLead (ISSM)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required), `file_base64` (required), optional `dry_run`
- **Response**: `hardware_created`, `software_created`, `rows_skipped`, `dry_run`, `errors[]`
- **Errors**: `SYSTEM_NOT_FOUND`, `INVALID_BASE64`

### `inventory_completeness`

Check inventory completeness — missing fields, unmatched boundary resources, hardware without software.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.Auditor (SCA)
- **RMF Step**: Implement (Phase 3), Assess (Phase 5)
- **Parameters**: `system_id` (required)
- **Response**: `completeness_score`, `is_complete`, `items_with_missing_fields[]`, `unmatched_boundary_resources[]`, `hardware_without_software[]`
- **Errors**: `SYSTEM_NOT_FOUND`

### `inventory_auto_seed`

Auto-create hardware inventory items from authorization boundary resources. Idempotent via BoundaryResourceId FK.

- **RBAC**: Compliance.Analyst (ISSO), Compliance.SecurityLead (ISSM)
- **RMF Step**: Implement (Phase 3)
- **Parameters**: `system_id` (required)
- **Response**: `created_count`, `items[]`
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_BOUNDARY_DATA`

---

## Implementation Roadmap Tools (Feature 031)

### `compliance_generate_roadmap`

Generate a phased implementation roadmap from a system's gap analysis data. Uses AI-driven clustering to group controls into phases based on severity, dependency chains, and control family relationships. Falls back to deterministic severity-first grouping when AI is unavailable. Historical Kanban task completion data refines effort estimates when available.

- **RBAC**: Compliance.SecurityLead (ISSM) only
- **RMF Step**: Implement (Phase 4)
- **Parameters**: `system_id` (required)
- **Preconditions**: System must have a selected baseline and at least one unmapped control (gap)
- **Response**: `roadmap_id`, `system_name`, `status` (Draft), `total_gaps`, `total_estimated_effort_days`, `total_risk_points`, `phases[]` (with items), `generation_method` (AI/Deterministic)
- **Business Rules**: Only one Active roadmap per system; generating a new roadmap archives any existing Active roadmap
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_BASELINE_SELECTED`, `NO_GAPS_FOUND`

### `compliance_get_roadmap`

Get the active implementation roadmap for a system with phases and items.

- **RBAC**: Any compliance role (read-only, no PIM tier required)
- **RMF Step**: Implement (Phase 4)
- **Parameters**: `system_id` (required), `include_items` (optional, default: true)
- **Response**: Full roadmap with phases and items (same structure as generate), or null if no active roadmap
- **Errors**: `SYSTEM_NOT_FOUND`

### `compliance_get_roadmap_progress`

Get progress metrics for a system's active roadmap including actual-vs-projected risk reduction and overdue phase detection.

- **RBAC**: Any compliance role (read-only, no PIM tier required)
- **RMF Step**: Implement (Phase 4), Monitor (Phase 7)
- **Parameters**: `system_id` (required)
- **Response**: `overall_completion_percent`, `items_completed`/`items_total`, `actual_risk_reduction`, `projected_risk_reduction`, `phases[]` (with `completion_percent`, `is_overdue`, `days_overdue`, `actual_risk_reduction_percent`), `untracked_gaps`
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_ACTIVE_ROADMAP`

### `compliance_update_roadmap`

Update a roadmap's items — move items between phases, change role assignments, update effort estimates, merge phases, or split phases. Changes propagate to linked Kanban tasks. Increments the roadmap Version counter.

- **RBAC**: Compliance.SecurityLead (ISSM) only
- **RMF Step**: Implement (Phase 4)
- **Parameters**: `system_id` (required), `move_item` (optional: `{ control_id, target_phase_order }`), `update_effort` (optional: `{ control_id, effort_days }`), `update_role` (optional: `{ control_id, assigned_role }`), `merge_phases` (optional: `{ source_phase_order, target_phase_order }`), `split_phase` (optional: `{ phase_order, split_after_item_index }`)
- **Response**: Updated roadmap (same structure as generate)
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_ACTIVE_ROADMAP`, `ITEM_NOT_FOUND`, `PHASE_NOT_FOUND`

### `compliance_create_board_from_roadmap`

Create a Kanban remediation board pre-populated from a roadmap. Maps phases to task groupings and items to tasks with effort estimates, role assignments, and dependency ordering. Establishes bi-directional sync — completing a Kanban task updates the corresponding roadmap item.

- **RBAC**: Compliance.SecurityLead (ISSM) only
- **RMF Step**: Implement (Phase 4)
- **Parameters**: `system_id` (required)
- **Preconditions**: System must have an active roadmap without an existing linked board
- **Response**: `board_id`, `board_name`, `tasks_created`, `roadmap_id`, `phases_mapped`
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_ACTIVE_ROADMAP`, `BOARD_ALREADY_EXISTS`

### `compliance_export_roadmap_pdf`

Export a roadmap as a PDF document using QuestPDF. Includes header with summary metrics, phase detail tables with control items, and paginated footer. Suitable for AO briefings and authorization package supplements.

- **RBAC**: Any compliance role (read-only, no PIM tier required)
- **RMF Step**: Implement (Phase 4), Authorize (Phase 6)
- **Parameters**: `system_id` (required)
- **Response**: `file_name`, `content_base64`, `content_type` (application/pdf)
- **Errors**: `SYSTEM_NOT_FOUND`, `NO_ACTIVE_ROADMAP`

### Troubleshooting — Implementation Roadmap Tools

**"No baseline selected" error**: The system must have a baseline selected before a roadmap can be generated. Run `compliance_select_baseline` first.

**"No gaps found" error**: All controls are fully covered — no roadmap is needed. This is a success condition, not an error.

**"Board already exists" error**: A Kanban board is already linked to this roadmap. Use the existing board or archive the current roadmap and regenerate.

### Troubleshooting — HW/SW Inventory Tools

**"Tool not found" error**: Inventory tools require Feature 025 to be deployed with `IInventoryService` registered in DI. Verify the service is registered in `ServiceCollectionExtensions.cs`.

```json
{
  "status": "error",
  "error": "Tool 'inventory_add_item' not found. Feature 025 (HW/SW Inventory) may not be deployed in this environment.",
  "resolution": "Verify IInventoryService is registered and database migration is applied."
}
```

---

## Boundary Definition Tools (Feature 033)

Tools for managing authorization boundary definitions and boundary-scoped compliance analysis.

### `compliance_list_boundary_definitions`

Lists all boundary definitions for a system.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |

### `compliance_create_boundary_definition`

Creates a new boundary definition.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `name` | string | Yes | Boundary name (unique within system) |
| `boundary_type` | string | Yes | Physical, Logical, or Hybrid |
| `description` | string | No | Boundary description |

### `compliance_delete_boundary_definition`

Deletes a boundary definition. Resources and components are reassigned to the Primary boundary.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `boundary_id` | string | Yes | Boundary definition ID |

### `compliance_boundary_gap_analysis`

Runs gap analysis with optional boundary filter. Returns boundary comparison table when no filter is applied.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | Registered system ID |
| `boundary_id` | string | No | Filter to a specific boundary (omit for all boundaries with comparison) |

### `compliance_define_boundary` (Modified)

The existing define boundary tool now supports an optional `boundary_definition_name` parameter to assign resources to a specific boundary definition.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `boundary_definition_name` | string | No | Name of boundary definition to assign resources to |

---

## POA&M Management Tools (Feature 039)

### `compliance_get_poam`

Retrieve detailed POA&M item information including milestones, history, components, and ticket sync status.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |

### `compliance_update_poam`

Update a POA&M item's fields. Enforces lifecycle transition rules.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `status` | string | No | Target status (Ongoing, Completed, Delayed, RiskAccepted) |
| `cat_severity` | string | No | Target severity (CatI, CatII, CatIII) |
| `poc` | string | No | Point of contact |
| `scheduled_completion` | string | No | Target date (ISO 8601) |
| `comment` | string | No | Comment text |
| `row_version` | string | Yes | Concurrency token |

### `compliance_close_poam`

Close a POA&M item as Completed with milestone and finding validation.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `row_version` | string | Yes | Concurrency token |

### `compliance_update_poam_milestone`

Add or update a milestone on a POA&M item.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `milestone_id` | string | No | Milestone ID (omit to create new) |
| `description` | string | Yes | Milestone description |
| `target_date` | string | Yes | Target date (ISO 8601) |
| `completed` | boolean | No | Mark as completed |

### `compliance_link_poam_component` / `compliance_unlink_poam_component`

Link or unlink system components to a POA&M item.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `component_ids` | string[] | Yes | Component IDs to link/unlink |

### `compliance_poam_by_component`

Get all POA&M items linked to a component with summary metrics.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `component_id` | string | Yes | Component ID |

### `compliance_link_poam_task` / `compliance_unlink_poam_task`

Link or unlink a remediation task to a POA&M item.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `task_id` | string | Yes (link only) | Remediation task ID |

### `compliance_create_task_from_poam`

Create a new Kanban task from a POA&M item.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `board_id` | string | Yes | Target Kanban board ID |

### `compliance_bulk_create_poam_from_findings`

Auto-generate POA&M items from scan findings with deduplication.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID |
| `finding_ids` | string[] | Yes | Finding IDs |
| `default_poc` | string | No | Default point of contact |

### `compliance_bulk_update_poam`

Bulk update multiple POA&M items.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_ids` | string[] | Yes | POA&M item IDs (1-100) |
| `status` | string | No | Target status |
| `cat_severity` | string | No | Target severity |
| `poc` | string | No | Target POC |
| `comment` | string | No | Comment for all items |

### `compliance_poam_metrics`

Get POA&M summary metrics (open, overdue, severity breakdown, avg days to close).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | No | System ID (omit for cross-system) |

### `compliance_poam_trend`

Get POA&M trend data (open over time, closure rates, aging, time-to-close).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID |
| `period` | string | No | daily, weekly, monthly (default: monthly) |
| `start_date` | string | No | Start date (ISO 8601) |
| `end_date` | string | No | End date (ISO 8601) |

### `compliance_export_poam`

Export POA&M data in specified format.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID |
| `format` | string | Yes | emass_excel, oscal_json, csv |
| `status_filter` | string | No | Filter by status |
| `severity_filter` | string | No | Filter by severity |
| `include_all` | boolean | No | Include all items ignoring filters |

### `compliance_configure_ticketing`

Configure Jira/ServiceNow integration for a system.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID |
| `provider` | string | Yes | jira or servicenow |
| `base_url` | string | Yes | Ticketing system base URL |
| `project_key` | string | Yes | Jira project key or ServiceNow table name |
| `api_key_secret` | string | Yes | Key Vault secret URI |
| `sync_enabled` | boolean | No | Enable auto-sync (default: true) |

### `compliance_sync_poam_ticket`

Sync a POA&M item with external ticketing system.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `poam_id` | string | Yes | POA&M item ID |
| `direction` | string | No | push, pull, or bidirectional |

### `compliance_bulk_sync_tickets`

Bulk sync all active POA&Ms for a system.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System ID |
| `direction` | string | No | push, pull, or bidirectional |

---

## eMASS Authorization Package Tools (Feature 041)

### `compliance_generate_package`

Generate a complete eMASS authorization package as a ZIP archive containing OSCAL SSP, POA&M, Assessment Results, Assessment Plan, SAR, and evidence. Runs readiness validation first. Returns immediately — generation runs in background.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `evidence_mode` | string | No | `embedded` (default) or `manifest_only` |

- **RBAC**: ISSM, AO
- **RMF Step**: Authorize (Step 5)

### `compliance_package_status`

Get current status of an authorization package generation job, including artifacts generated, validation results, and download link.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `package_id` | string | Yes | Package ID from `compliance_generate_package` |

- **RBAC**: ISSM, SCA, AO

### `compliance_validate_package`

Run pre-submission readiness check for an authorization package. Validates artifact presence, SSP completeness, SAR status, OSCAL schema conformance, cross-artifact consistency, and evidence coverage.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

- **RBAC**: ISSM, SCA, AO

### `compliance_list_packages`

List authorization packages for a system with pagination, sorted by most recent first.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `limit` | integer | No | Max results (default: 10) |
| `include_failed` | boolean | No | Include failed packages (default: false) |

- **RBAC**: ISSM, SCA, AO

### `compliance_validate_oscal_schema`

Validate an OSCAL JSON artifact against the NIST OSCAL 1.1.2 JSON schema. Generates the artifact for the given system, then validates.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `model` | string | Yes | `ssp`, `poam`, `assessment-results`, or `assessment-plan` |

- **RBAC**: ISSM, SCA, AO

### `compliance_generate_sar`

Generate a new Security Assessment Report auto-populated from assessment findings.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `title` | string | Yes | SAR title |

- **RBAC**: ISSM, SCA

### `compliance_edit_sar_section`

Edit a specific section of a Security Assessment Report.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sar_id` | string | Yes | SAR identifier |
| `section_type` | string | Yes | Section: ExecutiveSummary, Methodology, Findings, Recommendations, ConclusionRiskAssessment |
| `content` | string | Yes | New section content |

- **RBAC**: ISSM, SCA

### `compliance_review_sar`

Submit, approve, or reject a Security Assessment Report.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sar_id` | string | Yes | SAR identifier |
| `action` | string | Yes | `submit`, `approve`, or `reject` |
| `comments` | string | No | Review comments |

- **RBAC**: ISSM (submit), SCA (approve/reject), AO (approve)
