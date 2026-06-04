# Research: OSCAL SSP Export + CI Schema Validation (Epic #122)

## Trade-off 1: Hard Error vs Soft Warning for Missing Data

### The Problem
`OscalSspExportService.cs` currently produces a document even when data is
missing (no security categorization, no narratives). The question is whether
missing data should block the export or annotate it.

### Option A: Hard error — refuse to export if required OSCAL fields are absent
- **Pros**: No invalid or misleading OSCAL documents are produced.
- **Cons**: Blocks export for systems mid-workflow (before categorization is
  complete). ATO Copilot is designed to support incremental workflow — refusing
  export at any gap is too strict.

### Option B: Soft warning — export with placeholders, annotate (chosen)
- **Pros**: Works across all system states; progressive disclosure of quality.
- **Cons**: Consumer must check `validationStatus` to know the document quality.

### Option C: Distinguish schema errors (hard) from completeness warnings (soft) — chosen
Schema violations = hard errors (`IsValid = false`). Completeness gaps
(missing categorization, narratives) = soft warnings (`IsValid = true`,
`Warnings` non-empty). This matches the OSCAL specification's approach: an
OSCAL document is schema-valid as long as it uses the right structure; content
completeness is a separate concern.

**Decision**: Option C. `IsValid` reflects schema conformance only. `Warnings`
reflect business-rule completeness. Callers decide how to react.

---

## Trade-off 2: Validation at Export Time vs On-Demand

### Option A: Validate every time the export service runs
Every call to `ExportAsync` runs schema validation. Adds latency for large SSPs.

### Option B: Validate only when the API caller requests it (via `Accept` header or query param)
Export is fast by default; validation is opt-in.

**Cons**: CI and tests may forget to request validation; the signal is optional
and easy to skip.

### Option C: Always validate, but make it async / non-blocking for the response
Run validation in the background; cache result. Complex to implement.

**Decision**: Option A (always validate). OSCAL SSPs are not large enough to
make validation latency significant (schemas are loaded in memory; NJsonSchema
validates in <10ms for typical SSP sizes). Keeping validation mandatory ensures
every export caller always gets `validationStatus`.

---

## Trade-off 3: CI Validation Tool — ajv (Node) vs NJsonSchema (.NET) vs OSCAL CLI

### Option A: `ajv` (Node.js)
- Official JSON Schema Draft-07 support; fast; widely used in CI pipelines.
- OSCAL schemas are JSON Schema Draft-07 compatible.
- Simple to add a GitHub Actions step with `npm ci`.
- **Chosen**.

### Option B: `OscalSchemaValidationService.cs` (.NET)
- Reuses existing code; consistent with runtime behavior.
- Requires starting the full .NET app in CI just for validation.
- More complex CI step.

### Option C: NIST OSCAL CLI (`oscal-cli`)
- Official NIST tool; authoritative.
- Java dependency; heavier than needed for schema-only validation.
- Overkill for CI — full semantic validation is out of scope.

**Decision**: `ajv` for CI. `OscalSchemaValidationService.cs` (.NET) at
runtime. Both consume the same schema files. If NIST adds schema changes,
both are updated from the same source.

---

## Trade-off 4: API Response Envelope vs Content-Negotiation

### Problem
Existing OSCAL-native clients may expect a raw OSCAL JSON document (without a
`validationStatus` wrapper). Adding an envelope breaks those clients.

### Solution: Content-Negotiation
- `Accept: application/json` (default) → envelope with `document` + `validationStatus`.
- `Accept: application/oscal+json` → raw OSCAL document (no envelope).

This follows HTTP best practices and preserves backward compatibility.
Existing clients that set no `Accept` header get the envelope (new behavior
for new clients); clients that explicitly request OSCAL get the raw document.

**Decision**: Content-negotiation. Implement via a `Results.Content` branch
in the endpoint handler.

---

## Trade-off 5: In-Memory vs Real DB for Integration Tests

### Option A: In-memory EF Core
- Fast; no Docker required for local dev.
- Chosen for the OSCAL round-trip test (US3).

### Option B: Real database (Testcontainers)
- More realistic; catches DB-specific query issues.
- Slower; requires Docker in CI.

**Decision**: In-memory for this feature's integration test. A separate test
infrastructure track can add Testcontainers; that is out of scope for #122.
