# Azure IL5 · Shared services
## Customer responsibility matrix

SYNTHETIC DEMONSTRATION ONLY. All services, measurements, decisions, identities and evidence in this package are fictional. Not Microsoft provider data, a DoD approval, an IL5 authorization, a customer entitlement, or operational evidence.

Release 1.2 | Scenario date: 2026-09-15 | Provider-managed shared services

## Audit collection | AU-2, AU-6, AU-11

Provider accountable: Operate collector health checks every 15 minutes; reconcile ingestion counts daily; protect archived events from routine operator modification.

Joint handoff: Agree event categories and triage escalation with each mission; investigate missing source intervals jointly within one business day.

Customer accountable: Enable workload audit sources, review mission-specific events each business day, and set longer retention when mission policy requires it.

Acceptance artifact: AZ-E01: seven-day collector reconciliation and archive retrieval sample. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Network protection | SC-7

Provider accountable: Maintain deny-by-default hub rules, review exceptions monthly, and record firewall changes with rollback instructions.

Joint handoff: Approve traffic purpose and route changes jointly; exercise a denied-traffic test before connecting a new spoke.

Customer accountable: Manage application ingress, workload NSGs, DNS dependencies and endpoints outside the provider hub.

Acceptance artifact: AZ-E02: approved rule sample, route inventory and denied-flow test. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Privileged identity | IA-2, AC-2

Provider accountable: Require MFA for operators; review privileged membership monthly; remove provider leavers within four hours of confirmed notice.

Joint handoff: Maintain named escalation contacts and reconcile emergency access use after every activation.

Customer accountable: Approve mission accounts, enforce mission MFA, perform access reviews, and disable customer leavers.

Acceptance artifact: AZ-E03: synthetic access review, activation record and leaver test. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Configuration baselines | CM-2, CM-3

Provider accountable: Record baseline version, approver, test result and rollback plan; compare deployed shared settings weekly.

Joint handoff: Assess mission impact before changing shared defaults; agree maintenance windows and exception expiration.

Customer accountable: Maintain application baselines and approve workload changes; remediate customer-owned configuration drift.

Acceptance artifact: AZ-E04: baseline revision, drift review and approved change sample. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Encryption and key handling | SC-12, SC-13

Provider accountable: Restrict key administration, test rotation every 90 days, record custody, and enforce TLS 1.2 or higher on covered endpoints.

Joint handoff: Document key ownership and coordinate rotation windows for dependent workloads; test trust-chain changes jointly.

Customer accountable: Configure workload encryption and certificates; manage customer keys and classify data before using a covered endpoint.

Acceptance artifact: AZ-E05: synthetic rotation rehearsal and endpoint protocol inventory. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Incident coordination | IR-4, IR-6

Provider accountable: Acknowledge severity-one tickets within 30 minutes in the scenario; preserve provider records and issue initial impact notices.

Joint handoff: Exercise incident handoffs quarterly; coordinate containment approval where provider action affects mission service.

Customer accountable: Own mission incident declarations, regulatory reporting, application containment and mission recovery decisions.

Acceptance artifact: AZ-E06: tabletop timeline, escalation roster and notification template. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Vulnerability management | RA-5, SI-2

Provider accountable: Triage critical results within one business day, schedule fixes by risk, and verify remediation with a follow-up scan.

Joint handoff: Agree outage windows and compensating controls for shared infrastructure exceptions; review overdue items weekly.

Customer accountable: Scan and patch customer applications, containers and mission hosts; document mission risk acceptance separately.

Acceptance artifact: AZ-E07: synthetic scan sample, patch ticket and clean rescan. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

## Backup and recovery | CP-9, CP-10

Provider accountable: Monitor backup jobs daily, protect recovery copies from ordinary operator deletion, and record restore results quarterly.

Joint handoff: Agree recovery sequence and validate shared dependencies during a joint recovery exercise.

Customer accountable: Back up mission data and applications, define mission RTO/RPO, and validate business transactions after recovery.

Acceptance artifact: AZ-E08: backup reconciliation and shared-configuration restore exercise. Review cadence: monthly and after material change. Customer sign-off is required before claiming applicability; an unresolved customer duty remains customer-owned.

