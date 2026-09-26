## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F7

Priority: High.

The inspected ConMon overview queries enabled monitoring and drift counts by subscription IDs. It does not restrict those counts to the system boundary/resources. Enabled means a configuration exists, not that every intended scope has fresh successful telemetry.

## Required outcome

Attribute events against the reviewed system resource/boundary scope and relevant shared-provider dependencies. Distinguish system, shared-provider, out-of-scope and unknown changes. Present connected scope, monitored coverage, last successful collection, gaps and health separately. Preserve source evidence and trace component/control/document impact.

## Acceptance criteria

- [ ] Two systems sharing a subscription receive correct distinct attribution for scoped resource changes.
- [ ] Shared-provider changes show dependency-based impact without implying universal system coverage.
- [ ] Unmapped events are unknown, not silently attributed or discarded as safe.
- [ ] Missing/failed/stale telemetry shows degraded or unknown health, never a clear posture based only on zero alerts.
- [ ] Tests cover boundary revisions, resource moves/deletion, shared dependencies and tenant isolation.

## Source starting points

- `src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardConMonEndpoints.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs`

## Related work and scope boundary

Provides scope/health semantics for F6. Related #998/#999 own historical-report and overdue-count defects, not event attribution.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
