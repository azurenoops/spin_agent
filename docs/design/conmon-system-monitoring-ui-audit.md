# System-wide Continuous Monitoring: UI audit and proposed screens

Date: 2026-09-29. Branch audited: `feature/1002-workspace-delivery-1037`.

Scope: source-level audit of Dashboard routes, SystemLayout, ConMon.tsx and its API contract, using the preceding monitoring-service audit. No live browser or Azure verification was performed. Proposed screens use illustrative data; they are not implemented functionality.

## Verified current UI

- `src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx` routes `conmon` to one page. `components/layout/SystemLayout.tsx` labels its navigation item ConMon. This differs from the newer shell shown in the user's screenshots; those screenshots are branding references, not proof of this checkout's rendered UI.
- `pages/ConMon.tsx` renders scores, expiration, drift count, plan, monitoring status, authorization/expiration alerts, significant changes and reports on one page. It polls the overview every 30 seconds; this refresh is not the Azure collection frequency.
- Plan inputs are assessment frequency, annual review date, distribution strings and comma-separated significant-change strings. The form is not a structured executable monitoring-rule editor.
- Monitoring health is a boolean and one timestamp. No per-subscription/source/resource-scope health is exposed in `api/conmon.ts`.
- Significant changes support manual reporting and display review metadata. This UI lacks a before/after comparison, connected dependency analysis and linked follow-up workflow.
- Report generation/history/detail exist. Report rows label `newFindings` as Open Findings; the metric label and contract need reconciliation.
- Authorization trigger checks and workflow initiation exist; preserve access-controlled functionality while clearly separating monitoring observations from an authorization decision.

## Proposed navigation

Keep the left navigation label **Continuous monitoring**. Use six local tabs:

| Tab | Main question | Required behavior |
|---|---|---|
| Overview | Is monitoring working, and what do I do next? | Aggregate health over every required source; actionable gaps and review queue; linked work; no invented compliance score. |
| Coverage | What is monitored? | Reuse attached environments; reviewed resource scope across subscriptions; shared dependencies; source-specific permissions, freshness and health. |
| Changes | What changed and what does it affect? | Correlated system changes; before/after observations; provenance; impacted resources/controls; human-reviewed routing into existing work. |
| Rules & plan | What should trigger action? | Structured rule conditions, scope, source, severity, owner, frequency and follow-up; keep assessment/report cadence distinct from collection cadence. |
| Baselines | What approved state are we comparing against? | Versioned approved baseline; observed state; staged proposals; permission-controlled review and immutable history. |
| Reports | What can we document and share? | Period-specific coverage, gaps, changes, decisions and follow-up; retain existing generation/history; link package contribution and authorization records. |

## Screen interactions and state requirements

1. Overview: lead with degraded/healthy/not configured/unknown collection state. Show healthy scopes out of required scopes; source failures cannot be averaged away. Select a next-action row to open its authoritative record.
2. Coverage: configure through Select scope → Check access and sources → Review baseline → Enable monitoring. Existing environment attachment is reused. Display additions/removals and exclusions before saving scope changes. Failed access blocks affected collectors and remains visible. Retain historical scope and events on detachment.
3. Changes: filter by subscription, source, status and severity. Detail shows timestamps, source, baseline version, precise property changes and evidence. Separate attempted/failed operations from verified configuration changes. A shared resource can impact multiple authorized systems without leaking other tenants' details. Link existing assessments/tasks/POA&Ms/exceptions before creating duplicates. Ticket state does not automatically close compliance records.
4. Rules & plan: draft, test, review/enable, pause and history. Rule preview shows affected scope, source prerequisites and intended action. Test is non-mutating. Missing source means not evaluated, never passed. Assessment frequency, collection frequency, annual review and report distribution are separate controls. No automatic remediation enabled by default.
5. Baselines: compare approved and observed versions; select proposal changes; require rationale and appropriate reviewer. Reviewer authorization must be supplied by backend permissions, not inferred from a role label. Concurrency conflict requires reload/review. Rejecting a proposal does not delete observations. Approval does not grant ATO.
6. Reports: show evidence cutoff, scope/baseline versions, coverage gaps and completeness at report time. Incomplete reports remain clearly labeled. Retain manual significant-change reporting, ISA/PIA expiration alerts and authorization review links. Package export and actual eMASS submission remain distinct.

All screens: loading, empty, filtered-empty, permission-denied, source-unavailable, stale and retry states; accessible labels, keyboard-operable tabs/dialogs, status text in addition to color. On small screens use stacked cards and full-width details. Read-only users can inspect permitted records but cannot mutate them. Network errors must not erase the last successful observation or display healthy zero counts.

## Backend prerequisites for these screens

The current overview contract cannot power the proposed mocks. Extend it or introduce focused system-scoped endpoints for coverage/source health, reviewed scope, change observations/correlation, rules/evaluations, baseline proposals/reviews and report provenance. Exact routes and schema require implementation planning.

- Scope every read/write by organization, system and authorized resources; replace first-system-per-subscription attribution.
- Implement actual event collection and periodic inventory reconciliation; durable checkpoints, idempotency, retries and failure health.
- Represent unavailable measurements as unknown with reasons and observation timestamps.
- Correct snapshot scoring and aggregate monitoring health before surfacing them as reliable metrics.
- Preserve event/resource/baseline/source versions and link existing assessment/remediation/POA&M/exception/ticket records.
- Return server-evaluated allowed actions and enforce all permissions server-side.

## Acceptance scenarios

- Three subscriptions: all healthy; one access failure; one stale source; no baseline; partial telemetry coverage.
- Add/update/delete a scoped resource; exclude unrelated resources; review shared-resource impact across systems.
- Duplicate/out-of-order events and retried actions do not duplicate records or lose evidence.
- Change review → linked assessment → remediation task → POA&M/exception → verified evidence → reviewed documentation proposal is navigable without implying automatic approval.
- Approved baseline persists while proposals are pending; unauthorized approval fails; version conflicts remain visible.
- No events and missing collection are distinguishable. No monitoring score fabricates health or ATO/cATO status.
- Existing plan/report/manual change/expiration functionality remains accessible.

## Mock boards

Three proposed boards cover all six tabs: Overview/Coverage, Changes/Rules & plan, Baselines/Reports. Mock values are illustrative. Generated with the built-in image-generation tool; associated prompts are retained alongside the boards for reproducibility. No product code was changed by this audit.

- [Overview and Coverage](assets/conmon-system-monitoring/01-overview-coverage.png)
- [Changes and Rules & plan](assets/conmon-system-monitoring/02-changes-rules.png)
- [Baselines and Reports](assets/conmon-system-monitoring/03-baselines-reports.png)
- [Generation prompts](conmon-system-monitoring-mock-prompts.md)

Implementation clarifications after visual review: reuse the actual SPIN logo asset rather than generated approximations. Dates and counts in these boards are independent illustrative states, not a shared live dataset. A rule's enabled badge refers to its active version; saving edits creates a separate draft and must not alter the active version. Show that version distinction explicitly in implementation. Resource counts reflect the last recorded inventory when collection is unavailable. Observed state and proposed baseline versions must have separate identifiers even where this concept mock abbreviates them. A green icon must never imply that open actions are complete.
