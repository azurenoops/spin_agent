# Tasks — 074: Policy + Technical Narrative Split & Evidence Classification

_Implementation issue #892 (Epic #64 — Feature 052). Each task cites the file path(s) it touches and the relevant issue ref._

> **Repository alignment:** Additive database changes use the current idempotent SQL Server/SQLite
> `EnsureSchemaAdditions` convention. The legacy narrative backfill and evidence classification
> backfill must be safe to rerun; no conventional EF migration is generated for this feature.
>
> **Verified cross-feature constraints:** `EvidenceArtifact` has no source-provider field, so the
> deterministic classifier uses artifact category as the available technical-source signal plus the
> required filename rules. Feature 024 stores a single undifferentiated `NarrativeVersion.Content`
> stream, and Feature 044 exposes no `OrgDefaultUpdated` event. Per-half history and event-driven
> staleness therefore require follow-up contracts instead of speculative schema or event creation here.

---

## Requirement coverage continuation

### AI first-pass follow-up (October 5)

- [x] RF01 — Document readable parameters, scoped source-based AI and human
  review/provenance requirements before production changes.
- [x] RF02 — Extend existing generator/prompt with typed statement/parameter
  suggestions, source validation and conservative extractive assignments.
- [x] RF03 — Capture current authorized source context, protect generation
  proof, recheck authority/staleness and preserve assistance in existing JSON
  snapshots/native output without approval or baseline mutation.
- [x] RF04 — Replace raw insert wording/ID-first inputs, preview first-pass
  basis/questions, fill empty fields only and preserve manual/other-half edits.
- [x] RF05 — Complete API/native/browser/build/coverage and runtime/manual
  acceptance while preserving staged overview work and saved demo approvals.

RF05 evidence: 113 targeted backend/generator/coverage/native-export tests and
53 authenticated narrative/overview API tests passed, including real protected-
proof save, viewer/foreign scope rejection and unchanged unreviewed status.
31 Dashboard tests and 8 desktop/mobile narrative cases passed; type-check,
solution and production builds passed with existing warnings. First-pass
capture/proof executable lines 92.44%, generator 91.49%, native projection
100%; focused presentation 98.02% lines / 89.34% branches.

Matching API image is healthy on 4196 with unchanged normalized runtime
configuration/volumes/ports/user and peer containers, preserving the previously
running overview implementation. Docker used its existing package-only offline
feed path after NuGet TLS/EOF failure; no dependency/TLS policy changed.
Real PT-2 automatic generation returned HTTP 200, 5 scoped sources, 2 response
suggestions, 0 guessed parameter assignments and 5 questions. Narrative version
1 and saved/approved content remained unchanged; no save/review request or
page error occurred. Separate staged overview work was not staged/committed
by this task. Manual acceptance steps are in the existing capability guide.

### Hierarchical table correction

- [x] HT001: Add failing tests for parent-first groups, indentation, child-only
  parent context, catalog-only relationships and preserved pagination/navigation.
- [x] HT002: Implement the grouped table and accessible context links without
  changing independent statement states or system/workspace scope.
- [x] HT003: Run focused tests, Dashboard type/build checks and browser checks;
  expose the corrected table on port 5197 for user manual acceptance.

### Automatic catalog association

- [x] AB001: Record baseline framework identity and migrate the verified legacy
  NIST baseline contract without inferring relationships from identifier syntax.
- [x] AB002: Automatically retain available source snapshots at startup and after
  baseline/source changes, with tenant isolation, idempotency and diagnostics.
- [x] AB003: Preserve binding history during baseline reselection and remove
  catalog/rationale prompts from the narrative UI.
- [x] AB004: Test lifecycle/data-preservation behavior, validate builds/types,
  deploy locally and verify AC-11 requirements and parent links without user setup.

The large-source follow-up replaces buffered SQL JSON materialization with
scoped sequential streaming. Live list/detail/coverage requests are now measured
at 174-216 ms, and both AC-11 requirements and AC-11(1) navigation are visible.
See [the implementation report](implementation-report.md) for exact verification.

### Catalog administration repair

- [x] CA001: Enforce platform catalog authority on all import/capture mutations
  and expose permission/source status to the frontend.
- [x] CA002: Add non-destructive source-only capture and administrator-triggered
  missing-source backfill with explicit partial-failure reporting.
- [x] CA003: Keep catalog administration actions available for existing catalogs;
  replace the narrative dead end with an actionable administrator-refresh link.
- [x] CA004: Run authorization, data-preservation, UI and type-check tests, update
  the local preview, and verify the live workflow without approving a system
  binding on the user's behalf.

Catalog administration validation: full solution build passed; 7,959 unit and
1,763 integration tests passed (20 existing skips). Focused Dashboard checks
passed 33 tests, strict type checking, production build and six browser cases.
The local `catalog-admin-20260930-e98474e9` API is healthy. Live ordinary-workspace
source status returned 200 with mutations unavailable, and an attempted source
mutation correctly returned 403 `CATALOG_ADMIN_REQUIRED`. The live narrative and
baseline fingerprints remain unchanged. The signed-in organization user is not
a platform administrator; source backfill still requires that authorized login.

Design approved against main #1050. Local implementation is present; validation
and manual acceptance are tracked separately below. The exact proposed feature and four story issues were shown
to the user; the approval tool reported the user unavailable. No external writes
were made. Task RC001 is blocked on explicit approval, not implicitly approved
by acceptance of the implementation plan.

- [ ] RC001: Obtain approval for the previewed GitHub feature/story writes,
  verify canonical repository and parent/sub-issue links, and synchronize task
  checklists.   Release traceability depends on this gate; local implementation continues at
    the user's subsequent direction without external writes.
- [x] RC002 (local documentation gate): Write failing catalog/binding tests; implement immutable
  source identity, recursive requirements/parameters and per-baseline binding.
- [x] RC003 (RC002): Write failing mapping/history/evidence tests; implement
  explicit Policy/Technical mappings and governed reviewed snapshots.
- [x] RC004 (RC002): Write failing duplicate/concurrency/fault tests; implement
  rationale-required enhancement proposals and atomic reviewed selection plus
  separate Draft implementation creation.
- [x] RC005 (RC003, RC004): Write failing authenticated HTTP scope/permission
  tests; extend workspace reads and coverage/proposal operations.
- [x] RC006 (RC005): Write failing Dashboard API/component/browser tests;
  preserve the existing UI while adding relationships, coverage and proposals.
- [x] RC007 (RC003, RC004): Write failing document-content and catalog-reference
  tests; extend SSP/eMASS preparation, approved projections and readiness hashes.
- [x] RC008 (RC006, RC007): Run full solution build/test, actual touched-project
  TS checks, coverage, SQLite/SQL Server/RLS and browser acceptance. Record
  failures/warnings explicitly; do not suppress baseline failures.
- [ ] RC009 (RC008): Deliver local AC-11/AC-11(1) viewer/author/reviewer
  walkthrough, compliance/architecture/results report, rollback and limitations.
  Await user manual acceptance before claiming completion.

See [the contract](contracts/requirement-coverage.md) for verified gaps,
acceptance cases and the confirmed review/export boundaries.

Provider-specific validation caught and corrected invalid SQL Server Unicode
draft column widths and missing execution-strategy transaction wrapping.
Transient retries now reload persisted state before replay; a real SQL Server
fault-injection test verifies no duplicate proposal insert. Source/display alias
tests also verify that navigation, selection and generated narrative content
resolve through the catalog without creating duplicate implementation records.
Returned proposal notes remain visible, and accepted manual Technical drafts
do not retain AI-generated flags from an older narrative.

The final report must distinguish full-suite outcomes from focused checks,
existing warnings/unconfigured lint tooling, external traceability approval and
real-user manual acceptance. Synthetic browser tests do not close RC009.

See [the implementation report](implementation-report.md) for the final full
suite counts, scoped coverage, warning/lint limitations, rollback guidance and
the manual acceptance handoff. RC001 and RC009 remain open.

---

## #1001 workspace-backend continuation

- [x] Add failing tests for semantic freshness, component/boundary scope and
  declared inheritance; preserve unknown execution observations.
- [x] Implement revision-checked draft mapping/scope editing and private-draft
  write isolation; preserve immutable published revisions.
- [x] Preserve approved companion snapshots during typed acceptance and append
  governance review audit records.
- [x] Add persistent change-impact queue/generation entry points and failure
  states; keep model calls outside source mutation transactions.
- [x] Persist immutable per-target delivery receipts and source-change context,
  including removal provenance and no-change deliveries; verify shared-context
  transaction rollback, replay after review, and receipt schema upgrade.
- [x] Expose authorized paginated receipt history, retaining proposal creation
  origin separately from later source deliveries and preserving unknown legacy
  actor/source fields.
- [x] Add SQLite schema/rerun and concurrency checks, authenticated HTTP tests,
  mocked-model failures, and UTF-8/BOM/ambiguous-header parser regressions.
- [x] Use workspace access decisions and distinguish reference-author from
  narrative-generation permission.
- [ ] Parent: register `INarrativeChangeImpactService` to the scoped proposal
  service; wire source mutation events and durable generation/retry dispatch.
- [ ] Source owners: replace/gate legacy organization capability and component
  narrative cascades plus in-place regeneration paths identified in
  [the source-writer handoff](issue-1001.md#mandatory-legacy-source-writer-handoff).
  The component post-save/separate-context cascade is not an atomic delivery point.
- [x] Implement provider/organization standalone reference storage and API
  surfaces without fabricated systems, plus applicable published provider-reference
  consumption and durable source publication outboxes.
- [ ] Parent: register the provider reference entity, service and standalone
  endpoint mapper; consume source publication outboxes with authorized target
  dispatch before enabling provider grounding in the production context.
- [ ] Complete endpoint authorization handoff, frontend contract consumption,
  and upstream layout merge before browser acceptance.
- [ ] Verify SQL Server/RLS, complete required modified-path coverage, and
  offer interactive local manual acceptance before feature completion.

---

## Phase 1 — Data Model: Additive Columns & Enum

_Issue #892 | Priority: P1 | Unblocks all backend and export work_

- [x] **T001**: Add `EvidenceNarrativeType` enum to `ComplianceModels.cs`
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

- [x] **T002**: Add `PolicyNarrative` and `TechnicalNarrative` to `ControlImplementation`
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

- [x] **T003**: Add `NarrativeType` and `AutoTagRationale` / `ManuallyTaggedBy` to `EvidenceArtifact`
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

_Issue #892 | Priority: P1 | Depends on Phase 1_

- [x] **T004**: Add provider-aware, idempotent schema additions
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

- [x] **T005**: Add rerunnable legacy back-fill SQL
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

_Issue #892 | Priority: P1 | Depends on Phase 2_

- [x] **T006**: Create `NarrativeDualEndpoints.cs` route declarations
  - File: `src/Ato.Copilot.Mcp/Endpoints/NarrativeDualEndpoints.cs`
  - Namespace: `Ato.Copilot.Mcp.Endpoints`
  - Routes:
    - `GET /api/systems/{systemId}/controls/{controlId}/narrative` → `GetDualNarrativeAsync`
    - `PATCH /api/systems/{systemId}/controls/{controlId}/narrative` → `PatchDualNarrativeAsync`
  - Tag: `.WithTags("Narrative")`
  - Require auth on both; role-gate `PatchDualNarrativeAsync` by `narrativePart` (see FR-009)

- [x] **T007**: Register `MapNarrativeDualEndpoints()` in MCP startup
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs`
    (or wherever other endpoint maps are registered — search for `MapNarrativeGovernanceEndpoints`
    to find the right file)
  - Add: `app.MapNarrativeDualEndpoints();`

- [x] **T008**: Implement `GetDualNarrativeAsync` handler
  - Query `ControlImplementation` by `(RegisteredSystemId, ControlId)` (tenant-filtered)
  - Query `EvidenceArtifact` rows for this `ControlImplementationId`, group by `NarrativeType`
  - Return `DualNarrativeResponse` DTO (see `data-model.md §3`)
  - 404 if the `ControlImplementation` row does not exist

- [x] **T009**: Implement `PatchDualNarrativeAsync` handler
  - Accept `PatchDualNarrativeRequest` body (`policyNarrative?: string`, `technicalNarrative?: string`)
  - Apply role gate: if `policyNarrative` is provided and caller has only `PlatformEngineer` role → 403
  - Update only the provided fields; leave the other unchanged
  - Set `ModifiedAt = DateTime.UtcNow`
  - Return updated `DualNarrativeResponse`
  - Write audit log entry (same pattern as existing narrative governance endpoints)

- [x] **T010**: Create `PatchDualNarrativeRequest` and `DualNarrativeResponse` DTOs
  - File: `src/Ato.Copilot.Core/Models/Compliance/NarrativeDualDtos.cs`
  - See `data-model.md §3` for full field list

---

## Phase 4 — MCP Tools

_Issue #892 | Priority: P2 | Depends on Phase 3_

- [x] **T011**: Create `NarrativePolicyTool` (MCP tool: `narrative_set_policy`)
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/NarrativePolicyTool.cs`
  - Namespace: `Ato.Copilot.Agents.Compliance.Tools`
  - Parameters: `system_id (string, required)`, `control_id (string, required)`,
    `policy_narrative (string, required)`
  - Calls `PATCH /api/systems/{id}/controls/{controlId}/narrative` with `policyNarrative` only
  - Returns success envelope with `policyNarrative` echoed back

- [x] **T012**: Create `NarrativeTechnicalTool` (MCP tool: `narrative_set_technical`)
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeTechnicalTool.cs`
  - Parameters: `system_id`, `control_id`, `technical_narrative`
  - Calls PATCH with `technicalNarrative` only

- [x] **T013**: Create `EvidenceClassifyTool` (MCP tool: `evidence_classify`)
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/EvidenceClassifyTool.cs`
  - Parameters: `evidence_artifact_id (string, required)`,
    `narrative_type (string, required — "Policy"|"Technical"|"Combined"|"Unclassified")`,
    `rationale (string, optional)`
  - Calls `PATCH /api/evidence/{id}/classify` (new endpoint in T014)
  - Returns updated artifact summary

- [x] **T014**: Create `EvidenceClassifyEndpoint` for evidence reclassification
  - File: `src/Ato.Copilot.Mcp/Endpoints/EvidenceClassifyEndpoint.cs`
  - Route: `PATCH /api/evidence/{artifactId}/classify`
  - Body: `{ narrativeType: int, rationale?: string }`
  - Sets `NarrativeType`, clears `AutoTagRationale`, sets `ManuallyTaggedBy = caller OID`
  - Register in startup alongside T007

---

## Phase 5 — Auto-Tagger Service

_Issue #892 | Priority: P3 | Depends on Phase 2_

- [x] **T015**: Create `EvidenceNarrativeClassifier` service
  - File: `src/Ato.Copilot.Core/Services/Compliance/EvidenceNarrativeClassifier.cs`
  - Interface: `IEvidenceNarrativeClassifier` in `src/Ato.Copilot.Core/Interfaces/Compliance/`
  - Method: `ClassifyAsync(EvidenceArtifact artifact) : Task<(EvidenceNarrativeType type, string rationale)>`
  - Logic:
    1. Source-based rules first (highest priority): `AzurePolicy`, `Defender`, `SCAP`, `ACAS`,
       `PrismaCloud`, `NessusACASScan`, `AwsSecurityHub`, `GcpScc` → `Technical`
    2. Filename regex match: `*Policy*|*Procedure*|*SOP*|*Plan*|*Charter*|*Standard*` → `Policy`
    3. Fallback: `Combined` with `rationale = "LowConfidence"`

- [x] **T016**: Create one-time migration CLI command or background job to bulk-tag existing artifacts
  - File: `src/Ato.Copilot.Core/Services/Compliance/EvidenceNarrativeBulkClassifierJob.cs`
  - Reads all `EvidenceArtifact` rows where `NarrativeType = Combined` (back-filled from migration)
    and `ManuallyTaggedBy IS NULL`
  - Calls `IEvidenceNarrativeClassifier.ClassifyAsync` per row and saves result
  - Writes summary log: "X tagged Policy, Y Technical, Z Combined (low-confidence)"
  - Should be idempotent — safe to re-run

---

## Phase 6 — SSP Export Integration

_Issue #892 | Priority: P1 | Depends on Phase 2_

- [x] **T017**: Update OSCAL SSP export to emit both narrative halves
  - File: search for the OSCAL export service (likely in
    `src/Ato.Copilot.Core/Services/Compliance/` or `src/Ato.Copilot.Mcp/`)
  - For each `implemented-requirement`, emit two `statement` entries:
    - `statement-id: "{controlId}_smt.policy"`, `description: PolicyNarrative ?? "[Not Authored]"`
    - `statement-id: "{controlId}_smt.technical"`, `description: TechnicalNarrative ?? "[Not Authored]"`
  - The old `Narrative`/`legacyNarrative` field is NOT emitted in new exports to avoid duplication

- [x] **T018**: Update DOCX/PDF SSP export to render two labeled subsections
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

_Issue #892 | Priority: P1_

- [x] **T019**: Integration test — migration adds nullable columns without breaking existing rows
  - File: `tests/Ato.Copilot.Tests.Integration/Compliance/Feature052MigrationTests.cs`

- [x] **T020**: Integration test — PATCH dual narrative, GET reflects changes, role gate 403
  - File: `tests/Ato.Copilot.Tests.Integration/Compliance/DualNarrativeEndpointTests.cs`

- [x] **T021**: Unit test — `EvidenceNarrativeClassifier` classifies known filenames and sources
  - File: `tests/Ato.Copilot.Tests.Unit/Compliance/EvidenceNarrativeClassifierTests.cs`

- [x] **T022**: Integration test — OSCAL export emits `_smt.policy` + `_smt.technical` statement IDs
  - File: `tests/Ato.Copilot.Tests.Integration/Compliance/Feature052OscalExportTests.cs`

---

## Phase 8 — Issue #960: Canonical Narrative Persistence

- [x] **T023**: Add regression tests for post-startup baseline creation, governed combined writes,
  single/bulk capability regeneration, and supported imports persisting Technical narrative content
  without overwriting Policy narrative content.
- [x] **T024**: Synchronize combined/legacy-compatible writers to `TechnicalNarrative` while retaining
  `Narrative` only where compatibility consumers still require it; keep explicit dual writes independent.
- [x] **T025**: Update readiness/completeness checks and document exporters to use the canonical dual
  contract, including safe legacy-only startup repair coverage.
- [x] **T026**: Update dashboard regeneration state under the Technical editor key and add refresh/error
  regression coverage.
- [x] **T027**: Run focused unit, integration, dashboard, and browser E2E validation plus the affected
  broad suites.
  - Full backend unit: 5,664 passed.
  - Full backend integration: 824 passed, 40 skipped.
  - Dashboard production build: passed.
  - Focused Narratives Vitest: 2 passed; focused Playwright: 1 passed.
  - Full dashboard Vitest run: 441 passed, 13 failures in untouched auth, chat attachment,
    chat input, and system registration tests; the focused Narratives tests pass.
