# Environment Checklist: Persona End-to-End Test Suite

**Feature**: 020 | **Date**: _______________ | **Validated By**: _______________

---

## Purpose

This checklist must be completed **before** any persona test case execution begins. Every item must be verified to ensure a valid test environment. If any critical item fails, resolve it before proceeding.

---

## 1. MCP Server

| # | Check | Command / Action | Expected Result | Status | Notes |
|---|-------|-----------------|-----------------|--------|-------|
| 1.1 | Solution builds | `dotnet build Ato.Copilot.sln` | Build succeeded, 0 errors | ⬜ | |
| 1.2 | MCP server starts | `dotnet run --project src/Ato.Copilot.Mcp` | Server listening on configured port | ⬜ | Port: ___ |
| 1.3 | Health endpoint responds | `curl http://localhost:{port}/health` | 200 OK within 5s | ⬜ | |
| 1.4 | Tool count ≥ 145 | Query `/tools/list` endpoint | Returns ≥ 145 registered tools | ⬜ | Actual: ___ |
| 1.5 | All 106 spec tools present | Cross-reference `/tools/list` against research.md tool list | 0 missing tools | ⬜ | Missing: ___ |

### 106 Spec-Referenced Tools

Verify each tool is in the `/tools/list` response:

<details>
<summary>Click to expand full tool list</summary>

**System & RMF Lifecycle (11)**
- [ ] `compliance_register_system`
- [ ] `compliance_define_boundary`
- [ ] `compliance_exclude_from_boundary`
- [ ] `compliance_assign_rmf_role`
- [ ] `compliance_list_rmf_roles`
- [ ] `compliance_advance_rmf_step`
- [ ] `compliance_suggest_info_types`
- [ ] `compliance_categorize_system`
- [ ] `compliance_get_categorization`
- [ ] `compliance_select_baseline`
- [ ] `compliance_get_baseline`

**Baseline & Tailoring (3)**
- [ ] `compliance_tailor_baseline`
- [ ] `compliance_set_inheritance`
- [ ] `compliance_generate_crm`

**Narratives & SSP (6)**
- [ ] `compliance_narrative_progress`
- [ ] `compliance_suggest_narrative`
- [ ] `compliance_write_narrative`
- [ ] `compliance_batch_populate_narratives`
- [ ] `compliance_generate_ssp`
- [ ] `compliance_collect_evidence`

**Narrative Governance (8)**
- [ ] `compliance_narrative_history`
- [ ] `compliance_narrative_diff`
- [ ] `compliance_rollback_narrative`
- [ ] `compliance_submit_narrative`
- [ ] `compliance_review_narrative`
- [ ] `compliance_batch_review_narratives`
- [ ] `compliance_narrative_approval_progress`
- [ ] `compliance_batch_submit_narratives`

**Assessment (7)**
- [ ] `compliance_take_snapshot`
- [ ] `compliance_compare_snapshots`
- [ ] `compliance_check_evidence_completeness`
- [ ] `compliance_verify_evidence`
- [ ] `compliance_assess_control`
- [ ] `compliance_assess`
- [ ] `compliance_generate_sar`

**SAP (5)**
- [ ] `compliance_generate_sap`
- [ ] `compliance_update_sap`
- [ ] `compliance_finalize_sap`
- [ ] `compliance_get_sap`
- [ ] `compliance_list_saps`

**Risk & Authorization (7)**
- [ ] `compliance_generate_rar`
- [ ] `compliance_create_poam`
- [ ] `compliance_list_poam`
- [ ] `compliance_show_risk_register`
- [ ] `compliance_bundle_authorization_package`
- [ ] `compliance_issue_authorization`
- [ ] `compliance_accept_risk`

**Remediation (4)**
- [ ] `compliance_generate_plan`
- [ ] `compliance_remediate`
- [ ] `compliance_validate_remediation`
- [ ] `compliance_get_control_family`

**STIG & IaC (1)**
- [ ] `compliance_show_stig_mapping`

**Monitoring & ConMon (11)**
- [ ] `compliance_create_conmon_plan`
- [ ] `compliance_generate_conmon_report`
- [ ] `compliance_track_ato_expiration`
- [ ] `compliance_report_significant_change`
- [ ] `compliance_reauthorization_workflow`
- [ ] `compliance_multi_system_dashboard`
- [ ] `compliance_export_emass`
- [ ] `compliance_audit_log`
- [ ] `watch_enable_monitoring`
- [ ] `watch_monitoring_status`
- [ ] `watch_show_alerts`

**Watch Alerts (6)**
- [ ] `watch_get_alert`
- [ ] `watch_acknowledge_alert`
- [ ] `watch_fix_alert`
- [ ] `watch_dismiss_alert`
- [ ] `watch_alert_history`
- [ ] `watch_compliance_trend`

**Import (8)**
- [ ] `compliance_import_ckl`
- [ ] `compliance_import_xccdf`
- [ ] `compliance_list_imports`
- [ ] `compliance_get_import_summary`
- [ ] `compliance_import_prisma_csv`
- [ ] `compliance_import_prisma_api`
- [ ] `compliance_import_nessus`
- [ ] `compliance_list_nessus_imports`

**Prisma (2)**
- [ ] `compliance_list_prisma_policies`
- [ ] `compliance_prisma_trend`

**Privacy Analysis (4)** — F021
- [ ] `compliance_create_pta`
- [ ] `compliance_generate_pia`
- [ ] `compliance_review_pia`
- [ ] `compliance_check_privacy_compliance`

**Interconnection Management (8)** — F021
- [ ] `compliance_add_interconnection`
- [ ] `compliance_list_interconnections`
- [ ] `compliance_update_interconnection`
- [ ] `compliance_generate_isa`
- [ ] `compliance_register_agreement`
- [ ] `compliance_update_agreement`
- [ ] `compliance_validate_agreements`
- [ ] `compliance_certify_no_interconnections`

**SSP Authoring & OSCAL (5)** — F022
- [ ] `compliance_write_ssp_section`
- [ ] `compliance_review_ssp_section`
- [ ] `compliance_ssp_completeness`
- [ ] `compliance_export_oscal_ssp`
- [ ] `compliance_validate_oscal_ssp`

**CKL Export (1)** — F017
- [ ] `compliance_export_ckl`

**Kanban (9)**
- [ ] `kanban_create_board`
- [ ] `kanban_bulk_update`
- [ ] `kanban_export`
- [ ] `kanban_task_list`
- [ ] `kanban_get_task`
- [ ] `kanban_move_task`
- [ ] `kanban_assign_task`
- [ ] `kanban_remediate_task`
- [ ] `kanban_task_validate`

**Kanban Evidence & Comments (2)**
- [ ] `kanban_collect_evidence`
- [ ] `kanban_add_comment`

**PIM / Auth (8)**
- [ ] `cac_status`
- [ ] `pim_list_eligible`
- [ ] `pim_activate_role`
- [ ] `pim_list_active`
- [ ] `pim_deactivate_role`
- [ ] `pim_approve_request`
- [ ] `pim_deny_request`
- [ ] `jit_request_access`

**Tool Count**: 106 tools | **Verified**: ___/106

**HW/SW Inventory (9)** — F025
- [ ] `inventory_add_item`
- [ ] `inventory_update_item`
- [ ] `inventory_decommission_item`
- [ ] `inventory_list`
- [ ] `inventory_get`
- [ ] `inventory_export`
- [ ] `inventory_import`
- [ ] `inventory_completeness`
- [ ] `inventory_auto_seed`

**Inventory Tool Count**: 9 tools | **Verified**: ___/9

</details>

---

## 2. VS Code Extension

| # | Check | Action | Expected Result | Status | Notes |
|---|-------|--------|-----------------|--------|-------|
| 2.1 | `@ato` extension installed | Check VS Code Extensions panel | Security Posture Intelligence Navigator extension listed and enabled | ⬜ | Version: ___ |
| 2.2 | `@ato` chat participant active | Type `@ato` in VS Code chat | Auto-complete shows `@ato` participant | ⬜ | |
| 2.3 | Extension connected to MCP | `@ato Show my active PIM roles` | Returns response (not connection error) | ⬜ | |

---

## 3. Microsoft Teams

| # | Check | Action | Expected Result | Status | Notes |
|---|-------|--------|-----------------|--------|-------|
| 3.1 | Security Posture Intelligence Navigator bot installed | Check Teams Apps | Bot listed and accessible | ⬜ | |
| 3.2 | Bot responds to queries | Send any compliance query | Bot returns Adaptive Card response | ⬜ | |
| 3.3 | Adaptive Cards render | Send a query with structured output | Card renders with proper formatting | ⬜ | |

---

## 4. Azure Government Subscription

| # | Check | Action | Expected Result | Status | Notes |
|---|-------|--------|-----------------|--------|-------|
| 4.1 | Subscription accessible | `az account show --subscription sub-12345-abcde` | Subscription details returned | ⬜ | |
| 4.2 | Azure Government cloud | Check environment in subscription | Environment = AzureUSGovernment | ⬜ | |
| 4.3 | Test resources provisioned | Verify VMs, SQL, Key Vault exist | ≥ 3 resources for boundary definition | ⬜ | |

---

## 5. PIM Role Eligibility

| # | Role | PIM Eligible | Max Duration | Status | Notes |
|---|------|-------------|-------------|--------|-------|
| 5.1 | `Compliance.SecurityLead` (ISSM) | ⬜ | ___ hours | ⬜ | |
| 5.2 | `Compliance.Analyst` (ISSO) | ⬜ | ___ hours | ⬜ | |
| 5.3 | `Compliance.Auditor` (SCA) | ⬜ | ___ hours | ⬜ | |
| 5.4 | `Compliance.AuthorizingOfficial` (AO) | ⬜ | ___ hours | ⬜ | |
| 5.5 | `Compliance.PlatformEngineer` (Engineer) | ⬜ | ___ hours | ⬜ | |

**Verification Command**: `@ato What PIM roles am I eligible for?`

---

## 6. Test Data Prerequisites

| # | Check | Action | Expected Result | Status | Notes |
|---|-------|--------|-----------------|--------|-------|
| 6.1 | No existing "Eagle Eye" system | Query for system by name | System not found (clean slate) | ⬜ | If exists: delete or use alt name |
| 6.2 | Sample Prisma CSV available | Locate in test data directory | CSV with columns: Alert ID, Severity, Policy Name, Resource, NIST Mapping | ⬜ | Path: ___ |
| 6.3 | Prisma API JSON available | Locate in test data directory | JSON with alertRules, resourceConfig, remediationCli | ⬜ | Path: ___ |
| 6.4 | CKL checklist file available | Locate in test data directory | `.ckl` with STIG evaluation results | ⬜ | Path: ___ |
| 6.5 | XCCDF results file available | Locate in test data directory | `.xml` SCAP scan results with benchmark scores | ⬜ | Path: ___ |
| 6.6 | Nessus scan file available | Locate in test data directory | `.nessus` XML with ≥ 2 hosts and ≥ 20 plugins | ⬜ | Path: ___ |

---

## 7. Time & Logistics

| # | Check | Details | Status |
|---|-------|---------|--------|
| 7.1 | Estimated duration | 5–7 hours for full suite (172 tests + 4 scenarios) | ⬜ Scheduled |
| 7.2 | Execution order confirmed | ISSM → ISSO → SCA → AO → Engineer → Cross-Persona | ⬜ Understood |
| 7.3 | Results template ready | `specs/020-persona-test-cases/results-template.md` | ⬜ Available |
| 7.4 | Spec open for reference | `specs/020-persona-test-cases/spec.md` | ⬜ Open |
| 7.5 | Quickstart guide reviewed | `specs/020-persona-test-cases/quickstart.md` | ⬜ Reviewed |

---

## Overall Readiness

| Section | Items | Passed | Status |
|---------|-------|--------|--------|
| MCP Server | 5 | ___ | ⬜ |
| VS Code Extension | 3 | ___ | ⬜ |
| Microsoft Teams | 3 | ___ | ⬜ |
| Azure Government | 3 | ___ | ⬜ |
| PIM Roles | 5 | ___ | ⬜ |
| Test Data | 6 | ___ | ⬜ |
| Logistics | 5 | ___ | ⬜ |
| **Total** | **30** | **___** | ⬜ |

### Gate Decision

- ⬜ **GO** — All critical checks passed. Proceed to persona testing.
- ⬜ **NO-GO** — Critical failures found. Resolve before proceeding.

**Blocking Issues**: _______________

**Approved By**: _______________ | **Date**: _______________
