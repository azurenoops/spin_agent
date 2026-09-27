# Purpose-specific status contract

These are logical semantics; existing enums/DTOs should be extended compatibly,
not replaced wholesale. All transitions are authorized and persisted by the server.

| State axis | Required distinctions | Never implies |
|---|---|---|
| Source | Received, processing, analyzed/partial, unavailable, excluded with rationale | Reviewed or published |
| Candidate review | Private draft, pending review, approved/rejected, stale | Automatic customer sharing |
| Release | Draft/review, published version, superseded, withdrawn | Mission adoption or AO authorization |
| Allocation | Available/eligible scope, changed/revoked | Cloud provisioning or mission association |
| Association | Pending, explicitly associated, changed/unresolved | Applied capability |
| Adoption | Selected/pinned release, newer available, impact review required | Responsibility acceptance |
| Responsibility | Undesignated/inherited/shared/customer, pending/confirmed/stale review | Customer work completed |
| Evidence | Permitted artifact/reference, restricted, missing, stale, unavailable | Sufficient or reviewed evidence |
| Document | Draft, under review, approved version, stale relative to source | Package generation or transfer |
| Package | Queued, processing, completed, failed, retained purpose/version | Receipt/import acceptance |
| eMASS | Not transferred, transfer recorded, receipt recorded, accepted/rejected/partial import, unresolved reconciliation | AO decision |
| Authorization | Existing recorded AO decision, conditions, expiration | Derived from technical connectivity |
| Monitoring | Configured, collecting, healthy, degraded/failed, stale/missing/unknown | cATO or zero risk |

## Readiness

One server-owned assessment composes existing validators. Each requirement
contains stable identity, applicability, status, severity, owner/action
permission, source/version, document destination, diagnostic reference, and a
working workspace/system action link. Capture evaluation time and source
snapshot identity so consumers can detect stale results.

Statuses distinguish satisfied, missing, incomplete, stale, unable-to-verify,
and explicitly not-applicable. "No errors collected" is insufficient when a
required check failed to run. Missing required section presence must be tested,
not only approval of sections that happen to exist.

| Purpose | Required semantics |
|---|---|
| Initial submission | No prior AO decision required or generated; applicable reviewed documentation/assessment/evidence requirements remain enforced. |
| Authorized-baseline archive | Pins the relevant recorded decision and approved baseline. |
| Change/reauthorization submission | Pins previous reviewed baseline and identified changes; no automatic new authorization. |
| Draft preview | May show explicitly missing/unverified values; cannot be presented as final-ready output. |

Do not silently default legacy requests to the least restrictive purpose.
Retain legacy behavior via compatibility adapter or request clarification.
Expose purpose selection in all relevant clients, including MCP, before retiring
the adapter. Source facts cannot be invented to satisfy an export schema.
