# RMF Step Map

> Matrix of RMF lifecycle steps × tools × personas × artifacts produced.

---

## RMF Phase × Tool × Persona × Artifact Matrix

### Phase 1: Prepare

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_register_system` | ISSM | Register a new information system | RegisteredSystem entity |
| `compliance_define_boundary` | ISSM | Add Azure resources to authorization boundary | AuthorizationBoundary records |
| `compliance_exclude_from_boundary` | ISSM | Exclude resource with rationale | Updated boundary |
| `compliance_assign_rmf_role` | ISSM | Assign personnel to RMF roles | RmfRoleAssignment records |
| `compliance_list_rmf_roles` | ISSM | View current role assignments | Role listing |
| `compliance_list_systems` | ISSM / AO | View all registered systems | System inventory |
| `compliance_get_system` | Any | View system details and status | System status |
| `compliance_advance_rmf_step` | ISSM | Advance to Categorize phase | Phase transition |

**Gate Conditions (Prepare → Categorize):** ≥1 active RMF role + ≥1 boundary resource

---

### Phase 2: Categorize

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_suggest_info_types` | ISSM | AI-suggested SP 800-60 information types | Suggested types with confidence |
| `compliance_categorize_system` | ISSM | Perform FIPS 199 categorization | SecurityCategorization + InformationTypes |
| `compliance_get_categorization` | Any | Review stored categorization | Categorization details |

**Gate Conditions (Categorize → Select):** SecurityCategorization exists + ≥1 InformationType

**FIPS 199 Computation:**
- High-water mark across all information types → C/I/A individual impacts
- Overall = max(C, I, A)
- DoD IL mapping: Low→IL2, Moderate→IL4, High→IL5

---

### Phase 3: Select

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_select_baseline` | ISSM | Select NIST 800-53 baseline | ControlBaseline entity |
| `compliance_tailor_baseline` | ISSM | Add/remove controls with rationale | ControlTailoring records |
| `compliance_set_inheritance` | ISSM / Engineer | Designate inherited/shared/customer controls | ControlInheritance records |
| Dashboard: Derive Org Defaults | ISSM | Derive org-wide defaults from capabilities, cascade to systems | OrgInheritanceDefault + OrgDerived records |
| Dashboard: Revert to Org Defaults | ISSM | Revert overridden controls to org-level defaults | ControlInheritance updated |
| `compliance_get_baseline` | Any | View baseline with tailoring summary | Baseline details |
| `compliance_generate_crm` | ISSM | Generate Customer Responsibility Matrix (incl. Designation Source) | CRM document |
| `compliance_show_stig_mapping` | Engineer | View STIG-to-NIST mappings for controls | STIG cross-references |

**Gate Conditions (Select → Implement):** ControlBaseline exists

**Baseline Sizes:** Low=152, Moderate=329, High=400 controls

---

### Phase 4: Implement

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_write_narrative` | Engineer | Write per-control implementation narrative | ControlImplementation entity |
| `compliance_suggest_narrative` | Engineer | Get AI-generated narrative draft | Suggested narrative text |
| `compliance_batch_populate_narratives` | Engineer | Auto-populate inherited control narratives | Batch ControlImplementation records |
| `compliance_narrative_progress` | ISSM / Engineer | Track SSP completion percentage | Progress report by family |
| `compliance_generate_ssp` | ISSM | Generate System Security Plan document | SSP markdown/PDF/DOCX |
| `compliance_assess` | Engineer | Run automated compliance scan | ComplianceAssessment |
| `compliance_remediate` | Engineer | Execute remediation script | Remediation result |

**Gate Conditions (Implement → Assess):** Advisory only (no hard block)

---

### Phase 5: Assess

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_assess_control` | SCA | Record per-control effectiveness determination | ControlEffectiveness entity |
| `compliance_take_snapshot` | SCA | Create immutable assessment snapshot | AssessmentRecord (SHA-256 hashed) |
| `compliance_compare_snapshots` | SCA | Compare before/after snapshots | Delta report |
| `compliance_verify_evidence` | SCA | Verify evidence integrity (SHA-256) | Verification result |
| `compliance_check_evidence_completeness` | SCA | Check evidence coverage per control | Completeness report |
| `compliance_generate_sar` | SCA | Generate Security Assessment Report | SAR document |
| `compliance_collect_evidence` | SCA / Engineer | Collect compliance evidence from Azure | ComplianceEvidence records |

**Gate Conditions (Assess → Authorize):** Advisory only

---

### Phase 6: Authorize

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_issue_authorization` | **AO** | Issue ATO/IATT/DATO/ATOwC decision | AuthorizationDecision entity |
| `compliance_accept_risk` | **AO** | Accept risk on specific finding | RiskAcceptance entity |
| `compliance_show_risk_register` | Any | View risk register with expiration tracking | Risk register |
| `compliance_create_poam` | ISSM | Create POA&M item with milestones | PoamItem + PoamMilestone entities |
| `compliance_list_poam` | Any | List/filter POA&M items | POA&M listing |
| `compliance_generate_rar` | ISSM / SCA | Generate Risk Assessment Report | RAR document |
| `compliance_bundle_authorization_package` | ISSM | Bundle SSP + SAR + RAR + POA&M + CRM + ATO letter | Authorization package |

**Gate Conditions (Authorize → Monitor):** Advisory only

**AO-Exclusive Tools:** `compliance_issue_authorization`, `compliance_accept_risk` — require `Compliance.AuthorizingOfficial` role

---

### Phase 7: Monitor

| Tool | Persona | Purpose | Artifact |
|------|---------|---------|----------|
| `compliance_create_conmon_plan` | ISSM | Create continuous monitoring plan | ConMonPlan entity |
| `compliance_generate_conmon_report` | ISSM | Generate periodic compliance report | ConMonReport entity |
| `compliance_report_significant_change` | ISSM | Report change that may trigger reauth | SignificantChange entity |
| `compliance_track_ato_expiration` | ISSM / AO | Check authorization expiration status | Graduated alert levels |
| `compliance_multi_system_dashboard` | ISSM / AO | Portfolio-wide compliance dashboard | Multi-system summary |
| `compliance_reauthorization_workflow` | ISSM | Check triggers / initiate reauthorization | Trigger analysis, RMF regression |
| `compliance_notification_delivery` | ISSM | Send expiration/change notifications | Notification record |

---

## Cross-Phase Tools

These tools operate across all RMF phases:

| Tool | Persona | Purpose |
|------|---------|---------|
| `compliance_export_emass` | ISSM | Export controls/POA&M to eMASS Excel |
| `compliance_import_emass` | ISSM | Import from eMASS Excel |
| `compliance_export_oscal` | ISSM | Export SSP/Assessment/POA&M as OSCAL JSON |
| `compliance_upload_template` | ISSM / Engineer | Upload custom DOCX template |
| `compliance_list_templates` | Any | List uploaded templates |
| `compliance_update_template` | ISSM / Engineer | Update template name or content |
| `compliance_delete_template` | ISSM | Delete a template |
| `compliance_generate_document` | ISSM / Engineer | Generate SSP/POA&M/SAR in MD/PDF/DOCX |

---

## Persona × Phase Heat Map

| Phase | ISSM | SCA | Engineer | AO |
|-------|------|-----|----------|----|
| **Prepare** | ●●● Primary | | ● Support | |
| **Categorize** | ●●● Primary | | | |
| **Select** | ●●● Primary | | ●● Support | |
| **Implement** | ●● Oversight | | ●●● Primary | |
| **Assess** | ● Support | ●●● Primary | ●● Support | |
| **Authorize** | ●●● Prepares | ● Reviews | | ●●● Decides |
| **Monitor** | ●●● Primary | | | ●● Reviews |

**Legend:** ●●● = Primary responsibility, ●● = Significant involvement, ● = Occasional involvement

---

## Artifact Production Summary

| Artifact | Created During | Primary Tool | Persistence |
|----------|---------------|-------------|-------------|
| RegisteredSystem | Prepare | `compliance_register_system` | EF Core entity |
| AuthorizationBoundary | Prepare | `compliance_define_boundary` | EF Core entity |
| RmfRoleAssignment | Prepare | `compliance_assign_rmf_role` | EF Core entity |
| SecurityCategorization | Categorize | `compliance_categorize_system` | EF Core entity |
| InformationType | Categorize | `compliance_categorize_system` | EF Core entity |
| ControlBaseline | Select | `compliance_select_baseline` | EF Core entity |
| ControlTailoring | Select | `compliance_tailor_baseline` | EF Core entity |
| ControlInheritance | Select | `compliance_set_inheritance` | EF Core entity |
| OrgInheritanceDefault | Select | Dashboard: Derive Org Defaults | EF Core entity |
| CRM | Select | `compliance_generate_crm` | ComplianceDocument |
| ControlImplementation | Implement | `compliance_write_narrative` | EF Core entity |
| SSP | Implement | `compliance_generate_ssp` | ComplianceDocument |
| ControlEffectiveness | Assess | `compliance_assess_control` | EF Core entity |
| AssessmentRecord | Assess | `compliance_take_snapshot` | EF Core entity (immutable) |
| SAR | Assess | `compliance_generate_sar` | ComplianceDocument |
| AuthorizationDecision | Authorize | `compliance_issue_authorization` | EF Core entity |
| RiskAcceptance | Authorize | `compliance_accept_risk` | EF Core entity |
| PoamItem | Authorize | `compliance_create_poam` | EF Core entity |
| RAR | Authorize | `compliance_generate_rar` | ComplianceDocument |
| Auth Package | Authorize | `compliance_bundle_authorization_package` | Bundle metadata |
| ConMonPlan | Monitor | `compliance_create_conmon_plan` | EF Core entity |
| ConMonReport | Monitor | `compliance_generate_conmon_report` | EF Core entity |
| SignificantChange | Monitor | `compliance_report_significant_change` | EF Core entity |
