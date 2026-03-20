# RMF Phase 6: Monitor

> Maintain ongoing situational awareness about the security and privacy posture of the system and the organization.

---

## Phase Overview

| Attribute | Value |
|-----------|-------|
| **Phase Number** | 6 |
| **NIST Reference** | SP 800-37 Rev. 2, §3.7 |
| **Lead Persona** | ISSM (oversight) and ISSO (day-to-day) |
| **Supporting Personas** | AO (escalation), SCA (periodic assessment), Engineer (remediation) |
| **Key Outcome** | Continuous monitoring established, ConMon reports generated, reauthorization tracked |

---

## Persona Responsibilities

### ISSM (Lead — Oversight)

**Tasks in this phase**:

1. Create ConMon plan → Tool: `compliance_create_conmon_plan`
2. Generate ConMon reports → Tool: `compliance_generate_conmon_report`
3. Track ATO expiration → Tool: `compliance_track_ato_expiration`
4. Report significant changes → Tool: `compliance_report_significant_change`
5. Check reauthorization triggers → Tool: `compliance_reauthorization_workflow`
6. View portfolio dashboard → Tool: `compliance_multi_system_dashboard`
7. Send notifications → Tool: `compliance_send_notification`
8. Export to eMASS → Tool: `compliance_export_emass`
9. Export OSCAL → Tool: `compliance_export_oscal`

**Natural Language Queries**:

> **"Create a ConMon plan for system {id} with monthly assessments and annual review on June 15"** → `compliance_create_conmon_plan` — establishes monitoring cadence

> **"Generate a monthly ConMon report for system {id}, period 2026-02"** → `compliance_generate_conmon_report` — report with score delta, findings, POA&M status

> **"Check ATO expiration for system {id}"** → `compliance_track_ato_expiration` — graduated alerts (90d/60d/30d/expired)

> **"Report a significant change for system {id}: New Interconnection — added VPN tunnel to partner organization"** → `compliance_report_significant_change` — records change, flags for reauthorization

> **"Check reauthorization triggers for system {id}"** → `compliance_reauthorization_workflow` — checks expiration, significant changes, score drift

> **"Show the multi-system compliance dashboard"** → `compliance_multi_system_dashboard` — portfolio view of all systems

> **"Show all systems with expired ATOs"** → `compliance_multi_system_dashboard` — filtered portfolio view

> **"Export system {id} data to eMASS format"** → `compliance_export_emass` — eMASS-compatible Excel

> **"Export OSCAL JSON for system {id}"** → `compliance_export_oscal` — OSCAL v1.0.6 JSON

### ISSO (Lead — Day-to-Day)

**Tasks in this phase**:

1. Enable monitoring → Tool: `watch_enable_monitoring`
2. View/triage alerts → Tool: `watch_show_alerts`, `watch_get_alert`
3. Acknowledge alerts → Tool: `watch_acknowledge_alert`
4. Fix alerts → Tool: `watch_fix_alert`
5. Configure notifications → Tool: `watch_configure_notifications`
6. Configure escalation → Tool: `watch_configure_escalation`
7. Set quiet hours → Tool: `watch_configure_quiet_hours`

**Natural Language Queries**:

> **"Enable daily monitoring for subscription {sub-id}"** → `watch_enable_monitoring` — enables scheduled daily scans

> **"Show all critical alerts from the last 7 days"** → `watch_show_alerts` — filtered alert list

> **"Acknowledge alert ALT-12345"** → `watch_acknowledge_alert` — pauses SLA escalation

> **"What drifted this week?"** → `watch_show_alerts` — drift alerts for recent period

> **"Configure escalation: if Critical alert is not acknowledged in 30 minutes, notify the ISSM"** → `watch_configure_escalation` — defines escalation path

### AO (Escalation)

**Tasks in this phase**:

- Receive escalated alerts and expiration notifications
- Review risk acceptances approaching expiration
- Re-evaluate authorization when reauthorization triggers fire

**Natural Language Queries**:

> **"Show all systems with ATOs expiring in the next 90 days"** → `compliance_track_ato_expiration` — portfolio expiration view

> **"What significant changes have been reported for system {id}?"** → change records and reauthorization flags

### SCA (Periodic Assessment)

- Conduct periodic assessments as specified in the ConMon plan
- Uses the same assessment tools as Phase 4 (Assess)

### Engineer (Remediation)

- Fix drift findings surfaced by Watch alerts  
- Uses Kanban tools and `watch_fix_alert` for remediation

---

## ISA/MOU Expiration Monitoring

Interconnection agreements have expiration dates that must be tracked as part of continuous monitoring.

### Monitoring Agreement Status

```
Tool: compliance_validate_agreements
Parameters:
  system_id: "<system-guid>"
```

Returns the status of all ISA/MOU agreements including:
- Active agreements with expiration dates
- Agreements expiring within 90 days (flagged)
- Expired agreements requiring renewal

### Agreement Expiration Cadence

| Time Remaining | Action |
|----------------|--------|
| ≤ 90 days | Begin renewal planning with remote system POC |
| ≤ 30 days | Escalate to ISSM for agreement renewal |
| Expired | ISSM must renew or terminate the interconnection |

---

## PIA Annual Review

Privacy Impact Assessments require annual review to confirm continued accuracy.

### Tracking PIA Review Cycles

```
Tool: compliance_check_privacy_compliance
Parameters:
  system_id: "<system-guid>"
```

Flags PIAs approaching their annual review date. The ISSM uses `compliance_review_pia` to re-approve or request updates.

---

## SSP Section Status Monitoring

Track SSP section status as part of ongoing system maintenance.

```
Tool: compliance_ssp_completeness
Parameters:
  system_id: "<system-guid>"
```

Monitor for sections that revert from Approved to Draft after significant changes. Any section status regression should trigger ISSM review.

---

## Prisma Cloud Periodic Re-Import

Prisma Cloud scan data should be re-imported periodically as a ConMon data source to track cloud posture drift.

### Recommended Cadence

| ConMon Plan Interval | Prisma Import Cadence | Purpose |
|----------------------|----------------------|---------|
| Monthly | Monthly | Track incremental drift and new policy violations |
| Quarterly | Quarterly | Comprehensive cloud posture reassessment |
| Annual | Monthly + annual comprehensive | Continuous monitoring with annual deep review |

### Trend Analysis for Drift Detection

After each re-import, use `compliance_prisma_trend` to compare against previous scans:

```
Tool: compliance_prisma_trend
Parameters:
  system_id: "<system-guid>"
  group_by: "nist_control"
```

Key metrics to monitor:

- **`newFindings`**: Cloud policy violations discovered since last scan — investigate promptly
- **`resolvedFindings`**: Successfully remediated issues — validate they stay resolved
- **`remediationRate`**: Percentage trend — should improve or stay stable over time
- **`nist_control_breakdown`**: Identifies which control families have growing exposure

### Integration with ConMon Reports

Prisma-sourced findings automatically appear in `compliance_generate_conmon_report` output:

- Open Prisma findings contribute to the open findings count
- Resolved Prisma findings contribute to the resolved findings count
- Effectiveness records from Prisma imports update control assessment status

---

## Expiration Alert Levels

| Days Remaining | Alert Level | Severity | Action Required |
|----------------|-------------|----------|-----------------|
| ≤ 90 days | Info | Low | Begin reauthorization planning |
| ≤ 60 days | Warning | Medium | Submit reauthorization package |
| ≤ 30 days | Urgent | High | Escalate to AO immediately |
| Expired | Expired | Critical | System operating without authorization |

---

## Significant Change Types

| # | Change Type | Example |
|---|------------|---------|
| 1 | New Interconnection | Added VPN tunnel to partner |
| 2 | Major Upgrade | OS or platform version upgrade |
| 3 | Data Type Change | New PII data processing |
| 4 | Architecture Change | Moved from VMs to containers |
| 5 | Security Policy Change | New encryption requirements |
| 6 | Boundary Change | Added/removed cloud services |
| 7 | Key Personnel Change | New ISSO or AO assignment |
| 8 | Incident Response | Security incident on the system |
| 9 | Compliance Drop | Score decreased below threshold |
| 10 | Configuration Drift | Auto-detected by Watch service |

---

## Documents Produced

| Document | Owner | Format | Gate Dependency |
|----------|-------|--------|----------------|
| ConMon Reports (monthly/quarterly/annual) | ISSM / ISSO | Markdown | Ongoing (per ConMon plan) |
| Reauthorization Package | ISSM | Bundled | Triggers loop back to Assess |

---

## Phase Gates

This is a continuous phase — there is no outbound transition gate. Reauthorization triggers loop back to earlier phases:

| Trigger | Action |
|---------|--------|
| ATO expiration | Initiate reauthorization (loop to Assess or Prepare) |
| Significant change (reauth-triggering) | Initiate reauthorization |
| Compliance score drift below threshold | Investigate and potentially reauthorize |

---

## Air-Gapped Considerations

!!! warning "Disconnected / Air-Gapped Monitor Phase"
    In air-gapped or disconnected environments:
    
    - **eMASS export** (`compliance_export_emass`) generates Excel locally — manual transfer via removable media required.
    - **Notifications** (`compliance_send_notification`) limited to local channels (VS Code, audit log).
    - **OSCAL export** (`compliance_export_oscal`) works fully offline.
    - **Watch monitoring** — event-driven mode unavailable; use **scheduled-only mode** with local policy cache.
    - **ConMon reports** (`compliance_generate_conmon_report`) work fully offline using cached data.

---

## See Also

- [Previous Phase: Authorize](authorize.md)
- [ISSM Guide](../guides/issm-guide.md) — Portfolio management and oversight
- [ISSO Guide](../personas/isso.md) — Day-to-day monitoring workflows
- [Compliance Watch Guide](../guides/compliance-watch.md) — Detailed Watch documentation
- [AO Guide](../guides/ao-quick-reference.md) — Reauthorization and risk decisions
- [POA&M Management Guide](../guides/poam-management.md) — POA&M trend tracking and lifecycle

### POA&M Trend Monitoring (Feature 039)

During continuous monitoring, use the POA&M Trends tab or `compliance_poam_trend` to track open-over-time trends, closure rates, aging breakdown, and time-to-close distributions. Export trend reports as PDF for ConMon reporting.

### Package Re-generation for Continuous Authorization (Feature 041)

When significant changes occur during monitoring, re-generate the authorization package:

- `compliance_generate_package` — create an updated package reflecting current system state
- View **package history** on the Documents page to compare current vs. previous packages
- Expired packages (>90 days) retain metadata but ZIP files are automatically cleaned up
