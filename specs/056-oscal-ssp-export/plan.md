# Implementation Plan: OSCAL SSP Export + CI Schema Validation (Epic #122)

## Phase 1 — Structured Validation Result (Week 1, Days 1-3)

**Goal**: `OscalSspExportService` returns a typed `OscalExportResult` with
`IsValid`, `Errors[]`, `Warnings[]`. Callers stop parsing log strings.

1. Create `OscalValidationResult`, `OscalValidationEntry`, `OscalWarningCodes`.
2. Update `IOscalSchemaValidationService` interface return type.
3. Update `OscalSspExportService` to call `ValidateAsync` and return
   `OscalExportResult`.
4. Replace warning log lines with structured `Warnings.Add(...)` calls.
5. Unit tests for each warning code and schema error path.

**Exit criteria**: `OscalSspExportService` tests pass. `IsValid` is `true`
for a complete system; warning codes appear for incomplete fields.

---

## Phase 2 — API Envelope (Week 1, Days 4-5)

**Goal**: Export API responses include `validationStatus`.

1. Create `OscalValidationStatusDto`, `ValidationEntryDto`, `OscalExportResponse`.
2. Update export handlers in `PackageEndpoints.cs` to return envelope.
3. Implement content-negotiation: `application/oscal+json` → raw doc.
4. Unit tests for envelope and content-negotiation.

**Exit criteria**: `GET /exports/oscal-ssp` with default `Accept` returns
envelope. With `Accept: application/oscal+json` returns raw OSCAL.

---

## Phase 3 — Integration Test (Week 2, Days 1-2)

**Goal**: Automated test validates a complete OSCAL SSP export round-trip.

1. Create `OscalTestSystemSeeder` helper.
2. Create `OscalSspRoundTripTests.cs` seeding a complete system.
3. Assert `IsValid == true`, `Errors.Count == 0`.
4. Verify test is tagged `[Category("OscalIntegration")]`.

**Exit criteria**: `dotnet test --filter Category=OscalIntegration` passes.

---

## Phase 4 — CI Validation Step (Week 2, Days 3-5)

**Goal**: Every PR build validates OSCAL exports against schemas.

1. Create `ci-tools/package.json` with `ajv` dependency.
2. Create `ci-tools/validate-oscal-exports.mjs`.
3. Add `oscal-validation` job to `ci.yml`.
4. Write `ci-tools/README.md`.
5. Run CI end-to-end in a feature branch; confirm the step passes for
   main codebase and fails when a deliberate schema violation is introduced.

**Exit criteria**: `ci.yml` runs the validation step on PR; build fails for
a schema-violating export.

---

## Dependencies

| Phase | Depends on |
|-------|-----------|
| 2 | Phase 1 (`OscalExportResult` must exist) |
| 3 | Phase 1 (`OscalSspExportService` must return typed result) |
| 4 | Phase 1 + Phase 2 (API must return envelope for CI to check `validationStatus`) |

## Risks

- **NJsonSchema and ajv may produce different results for the same document.**
  Mitigate by running both against the same test vector during Phase 4 and
  resolving any discrepancy before merging.
- **OSCAL schema evolution.** NIST periodically updates OSCAL schemas. Pin
  schema versions; schedule quarterly review.
- **CI step flakiness from API startup time.** Add a retry-with-backoff health
  check loop in the CI script before calling export endpoints.
