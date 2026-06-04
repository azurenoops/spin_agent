# Requirements Checklist: OSCAL SSP Export + CI Schema Validation (Epic #122)

## SDK Completeness

### Spec artifacts
- [x] `spec.md` — background, verified code state, 4 user stories with full
      acceptance criteria and edge cases
- [x] `tasks.md` — phased tasks with exact file paths and [P] markers
- [x] `data-model.md` — new C# types, DTOs, schema file references
- [x] `research.md` — trade-off analysis (hard vs soft errors, ajv vs NJsonSchema,
      envelope vs content-negotiation, in-memory vs real DB)
- [x] `plan.md` — phased implementation plan with dependencies and risks
- [x] `quickstart.md` — local dev setup, seeder commands, CI script local run
- [x] `checklists/requirements.md` (this file)
- [x] `contracts/http-api.md` — export endpoint response shape changes
- [x] `contracts/oscal-validation.md` — full CI validation contract
- [x] `contracts/internal-services.md` — `IOscalSchemaValidationService` interface

### User stories
- [x] US1 (P1): CI OSCAL validation — acceptance criteria + edge cases
- [x] US2 (P1): Structured `OscalValidationResult` — acceptance criteria + edge cases
- [x] US3 (P2): Integration test round-trip — acceptance criteria + edge cases
- [x] US4 (P2): API `validationStatus` field — acceptance criteria + edge cases

### Backend
- [x] New types named: `OscalValidationResult`, `OscalValidationEntry`,
      `OscalExportResult`, `OscalWarningCodes`, `OscalDocumentType`
- [x] Warning codes defined as constants (not magic strings)
- [x] `IOscalSchemaValidationService` interface contract documented
- [x] Export endpoint response shape documented (envelope + content-negotiation)
- [x] Unit test file paths named
- [x] Integration test file path named and seeder helper defined
- [x] No DB migrations needed — confirmed

### CI
- [x] CI script file named (`ci-tools/validate-oscal-exports.mjs`)
- [x] `ci.yml` job/step described with steps enumerated
- [x] Schema tool and version specified (`ajv` v8)
- [x] Pass/fail criteria defined
- [x] Human-readable error output format described

### Quality gates
- [ ] PR passes existing CI (build + test + lint)
- [ ] New `OscalSspExportService` behavior covered by ≥ 5 unit tests
- [ ] Integration test passes with seeded complete system
- [ ] CI step passes for main codebase; fails for deliberate schema violation
- [ ] Content-negotiation behavior unit-tested (envelope vs raw)
- [ ] CHANGELOG updated

### Out of scope
- Semantic OSCAL validation beyond schema conformance (e.g., NIST 800-53 control
  implementation completeness checks)
- OSCAL CLI integration (NIST Java tool) — `ajv` is sufficient for schema
- Real database (Testcontainers) for integration tests — in-memory is sufficient
- UI export download UI changes (dashboard badge is US4 scope only)
- OSCAL version upgrade (schemas stay at current bundled version)
