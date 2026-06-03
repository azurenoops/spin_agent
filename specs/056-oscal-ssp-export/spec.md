# Feature Specification: OSCAL-Complete SSP Export + CI Schema Validation

**Feature Branch**: `056-oscal-ssp-export`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #122
**Builds on**: Spec 022 (`specs/022-ssp-full-oscal/`) — original OSCAL SSP work

## Background

ATO Copilot can export system security plans in OSCAL JSON format via
`OscalSspExportService.cs`. However, the export pipeline has three critical
gaps that allow silent regressions and compliance failures:

1. **CI does not validate OSCAL exports against schemas.** The schemas exist
   (`oscal_ssp_schema.json`, assessment-plan, assessment-results, poam) and a
   validation service exists (`OscalSchemaValidationService.cs`), but no CI
   step calls it. A developer can break OSCAL conformance and merge without
   any build signal.

2. **Export service silently degrades.** When a system has no security
   categorization, no control baseline, or no narratives,
   `OscalSspExportService.cs` produces placeholder values with a warning log —
   not a structured error surfaced to the caller. Callers (API, tests) cannot
   distinguish "valid OSCAL" from "OSCAL with placeholders."

3. **No E2E test for the full export pipeline.** No integration test seeds a
   complete system, calls the export service, and validates the output against
   the OSCAL schema.

4. **The `/exports/validate-oscal` endpoint is idle.** It exists but is never
   called from CI, tests, or any scheduled check.

### Verified state of the code (current `main`)

1. **Schemas are present.** `src/Ato.Copilot.Agents/Compliance/Resources/oscal-schemas/`
   contains `oscal_ssp_schema.json`, `oscal_assessment-plan_schema.json`,
   `oscal_assessment-results_schema.json`, `oscal_poam_schema.json`.

2. **`OscalSspExportService.cs` warns but doesn't error.** Log messages include
   "Using placeholder values" for missing security categorization, missing
   control baseline, no RMF role assignments, and no narratives. The service
   returns a document in all cases — callers cannot tell if it is valid OSCAL.

3. **`OscalSchemaValidationService.cs` and `IOscalValidationService.cs` exist.**
   The implementation and interface are in place. They are NOT wired into the
   export pipeline return value or CI.

4. **`ci.yml` has no OSCAL validation step.** No job or step calls schema
   validation on generated export artifacts.

5. **DOCX and PDF generation are implemented** (not stubs).
   `SspExportService.cs` calls `_templateService.RenderDocxAsync` and
   `RenderPdfAsync`.

6. **Export endpoints exist** at `/api/v1/systems/{systemId}/exports/`:
   `oscal-poam`, `oscal-assessment-results`, `oscal-sap`. The SSP export
   endpoint is also present (from Spec 022).

7. **`/exports/validate-oscal` endpoint exists** but is unused in CI/tests.

### Why this matters

- A schema-invalid OSCAL SSP will be rejected by NIST-compliant tooling,
  government assessment tools, and FedRAMP submission portals.
- Silently exporting placeholder-filled OSCAL creates an audit risk: the
  document looks complete but fails validation downstream.
- Without a CI gate, every merged PR is a potential regression.

## Clarifications

- **Q: Should schema validation block the export (hard error) or annotate
  the response (soft warning)?**
  **A:** Both, depending on context. CI must fail hard. The API response adds
  a `validationStatus` field so the dashboard can surface validity inline
  without blocking the export download. Callers that need a hard gate can
  check `validationStatus.isValid`.

- **Q: Which schema validation library for CI?**
  **A:** `ajv` (Ajv JSON Schema Validator, Node.js) for the CI step — it has
  official NIST OSCAL support and runs fast in a GitHub Actions step.
  The .NET `OscalSchemaValidationService.cs` uses `NJsonSchema` — keep both
  so the .NET service and CI step use the same authoritative schemas.

- **Q: What counts as a hard error vs a soft warning in `OscalValidationResult`?**
  **A:** Hard errors: schema constraint violations (required fields missing,
  type mismatches, invalid enum values). Soft warnings: business-rule gaps
  that produce valid OSCAL but incomplete content (no narratives, placeholder
  security categorization). The distinction is schema conformance vs content
  completeness.

- **Q: Should the integration test use a real database or in-memory?**
  **A:** In-memory EF Core (`UseInMemoryDatabase`) for speed; seed enough data
  to produce a complete SSP (system, categorization, baseline, narratives,
  roles) so the exported OSCAL passes schema validation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — CI validates OSCAL exports on every PR (Priority: P1)

**As a** developer merging a PR
**I want** the CI build to fail if any OSCAL export violates its schema
**So that** I catch schema regressions before they reach `main`.

**Why this priority**: P1 because without this, any merged PR can silently
break OSCAL conformance. This is the highest-leverage, lowest-effort change —
the schemas and validation service already exist.

**Independent Test**: Introduce a deliberate schema violation in
`OscalSspExportService.cs` (e.g., remove a required field); run CI; assert
the build fails at the schema validation step. Revert the violation; assert
build passes.

**Acceptance Criteria**:
- `ci.yml` has a new step: "Validate OSCAL exports against schemas".
- The step runs on every PR and push to `main`.
- The step: (a) starts the API in test mode, (b) calls each export endpoint
  for a seeded test system, (c) validates each response body against the
  corresponding schema using `ajv` or equivalent, (d) exits non-zero on any
  schema violation.
- CI passes for a fully-seeded system with all required OSCAL fields present.
- CI fails and produces a human-readable error message identifying the failing
  schema constraint (e.g., `"#/system-security-plan/metadata/title: required"`).
- The step logs the validation result (pass/fail) for each export type
  (SSP, SAP, Assessment Results, POA&M).

**Edge Cases**:
- Validation tool not installed: CI step fails with clear message "ajv not
  found — run npm install in ci-tools/".
- Export endpoint returns non-200: CI step treats this as a failure and logs
  the HTTP status.
- Schema file missing from repo: CI step fails with "Schema file not found"
  before calling the API.

---

### User Story 2 — Structured validation result from OscalSspExportService (Priority: P1)

**As a** developer or API consumer
**I want** `OscalSspExportService` to return a structured `OscalValidationResult`
**So that** I can differentiate schema-valid OSCAL from placeholder-degraded
output without parsing log strings.

**Why this priority**: P1 because the current warning-only path makes it
impossible for callers to react programmatically to export quality. This also
unblocks US4 (API response includes `validationStatus`).

**Independent Test**: Call `OscalSspExportService.ExportAsync` for a system
with no security categorization; assert `result.Warnings` contains an entry
with code `NO_SECURITY_CATEGORIZATION`; assert `result.IsValid` is `true`
(it is schema-valid OSCAL, just incomplete). Call for a system that produces
schema-invalid OSCAL (mocked); assert `result.IsValid` is `false` and
`result.Errors` contains the constraint description.

**Acceptance Criteria**:
- `OscalSspExportService.ExportAsync` (or equivalent public method) returns
  `OscalExportResult` containing:
  - `Document` — the OSCAL JSON document (unchanged).
  - `ValidationResult` — `OscalValidationResult` with `IsValid`, `Errors[]`,
    `Warnings[]`.
- `OscalValidationResult` is the type defined by the existing
  `IOscalSchemaValidationService` interface (or extended to match
  `contracts/internal-services.md`).
- Warning codes (strings) are defined as constants, not magic strings.
  Initial set: `NO_SECURITY_CATEGORIZATION`, `NO_CONTROL_BASELINE`,
  `NO_RMF_ROLE_ASSIGNMENTS`, `NO_NARRATIVES`.
- Existing warning log calls are replaced (or supplemented) by structured
  entries in `ValidationResult.Warnings`.
- Callers that previously ignored warnings continue to work (no breaking
  change to existing consumers).
- `IOscalSchemaValidationService.ValidateAsync` is called from within the
  export service to produce `Errors[]`.

**Edge Cases**:
- Schema file missing at runtime: `Errors` contains one entry with code
  `SCHEMA_FILE_NOT_FOUND`; `IsValid` is `false`.
- Export produces valid JSON that is not valid OSCAL: `IsValid` is `false`;
  `Errors` lists all constraint violations.
- All fields present and correct: `IsValid` is `true`; `Errors` is empty;
  `Warnings` may still have zero or more business-rule warnings.

---

### User Story 3 — Integration test for OSCAL SSP round-trip (Priority: P2)

**As a** developer
**I want** an integration test that seeds a complete system, exports OSCAL, and
validates the result against the schema
**So that** regressions are caught at the test layer before they reach CI.

**Why this priority**: P2 because the CI step (US1) catches regressions
post-PR; this test catches them at the unit test stage, providing faster
feedback.

**Independent Test**: Run `dotnet test` with the filter
`Category=OscalIntegration`; assert the test passes without schema errors.
Modify the seeded system to remove `securityCategorization`; assert test
produces a `ValidationResult.Warnings` entry but still passes (warnings
don't fail tests — only errors do).

**Acceptance Criteria**:
- Test file: `tests/Ato.Copilot.Api.Tests/Integration/OscalSspRoundTripTests.cs`.
- Uses `UseInMemoryDatabase`; seeds: system, security categorization
  (FIPS 199 Moderate), control baseline (NIST SP 800-53 Rev 5 Moderate),
  at least 5 narratives, RMF role assignments.
- Calls `OscalSspExportService.ExportAsync`.
- Asserts `result.ValidationResult.IsValid == true`.
- Asserts `result.ValidationResult.Errors.Count == 0`.
- Asserts exported JSON deserializes to a valid OSCAL SSP object.
- Test is tagged `[Category("OscalIntegration")]` for CI filter support.

**Edge Cases**:
- Schema validation service throws: test catches and fails with a descriptive
  message (not a generic NullReferenceException).
- Empty database (no seed): test is isolated — seeding is done in test setup,
  not assumed from shared state.

---

### User Story 4 — API response includes validationStatus (Priority: P2)

**As a** dashboard developer
**I want** the OSCAL export API response to include a `validationStatus` field
**So that** the dashboard can surface schema validity inline without a separate
validation call.

**Why this priority**: P2 — adds UX value for developers and compliance
officers reviewing the dashboard, but does not block core export functionality.

**Independent Test**: Call `GET /api/v1/systems/{id}/exports/oscal-ssp` for a
fully-seeded system; assert response includes `validationStatus.isValid: true`
and `validationStatus.warnings: []`. Call for an incomplete system; assert
`validationStatus.warnings` contains `NO_SECURITY_CATEGORIZATION`.

**Acceptance Criteria**:
- Export endpoints (`/exports/oscal-ssp`, `/exports/oscal-sap`,
  `/exports/oscal-assessment-results`, `/exports/oscal-poam`) return a JSON
  envelope with `document` (the OSCAL JSON) and `validationStatus`.
- `validationStatus` shape: `{ isValid: boolean, errors: ValidationEntry[],
  warnings: ValidationEntry[] }`.
- `ValidationEntry` shape: `{ code: string, message: string, path?: string }`.
- When `Accept: application/json` is sent, the envelope is returned.
- When `Accept: application/oscal+json` is sent, the raw OSCAL document is
  returned (no envelope) for compatibility with OSCAL-native tools.
- Dashboard renders a green checkmark when `isValid: true`, a yellow warning
  badge when `warnings.length > 0 && isValid`, and a red error badge when
  `!isValid`.

**Edge Cases**:
- Validation service unavailable: export still succeeds; `validationStatus` is
  `{ isValid: null, errors: [{ code: "VALIDATION_SERVICE_UNAVAILABLE" }] }`.
- Client sends no `Accept` header: defaults to envelope response.
