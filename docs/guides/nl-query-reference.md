# Natural Language Query Reference

> Complete reference of natural language queries organized by category. Use these examples as-is or adapt them to your specific system and context.

---

## How to Use This Reference

ATO Copilot accepts natural language queries through all supported interfaces:

- **VS Code**: `@ato "your query"` or `@ato /compliance "your query"`
- **Microsoft Teams**: Message the ATO Copilot bot directly
- **MCP API**: Send as tool invocations via any MCP client

Replace `{id}`, `{sub-id}`, and similar placeholders with your actual system, subscription, or resource identifiers.

---

## 1. System Registration & Setup

| Query | Tool | Persona |
|-------|------|---------|
| Register a new system called 'ACME Portal' as a Major Application, mission-critical, hosted in Azure Government | `compliance_register_system` | ISSM |
| List all registered systems | `compliance_list_systems` | All |
| Show system details for {id} | `compliance_get_system` | All |
| What systems am I assigned to? | `compliance_list_systems` | ISSO, Engineer |
| Add the production VMs and database to system {id}'s boundary | `compliance_define_boundary` | ISSM |
| Exclude the shared logging service from system {id}'s boundary — it's under a separate ATO | `compliance_exclude_from_boundary` | ISSM |
| Assign Jane Smith as ISSM for system {id} | `compliance_assign_rmf_role` | ISSM |
| Who is assigned to system {id}? | `compliance_list_rmf_roles` | All |

---

## 2. Categorization

| Query | Tool | Persona |
|-------|------|---------|
| What information types should I use for a financial management system? | `compliance_suggest_info_types` | ISSM |
| Categorize system {id} as Moderate confidentiality, High integrity, Moderate availability | `compliance_categorize_system` | ISSM |
| What's the DoD Impact Level for system {id}? | `compliance_get_categorization` | All |
| What's the FIPS 199 notation for system {id}? | `compliance_get_categorization` | All |
| Re-categorize system {id} — we added PII data types | `compliance_categorize_system` | ISSM |

---

## 3. Baseline & Controls

| Query | Tool | Persona |
|-------|------|---------|
| Select the NIST 800-53 baseline for system {id} | `compliance_select_baseline` | ISSM |
| How many controls are in the High baseline? | `compliance_get_baseline` | All |
| Apply the CNSSI 1253 overlay for IL5 | `compliance_select_baseline` | ISSM |
| Remove PE-5 from the baseline — not applicable for cloud-only systems | `compliance_tailor_baseline` | ISSM |
| Set AC-1 as inherited from Azure Government FedRAMP High | `compliance_set_inheritance` | ISSM |
| Generate the Customer Responsibility Matrix | `compliance_generate_crm` | ISSM |
| What STIG rules map to AC-2? | `compliance_show_stig_mapping` | All |
| Show all controls in the AC family | `compliance_get_baseline` | All |
| What controls require customer implementation? | `compliance_get_baseline` | All |

---

## 4. SSP Authoring

| Query | Tool | Persona |
|-------|------|---------|
| Auto-populate inherited control narratives | `compliance_batch_populate_narratives` | ISSO, Engineer |
| Suggest a narrative for AC-2 on system {id} | `compliance_suggest_narrative` | ISSO, Engineer |
| Write the narrative for SC-7: "Network segmentation is..." | `compliance_write_narrative` | ISSO, Engineer |
| What's the SSP completion percentage? | `compliance_narrative_progress` | All |
| Show narrative progress for the IA family | `compliance_narrative_progress` | All |
| Which controls still need narratives? | `compliance_narrative_progress` | All |
| Generate the SSP for system {id} | `compliance_generate_ssp` | ISSM |
| Generate SSP as PDF using our DISA template | `compliance_generate_ssp` | ISSM |

---

## 5. Assessment

| Query | Tool | Persona |
|-------|------|---------|
| Assess control AC-2 as Satisfied — tested and verified | `compliance_assess_control` | SCA |
| Assess AC-3 as Other Than Satisfied, CAT II — missing MAC enforcement | `compliance_assess_control` | SCA |
| Take a snapshot of the current assessment state | `compliance_take_snapshot` | SCA |
| Compare the before and after snapshots | `compliance_compare_snapshots` | SCA |
| Is evidence complete for the AC family? | `compliance_check_evidence_completeness` | SCA, ISSO |
| Has evidence {id} been tampered with? | `compliance_verify_evidence` | SCA |
| Generate the Security Assessment Report | `compliance_generate_sar` | SCA |
| What's the overall compliance score? | — | All |
| How many CAT I findings are there? | — | All |

---

## 6. Authorization

| Query | Tool | Persona |
|-------|------|---------|
| Bundle the authorization package for system {id} | `compliance_bundle_authorization_package` | ISSM |
| Issue an ATO for system {id} expiring January 2028 with Low residual risk | `compliance_issue_authorization` | AO |
| Issue ATO with conditions — MFA required within 90 days | `compliance_issue_authorization` | AO |
| Accept risk on finding {id} for CM-6 — compensating control: monitoring alerts | `compliance_accept_risk` | AO |
| Deny authorization — 3 unmitigated CAT I findings | `compliance_issue_authorization` | AO |
| Show the risk register | `compliance_show_risk_register` | All |
| What accepted risks are expiring soon? | `compliance_show_risk_register` | AO, ISSM |

---

## 7. Continuous Monitoring

| Query | Tool | Persona |
|-------|------|---------|
| Create a ConMon plan with monthly assessments | `compliance_create_conmon_plan` | ISSM |
| Generate the February 2026 ConMon report | `compliance_generate_conmon_report` | ISSM |
| When does the ATO expire for system {id}? | `compliance_track_ato_expiration` | All |
| Report a significant change: new VPN interconnection | `compliance_report_significant_change` | ISSM |
| Check reauthorization triggers | `compliance_reauthorization_workflow` | ISSM |
| Show the multi-system dashboard | `compliance_multi_system_dashboard` | ISSM, AO |
| Which systems have expired ATOs? | `compliance_track_ato_expiration` | ISSM, AO |
| Export to eMASS format | `compliance_export_emass` | ISSM |
| Export OSCAL JSON | `compliance_export_oscal` | ISSM |

---

## 8. Compliance Watch

| Query | Tool | Persona |
|-------|------|---------|
| Enable daily monitoring for subscription {sub-id} | `watch_enable_monitoring` | ISSO |
| Show monitoring status | `watch_monitoring_status` | ISSO |
| Show all critical alerts | `watch_show_alerts` | ISSO |
| Show alerts from the last 7 days for the AC family | `watch_show_alerts` | ISSO |
| What drifted this week? | `watch_show_alerts` | ISSO |
| Acknowledge alert ALT-12345 | `watch_acknowledge_alert` | ISSO |
| Fix alert ALT-12345 with dry run | `watch_fix_alert` | ISSO |
| Dismiss alert ALT-12345 — false positive, documented in ticket SNOW-123 | `watch_dismiss_alert` | ISSM |
| Show alert statistics for the last 30 days | `watch_show_alerts` | ISSO |
| Show compliance trend for subscription {sub-id} | `watch_show_alerts` | ISSO |
| Configure quiet hours from 22:00 to 06:00 weekdays | `watch_configure_quiet_hours` | ISSO |
| Escalate Critical alerts to ISSM if not acknowledged in 30 minutes | `watch_configure_escalation` | ISSO |
| Create a suppression rule for PE controls expiring March 31 | `watch_suppress_alerts` | ISSO |

---

## 9. Kanban & Remediation

### Standalone Remediation

| Query | Tool | Persona |
|-------|------|---------|
| Generate a remediation plan for subscription {sub-id} | `compliance_generate_plan` | ISSM, Engineer |
| Remediate finding {finding-id} with dry run | `compliance_remediate` | Engineer |
| Apply fix for finding {finding-id} | `compliance_remediate` | Engineer |
| Validate remediation for finding {finding-id} | `compliance_validate_remediation` | Engineer |

### Kanban Task Management

| Query | Tool | Persona |
|-------|------|---------|
| Create a remediation board from the latest assessment | `kanban_create_board` | ISSO |
| Show the board overview | `kanban_board_show` | All |
| Show my assigned tasks | `kanban_task_list` | Engineer |
| Assign REM-003 to Bob Jones | `kanban_assign_task` | ISSO |
| Move REM-005 to In Progress | `kanban_move_task` | Engineer |
| Fix REM-005 with dry run | `kanban_remediate_task` | Engineer |
| Validate REM-005 | `kanban_task_validate` | Engineer |
| Collect evidence for REM-005 | `kanban_collect_evidence` | Engineer |
| Move REM-005 to In Review | `kanban_move_task` | Engineer |
| Show all overdue tasks | `kanban_task_list` | ISSO, ISSM |
| Export the board as POA&M format | `kanban_export` | ISSM |
| Bulk assign all High severity tasks to the security team | `kanban_bulk_update` | ISSO |

---

## 10. Knowledge & Education

| Query | Tool | Persona |
|-------|------|---------|
| What does NIST control AC-2 mean? | Knowledge Base | All |
| Explain control SC-7 in terms of Azure implementation | Knowledge Base | Engineer |
| What are the FedRAMP Moderate requirements for encryption at rest? | Knowledge Base | All |
| What STIG rules apply to Azure SQL Database? | `compliance_show_stig_mapping` | Engineer |
| What is a CAT I finding? | Knowledge Base | All |
| Explain the difference between ATO and ATOwC | Knowledge Base | All |
| What triggers reauthorization? | Knowledge Base | All |
| What is a Customer Responsibility Matrix? | Knowledge Base | All |

---

## 11. PIM (Privileged Identity Management)

| Query | Tool | Persona |
|-------|------|---------|
| What PIM roles am I eligible for? | PIM tools | All |
| Activate the Reader role for subscription {sub-id} — running quarterly assessment | PIM tools | SCA, ISSO |
| List my active PIM roles | PIM tools | All |
| Deactivate the Contributor role — remediation complete | PIM tools | Engineer |
| Show PIM activation history | PIM tools | All |

---

## 12. STIG & SCAP Import

| Query | Tool | Persona |
|-------|------|---------|
| Import this CKL file for system {id} | `compliance_import_ckl` | ISSM, ISSO |
| Import SCAP scan results for system {id} | `compliance_import_xccdf` | ISSM, ISSO |
| Export a CKL checklist for the Windows Server 2022 STIG | `compliance_export_ckl` | ISSM, ISSO, SCA |
| Show import history for system {id} | `compliance_list_imports` | All |
| Show details of import {import-id} | `compliance_get_import_summary` | All |

---

## 13. SAP Generation

| Query | Tool | Persona |
|-------|------|---------|
| Generate a security assessment plan for system {id} | `compliance_generate_sap` | SCA, ISSM |
| Update the SAP methodology for control AC-2 | `compliance_update_sap` | SCA, ISSM |
| Finalize the SAP for system {id} | `compliance_finalize_sap` | SCA |
| Show the current SAP for system {id} | `compliance_get_sap` | All |
| List all SAPs | `compliance_list_saps` | All |

---

## 14. Prisma Cloud

| Query | Tool | Persona |
|-------|------|---------|
| Import the Prisma Cloud CSV export for system {id} | `compliance_import_prisma_csv` | ISSM, ISSO |
| Connect Prisma Cloud API and pull the latest scan data | `compliance_import_prisma_api` | ISSM, ISSO |
| List Prisma policies mapped to our controls | `compliance_list_prisma_policies` | All |
| Show the Prisma compliance trend for the last 30 days | `compliance_prisma_trend` | All |

---

## 15. Privacy (PTA & PIA)

| Query | Tool | Persona |
|-------|------|---------|
| Create a privacy threshold analysis for system {id} | `compliance_create_pta` | ISSM, ISSO |
| Generate a privacy impact assessment based on the PTA | `compliance_generate_pia` | ISSO, ISSM |
| Review the PIA for system {id} — approved | `compliance_review_pia` | ISSM |
| Check overall privacy compliance for system {id} | `compliance_check_privacy_compliance` | All |

---

## 16. Interconnections

| Query | Tool | Persona |
|-------|------|---------|
| Add an interconnection to the HR payroll system for system {id} | `compliance_add_interconnection` | ISSM, ISSO, Eng |
| List all interconnections for system {id} | `compliance_list_interconnections` | All |
| Update the bandwidth for interconnection {icn-id} | `compliance_update_interconnection` | ISSM, ISSO |
| Generate an ISA for interconnection {icn-id} | `compliance_generate_isa` | ISSM |
| Register the ISA agreement for interconnection {icn-id} | `compliance_register_agreement` | ISSM |
| Update agreement expiration for interconnection {icn-id} | `compliance_update_agreement` | ISSM |
| Certify this system has no external interconnections | `compliance_certify_no_interconnections` | ISSM |
| Validate all interconnection agreements for system {id} | `compliance_validate_agreements` | ISSM, ISSO, SCA |

---

## 17. SSP Authoring & OSCAL

| Query | Tool | Persona |
|-------|------|---------|
| Write SSP section 1 (System Identification) for system {id} | `compliance_write_ssp_section` | ISSO, Eng |
| Review SSP section 5 for system {id} — approved | `compliance_review_ssp_section` | ISSM |
| What is the SSP completeness for system {id}? | `compliance_ssp_completeness` | All |
| Export the OSCAL SSP for system {id} | `compliance_export_oscal_ssp` | ISSM, SCA, AO |
| Validate the OSCAL SSP for system {id} | `compliance_validate_oscal_ssp` | ISSM, SCA |

---

## POA&M Management Queries (Feature 039)

| Query | Tool | Persona |
|-------|------|---------|
| Show all POA&M items for system {id} | `compliance_list_poam` | All |
| Show overdue POA&M items for system {id} | `compliance_list_poam` (overdue filter) | All |
| Show POA&M details for item {poamId} | `compliance_get_poam` | All |
| Create a POA&M for AC-2 on system {id} | `compliance_create_poam` | ISSO, ISSM |
| Update POA&M {poamId} status to Completed | `compliance_update_poam` | ISSO, ISSM |
| Close POA&M {poamId} | `compliance_close_poam` | ISSO, ISSM |
| What's the POA&M status for system {id}? | `compliance_poam_metrics` | All |
| Show POA&M trends for system {id} | `compliance_poam_trend` | All |
| Export POA&M to eMASS format for system {id} | `compliance_export_poam` | ISSO, ISSM, AO |
| Export POA&M to CSV for system {id} | `compliance_export_poam` | ISSO, ISSM, AO |
| Sync POA&M {poamId} to Jira | `compliance_sync_poam_ticket` | ISSO, ISSM |
| Show POA&M items for component {componentId} | `compliance_poam_by_component` | All |
| Link POA&M {poamId} to component {componentId} | `compliance_link_poam_component` | ISSO, ISSM |
| Create a task from POA&M {poamId} | `compliance_create_task_from_poam` | ISSO, ISSM |

---

## See Also

- [Getting Started](../getting-started/index.md) — Per-persona onboarding
- [Persona Overview](../personas/index.md) — Role definitions and permissions
- [Quick Reference Cards](../reference/quick-reference-cards.md) — Printable per-persona cheat sheets
