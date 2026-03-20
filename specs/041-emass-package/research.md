# Research: eMASS Authorization Package Export

**Feature**: 041-emass-package | **Date**: 2026-03-19

## R1: OSCAL 1.1.2 Schema Differences from 1.0.6

**Context**: Existing `BuildOscalAssessmentResults()` and `BuildOscalPoam()` in `EmassExportService` produce OSCAL 1.0.6. Must upgrade to 1.1.2 for consistency with SSP export.

**Decision**: Upgrade both builders in-place to emit OSCAL 1.1.2 structures.

**Rationale**:
- OSCAL 1.1.2 is the current stable NIST release and what eMASS targets.
- The SSP export (`OscalSspExportService`) already emits 1.1.2; inconsistent versions cause eMASS rejection.
- Key 1.1.2 changes for assessment-results: `results[]` items require `reviewed-controls` with `control-selections`; findings require `target.type = "objective-id"` and `target.status.state` (same as 1.0.6 but stricter validation).
- Key 1.1.2 changes for POA&M: `poam-items[]` become top-level array under `plan-of-action-and-milestones`; each item MUST have `related-findings` (replacing `related-observations`); metadata MUST include `oscal-version: "1.1.2"`.
- The OSCAL SAP (assessment-plan) model in 1.1.2 includes `reviewed-controls`, `assessment-subjects`, `assessment-activities`, and `tasks` — all mappable from existing `SapControlEntry` and `SapTeamMember` entities.

**Alternatives Considered**:
- Maintain 1.0.6 alongside 1.1.2: Rejected — doubles maintenance burden, eMASS requires consistent version.
- Use OSCAL 1.0.4: Rejected — deprecated, not accepted by current eMASS import.

---

## R2: JSON Schema Validation in .NET

**Context**: FR-023 requires validating OSCAL artifacts against official NIST JSON schemas, not just structural checks. Need a .NET library for JSON Schema validation.

**Decision**: Use `JsonSchema.Net` (json-everything) NuGet package for JSON Schema Draft 2020-12 validation.

**Rationale**:
- `JsonSchema.Net` by Greg Dennis is the most actively maintained .NET JSON Schema library (supports Draft 2020-12, 2019-09, Draft 7).
- NIST OSCAL 1.1.2 JSON schemas use JSON Schema Draft 2020-12 (the `$schema` value in NIST-published schemas).
- The library works with `System.Text.Json` (already used throughout the codebase), avoiding a Jackson/Newtonsoft dependency.
- Schema files (~50 KB each for SSP, POA&M, AR, SAP) can be bundled as embedded resources in `Ato.Copilot.Core` — no network access required (air-gapped compatible).
- Validation returns detailed error paths (JSON Pointer) matching FR-024 requirement.

**Alternatives Considered**:
- `NJsonSchema`: Rejected — primarily OpenAPI-focused, JSON Schema Draft 2020-12 support is incomplete.
- `Newtonsoft.Json.Schema`: Rejected — commercial license required for production use; adds Newtonsoft.Json dependency.
- Manual structural validation (extend current `OscalValidationService`): Rejected — replicating full JSON Schema semantics is error-prone and unmaintainable.

**Implementation Notes**:
- Bundle four schema files: `oscal_ssp_schema.json`, `oscal_poam_schema.json`, `oscal_assessment-results_schema.json`, `oscal_assessment-plan_schema.json`.
- Schema files sourced from https://github.com/usnistgov/OSCAL/releases (versioned with OSCAL 1.1.2 release tag).
- Register schemas at startup using `SchemaRegistry` for `$ref` resolution between schema files.

---

## R3: SAR Document Structure (NIST SP 800-37)

**Context**: FR-007–FR-010 require a Security Assessment Report. No existing SAR model or export exists. Need to define the required structure per NIST guidance.

**Decision**: Model SAR as a multi-section document with editable narratives and auto-populated findings data, following NIST SP 800-37 Rev 2 guidance.

**Rationale**:
- NIST SP 800-37 Rev 2, Step 4 (Assess) specifies the SAR must contain:
  1. Executive Summary — overall risk posture narrative
  2. Assessment Scope & Methodology — what was assessed, how, assessment team
  3. Findings Summary — aggregate view by severity, by control family
  4. Individual Finding Details — per-control assessment results with risk ratings
  5. Recommendations — assessor guidance for remediation
- Sections 1, 2, and 5 are narrative (user-editable). Sections 3 and 4 are auto-generated from `ControlEffectivenessRecord` and `ComplianceFinding` data.
- eMASS accepts the SAR as a Word document attachment (not OSCAL) — confirmed by eMASS import documentation.
- The four-state lifecycle (NotStarted → Draft → UnderReview → Approved) matches the existing SSP section lifecycle (`SspSectionStatus` enum) per spec clarifications.

**Alternatives Considered**:
- OSCAL SAR format: Rejected — eMASS does not currently support OSCAL assessment-results as a SAR import. The OSCAL Assessment Results artifact serves a different purpose (machine-readable findings), while the SAR is a narrative report for the AO.
- PDF-only SAR: Rejected — Word format allows AO to annotate and comment before signing. PDF generation can be added later as an enhancement.

---

## R4: ZIP Archive Generation Pattern

**Context**: FR-001 requires a ZIP archive. Need a reliable .NET approach for atomic ZIP generation.

**Decision**: Use `System.IO.Compression.ZipArchive` with a `MemoryStream` for atomic assembly, then persist to file storage only on success.

**Rationale**:
- `System.IO.Compression` is part of the .NET BCL — zero external dependencies.
- Writing to `MemoryStream` first ensures atomic behavior: if any artifact fails, no partial ZIP is written to disk (FR-004a).
- For packages exceeding reasonable memory (>200 MB with evidence), switch to file-backed `FileStream` with cleanup on failure.
- The existing `SspExportService.ProcessExportAsync()` pattern writes to `ExportSettings.ExportsPath` — the package service will write to a parallel `ExportSettings.PackagesPath`.

**Alternatives Considered**:
- SharpZipLib: Rejected — external dependency not needed, BCL `ZipArchive` is sufficient.
- Stream directly to disk ZIP: Rejected — cannot guarantee atomic failure if artifact generation fails mid-stream.

---

## R5: OSCAL SAP (Assessment Plan) Export Mapping

**Context**: FR-011–FR-012 require converting the existing markdown SAP (Feature 018) to OSCAL assessment-plan JSON format.

**Decision**: Map from `SecurityAssessmentPlan` + `SapControlEntry` + `SapTeamMember` entities to OSCAL 1.1.2 `assessment-plan` model.

**Rationale**:
- The OSCAL assessment-plan model requires:
  - `metadata` — title, last-modified, version, oscal-version
  - `import-ssp` — href reference to the system SSP
  - `reviewed-controls` — control selections (maps from `SapControlEntry.ControlId` list)
  - `assessment-subjects` — systems/components under assessment (RegisteredSystem + SystemComponents)
  - `assessment-activities` — one per assessment method (Test/Interview/Examine from `SapControlEntry.AssessmentMethods`)
  - `tasks` — schedule items (maps from `ScheduleStart`/`ScheduleEnd`)
  - `responsible-parties` — team members in OSCAL party format (maps from `SapTeamMember`)
- All source data is already captured in the SAP model from Feature 018.
- The SAP entity has `SapStatus.Finalized` — for package purposes, Finalized SAPs can be exported.

**Mapping Table**:

| OSCAL Element | Source Entity/Field |
|---|---|
| `metadata.title` | `SecurityAssessmentPlan.Title` |
| `metadata.oscal-version` | `"1.1.2"` (hardcoded) |
| `import-ssp.href` | Generated SSP UUID reference |
| `reviewed-controls.control-selections[].include-controls` | `SapControlEntry.ControlId[]` |
| `assessment-activities[].props[name=method]` | `SapControlEntry.AssessmentMethods` |
| `tasks[].timing.within-date-range` | `ScheduleStart` / `ScheduleEnd` |
| `responsible-parties` | `SapTeamMember.Name` + `Role` |

**Alternatives Considered**:
- Parse SAP Markdown content into OSCAL: Rejected — unreliable; structured entity data provides deterministic mapping.

---

## R6: Evidence Manifest Structure

**Context**: FR-016–FR-019 require integrating the evidence repository into the package. Need a manifest format.

**Decision**: Generate an `evidence-manifest.json` file in the package root that maps evidence artifacts to controls, with optional file embedding.

**Rationale**:
- The manifest provides a machine-readable index for eMASS reviewers and automated processing.
- Each manifest entry maps to an `EvidenceArtifact` with fields: `artifactId`, `fileName`, `controlId`, `category`, `collectionMethod`, `uploadedAt`, `contentHash`, `fileSizeBytes`, and `storagePath` (within the ZIP or as a link).
- Evidence files are stored under `evidence/` directory within the ZIP when embedded.
- When total evidence exceeds 100 MB, the manifest includes download URLs instead of embedded files (per FR-018/spec clarification).
- The `IEvidenceArtifactService.ListForSystemAsync()` and `IFileStorageProvider` already provide all data needed.

**Manifest Schema**:
```json
{
  "generatedAt": "2026-03-19T00:00:00Z",
  "systemId": "...",
  "totalArtifacts": 12,
  "totalSizeBytes": 45000000,
  "embeddingMode": "embedded|manifest-only",
  "artifacts": [
    {
      "artifactId": "...",
      "fileName": "scan-results.pdf", 
      "controlId": "AC-2",
      "category": "ScanResult",
      "collectionMethod": "AutomatedScan",
      "uploadedAt": "2026-03-15T10:30:00Z",
      "contentHash": "sha256:...",
      "fileSizeBytes": 1234567,
      "path": "evidence/ac-2/scan-results.pdf"
    }
  ]
}
```

**Alternatives Considered**:
- CSV manifest: Rejected — JSON is more structured and parseable; consistent with OSCAL JSON.
- OSCAL back-matter resources: Rejected — evidence references in OSCAL back-matter would bloat individual artifact files; a separate manifest is cleaner.

---

## R7: Background Job Pattern for Package Generation

**Context**: FR-004 requires background job processing consistent with Feature 037.

**Decision**: Reuse the `Channel<T>` + `BackgroundService` pattern from SSP export (Feature 037) with a dedicated `PackageExportJob` and `PackageBackgroundService`.

**Rationale**:
- The `SspExportBackgroundService` pattern is proven: bounded channel (100 items), sequential processing, error isolation (log + continue), SignalR notifications.
- Package generation is heavier than SSP export (multiple artifact generation + ZIP assembly) but the same pattern applies.
- SignalR notifications provide real-time progress: `PackageGenerating`, `PackageArtifactComplete(artifactType)`, `PackageValidating`, `PackageComplete`, `PackageFailed`.
- The `ISspExportNotifier` interface pattern will be replicated as `IPackageExportNotifier`.

**Implementation**:
- `Channel<PackageExportJob>` registered as singleton (bounded, capacity 20 — fewer than SSP since packages are heavier).
- `PackageBackgroundService : BackgroundService` consumes from channel.
- `IAuthorizationPackageService.EnqueuePackageAsync()` writes to channel.
- Progress tracked via `AuthorizationPackage.Status`: Pending → Generating → Validating → Completed | Failed.

**Alternatives Considered**:
- Hangfire/Quartz scheduler: Rejected — external dependency, existing Channel pattern is simpler and sufficient.
- Inline generation (no background job): Rejected — package generation exceeds 2 seconds, violates UX consistency principle.

---

## R8: SAR Word Document Generation

**Context**: The SAR must be exported as a Word document for eMASS upload (not OSCAL).

**Decision**: Use `DocumentFormat.OpenXml` (already a transitive dependency via ClosedXML or explicit reference) to generate the SAR as a `.docx` file.

**Rationale**:
- The existing SSP Word export in `SspExportService.ProcessExportAsync()` already uses Open XML SDK for .docx generation — consistent pattern.
- SAR requires formatted sections: title page, table of contents, executive summary, findings tables, recommendations.
- Word format allows AO annotation before authorization decision.
- No additional NuGet dependency if already referenced (check: ClosedXML depends on Open XML SDK).

**Alternatives Considered**:
- Markdown-to-DOCX conversion (Pandoc): Rejected — external process dependency, harder to control formatting.
- HTML-to-PDF: Rejected — eMASS expects Word for annotation workflow.

---

## R9: Cross-Artifact UUID Consistency

**Context**: FR-006 requires consistent UUID cross-references across OSCAL artifacts in a package.

**Decision**: Generate a shared UUID registry at the start of package assembly and inject it into all OSCAL builders.

**Rationale**:
- OSCAL artifacts cross-reference each other via UUIDs: the Assessment Results `import-ap` references the SAP UUID; the SAP `import-ssp` references the SSP UUID; POA&M findings reference Assessment Results UUIDs.
- Currently, each builder generates random UUIDs independently — these won't match across artifacts.
- A `PackageUuidRegistry` (simple dictionary) created at package start provides stable UUIDs that all builders reference.
- The SSP builder already generates component UUIDs deterministically from entity IDs (Feature 022) — extend this pattern.

**Implementation**:
- `PackageUuidRegistry` contains: `SspUuid`, `SapUuid`, `AssessmentResultsUuid`, `PoamUuid`, plus component and party UUID mappings.
- Generated once per package, passed to each OSCAL builder method.
- UUIDs are deterministic within a package but unique across packages (seeded from package ID + entity ID).

**Alternatives Considered**:
- Post-process UUID replacement: Rejected — fragile regex substitution in generated JSON.
- Store canonical UUIDs on entities: Rejected — UUIDs are document-scoped in OSCAL, not entity-scoped.

---

## R10: Standalone Dashboard Export Endpoints

**Context**: FR-013–FR-015 require POA&M and Assessment Results exports from the dashboard, currently only available via MCP tools.

**Decision**: Add dashboard API endpoints that delegate to the same `EmassExportService.ExportOscalAsync()` method, returning downloadable JSON files.

**Rationale**:
- The backend code already exists in `EmassExportService.BuildOscalPoam()` and `BuildOscalAssessmentResults()`.
- After upgrading to OSCAL 1.1.2 (R1), these methods produce valid artifacts.
- Dashboard endpoints follow the existing pattern in `SspExportService` — enqueue or direct generation based on complexity.
- Since standalone exports are lightweight (single artifact, no ZIP), they can be synchronous (no background job needed).

**Alternatives Considered**:
- Background job for standalone exports: Rejected — single OSCAL JSON generation completes in <5 seconds; background processing is unnecessary overhead.
- Direct MCP tool exposure to dashboard: Rejected — MCP tools use base64 encoding and envelope format unsuitable for browser download.
