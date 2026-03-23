# Tool Inventory

> Complete reference of all 140 MCP tools available in ATO Copilot, grouped by category.

---

## How to Use This Reference

Each tool listing includes the tool name, a short description, the RMF phase(s) where it applies, and which RBAC roles can invoke it.

**RBAC Role Abbreviations**:

| Abbreviation | RBAC Role |
|--------------|-----------|
| **ISSM** | `Compliance.SecurityLead` |
| **ISSO** | `Compliance.Analyst` |
| **SCA** | `Compliance.Auditor` |
| **AO** | `Compliance.AuthorizingOfficial` |
| **Eng** | `Compliance.PlatformEngineer` (default) |
| **Admin** | `Compliance.Administrator` |

---

## Category 1: RMF Lifecycle Tools (34 tools)

Tools that drive the seven-phase RMF lifecycle from Prepare through Authorize.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 1 | `compliance_register_system` | Register a new information system | Prepare | ISSM |
| 2 | `compliance_list_systems` | List registered systems | All | All |
| 3 | `compliance_get_system` | Get system details | All | All |
| 4 | `compliance_advance_rmf_step` | Advance or regress RMF phase | All | ISSM |
| 5 | `compliance_define_boundary` | Define authorization boundary | Prepare | ISSM |
| 6 | `compliance_exclude_from_boundary` | Exclude resource from boundary | Prepare | ISSM |
| 7 | `compliance_assign_rmf_role` | Assign RMF role to user | Prepare | ISSM |
| 8 | `compliance_list_rmf_roles` | List role assignments | Prepare | ISSM, ISSO |
| 9 | `compliance_categorize_system` | FIPS 199 categorization | Categorize | ISSM |
| 10 | `compliance_get_categorization` | View categorization | Categorize | All |
| 11 | `compliance_suggest_info_types` | AI-suggest SP 800-60 info types | Categorize | ISSM |
| 12 | `compliance_select_baseline` | Select NIST 800-53 baseline | Select | ISSM |
| 13 | `compliance_tailor_baseline` | Add/remove controls | Select | ISSM |
| 14 | `compliance_set_inheritance` | Set control inheritance | Select | ISSM |
| 15 | `compliance_get_baseline` | View baseline details | Select | All |
| 16 | `compliance_generate_crm` | Generate CRM | Select | ISSM |
| 16a | `GET /inheritance/org-defaults` | List org-level inheritance defaults | Select | ISSM |
| 16b | `POST /inheritance/org-defaults/derive` | Derive org defaults from capabilities, cascade to systems | Select | ISSM |
| 16c | `POST /systems/{id}/inheritance/revert-to-org-defaults` | Revert controls to org defaults | Select | ISSM |
| 16d | `GET /capabilities/coverage` | Capabilities coverage dashboard (providers, KPI, gaps) | Select | ISSM, ISSO |
| 16e | `POST /capabilities/import/csp-profile` | Import CSP profile → components + capabilities + mappings | Select | ISSM |
| 16f | `POST /capabilities/import/crm` | Import CRM spreadsheet → capabilities + mappings | Select | ISSM |
| 16g | `POST /components/{id}/capabilities` | Bulk link capabilities to a component | Select | ISSM |
| 17 | `compliance_write_narrative` | Write control narrative | Implement | ISSO, Eng |
| 18 | `compliance_suggest_narrative` | AI-suggest narrative | Implement | ISSO, Eng |
| 19 | `compliance_batch_populate_narratives` | Auto-fill inherited narratives | Implement | ISSO |
| 20 | `compliance_narrative_progress` | Track SSP completion | Implement | ISSM, ISSO |
| 21 | `compliance_generate_ssp` | Generate SSP document | Implement | ISSM, ISSO |
| 22 | `compliance_assess_control` | Record control effectiveness | Assess | SCA |
| 23 | `compliance_take_snapshot` | Immutable assessment snapshot | Assess | SCA |
| 24 | `compliance_compare_snapshots` | Compare snapshots | Assess | SCA, ISSM |
| 25 | `compliance_verify_evidence` | Evidence integrity check | Assess | SCA |
| 26 | `compliance_check_evidence_completeness` | Evidence coverage report | Assess | SCA |
| 27 | `compliance_generate_sar` | Generate SAR | Assess | SCA |
| 28 | `compliance_issue_authorization` | Issue ATO/ATOwC/IATT/DATO | Authorize | AO |
| 29 | `compliance_accept_risk` | Accept risk on finding | Authorize | AO |
| 30 | `compliance_show_risk_register` | View risk register | Authorize | AO, ISSM |
| 31 | `compliance_create_poam` | Create POA&M item | Assess | ISSM |
| 32 | `compliance_list_poam` | List POA&M items | Assess | ISSM, ISSO |
| 33 | `compliance_generate_rar` | Generate RAR | Assess | ISSM |
| 34 | `compliance_bundle_authorization_package` | Bundle SSP+SAR+RAR+POA&M+CRM | Authorize | ISSM |

---

## Category 2: Continuous Monitoring Tools (7 tools)

Tools for ConMon plans, reports, expiration tracking, and portfolio dashboards.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 35 | `compliance_create_conmon_plan` | Create/update ConMon plan | Monitor | ISSM |
| 36 | `compliance_generate_conmon_report` | Generate periodic report | Monitor | ISSM, ISSO |
| 37 | `compliance_track_ato_expiration` | Check expiration status | Monitor | ISSM, AO |
| 38 | `compliance_report_significant_change` | Report significant change | Monitor | ISSM, ISSO |
| 39 | `compliance_reauthorization_workflow` | Check/initiate reauthorization | Monitor | ISSM |
| 40 | `compliance_multi_system_dashboard` | Portfolio dashboard | Monitor | ISSM, AO |
| 41 | `compliance_send_notification` | Send notification via channels | Monitor | ISSM |

---

## Category 3: Interoperability Tools (4 tools)

Tools for eMASS, OSCAL, and STIG cross-reference.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 42 | `compliance_export_emass` | Export to eMASS Excel format | All | ISSM |
| 43 | `compliance_import_emass` | Import eMASS Excel with conflict resolution | All | ISSM |
| 44 | `compliance_export_oscal` | Export OSCAL v1.0.6 JSON | All | ISSM |
| 45 | `compliance_show_stig_mapping` | NIST-to-STIG cross-reference | All | All |

---

## Category 4: Template Management Tools (4 tools)

Tools for custom document templates. **Administrator only**.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 46 | `compliance_upload_template` | Upload custom DOCX template | All | Admin |
| 47 | `compliance_list_templates` | List templates by document type | All | Admin, ISSM |
| 48 | `compliance_update_template` | Update template content | All | Admin |
| 49 | `compliance_delete_template` | Delete template | All | Admin |

---

## Category 5: Core Compliance Tools (11 tools)

General-purpose compliance tools from Features 001–014.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 50 | `compliance_assess` | Run NIST 800-53 assessment | Assess | SCA, ISSM |
| 51 | `compliance_get_control_family` | Get control family info | All | All |
| 52 | `compliance_generate_document` | Generate compliance documents | All | ISSM, ISSO |
| 53 | `compliance_collect_evidence` | Collect Azure evidence | Assess | ISSO, Eng |
| 54 | `compliance_remediate` | Remediate findings | Implement | Eng |
| 55 | `compliance_validate_remediation` | Validate remediation | Implement | Eng, ISSO |
| 56 | `compliance_generate_plan` | Generate remediation plan | Implement | ISSM, Eng |
| 57 | `compliance_audit_log` | View audit trail | All | ISSM |
| 58 | `compliance_history` | View compliance history | All | ISSM, ISSO |
| 59 | `compliance_status` | Current compliance posture | All | All |
| 60 | `compliance_monitoring` | Monitoring setup | Monitor | ISSM |

---

## Category 6: Compliance Watch Tools (23 tools)

Real-time monitoring, alerting, auto-remediation, and trend analysis.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 61 | `watch_enable_monitoring` | Enable scheduled monitoring | Monitor | ISSM, ISSO |
| 62 | `watch_disable_monitoring` | Disable monitoring | Monitor | ISSM |
| 63 | `watch_configure_monitoring` | Update frequency/mode | Monitor | ISSM |
| 64 | `watch_monitoring_status` | View monitoring status | Monitor | ISSM, ISSO |
| 65 | `watch_show_alerts` | List alerts with filters | Monitor | All |
| 66 | `watch_get_alert` | Get alert details | Monitor | All |
| 67 | `watch_acknowledge_alert` | Acknowledge alert | Monitor | ISSO, ISSM |
| 68 | `watch_fix_alert` | Remediate alert finding | Monitor | Eng, ISSO |
| 69 | `watch_dismiss_alert` | Dismiss alert (officer only) | Monitor | ISSM |
| 70 | `watch_create_rule` | Create custom alert rule | Monitor | ISSM |
| 71 | `watch_list_rules` | List alert rules | Monitor | ISSM, ISSO |
| 72 | `watch_suppress_alerts` | Suppress alert pattern | Monitor | ISSM |
| 73 | `watch_list_suppressions` | List suppressions | Monitor | ISSM |
| 74 | `watch_configure_quiet_hours` | Set notification quiet hours | Monitor | ISSM |
| 75 | `watch_configure_notifications` | Configure channels | Monitor | ISSM |
| 76 | `watch_configure_escalation` | Define escalation paths | Monitor | ISSM |
| 77 | `watch_alert_history` | Natural language alert queries | Monitor | ISSM, ISSO |
| 78 | `watch_compliance_trend` | Compliance score over time | Monitor | All |
| 79 | `watch_alert_statistics` | Alert counts and metrics | Monitor | ISSM, ISSO |
| 80 | `watch_auto_remediation_create` | Create auto-remediation rule | Monitor | ISSM |
| 81 | `watch_auto_remediation_list` | List auto-remediation rules | Monitor | ISSM |
| 82 | `watch_auto_remediation_status` | View execution status | Monitor | ISSM, ISSO |
| 83 | `watch_capture_baseline` | Capture compliance baseline | Monitor | ISSM |

---

## Category 7: Kanban Remediation Tools (18 tools)

Task management for remediation lifecycle tracking.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 84 | `kanban_create_board` | Create board from assessment | Assess | ISSM |
| 85 | `kanban_board_show` | Display board overview | Assess | All |
| 86 | `kanban_get_task` | Get task details | Assess | All |
| 87 | `kanban_create_task` | Create remediation task | Assess | ISSM, ISSO |
| 88 | `kanban_assign_task` | Assign/reassign task | Assess | ISSM, ISSO |
| 89 | `kanban_move_task` | Move task between columns | Assess | All |
| 90 | `kanban_task_list` | List/filter tasks | Assess | All |
| 91 | `kanban_task_history` | View task audit trail | Assess | All |
| 92 | `kanban_task_validate` | Validate remediation | Assess | Eng, ISSO |
| 93 | `kanban_add_comment` | Add comment/@mention | Assess | All |
| 94 | `kanban_task_comments` | List comments | Assess | All |
| 95 | `kanban_edit_comment` | Edit comment (24hr window) | Assess | All |
| 96 | `kanban_delete_comment` | Delete comment (1hr window) | Assess | All |
| 97 | `kanban_remediate_task` | Execute remediation script | Assess | Eng |
| 98 | `kanban_collect_evidence` | Collect evidence for task | Assess | Eng, ISSO |
| 99 | `kanban_bulk_update` | Bulk assign/move/set dates | Assess | ISSM |
| 100 | `kanban_export` | Export as CSV or POA&M | Assess | ISSM |
| 101 | `kanban_archive_board` | Archive completed board | Assess | ISSM |

---

## Category 8: PIM & Authentication Tools (13 tools)

Privileged Identity Management, just-in-time access, and CAC session management.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 102 | `pim_list_eligible` | List PIM-eligible roles | All | All |
| 103 | `pim_list_active` | List active PIM roles | All | All |
| 104 | `pim_activate_role` | Activate PIM role | All | All |
| 105 | `pim_deactivate_role` | Deactivate PIM role | All | All |
| 106 | `pim_extend_role` | Extend role activation | All | All |
| 107 | `pim_approve_request` | Approve activation request | All | ISSM |
| 108 | `pim_deny_request` | Deny activation request | All | ISSM |
| 109 | `pim_history` | View activation history | All | ISSM |
| 110 | `jit_request_access` | Request just-in-time access | All | All |
| 111 | `jit_list_sessions` | List active JIT sessions | All | All |
| 112 | `jit_revoke_access` | Revoke JIT session | All | ISSM |
| 113 | `cac_status` | Check CAC session status | All | All |
| 114 | `cac_sign_out` | Sign out CAC session | All | All |

---

## Category 9: Prisma Cloud Import Tools (4 tools)

Cloud security posture management scan import, policy catalog, and trend analysis.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 115 | `compliance_import_prisma_csv` | Import Prisma Cloud CSV export | Assess, Monitor | ISSM, SCA, Admin |
| 116 | `compliance_import_prisma_api` | Import Prisma Cloud API JSON | Assess, Monitor | ISSM, SCA, Admin |
| 117 | `compliance_list_prisma_policies` | List Prisma policies with NIST mappings | Assess, Monitor | ISSM, SCA, Assessor, Admin |
| 118 | `compliance_prisma_trend` | Compare imports for trend analysis | Assess, Monitor | ISSM, SCA, Assessor, Admin |

---

## Category 10: ACAS/Nessus Scan Import Tools (2 tools)

Tenable Nessus/ACAS vulnerability scan import with plugin-family → NIST 800-53 control mapping and POA&M auto-generation.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 119 | `compliance_import_nessus` | Import Nessus .nessus scan file | Assess | ISSO, SCA, Admin |
| 120 | `compliance_list_nessus_imports` | List Nessus import history | Assess, Monitor | All |

---

## Category 11: SAP Generation Tools (5 tools)

Security Assessment Plan generation, update, and finalization for RMF Step 4.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 121 | `compliance_generate_sap` | Generate SAP from baseline, objectives, and STIGs | Assess | SCA, ISSM |
| 122 | `compliance_update_sap` | Update Draft SAP schedule, scope, team, methods | Assess | SCA, ISSM |
| 123 | `compliance_finalize_sap` | Lock SAP with SHA-256 content hash | Assess | SCA |
| 124 | `compliance_get_sap` | Retrieve SAP by ID or latest for system | Assess | All |
| 125 | `compliance_list_saps` | List SAP history for system | Assess | All |

---

## Category 12: Privacy & Interconnection Tools (12 tools)

Privacy threshold/impact analysis, interconnection registration, ISA/MOU lifecycle, and compliance gates.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 126 | `compliance_create_pta` | Conduct Privacy Threshold Analysis | Prepare | ISSO, ISSM |
| 127 | `compliance_generate_pia` | Generate Privacy Impact Assessment (8 sections) | Prepare | ISSO, ISSM |
| 128 | `compliance_review_pia` | Approve or request revision on PIA | Prepare | ISSM |
| 129 | `compliance_check_privacy_compliance` | Privacy and interconnection compliance dashboard | Prepare | All |
| 130 | `compliance_add_interconnection` | Register system-to-system interconnection | Prepare | Eng, ISSO, ISSM |
| 131 | `compliance_list_interconnections` | List interconnections with agreement status | Prepare | All |
| 132 | `compliance_update_interconnection` | Update interconnection details or status | Prepare | ISSO, ISSM |
| 133 | `compliance_generate_isa` | Generate ISA from interconnection data (NIST 800-47) | Prepare | ISSM |
| 134 | `compliance_register_agreement` | Register pre-existing ISA/MOU/SLA | Prepare | ISSM |
| 135 | `compliance_update_agreement` | Update agreement status, dates, signatories | Prepare | ISSM |
| 136 | `compliance_certify_no_interconnections` | Certify no external interconnections | Prepare | ISSM |
| 137 | `compliance_validate_agreements` | Validate all interconnection agreements are current | Prepare | ISSO, ISSM, SCA |

---

## Category 13: SSP Authoring & OSCAL Export Tools (5 tools)

NIST 800-18 SSP section authoring, review workflow, and OSCAL 1.1.2 SSP export.

| # | Tool | Description | Phase(s) | Roles |
|---|------|-------------|----------|-------|
| 138 | `compliance_write_ssp_section` | Write/update SSP section (§1–§13) | Implement | ISSO, Eng |
| 139 | `compliance_review_ssp_section` | Review and approve SSP section | Implement, Assess | ISSM |
| 140 | `compliance_ssp_completeness` | SSP section completeness dashboard | Implement, Assess | All |
| 141 | `compliance_export_oscal_ssp` | Export OSCAL 1.1.2 SSP JSON | Authorize | ISSM, SCA, AO |
| 142 | `compliance_validate_oscal_ssp` | Validate OSCAL SSP structural correctness | Authorize | ISSM, SCA |

---

## Tool Count Summary

| Category | Tools | Description |
|----------|-------|-------------|
| RMF Lifecycle | 34 | Seven-phase lifecycle management |
| Continuous Monitoring | 7 | ConMon, expiration, dashboards |
| Interoperability | 4 | eMASS, OSCAL, STIG |
| Template Management | 4 | Custom document templates |
| Core Compliance | 11 | Assessment, evidence, remediation |
| Compliance Watch | 23 | Real-time alerts and monitoring |
| Kanban Remediation | 18 | Task lifecycle management |
| PIM & Authentication | 13 | JIT access and CAC sessions |
| Prisma Cloud Import | 4 | CSPM scan import and analysis |
| ACAS/Nessus Import | 2 | Nessus scan import and history |
| SAP Generation | 5 | Security Assessment Plan lifecycle |
| Privacy & Interconnection | 12 | Privacy analysis, ISA/MOU management |
| SSP Authoring & OSCAL | 5 | SSP sections and OSCAL export |
| POA&M Lifecycle | 6 | Create, update, close, milestones, bulk update, bulk create |
| POA&M Component Linkage | 3 | Link/unlink components, component POA&M view |
| POA&M Remediation Sync | 3 | Link/unlink/create tasks |
| POA&M Trend & Metrics | 2 | Metrics summary, trend analysis |
| POA&M Export | 1 | eMASS Excel, OSCAL JSON, CSV |
| POA&M Ticketing | 3 | Configure, sync, bulk sync |
| POA&M Bulk Operations | 2 | Bulk update, bulk create from findings |
| **Total** | **162** | |

---

## See Also

- [NL Query Reference](../guides/nl-query-reference.md) — Natural language queries mapped to tools
- [RBAC Roles](../personas/index.md#rbac-role-resolution) — Role resolution and inheritance
- [Troubleshooting](troubleshooting.md) — Common error scenarios
