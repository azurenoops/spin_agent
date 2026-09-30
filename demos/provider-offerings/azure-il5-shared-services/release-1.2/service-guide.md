# Azure IL5 · Shared services
## Service guide and customer onboarding

SYNTHETIC DEMONSTRATION ONLY. All services, measurements, decisions, identities and evidence in this package are fictional. Not Microsoft provider data, a DoD approval, an IL5 authorization, a customer entitlement, or operational evidence.

Release 1.2 | Scenario date: 2026-09-15 | Provider-managed shared services

## Service boundary

Only the eight listed provider-operated shared services; no mission workloads or SaaS collaboration tenant.

Environment: Azure Government (fictional scenario).

## Exclusions

Customer application code, databases, mission data, workstation configuration, customer subscriptions not explicitly enrolled, Microsoft 365, and any actual IL5 approval.

## Synthetic hosting identity

SYNTHETIC DEMONSTRATION ONLY. Fictional hosting identity; kind=Azure; cloud=AzureUSGovernment; directoryTenantId=07900000-0000-0000-0000-000000000101; subscriptionId=07900000-0000-0000-0000-000000000102; resourceId=/subscriptions/07900000-0000-0000-0000-000000000102/resourceGroups/rg-synthetic-il5-shared-services. No live resource, tenant entitlement, or connector is asserted.

## Operating team

Northstar Shared Services Demo Team | shared-services@example.invalid. This address is intentionally non-routable.

## Customer onboarding and acceptance

1. Identify a fictional mission information owner and select only applicable capabilities.

2. Review the boundary, customer duties, reference conditions, and disclosed moderate finding.

3. Confirm the application records a reviewed provider release and a separate customer association; importing a file creates neither entitlement nor authority.

4. Record customer-owned controls and evidence tasks before any inheritance decision.

5. Obtain mission-level review through the normal authorization process. No synthetic reference substitutes for it.

## Service change and exit

The team provides a fictional ten-business-day notice for planned customer-impacting changes. Emergency changes require a next-business-day review. On exit, agree evidence export scope, revoke demo associations, and document customer retention obligations; this package does not implement those actions.

## Audit collection

Component: AZ-component-audit — Central audit collector and archive.

Coverage: AU-2, AU-6, AU-11. This is partial control support, not full control satisfaction.

Collect administrative and platform security events from the enrolled shared-service boundary; retain 90 days searchable and 365 days archived in this scenario.

Provider: Operate collector health checks every 15 minutes; reconcile ingestion counts daily; protect archived events from routine operator modification.

Shared: Agree event categories and triage escalation with each mission; investigate missing source intervals jointly within one business day.

Customer: Enable workload audit sources, review mission-specific events each business day, and set longer retention when mission policy requires it.

Review evidence: AZ-E01: seven-day collector reconciliation and archive retrieval sample.

## Network protection

Component: AZ-component-network — Shared hub firewall and private routing.

Coverage: SC-7. This is partial control support, not full control satisfaction.

Inspect approved north-south shared-hub flows and maintain documented private routing for enrolled spokes.

Provider: Maintain deny-by-default hub rules, review exceptions monthly, and record firewall changes with rollback instructions.

Shared: Approve traffic purpose and route changes jointly; exercise a denied-traffic test before connecting a new spoke.

Customer: Manage application ingress, workload NSGs, DNS dependencies and endpoints outside the provider hub.

Review evidence: AZ-E02: approved rule sample, route inventory and denied-flow test.

## Privileged identity

Component: AZ-component-identity — Provider operations identity service.

Coverage: IA-2, AC-2. This is partial control support, not full control satisfaction.

Control provider operator accounts with MFA and time-limited privileged access; customer identities remain a separate administration responsibility.

Provider: Require MFA for operators; review privileged membership monthly; remove provider leavers within four hours of confirmed notice.

Shared: Maintain named escalation contacts and reconcile emergency access use after every activation.

Customer: Approve mission accounts, enforce mission MFA, perform access reviews, and disable customer leavers.

Review evidence: AZ-E03: synthetic access review, activation record and leaver test.

## Configuration baselines

Component: AZ-component-configuration — Shared-service configuration repository.

Coverage: CM-2, CM-3. This is partial control support, not full control satisfaction.

Version the shared-service baseline and capture approved changes; application code and mission images are excluded.

Provider: Record baseline version, approver, test result and rollback plan; compare deployed shared settings weekly.

Shared: Assess mission impact before changing shared defaults; agree maintenance windows and exception expiration.

Customer: Maintain application baselines and approve workload changes; remediate customer-owned configuration drift.

Review evidence: AZ-E04: baseline revision, drift review and approved change sample.

## Encryption and key handling

Component: AZ-component-encryption — Shared-service key vault and transport configuration.

Coverage: SC-12, SC-13. This is partial control support, not full control satisfaction.

Manage fictional shared-service keys and transport settings for provider-owned endpoints only.

Provider: Restrict key administration, test rotation every 90 days, record custody, and enforce TLS 1.2 or higher on covered endpoints.

Shared: Document key ownership and coordinate rotation windows for dependent workloads; test trust-chain changes jointly.

Customer: Configure workload encryption and certificates; manage customer keys and classify data before using a covered endpoint.

Review evidence: AZ-E05: synthetic rotation rehearsal and endpoint protocol inventory.

## Incident coordination

Component: AZ-component-incident — Provider incident coordination desk.

Coverage: IR-4, IR-6. This is partial control support, not full control satisfaction.

Coordinate incidents affecting the shared-service boundary using a fictional 24-hour on-call roster.

Provider: Acknowledge severity-one tickets within 30 minutes in the scenario; preserve provider records and issue initial impact notices.

Shared: Exercise incident handoffs quarterly; coordinate containment approval where provider action affects mission service.

Customer: Own mission incident declarations, regulatory reporting, application containment and mission recovery decisions.

Review evidence: AZ-E06: tabletop timeline, escalation roster and notification template.

## Vulnerability management

Component: AZ-component-vulnerability — Shared infrastructure scan and patch service.

Coverage: RA-5, SI-2. This is partial control support, not full control satisfaction.

Scan provider-maintained hosts weekly and evaluate urgent advisories daily; mission applications are excluded.

Provider: Triage critical results within one business day, schedule fixes by risk, and verify remediation with a follow-up scan.

Shared: Agree outage windows and compensating controls for shared infrastructure exceptions; review overdue items weekly.

Customer: Scan and patch customer applications, containers and mission hosts; document mission risk acceptance separately.

Review evidence: AZ-E07: synthetic scan sample, patch ticket and clean rescan.

## Backup and recovery

Component: AZ-component-backup — Shared configuration backup vault.

Coverage: CP-9, CP-10. This is partial control support, not full control satisfaction.

Back up shared configuration daily; exercise quarterly restore with a scenario target of 24-hour RPO and eight-hour RTO.

Provider: Monitor backup jobs daily, protect recovery copies from ordinary operator deletion, and record restore results quarterly.

Shared: Agree recovery sequence and validate shared dependencies during a joint recovery exercise.

Customer: Back up mission data and applications, define mission RTO/RPO, and validate business transactions after recovery.

Review evidence: AZ-E08: backup reconciliation and shared-configuration restore exercise.

