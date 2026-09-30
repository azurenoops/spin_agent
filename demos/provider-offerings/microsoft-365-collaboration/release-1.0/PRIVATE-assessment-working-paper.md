# Microsoft 365 · Collaboration
## PRIVATE detailed assessment working paper

SYNTHETIC DEMONSTRATION ONLY. All services, measurements, decisions, identities and evidence in this package are fictional. Not Microsoft provider data, a DoD approval, an IL5 authorization, a customer entitlement, or operational evidence.

Release 1.0 | Scenario date: 2026-09-15 | SaaS collaboration

PRIVATE | SYNTHETIC INTERNAL DEMO ONLY | NOT REAL CUI | M365-PRIVATE-WP-001

Do not include this attachment in customer-facing exports. Its PRIVATE label exercises document-handling choices, not a claim of real sensitivity.

## Synthetic sample ledger

Each period contains 100 invented events; expected and retained totals are 700. Bundle delay is measured from 00:00 UTC at the end of the period to delivery.

- 2026-08-18: 100 expected / 100 retained; delivery lag 12h; within target.

- 2026-08-19: 100 expected / 100 retained; delivery lag 13h; within target.

- 2026-08-20: 100 expected / 100 retained; delivery lag 31h; LATE.

- 2026-08-21: 100 expected / 100 retained; delivery lag 11h; within target.

- 2026-08-22: 100 expected / 100 retained; delivery lag 38h; LATE.

- 2026-08-23: 100 expected / 100 retained; delivery lag 14h; within target.

- 2026-08-24: 100 expected / 100 retained; delivery lag 12h; within target.

## Reviewer analysis

Two of seven bundles exceeded the target (28.6%). The invented failure sequence was a scheduled export failure followed by the next day's retry, with no delivery-age notification. No inference about real provider reliability is valid.

## Corrective test script

Create a synthetic delivery failure; verify the secondary attempt preserves event IDs; trigger an 18-hour age alert; verify escalation contacts acknowledge it; reconcile 14 daily bundles and demonstrate each arrives within 24 hours. Do not disable controls or inject faults into a real service for this demonstration.

## Review notes

Keep the finding Open while milestones are planned. The 1.3 scenario improves documentation but does not itself close the finding. No real assessor name, signature, tenant identifier or security configuration is included.
