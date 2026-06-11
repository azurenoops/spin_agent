# Tasks — 074: Policy + Technical Narrative Split & Evidence Classification

_Epic #64 — Feature 052. Each task cites the file path(s) it touches and the relevant issue ref._

---

## Phase 1 — Data Model: Additive Columns & Enum

_Issue #64 | Priority: P1 | Unblocks all backend and export work_

- [ ] **T001**: Add `EvidenceNarrativeType` enum to `ComplianceModels.cs`
  - File: `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
  - Insert after the existing `EvidenceCategory` enum (around line 235):
    ```csharp
    /// <summary>
    /// Feature 052 (#64): which narrative half an evidence artifact supports.
    /// </summary>
    public enum EvidenceNarrativeType
    {
        /// <summary>Supports the Policy/Procedural narrative half.</summary>
        Policy = 0,
        /// <summary>Supports the Technical/Implementation narrative half.</summary>
        Technical = 1,
        /// <summary>Supports both halves equally (e.g., cross-cutting evidence).</summary>
        Combined = 2,
        /// <summary>Not yet classified — default for new uploads.</summary>
        Unclassified = 3
    }
    ```

- [ ] **T002**: Add `PolicyNarrative` and `TechnicalNarrative` to `ControlImplementation`
  - File: `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs`
  - After the existing `Narrative` property (around line 44), insert:
    ```csharp
    /// <summary>
    /// Feature 052 (#64): policy/procedural narrative half. Nullable — null = not yet authored.
    /// Do NOT rename Narrative above; this is an additive companion field.
    /// </summary>
    [MaxLength(8000)]
    public string? PolicyNarrative { get; set; }

    /// <summary>
    /// Feature 052 (#64): technical/implementation narrative half. Nullable — null = not yet authored.
    /// On migration this is back-filled from Narrative for existing rows (MigratedFromLegacy=true).
    /// </summary>
    [MaxLength(8000)]
    public string? TechnicalNarrative { get; set; }

    /// <summary>
    /// Feature 052 (#64): true when TechnicalNarrative was populated from the legacy Narrative
    /// field during the additive migration. Allows audit trail to distinguish migrated vs.
    /// freshly-authored content.
    /// </summary>
    public bool MigratedFromLegacy { get; set; }
    ```

- [ ] **T003**: Add `NarrativeType` and `AutoTagRationale` / `ManuallyTaggedBy` to `EvidenceArtifact`
  - File: `src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs`
  - After the `CollectionMethod` property, insert:
    ```csharp
    // ─── Feature 052 (#64): Narrative-half classification ────────────────────

    /// <summary>
    /// Which narrative half this artifact supports (Policy | Technical | Combined | Unclassified).
    /// Defaults to Unclassified for new uploads; migration back-fills existing rows as Combined.
    /// </summary>
    public EvidenceNarrativeType NarrativeType { get; set; } = EvidenceNarrativeType.Unclassified;

    /// <summary>
    /// Rationale written by the auto-tagging classifier (null when manually tagged).
    /// Example: "Filename matches PolicyDocumentRegex" or "Source=AzurePolicy".
    /// </summary>
    [MaxLength(500)]
    public string? AutoTagRationale { get; set; }

    /// <summary>
    /// OID of the user who manually re-tagged this artifact (null when auto-tagged or not yet tagged).
    /// Set on manual override; clears AutoTagRationale.
    /// </summary>
    [MaxLength(200)]
    public string? ManuallyTaggedBy { get; set; }
    ```

---

## Phase 2 — EF Core Migration

_Issue #64 | Priority: P1 | Depends on Phase 1_

- [ ] **T004**: Generate EF Core migration
  - Command: `dotnet ef migrations add Feature052_PolicyTechnicalNarrativeSplit --project src/Ato.Copilot.Core --startup-project src/Ato.Copilot.Mcp`
  - Expected changes in migration `Up()`:
    - `migrationBuilder.AddColumn<string>("PolicyNarrative", "ControlImplementations", nullable: true, maxLength: 8000)`
    - `migrationBuilder.AddColumn<string>("TechnicalNarrative", "ControlImplementations", nullable: true, maxLength: 8000)`
    - `migrationBuilder.AddColumn<bool>("MigratedFromLegacy", "ControlImplementations", nullable: false, defaultValue: false)`
    - `migrationBuilder.AddColumn<int>("NarrativeType", "EvidenceArtifacts", nullable: false, defaultValue: 3)` _(Unclassified)_
    - `migrationBuilder.AddColumn<string>("AutoTagRationale", "EvidenceArtifacts", nullable: true, maxLength: 500)`
    - `migrationBuilder.AddColumn<string>("ManuallyTaggedBy", "EvidenceArtifacts", nullable: true, maxLength: 200)`
  - Expected changes in migration `Down()`:
    - `migrationBuilder.DropColumn("PolicyNarrative", "ControlImplementations")`
    - `migrationBuilder.DropColumn("TechnicalNarrative", "ControlImplementations")`
    - `migrationBuilder.DropColumn("MigratedFromLegacy", "ControlImplementations")`
    - `migrationBuilder.DropColumn("NarrativeType", "EvidenceArtifacts")`
    - `migrationBuilder.DropColumn("AutoTagRationale", "EvidenceArtifacts")`
    - `migrationBuilder.DropColumn("ManuallyTaggedBy", "EvidenceArtifacts")`

- [ ] **T005**: Add back-fill SQL to migration `Up()` after column additions
  - In the migration `Up()` method, after adding columns:
    ```csharp
    // Back-fill TechnicalNarrative from legacy Narrative for all existing rows
    migrationBuilder.Sql(
        "UPDATE ControlImplementations " +
        "SET TechnicalNarrative = Narrative, MigratedFromLegacy = 1 " +
        "WHERE Narrative IS NOT NULL");

    // Back-fill EvidenceArtifacts: existing rows default to Combined (2), not Unclassified (3)
    migrationBuilder.Sql(
        "UPDATE EvidenceArtifacts SET NarrativeType = 2 WHERE NarrativeType = 3");
    ```
  - Note: The column default of `3` (Unclassified) applies only to **new** rows inserted after
    the migration; the back-fill overrides existing rows to `2` (Combined) so no evidence is lost.

---

## Phase 3 — HTTP Endpoints

_Issue #64 | Priority: P1 | Depends on Phase 2_

- [ ] **T006**: Create `NarrativeDualEndpoints.cs` route declarations
  - File: `src/Ato.Copilot.Mcp/Endpoints/NarrativeDualEndpoints.cs`
  - Namespace: `Ato.Copilot.Mcp.Endpoints`
  - Routes:
    - `GET /api/systems/{systemId}/controls/{controlId}/narrative` → `GetDualNarrativeAsync`
    - `PATCH /api/systems/{systemId}/controls/{controlId}/narrative` → `PatchDualNarrativeAsync`
  - Tag: `.WithTags("Narrative")`
  - Require auth on both; role-gate `PatchDualNarrativeAsync` by `narrativePart` (see FR-009)

- [ ] **T007**: Register `MapNarrativeDualEndpoints()` in MCP startup
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs`
    (or wherever other endpoint maps are registered — search for `MapNarrativeGovernanceEndpoints`
    to find the right file)
  - Add: `app.MapNarrativeDualEndpoints();`

- [ ] **T008**: Implement `GetDualNarrativeAsync` handler
  - Query `ControlImplementation` by `(RegisteredSystemId, ControlId)` (tenant-filtered)
  - Query `EvidenceArtifact` rows for this `ControlImplementationId`, group by `NarrativeType`
  - Return `DualNarrativeResponse` DTO (see `data-model.md §3`)
  - 404 if the `ControlImplementation` row does not exist

- [ ] **T009**: Implement `PatchDualNarrativeAsync` handler
  - Accept `PatchDualNarrativeRequest` body (`policyNarrative?: string`, `technicalNarrative?: string`)
  - Apply role gate: if `policyNarrative` is provided and caller has only `PlatformEngineer` role → 403
  - Update only the provided fields; leave the other unchanged
  - Set `ModifiedAt = DateTime.UtcNow`
  - Return updated `DualNarrativeResponse`
  - Write audit log entry (same pattern as existing narrative governance endpoints)

- [ ] **T010**: Create `PatchDualNarrativeRequest` and `DualNarrativeResponse` DTOs
  - File: `src/Ato.Copilot.Core/Models/Compliance/NarrativeDualDtos.cs`
  - See `data-model.md §3` for full field list

---

## Phase 4 — MCP Tools

_Issue #64 | Priority: P2 | Depends on Phase 3_

- [ ] **T011**: Create `NarrativePolicyTool` (MCP tool: `narrative_set_policy`)
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/NarrativePolicyTool.cs`
  - Namespace: `Ato.Copilot.Agents.Compliance.Tools`
  - Parameters: `system_id (string, required)`, `control_id (string, required)`,
    `policy_narrative (string, required)`
  - Calls `PATCH /api/systems/{id}/controls/{controlId}/narrative` with `policyNarrative` only
  - Returns success envelope with `policyNarrative` echoed back

- [ ] **T012**: Create `NarrativeTechnicalTool` (MCP tool: `narrative_set_technical`)
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeTechnicalTool.cs`
  - Parameters: `system_id`, `control_id`, `technical_narrative`
  - Calls PATCH with `technicalNarrative` only

- [ ] **T013**: Create `EvidenceClassifyTool` (MCP tool: `evidence_classify`)
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/EvidenceClassifyTool.cs`
  - Parameters: `evidence_artifact_id (string, required)`,
    `narrative_type (string, required — "Policy"|"Technical"|"Combined"|"Unclassified")`,
    `rationale (string, optional)`
  - Calls `PATCH /api/evidence/{id}/classify` (new endpoint in T014)
  - Returns updated artifact summary

- [ ] **T014**: Create `EvidenceClassifyEndpoint` for evidence reclassification
  - File: `src/Ato.Copilot.Mcp/Endpoints/EvidenceClassifyEndpoint.cs`
  - Route: `PATCH /api/evidence/{artifactId}/classify`
  - Body: `{ narrativeType: int, rationale?: string }`
  - Sets `NarrativeType`, clears `AutoTagRationale`, sets `ManuallyTaggedBy = caller OID`
  - Register in startup alongside T007

---

## Phase 5 — Auto-Tagger Service

_Issue #64 | Priority: P3 | Depends on Phase 2_

- [ ] **T015**: Create `EvidenceNarrativeClassifier` service
  - File: `src/Ato.Copilot.Core/Services/Compliance/EvidenceNarrativeClassifier.cs`
  - Interface: `IEvidenceNarrativeClassifier` in `src/Ato.Copilot.Core/Interfaces/Compliance/`
  - Method: `ClassifyAsync(EvidenceArtifact artifact) : Task<(EvidenceNarrativeType type, string rationale)>`
  - Logic:
    1. Source-based rules first (highest priority): `AzurePolicy`, `Defender`, `SCAP`, `ACAS`,
       `PrismaCloud`, `NessusACASScan`, `AwsSecurityHub`, `GcpScc` → `Technical`
    2. Filename regex match: `*Policy*|*Procedure*|*SOP*|*Plan*|*Charter*|*Standard*` → `Policy`
    3. Fallback: `Combined` with `rationale = "LowConfidence"`

- [ ] **T016**: Create one-time migration CLI command or background job to bulk-tag existing artifacts
  - File: `src/Ato.Copilot.Core/Services/Compliance/EvidenceNarrativeBulkClassifierJob.cs`
  - Reads all `EvidenceArtifact` rows where `NarrativeType = Combined` (back-filled from migration)
    and `ManuallyTaggedBy IS NULL`
  - Calls `IEvidenceNarrativeClassifier.ClassifyAsync` per row and saves result
  - Writes summary log: "X tagged Policy, Y Technical, Z Combined (low-confidence)"
  - Should be idempotent — safe to re-run

---

## Phase 6 — SSP Export Integration

_Issue #64 | Priority: P1 | Depends on Phase 2_

- [ ] **T017**: Update OSCAL SSP export to emit both narrative halves
  - File: search for the OSCAL export service (likely in
    `src/Ato.Copilot.Core/Services/Compliance/` or `src/Ato.Copilot.Mcp/`)
  - For each `implemented-requirement`, emit two `statement` entries:
    - `statement-id: "{controlId}_smt.policy"`, `description: PolicyNarrative ?? "[Not Authored]"`
    - `statement-id: "{controlId}_smt.technical"`, `description: TechnicalNarrative ?? "[Not Authored]"`
  - The old `Narrative`/`legacyNarrative` field is NOT emitted in new exports to avoid duplication

- [ ] **T018**: Update DOCX/PDF SSP export to render two labeled subsections
  - File: search for QuestPDF/DOCX export service (spec 037 — `ssp-document-export`)
  - Per control, insert:
    ```
    **Implementation Statement (Policy):**
    {PolicyNarrative ?? configuredPlaceholder}

    **Implementation Statement (Technical):**
    {TechnicalNarrative ?? configuredPlaceholder}
    ```
  - Order: Policy always first, Technical second
  - Read placeholder from `appsettings.json` `Narrative:ExportPlaceholder` (default: `[Not Authored]`)

---

## Phase 7 — Tests

_Issue #64 | Priority: P1_

- [ ] **T019**: Integration test — migration adds nullable columns without breaking existing rows
  - File: `tests/Ato.Copilot.Tests.Integration/Compliance/Feature052MigrationTests.cs`

- [ ] **T020**: Integration test — PATCH dual narrative, GET reflects changes, role gate 403
  - File: `tests/Ato.Copilot.Tests.Integration/Compliance/DualNarrativeEndpointTests.cs`

- [ ] **T021**: Unit test — `EvidenceNarrativeClassifier` classifies known filenames and sources
  - File: `tests/Ato.Copilot.Tests.Unit/Compliance/EvidenceNarrativeClassifierTests.cs`

- [ ] **T022**: Integration test — OSCAL export emits `_smt.policy` + `_smt.technical` statement IDs
  - File: `tests/Ato.Copilot.Tests.Integration/Compliance/Feature052OscalExportTests.cs`
