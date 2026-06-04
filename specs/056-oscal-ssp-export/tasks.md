# Tasks: OSCAL-Complete SSP Export + CI Schema Validation (Epic #122)

All tasks reference GitHub Issue #122. [P1] = must-ship, [P2] = ship if time.

---

## Phase 1 — Structured Validation Result (US2 — unblocks all other phases)

### T1.1 [P1] Define `OscalValidationResult` and `OscalValidationEntry`
**File**: `src/Ato.Copilot.Agents/Compliance/Models/OscalValidationResult.cs` *(new)*
- `OscalValidationResult`: `bool IsValid`, `List<OscalValidationEntry> Errors`,
  `List<OscalValidationEntry> Warnings`.
- `OscalValidationEntry`: `string Code`, `string Message`, `string? Path`.
- Issue #122

### T1.2 [P1] Define warning code constants
**File**: `src/Ato.Copilot.Agents/Compliance/Models/OscalWarningCodes.cs` *(new)*
- `public const string NO_SECURITY_CATEGORIZATION = "NO_SECURITY_CATEGORIZATION";`
- `public const string NO_CONTROL_BASELINE = "NO_CONTROL_BASELINE";`
- `public const string NO_RMF_ROLE_ASSIGNMENTS = "NO_RMF_ROLE_ASSIGNMENTS";`
- `public const string NO_NARRATIVES = "NO_NARRATIVES";`
- `public const string SCHEMA_FILE_NOT_FOUND = "SCHEMA_FILE_NOT_FOUND";`
- `public const string VALIDATION_SERVICE_UNAVAILABLE = "VALIDATION_SERVICE_UNAVAILABLE";`
- Issue #122

### T1.3 [P1] Update `IOscalSchemaValidationService` interface
**File**: `src/Ato.Copilot.Agents/Compliance/Services/IOscalSchemaValidationService.cs`
- Ensure `ValidateAsync(string oscalJson, OscalDocumentType docType)` returns
  `Task<OscalValidationResult>`.
- `OscalDocumentType` enum: `Ssp | AssessmentPlan | AssessmentResults | Poam`.
- Issue #122

### T1.4 [P1] Update `OscalSspExportService` to return `OscalExportResult`
**File**: `src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs`
- Define `OscalExportResult` record: `string Document`, `OscalValidationResult ValidationResult`.
- Replace warning log calls with `ValidationResult.Warnings.Add(...)` using constant codes.
- After generating the document, call `IOscalSchemaValidationService.ValidateAsync`
  and merge schema errors into `ValidationResult.Errors`.
- Issue #122

### T1.5 [P1] Unit tests — OscalSspExportService validation result
**File**: `tests/Ato.Copilot.Agents.Tests/OscalSspExportServiceTests.cs` *(new or extend)*
- No security categorization → `Warnings` contains `NO_SECURITY_CATEGORIZATION`.
- No narratives → `Warnings` contains `NO_NARRATIVES`.
- Schema-valid export → `IsValid == true`, `Errors.Count == 0`.
- Missing schema file → `Errors` contains `SCHEMA_FILE_NOT_FOUND`, `IsValid == false`.
- Issue #122

---

## Phase 2 — API Response Envelope (US4)

### T2.1 [P2] Define `OscalExportResponse` API DTO
**File**: `src/Ato.Copilot.Api/Dtos/OscalExportResponse.cs` *(new)*
- `string Document` (raw OSCAL JSON serialized as string or embedded object).
- `OscalValidationStatusDto ValidationStatus`.
- Issue #122

### T2.2 [P2] Define `OscalValidationStatusDto`
**File**: `src/Ato.Copilot.Api/Dtos/OscalValidationStatusDto.cs` *(new)*
- `bool? IsValid`, `List<ValidationEntryDto> Errors`, `List<ValidationEntryDto> Warnings`.
- `ValidationEntryDto`: `string Code`, `string Message`, `string? Path`.
- Issue #122

### T2.3 [P2] Update export endpoints to return envelope
**File**: `src/Ato.Copilot.Api/Endpoints/PackageEndpoints.cs`
- For SSP, SAP, assessment-results, and POAM export routes:
  - If `Accept: application/oscal+json` → return raw document.
  - Otherwise → return `OscalExportResponse` envelope with `validationStatus`.
- Issue #122

### T2.4 [P2] Unit tests — export endpoint envelope
**File**: `tests/Ato.Copilot.Api.Tests/Endpoints/OscalExportEndpointTests.cs` *(new or extend)*
- JSON Accept → envelope returned with `validationStatus`.
- `application/oscal+json` Accept → raw document returned.
- `validationStatus.isValid = true` for complete system.
- `validationStatus.warnings` contains code for incomplete system.
- Issue #122

---

## Phase 3 — Integration Test (US3)

### T3.1 [P2] Create OSCAL SSP round-trip integration test
**File**: `tests/Ato.Copilot.Api.Tests/Integration/OscalSspRoundTripTests.cs` *(new)*
- Uses `UseInMemoryDatabase`.
- Seeds: system, security categorization (FIPS 199 Moderate), control baseline,
  5 narratives, RMF role assignments.
- Calls `OscalSspExportService.ExportAsync`.
- Asserts `IsValid == true`, `Errors.Count == 0`.
- Tagged `[Category("OscalIntegration")]`.
- Issue #122

### T3.2 [P2] Create test seed helper
**File**: `tests/Ato.Copilot.Api.Tests/Integration/Helpers/OscalTestSystemSeeder.cs` *(new)*
- Static method `SeedCompleteSystem(DbContext ctx)` — creates minimum viable
  system for OSCAL SSP export to pass schema validation.
- Issue #122

---

## Phase 4 — CI Schema Validation Step (US1)

### T4.1 [P1] Create CI OSCAL validation script
**File**: `ci-tools/validate-oscal-exports.mjs` *(new)*
- Node.js ES module; uses `ajv` + `ajv-formats`.
- Accepts base URL of running API, system ID, and schema directory as args.
- Calls each export endpoint; validates response against corresponding schema.
- Exits 0 on all-pass; exits 1 on any failure.
- Prints per-document pass/fail with schema error details.
- Issue #122

### T4.2 [P1] Add `package.json` for CI tools
**File**: `ci-tools/package.json` *(new)*
- `dependencies: { "ajv": "^8", "ajv-formats": "^3", "node-fetch": "^3" }`.
- Issue #122

### T4.3 [P1] Add CI step to `ci.yml`
**File**: `.github/workflows/ci.yml`
- Add new job `oscal-validation` that:
  1. `npm ci` in `ci-tools/`.
  2. Starts API in integration-test mode (`dotnet run --environment IntegrationTest`).
  3. Waits for API health check.
  4. Runs `node ci-tools/validate-oscal-exports.mjs`.
  5. Shuts down API.
- Job runs on: `push` to `main`, all PRs.
- Issue #122

### T4.4 [P1] Document CI tool setup in README or quickstart
**File**: `ci-tools/README.md` *(new)*
- How to run locally, required Node.js version, environment variables.
- Issue #122

---

## Phase 5 — Documentation & Cleanup

### T5.1 [P1] Update `CHANGELOG.md`
**File**: `CHANGELOG.md`
- Entry: "feat: OSCAL SSP export structured validation + CI schema gate (#122)"
- Issue #122

### T5.2 [P1] Add OpenAPI annotations to updated export endpoints
**File**: `src/Ato.Copilot.Api/Endpoints/PackageEndpoints.cs`
- Document `validationStatus` field in response schema.
- Document `Accept` header behavior.
- Issue #122
