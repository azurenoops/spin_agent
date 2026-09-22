# Troubleshooting

> Common errors, causes, and resolutions for Security Posture Intelligence Navigator organized by category.

---

## 1. RBAC / Authorization Errors

Errors caused by insufficient permissions for the requested operation.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Access denied: Compliance.Auditor cannot invoke compliance_write_narrative` | SCA attempting to modify SSP (write operation) | SCA role is read-only by design. Ask the assigned ISSO or Engineer to write the narrative. |
| `Access denied: Compliance.PlatformEngineer cannot invoke compliance_issue_authorization` | Engineer attempting to issue an ATO decision | Only the AO (`Compliance.AuthorizingOfficial`) can issue authorization decisions. |
| `Access denied: Compliance.Analyst cannot invoke watch_dismiss_alert` | ISSO attempting to dismiss an alert | Only officers (`Compliance.SecurityLead` or above) can dismiss alerts. Escalate to ISSM. |
| `Role not recognized` | CAC certificate not mapped to any RBAC role | Contact Administrator to map your CAC thumbprint, or verify Azure AD group membership. Default fallback is `PlatformEngineer`. |

!!! tip "RBAC Role Chain"
    Role resolution follows the 4-tier chain: **CAC → Azure AD → System Role → Default**.
    If you're getting unexpected access denials, check each tier in order.
    See the [Persona Overview](../personas/index.md#rbac-role-resolution) for details.

---

## 2. RMF Gate Validation Errors

Errors when trying to advance between RMF phases without meeting gate conditions.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Cannot advance: Prepare → Categorize requires at least 1 RMF role and 1 boundary resource` | Tried to advance before assigning roles or defining boundary | Use `compliance_assign_rmf_role` and `compliance_define_boundary` first. |
| `Cannot advance: Categorize → Select requires SecurityCategorization` | Tried to advance before categorizing | Use `compliance_categorize_system` with at least one information type. |
| `Cannot advance: Select → Implement requires ControlBaseline` | Tried to advance before selecting baseline | Use `compliance_select_baseline` to select Low/Moderate/High baseline. |
| `Cannot regress RMF step without force: true` | Tried to move backward in the RMF lifecycle | Add `force: true` parameter to `compliance_advance_rmf_step` (ISSM only). This is intentionally guarded. |
| `System in DATO status: advancement blocked` | AO denied authorization; system is in read-only mode | The AO must issue a new authorization decision (ATO/ATOwC/IATT) before the system can advance. |

!!! info "Gate Conditions Reference"
    For the full list of gate conditions per phase, see [RMF Phase Overview](../rmf-phases/index.md).

---

## 3. Evidence & Integrity Errors

Errors related to evidence collection, verification, and snapshots.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Evidence verification failed: hash mismatch` | Evidence artifact was modified after collection | Re-collect evidence using `compliance_collect_evidence`. If the modification was intentional, document the change and re-collect. |
| `Evidence completeness: 12 controls missing evidence` | Not all controls in scope have associated evidence | Use `compliance_check_evidence_completeness` to identify gaps, then collect evidence for each missing control. |
| `Snapshot creation failed: no assessment data` | Tried to take a snapshot before any controls were assessed | Assess at least one control using `compliance_assess_control` first. |

---

## 4. Authorization & Risk Errors

Errors during the Authorize phase and risk management lifecycle.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Risk acceptance expired` | An accepted risk passed its expiration date | The AO must either re-accept the risk with a new expiration date or the finding reverts to active. Linked POA&M items revert to `Ongoing`. |
| `Authorization superseded` | A new authorization decision deactivated the prior one | Expected behavior — only one active authorization per system. Review the new decision. |
| `Cannot bundle: SAR not generated` | Tried to bundle authorization package before SCA generated the SAR | SCA must complete assessment and run `compliance_generate_sar` first. |
| `Cannot bundle: SSP not generated` | SSP has not been generated yet | Run `compliance_generate_ssp` to produce the SSP before bundling. |

---

## 5. Azure Connectivity Errors

Errors when connecting to Azure services for evidence collection or policy evaluation.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Azure Policy query failed: connection timeout` | Cannot reach Azure Resource Manager APIs | Check network connectivity. In air-gapped environments, use scheduled monitoring with local policy cache. |
| `Defender for Cloud unavailable` | Azure Defender not enabled or unreachable | Verify Defender for Cloud is enabled on the subscription. In disconnected mode, assessment runs against cached policy data only. |
| `Subscription not found: {sub-id}` | Subscription ID is incorrect or Security Posture Intelligence Navigator lacks access | Verify the subscription ID and ensure the Security Posture Intelligence Navigator service principal has Reader access. |

!!! warning "Air-Gapped Environments"
    In air-gapped (IL5+) environments, Azure connectivity errors are expected during offline periods.
    Use scheduled monitoring mode (`watch_configure_monitoring` with `mode: "scheduled"`) and set
    the interval to match your data-transfer window. See the [Compliance Watch Guide](../guides/compliance-watch.md)
    for air-gapped configuration details.

---

## 6. Monitoring & Alert Errors

Errors from the Compliance Watch system.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Monitoring already enabled for subscription {sub-id}` | Attempted to enable monitoring that is already active | Use `watch_configure_monitoring` to update frequency or mode instead. |
| `Alert not found: ALT-{id}` | Alert ID is incorrect or alert has been archived | Use `watch_show_alerts` to list current alerts. Archived alerts are available via `watch_alert_history`. |
| `Cannot dismiss: justification required` | Tried to dismiss alert without providing a reason | Include a justification string when calling `watch_dismiss_alert`. |
| `SLA escalation triggered` | Alert remained unacknowledged beyond SLA threshold | Acknowledge the alert immediately. Review escalation configuration with `watch_configure_escalation`. |
| `Quiet hours active: notification suppressed` | Notification not delivered due to quiet hours configuration | Alert is still recorded — check `watch_show_alerts` after quiet hours end. Adjust with `watch_configure_quiet_hours`. |

---

## 7. Kanban & Remediation Errors

Errors in the remediation task lifecycle.

| Error | Cause | Resolution |
|-------|-------|------------|
| `Cannot move to Done: validation failed` | Task remediation did not pass re-scan validation | Review the validation results, fix remaining issues, and re-validate with `kanban_task_validate`. An officer can override with `force: true`. |
| `Cannot move from Blocked: resolution comment required` | Tried to unblock a task without explaining the resolution | Add a comment explaining how the blocker was resolved before moving the task. |
| `Cannot reopen: Done is terminal` | Tried to move a completed task back to an earlier column | Create a new task if additional work is needed on the same finding. |
| `Dry run completed: no changes applied` | Used `dryRun: true` on remediation | Expected behavior — review the dry run output, then re-run without `dryRun` to apply changes. |

---

## General Troubleshooting Steps

If you encounter an error not listed above:

1. **Check your role** — Run `pim_list_active` to verify your current RBAC role
2. **Check system phase** — Run `compliance_get_system` to verify the system's current RMF phase
3. **Check audit log** — Run `compliance_audit_log` to see the full audit trail of recent actions
4. **Retry with verbose output** — Some tools support additional detail when errors occur
5. **Contact ISSM** — If the issue persists, the ISSM can review the audit trail and escalate

---

## See Also

- [Tool Inventory](tool-inventory.md) — All 114 tools with RBAC roles
- [Persona Overview](../personas/index.md) — Role definitions and RBAC resolution
- [RMF Phase Overview](../rmf-phases/index.md) — Gate conditions per phase
- [Compliance Watch Guide](../guides/compliance-watch.md) — Monitoring and alert management
