# OSCAL Validation Contract (Epic #122)

## What is validated

Each OSCAL export type is validated against the corresponding NIST OSCAL JSON
Schema bundled in the repository:

| Document Type | Schema File |
|---------------|-------------|
| System Security Plan (SSP) | `oscal_ssp_schema.json` |
| Assessment Plan (SAP) | `oscal_assessment-plan_schema.json` |
| Assessment Results | `oscal_assessment-results_schema.json` |
| Plan of Action & Milestones (POA&M) | `oscal_poam_schema.json` |

Schema files are located at:
`src/Ato.Copilot.Agents/Compliance/Resources/oscal-schemas/`

---

## CI Validation Step

### When it runs
- Every `push` to `main`.
- Every pull request (all PRs, all branches).

### How it works

```
oscal-validation job
├── checkout repo
├── npm ci (in ci-tools/)
├── dotnet run (API in IntegrationTest environment)
├── wait-for-api (health check with retry)
├── node ci-tools/validate-oscal-exports.mjs
│     ├── GET /api/v1/systems/$SEED_SYSTEM_ID/exports/oscal-ssp
│     │     → validate response.document against oscal_ssp_schema.json
│     ├── GET /api/v1/systems/$SEED_SYSTEM_ID/exports/oscal-sap
│     │     → validate against oscal_assessment-plan_schema.json
│     ├── GET /api/v1/systems/$SEED_SYSTEM_ID/exports/oscal-assessment-results
│     │     → validate against oscal_assessment-results_schema.json
│     └── GET /api/v1/systems/$SEED_SYSTEM_ID/exports/oscal-poam
│           → validate against oscal_poam_schema.json
└── shutdown API
```

### Pass criteria
- All 4 export endpoints return HTTP 200.
- All 4 exported documents pass schema validation (zero schema errors).
- Business-rule warnings (`validationStatus.warnings`) do NOT cause CI failure.

### Fail criteria (exit code 1)
- Any export endpoint returns non-200.
- Any exported document has `validationStatus.isValid == false`.
- Any exported document has `validationStatus.errors.length > 0`.
- Schema file not found.
- `ajv` throws a parse error on the schema.

### Output format

On pass:
```
✓ SSP export valid (0 errors, 1 warning)
✓ Assessment Plan export valid (0 errors, 0 warnings)
✓ Assessment Results export valid (0 errors, 0 warnings)
✓ POA&M export valid (0 errors, 0 warnings)
All OSCAL exports passed schema validation.
```

On fail:
```
✗ SSP export FAILED
  Errors:
    [SCHEMA_VIOLATION] #/system-security-plan/metadata/title: required
      at path: /system-security-plan/metadata
  Warnings:
    [NO_NARRATIVES] No narratives found; placeholders used.
OSCAL validation failed. See errors above.
```

---

## Runtime Validation (in-process)

### Tool
`NJsonSchema` .NET library, called from `OscalSchemaValidationService.cs`.

### When it runs
- Every call to any `OscalSsp*ExportService.ExportAsync` method.
- The result is included in the `OscalExportResult.ValidationResult` returned
  to the caller.

### Schema loading
Schemas are embedded as resources in `Ato.Copilot.Agents` assembly.
They are loaded once (cached) via `IOscalSchemaValidationService`.

### Error vs Warning distinction

| Type | Condition | `IsValid` impact |
|------|-----------|-----------------|
| Error | JSON Schema constraint violation (required field missing, type mismatch, enum violation) | Sets `IsValid = false` |
| Warning | Business-rule incompleteness (no categorization, no narratives, no role assignments) | `IsValid` remains `true` |

---

## Schema Version

OSCAL schemas are pinned at the version bundled in the repository at the time
of this feature. Schema version: **OSCAL 1.1.2** (NIST SP 800-53 Rev 5).

A quarterly review process (tracked separately) will update schema files
when NIST releases new versions. The CI step and runtime service will
automatically pick up schema updates from the file system — no code changes
required when schemas are updated.

---

## Known Limitations

1. **ajv and NJsonSchema may disagree on edge cases.** Both consume the same
   JSON Schema files, but Draft-07 has ambiguities. If discrepancies are found,
   `ajv` is authoritative for CI (it is more widely tested against OSCAL
   schemas); NJsonSchema is authoritative for runtime warnings.

2. **No semantic validation.** Schema validation confirms structural
   conformance only. It does not validate that the stated controls are complete,
   that role assignments match actual personnel, or that narratives are
   substantive. Semantic validation is a future track.

3. **Warnings do not block CI.** A system with no narratives produces a
   warning, not a CI failure. This is intentional — many systems are legitimately
   mid-workflow during active development.
