# Implementation Plan: Connected Assessment Workflow

**Feature**: 018-sap-generation | **Amended**: 2026-09-28 | **Spec**: [spec.md](spec.md)

## Summary

Reuse SecurityAssessmentPlan/SapControlEntry/SapTeamMember, ComplianceAssessment,
ScanImportRecord, ControlEffectiveness, ComplianceSnapshot and
SecurityAssessmentReport. Add scope/revision/provenance metadata to those records
where required; do not add a parallel SAP, assessment, or report store.

Plan and Results share a selected SAP ID/revision. A plan projection supplies
readable context, real advisory validation, permissions, named lead candidates,
structured editors, saved preview and history. Result projections preserve
original collection scope while comparing coverage against the selected plan.
Existing collection/import services retain supported methods and explicit
permissions; failed checks cannot fabricate assessment acceptance.

Preserve preliminary collection and draft SAR behavior. SAR creation must select
and retain exact result/review sources, not silently aggregate all effectiveness
rows ever recorded for the system. Safe retries use persistent operation identity
and existing records. Collection, result review, SAR lifecycle and AO decisions
are separate.

See [the connected-workflow contract](contracts/assessment-workspace.md) for
verified starting gaps and proposed additive contracts.

## Technical Context

- C# / .NET 9 backend; TypeScript / React 19 Dashboard.
- Existing ASP.NET Core, EF Core, React Router, Axios and Markdown renderers.
- SQLite / SQL Server existing entities; additive schema changes only.
- Production-route xUnit integration, Vitest and Playwright.
- Paged reads, cancellation, explicit progress/partial failure for long operations.
- No new package dependency, agent, tool, parallel plan/report store or automatic
  authorization-role assignment.

## Constitution Check

Document first; failing AAA tests before production code; server-side tenant,
system, record and action checks; audited version/idempotency checks; reuse
existing services; Dashboard static build and targeted backend/frontend/browser
tests. External issue linkage remains pending approval.

Existing full-solution unit-project compile blockers must be reported separately,
not hidden or fixed as unrelated scope.

## Implementation surfaces

- Core SAP, assessment, scan-import and SAR models/DTOs plus additive schema.
- Existing SAP, assessment engine/artifact, scan import and SAR services.
- MCP assessment/SAP/SAR/scan-import endpoints and scoped workspace projections.
- Dashboard assessment page, guided planning/result drawers, existing import,
  readiness, findings/remediation actions and protected document access.
- Feature 018 contract/tasks and assessment/documentation guides.

## Complexity Tracking

| Decision | Why needed | Rejected alternative |
|----------|------------|----------------------|
| Retained plan/result metadata on existing records | Scope and review must survive later plan edits and safe retries | Reconstructing historical scope from the current baseline mislabels results |
| Scoped workspace projections | Existing latest-SAP readers disagree and results APIs are broad | Independent client plan cards/queries can select different versions |
| Selected-source SAR extension | Current SAR uses all system effectiveness records | A second report store or unscoped latest-data aggregation breaks provenance |
