# Feature Specification: Compliance Watch

**Feature Branch**: `005-compliance-watch`  
**Created**: 2026-02-22  
**Status**: Draft  
**Input**: User description: "Continuous Compliance Monitoring — real-time monitoring and alerting system for compliance drift detection within the Security Posture Intelligence Navigator"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Scheduled Compliance Monitoring & Drift Detection (Priority: P1)

As a **Compliance Officer**, I want the system to automatically run compliance checks on a configurable schedule so that I am alerted to drift from known-good baselines without manual effort.

The system captures a "compliant baseline" after each successful assessment or remediation. When a subsequent scheduled check detects that a resource configuration has deviated from its baseline, a drift alert is raised. The Compliance Officer configures monitoring frequency per scope (subscription, resource group) via the Configuration Agent or chat — choosing from intervals such as every 15 minutes, hourly, daily, or weekly.

**Why this priority**: Scheduled monitoring is the foundational detection mechanism. Without it, compliance drift goes unnoticed between manual assessments. This story delivers the core monitoring loop, drift comparison engine, alert creation, and alert lifecycle — all of which are prerequisites for every other story.

**Independent Test**: Enable hourly monitoring for a subscription. Run an assessment to capture a baseline. Simulate a configuration change that violates a control. Wait for the next scheduled check. Verify a drift alert is created with the correct severity, control mapping, change details, and recommended action.

**Acceptance Scenarios**:

1. **Given** monitoring is enabled for a subscription at hourly frequency, **When** the hourly check runs, **Then** the system evaluates all in-scope resources against their baselines and creates alerts for any detected drift.
2. **Given** a storage account was compliant at baseline (HTTPS-only enabled), **When** HTTPS-only is disabled and a scheduled check runs, **Then** a DRIFT alert is created with severity High, control SC-8, the changed property, old value, new value, and recommended remediation.
3. **Given** no drift exists since the last check, **When** a scheduled check completes, **Then** no new alerts are created and the monitoring run is logged.
4. **Given** a previously drifted resource is returned to its baseline state, **When** the next check runs, **Then** the corresponding alert is automatically moved to RESOLVED.
5. **Given** a Compliance Officer changes monitoring frequency via chat ("Set weekly monitoring for rg-archive"), **When** the configuration is saved, **Then** subsequent checks for that scope run on the new schedule.

---

### User Story 2 — Alert Lifecycle & Management (Priority: P1)

As a **Compliance Officer** or **Platform Engineer**, I want to view, acknowledge, dismiss, and manage compliance alerts through chat so that I can triage and respond to issues efficiently.

Alerts follow a defined lifecycle: NEW → ACKNOWLEDGED → IN_PROGRESS → RESOLVED or DISMISSED. Anyone can acknowledge an alert. Only the assigned user or Compliance Officer can move an alert to IN_PROGRESS. The system automatically resolves alerts when a re-scan confirms compliance. Only a Compliance Officer can dismiss an alert, and dismissal requires a justification comment. If an alert is not acknowledged within its SLA window, it is automatically escalated.

**Why this priority**: Alert management is the primary user-facing interaction. Even with detection working (US1), alerts are useless if users cannot see, triage, and act on them. This story is co-equal priority with US1 because together they form the minimum viable product.

**Independent Test**: Create a test alert manually. View it via "Show active alerts." Acknowledge it. Move it to IN_PROGRESS. Simulate a re-scan that resolves it. Verify all state transitions are logged and the alert reaches RESOLVED status.

**Acceptance Scenarios**:

1. **Given** one or more active alerts exist, **When** a user says "Show active alerts," **Then** the system displays all NEW and ACKNOWLEDGED alerts with severity, type, timestamp, title, and affected resources.
2. **Given** a NEW alert exists, **When** a user says "Acknowledge alert ALT-123," **Then** the alert moves to ACKNOWLEDGED and the escalation timer pauses.
3. **Given** an ACKNOWLEDGED alert, **When** the assigned user says "Fix alert ALT-123," **Then** the system executes remediation, re-validates compliance, and moves the alert to RESOLVED if the violation is corrected.
4. **Given** a NEW alert, **When** a Compliance Officer says "Dismiss alert ALT-123 as false positive" and provides justification, **Then** the alert moves to DISMISSED with the justification logged.
5. **Given** a Platform Engineer attempts to dismiss an alert, **When** they say "Dismiss alert ALT-456," **Then** the system denies the action with a message explaining that only Compliance Officers can dismiss alerts.
6. **Given** a Critical alert is not acknowledged within 30 minutes, **When** the SLA window expires, **Then** the alert is automatically escalated — additional recipients are notified and the alert moves to ESCALATED.
7. **Given** active alerts exist, **When** a user says "Show Critical alerts from last 24 hours," **Then** only Critical alerts created within the last 24 hours are displayed.

---

### User Story 3 — Alert Rules & Suppression Configuration (Priority: P2)

As a **Compliance Officer**, I want to create custom alert rules, severity overrides, and suppression rules so that alerts are relevant and alert fatigue is minimized.

The Compliance Officer configures rules via chat or Configuration Agent. Rules define a scope (subscription, resource group, resource type, specific resource), a trigger (control family, specific control, resource property), an optional severity override, and optional recipient overrides. Suppression rules mute alerts for a defined scope — either temporarily (maintenance window) or permanently (with required justification). Default rules are pre-created for high-risk controls (AC, SC, AU, IA).

**Why this priority**: Without rules configuration, the system uses defaults only. This story is important for real-world usability but the system can function (with defaults) without it — making it P2.

**Independent Test**: Create a custom rule via chat ("Alert Critical for any encryption setting changes"). Trigger a change matching the rule. Verify the alert fires with the overridden severity. Then create a suppression rule for the same scope and verify subsequent alerts are suppressed.

**Acceptance Scenarios**:

1. **Given** a Compliance Officer says "Alert me when secure score drops below 70%," **When** the secure score drops to 68%, **Then** a DEGRADATION alert is created with the configured threshold context.
2. **Given** a suppression rule exists for resource group "rg-sandbox," **When** drift is detected on a resource in rg-sandbox, **Then** no alert is created and the suppression is logged.
3. **Given** a Compliance Officer creates a temporary suppression ("Snooze alerts for stgdev001 for 4 hours"), **When** the 4 hours expire, **Then** alerting resumes for that resource.
4. **Given** default rules exist for AC, SC, AU, IA control families, **When** monitoring is first enabled, **Then** alerts for those families fire at their default severity without additional configuration.
5. **Given** quiet hours are configured (10 PM–6 AM) for non-Critical alerts, **When** a Medium alert fires at 11 PM, **Then** the notification is held until 6 AM, but a Critical alert at 11 PM is delivered immediately.

---

### User Story 4 — Notification Channels & Escalation Paths (Priority: P2)

As a **Compliance Officer** or **Security Lead**, I want alerts delivered through multiple channels (in-app, chat, email, webhook) and automatically escalated when not acknowledged within SLA so that the right people are always reached.

In-app chat notifications are always enabled. Email notifications are configurable — immediate for Critical/High, daily digest for Medium/Low. Webhook notifications allow integration with external systems (incident management, chat platforms, SIEM). Escalation paths define the chain of notification recipients and actions triggered when an alert is not acknowledged within its SLA window.

**Why this priority**: The system can deliver value with in-app/chat notifications alone (which are always enabled). Email, webhook, and escalation are important for operational maturity but not required for an MVP.

**Independent Test**: Configure an email notification rule for Critical alerts. Trigger a Critical alert. Verify the email is sent. Configure an escalation path. Let an alert exceed its SLA. Verify escalation occurs (additional recipients notified, alert status updated).

**Acceptance Scenarios**:

1. **Given** a Critical alert is created, **When** the alert fires, **Then** a proactive chat message is sent to the Compliance Officer with alert summary and action buttons (Show details, Acknowledge, Fix it).
2. **Given** email notifications are configured for Critical alerts, **When** a Critical alert fires, **Then** an email is sent to the configured recipient within 2 minutes.
3. **Given** a webhook is configured for Slack, **When** any alert fires, **Then** a signed JSON payload is POSTed to the configured URL.
4. **Given** an escalation path is configured ("If Critical not acknowledged in 30 minutes, escalate to Security Lead"), **When** 30 minutes pass without acknowledgment, **Then** the Security Lead receives a notification and the alert moves to ESCALATED.
5. **Given** a daily email digest is configured, **When** Medium and Low alerts accumulate during the day, **Then** a single digest email is sent at the configured time summarizing all alerts.

---

### User Story 5 — Event-Driven & Real-Time Monitoring (Priority: P2)

As a **Platform Engineer**, I want the system to detect compliance-relevant changes in near real-time (within 5 minutes of the event) so that violations are caught immediately rather than at the next scheduled check.

Event-driven monitoring responds to platform activity events — resource creation, modification, policy assignment changes, and role assignment changes. When an event is detected, the system immediately runs a targeted compliance check on the affected resource. This mode supplements scheduled monitoring and can be enabled per scope.

**Why this priority**: Scheduled monitoring (US1) provides coverage, but event-driven adds responsiveness. This is P2 because the system delivers value with scheduled checks alone, and event-driven monitoring requires additional infrastructure (event subscriptions, webhooks).

**Independent Test**: Enable event-driven monitoring for a resource group. Create a new resource in that group. Verify that within 5 minutes, the system has run a compliance check and created an alert if the resource is non-compliant.

**Acceptance Scenarios**:

1. **Given** event-driven monitoring is enabled for rg-production, **When** a new resource is created in rg-production, **Then** a compliance check runs on that resource within 5 minutes.
2. **Given** event-driven monitoring is enabled, **When** a policy assignment is removed from a subscription, **Then** a POLICY DRIFT alert is created listing the controls no longer enforced, the actor who made the change, and the timestamp.
3. **Given** a role assignment change is detected, **When** a privileged role is granted, **Then** an access control compliance check runs on the affected scope.
4. **Given** event-driven monitoring is not enabled for a scope, **When** a change occurs in that scope, **Then** the change is detected at the next scheduled check (no immediate alert).

---

### User Story 6 — Alert Correlation & Noise Reduction (Priority: P3)

As a **Compliance Officer**, I want related alerts to be automatically grouped and correlated so that I see meaningful incidents rather than a flood of individual alerts.

The system correlates alerts in three ways: (1) Same-resource grouping — multiple changes to the same resource within 5 minutes merge into a single grouped alert. (2) Same-control grouping — multiple resources violating the same control are grouped by control. (3) Actor correlation — multiple security-relevant changes by the same actor within 5 minutes trigger an ANOMALY alert suggesting potential incident investigation.

**Why this priority**: Correlation is a quality-of-life improvement. The system functions correctly (albeit noisily) without it. Alert storms are handled by rate limiting (max 10 notifications per minute per channel) as a simpler fallback.

**Independent Test**: Simulate 5 changes to the same resource within 5 minutes. Verify only one grouped alert is created (not 5). Simulate one actor making 12 security changes in 5 minutes. Verify an ANOMALY alert is created.

**Acceptance Scenarios**:

1. **Given** 3 configuration changes occur on vm-web01 within 5 minutes, **When** the system processes these events, **Then** a single grouped alert is created listing all 3 changes.
2. **Given** 5 resources become non-compliant with AC-2 simultaneously, **When** alerts are created, **Then** they are grouped under a single "AC-2 violations detected on 5 resources" alert with expandable details.
3. **Given** one actor makes 12 security-related changes in 5 minutes, **When** the system detects this pattern, **Then** an ANOMALY alert is created suggesting possible incident investigation.
4. **Given** more than 50 alerts fire within 5 minutes, **When** the alert storm is detected, **Then** notifications are rate-limited and a summary alert is created: "50+ alerts in last 5 minutes — possible incident."

---

### User Story 7 — Compliance Dashboard Queries & Historical Reporting (Priority: P3)

As a **Compliance Officer** or **Auditor**, I want to query alert history, compliance trends, and monitoring statistics via chat so that I can understand compliance posture over time and support audit activities.

Users can ask natural-language questions about compliance state. The system provides filtered alert lists, compliance trend summaries, drift reports, actor activity, and statistical breakdowns. Auditors have read-only access to all alert and event history for audit trail purposes.

**Why this priority**: Reporting and history enhance the value of monitoring but are not required for core detection and response. The system can operate without historical queries.

**Independent Test**: Accumulate alerts over several days. Ask "What drifted this week?" and verify the system returns an accurate summary. Ask "Show alert statistics this month" and verify counts by severity and type.

**Acceptance Scenarios**:

1. **Given** alerts have accumulated over the past week, **When** a user asks "What drifted this week?", **Then** the system returns a summary of all drift detections with affected resources, controls, and resolution status.
2. **Given** a 30-day history exists, **When** a user asks "Show compliance trend for last 30 days," **Then** the system returns a summary of compliance percentage over time with directional indicators.
3. **Given** an Auditor asks "Show all alerts John dismissed this month," **When** the query runs, **Then** all dismissed alerts by that actor are listed with timestamps and justifications.
4. **Given** activity log data is available, **When** a user asks "Who made changes to Access Control settings?", **Then** the system lists actors, changes, timestamps, and any resulting alerts.

---

### User Story 8 — Integration with Existing Compliance Features (Priority: P3)

As a **Compliance Officer**, I want compliance alerts to integrate with the existing assessment, remediation, evidence, and reporting workflows so that monitoring data flows into the established compliance management processes.

Alerts can auto-create remediation tasks on the Kanban board ("Create task from alert ALT-123"). Resolving an alert can auto-close related remediation tasks. Alert state changes are captured as evidence events. Compliance reports and POA&M documents can include monitoring statistics and open alerts as findings.

**Why this priority**: These integrations improve workflow continuity but each integration point is bonus value — the monitoring system delivers its core value independently.

**Independent Test**: Create a task from an alert via chat. Verify the Kanban task is created with alert details. Resolve the alert and verify the task is auto-closed.

**Acceptance Scenarios**:

1. **Given** an active alert ALT-123 exists, **When** a user says "Create task from alert ALT-123," **Then** a remediation task is created on the Kanban board with the alert title, description, severity, and control mapping as task details.
2. **Given** a remediation task was created from an alert, **When** the alert is resolved, **Then** the corresponding Kanban task is automatically moved to Done.
3. **Given** a user says "Collect evidence for alert ALT-123," **When** evidence collection runs, **Then** the alert details, timeline, and remediation steps are captured as compliance evidence.
4. **Given** a user generates a POA&M report, **When** the report includes monitoring data, **Then** open alerts appear as findings with severity, control mapping, and remediation status.

---

### User Story 9 — Automated Response (Priority: P3)

As a **Compliance Officer**, I want to configure auto-remediation rules for trusted, low-risk violations so that known-good fixes are applied automatically without manual intervention.

Auto-remediation is disabled by default and requires explicit opt-in per rule. The Compliance Officer defines a scope, trigger condition, remediation action, and approval mode (auto-apply or require approval). For safety, high-risk control families (AC, IA, SC) cannot be auto-remediated — they always require human approval. All auto-remediations are logged for audit.

**Why this priority**: Auto-remediation is an advanced capability that builds on all prior stories. It is high-value for mature environments but carries risk, making it appropriate as a later addition.

**Independent Test**: Create an auto-remediation rule for missing tags. Trigger a missing-tag violation. Verify the tag is automatically applied and the remediation is logged.

**Acceptance Scenarios**:

1. **Given** an auto-remediation rule exists for missing required tags, **When** a resource is created without the required tags, **Then** the system automatically applies the tags and logs the remediation action.
2. **Given** an auto-remediation rule targets encryption violations, **When** encryption is disabled on a storage account, **Then** the system re-enables encryption and creates a RESOLUTION alert.
3. **Given** an auto-remediation rule targets the AC control family, **When** a user attempts to create the rule, **Then** the system rejects it with a message explaining that high-risk controls require human approval.
4. **Given** auto-remediation runs, **When** the remediation is executed, **Then** an audit log entry is created with the rule that triggered it, the action taken, the affected resource, and the outcome.

---

### Edge Cases

- What happens when the monitoring system itself loses connectivity? → The system retries with exponential backoff, creates a meta-alert ("Compliance monitoring connection lost for subscription XYZ. Retrying..."), and falls back to scheduled polling when event-driven monitoring fails.
- What happens during an alert storm (50+ alerts in 5 minutes)? → Notifications are rate-limited to a maximum of 10 per minute per channel, alerts are grouped into a summary, and the system suggests running a full assessment to understand scope.
- What happens when platform activity log data is unavailable? → The system falls back to scheduled polling and notes in the alert that actor information is unavailable.
- What happens when auto-remediation fails? → The remediation failure is logged, the alert remains active (or is moved back to NEW), and a notification is sent indicating manual intervention is needed.
- What happens when a suppression rule is active and a Critical alert fires? → Critical alerts fire immediately regardless of quiet hours. Permanent suppression rules for Critical resources require Compliance Officer justification and are visible to auditors.
- What happens when a user configures conflicting rules (e.g., "Alert Critical" and "Suppress all" on the same scope)? → Suppression rules take precedence over severity overrides. The system warns the user about the conflict.
- What happens when required monitoring permissions are missing? → The system alerts on setup: "Missing [specific permission]. [Specific detection capability] disabled." Monitoring continues for capabilities that have sufficient permissions.

## Requirements *(mandatory)*

### Functional Requirements

#### Monitoring Engine

- **FR-001**: System MUST support scheduled monitoring at configurable intervals: every 15 minutes, hourly, daily, and weekly.
- **FR-002**: System MUST capture a compliant baseline after each successful assessment or remediation.
- **FR-003**: System MUST compare current resource state against stored baselines during each monitoring run and detect deviations (drift).
- **FR-004**: System MUST detect baseline drift (resource configuration changed from known-good state), policy drift (policy assignments changed), compliance state drift (policy compliance state changed), and secure score drift (score dropped beyond threshold).
- **FR-005**: System MUST allow users to configure monitoring frequency per scope (subscription, resource group) via chat or Configuration Agent.
- **FR-006**: System MUST support event-driven monitoring that triggers compliance checks within 5 minutes of a qualifying platform event (resource created, modified, policy changed, role changed).
- **FR-007**: System MUST allow event-driven monitoring to be enabled or disabled per scope independently from scheduled monitoring.

#### Alerts

- **FR-008**: System MUST create alerts with the following attributes: auto-generated ID (format ALT-YYYYMMDDNNNNN), type, severity, timestamp, title, description, affected resources, control mapping, change details (old → new), actor, recommended action, and link to remediation.
- **FR-009**: System MUST classify alerts into severity levels: Critical (SLA < 1 hour), High (SLA < 4 hours), Medium (SLA < 24 hours), and Low (SLA < 7 days).
- **FR-010**: System MUST classify alerts into types: DRIFT, VIOLATION, DEGRADATION, ANOMALY, ESCALATION, and RESOLUTION.
- **FR-011**: System MUST enforce the alert lifecycle: NEW → ACKNOWLEDGED → IN_PROGRESS → RESOLVED or DISMISSED, plus ESCALATED when SLA expires.
- **FR-012**: System MUST auto-resolve alerts when a subsequent compliance check confirms the violation is corrected.
- **FR-013**: System MUST restrict alert dismissal to Compliance Officers and require a justification comment for every dismissal.
- **FR-014**: System MUST auto-escalate alerts that are not acknowledged within their configured SLA window.

#### Alert Rules & Suppression

- **FR-015**: System MUST support custom alert rules with configurable scope, trigger, severity override, recipient override, and schedule.
- **FR-016**: System MUST provide default alert rules for high-risk control families: AC (High), SC encryption changes (Critical), AU logging disabled (Critical), IA MFA changes (Critical).
- **FR-017**: System MUST support temporary suppression rules with automatic expiration (e.g., "snooze for 4 hours").
- **FR-018**: System MUST support permanent suppression rules that require Compliance Officer justification and are logged for audit.
- **FR-019**: System MUST support quiet hours configuration where non-Critical notifications are held until the quiet period ends; Critical alerts always fire immediately.

#### Notifications & Escalation

- **FR-020**: System MUST deliver in-app chat notifications for all alerts (always enabled, not configurable off).
- **FR-021**: System MUST support configurable email notifications — immediate for Critical/High, daily digest for Medium/Low.
- **FR-022**: System MUST support configurable webhook notifications with signed JSON payloads for external system integration.
- **FR-023**: System MUST support configurable escalation paths that define recipients, notification frequency, and external actions triggered when SLA is exceeded.
- **FR-024**: System MUST rate-limit notifications to a maximum of 10 per minute per channel to prevent alert fatigue during storms.

#### Correlation & Grouping

- **FR-025**: System MUST group multiple changes to the same resource within a 5-minute window into a single grouped alert.
- **FR-026**: System MUST group multiple resources violating the same control into a single alert with expandable detail.
- **FR-027**: System MUST detect anomalous patterns (e.g., one actor making many security-related changes in 5 minutes) and create an ANOMALY alert.

#### User Roles & Permissions

- **FR-028**: Compliance Officers MUST be able to receive all alerts, configure rules, acknowledge, dismiss (with justification), and set thresholds.
- **FR-029**: Platform Engineers MUST be able to receive alerts for assigned resources and acknowledge alerts, but MUST NOT be able to configure rules or dismiss alerts.
- **FR-030**: Security Leads MUST be able to receive Critical/High alerts, escalate alerts, and configure escalation paths.
- **FR-031**: Auditors MUST have read-only access to alert history and compliance events for audit purposes.

#### History & Queries

- **FR-032**: System MUST retain alert records for at least 2 years (configurable up to 7 years).
- **FR-033**: System MUST support natural-language chat queries for alert filtering, drift summaries, compliance trends, actor activity, and alert statistics.
- **FR-034**: System MUST store daily compliance state snapshots for at least 90 days and weekly snapshots for 2 years.

#### Integration

- **FR-035**: System MUST allow creating a Kanban remediation task from an alert via chat, with alert details pre-populated.
- **FR-036**: System MUST auto-close related Kanban tasks when an alert is resolved, if a linked task exists.
- **FR-037**: System MUST capture alert state changes as evidence events for evidence collection workflows.
- **FR-038**: System MUST include open alerts and monitoring statistics in compliance reports and POA&M documents when requested.

#### Automated Response

- **FR-039**: System MUST support opt-in auto-remediation rules with configurable scope, trigger, action, and approval mode.
- **FR-040**: System MUST prohibit auto-remediation for high-risk control families (AC, IA, SC) — these MUST always require human approval.
- **FR-041**: System MUST log all auto-remediation actions for audit, including the triggering rule, action taken, affected resource, and outcome.

#### System Health & Error Handling

- **FR-042**: System MUST retry failed monitoring connections with exponential backoff and create a meta-alert when monitoring is interrupted.
- **FR-043**: System MUST detect missing monitoring permissions at setup and alert the user with specific details about which capabilities are disabled.

### Key Entities

- **MonitoringConfiguration**: Defines the monitoring mode, frequency, scope, and enabled state for a subscription or resource group.
- **ComplianceBaseline**: A point-in-time snapshot of a resource's compliant configuration, captured after successful assessment or remediation. Used as the reference point for drift detection.
- **ComplianceAlert**: A detected compliance issue with ID, type, severity, lifecycle state, affected resources, control mapping, change details, actor, timestamps, and resolution history.
- **AlertRule**: A user-defined or default rule that specifies scope, trigger conditions, severity overrides, recipients, and active schedule.
- **SuppressionRule**: A temporary or permanent rule that mutes alerts for a defined scope, with expiration (if temporary) and justification (if permanent).
- **EscalationPath**: A chain of notification actions triggered when an alert is not acknowledged within its SLA window — defines recipients, frequency, and external integrations.
- **AlertNotification**: A record of a notification sent through a specific channel (chat, email, webhook) for a specific alert.
- **ComplianceSnapshot**: A periodic capture of overall compliance state (scores, control status, resource counts) used for trend analysis and historical reporting.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Scheduled monitoring detects drift within one monitoring interval of the configuration change (e.g., within 1 hour for hourly monitoring).
- **SC-002**: Event-driven monitoring creates alerts within 5 minutes of a qualifying platform event.
- **SC-003**: 100% of Critical and High alerts are delivered via configured notification channels within 2 minutes of detection.
- **SC-004**: Alerts not acknowledged within SLA are automatically escalated within 5 minutes of SLA expiration.
- **SC-005**: Alert correlation reduces duplicate notifications by at least 60% compared to uncorrelated alerting during multi-change events.
- **SC-006**: Users can view, acknowledge, and act on alerts through chat in under 30 seconds per interaction.
- **SC-007**: Compliance Officers can create or modify alert rules and suppressions via chat in under 2 minutes.
- **SC-008**: Historical queries (trend, drift summary, statistics) return results within 5 seconds for 90-day lookback periods.
- **SC-009**: Auto-resolved alerts are marked RESOLVED within one monitoring interval of the remediation being applied.
- **SC-010**: All alert state transitions, dismissals, and auto-remediations are auditable with complete actor and timestamp details.

## Assumptions

- The existing Compliance Agent and assessment infrastructure (Feature 001) is operational and provides the compliance engine, NIST control mappings, and remediation capabilities that monitoring extends.
- The existing Kanban board (Feature 002) and user context (Feature 004) are available for task creation and user identity resolution.
- Platform activity events (resource changes, policy changes) are accessible through the cloud provider's activity log or equivalent event mechanism.
- Email and webhook delivery infrastructure is provided by the hosting environment — the system formats and queues notifications but delegates transport to external services.
- Secure score and policy compliance state data are accessible through the cloud provider's security and policy APIs.
- Initial deployment will use the existing data store (same as the rest of the system) for alerts and configuration, with optional long-term storage for historical snapshots as a future enhancement.
- Monitoring intervals shorter than 15 minutes are not supported in the initial release to manage cost and rate-limiting concerns.
- Continuous stream monitoring (sub-60-second latency) is deferred to a future release as it requires persistent connections and premium event infrastructure.
