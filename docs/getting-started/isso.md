# Getting Started: ISSO

> First-time setup and orientation for Information System Security Officer users.

---

## Prerequisites

| Requirement | Details |
|------------|---------|
| **Access** | CAC enrolled with `Compliance.Analyst` role |
| **Tools** | VS Code with GitHub Copilot Chat extension (primary), Microsoft Teams |
| **Knowledge** | Assigned as ISSO to one or more systems by the ISSM (via `compliance_assign_rmf_role`) |

## First-Time Setup

1. **Verify your role and system assignments**

    ```
    @ato "What systems am I assigned to?"
    ```

    Expected result: List of systems where you are assigned as ISSO.

2. **Check the current RMF phase for your system**

    ```
    @ato "Show system details for {id}"
    ```

    Expected result: System summary with current RMF phase, authorization status, and compliance score.

3. **Start your primary workflow**

    > **Tip:** If you need to register a new system, use the **System Intake Wizard** in the Compliance Dashboard (Systems → "+ Add System") for a guided 7-step setup process. See the [System Intake Wizard Guide](../guides/system-intake-wizard.md) for details.

    If your system is in the **Implement** phase:
    ```
    @ato "Show narrative progress for system {id}"
    ```

    If your system is in the **Monitor** phase:
    ```
    @ato "Show monitoring status for all subscriptions"
    ```

## Your First 3 Commands

### 1. Check Narrative Progress

> **"Show narrative progress for system {id}"**

Expected result: Per-control-family completion percentages showing how many narratives are written vs. remaining.

### 2. Auto-Populate Inherited Narratives

> **"Auto-populate the inherited control narratives for system {id}"**

Expected result: ~40–60% of narratives auto-filled from the embedded control catalog. Remaining customer controls need manual authoring.

### 3. Enable Watch Monitoring

> **"Enable daily monitoring for subscription {sub-id}"**

Expected result: Compliance Watch enabled with daily scheduled scans. Alerts will appear for drift and violations.

## What You Can Do

Beyond narrative authoring and Watch monitoring, ISSOs can also:

- **Import STIG scan results** — Upload CKL/XCCDF files from DISA STIG Viewer or SCAP Compliance Checker (`compliance_import_ckl`, `compliance_import_xccdf`)
- **Import Prisma Cloud scans** — Upload CSV or API JSON exports for cloud security posture tracking (`compliance_import_prisma_csv`, `compliance_import_prisma_api`)
- **Import ACAS/Nessus vulnerability scans** — Upload .nessus files from Tenable Nessus/ACAS for vulnerability mapping and POA&M auto-generation (`compliance_import_nessus`)
- **Conduct Privacy Threshold Analysis** — Determine whether a PIA is required (`compliance_create_pta`)
- **Author Privacy Impact Assessments** — Generate and submit PIAs for ISSM review (`compliance_generate_pia`)
- **Register interconnections** — Document system-to-system connections crossing the authorization boundary (`compliance_add_interconnection`)
- **Author SSP sections** — Write NIST 800-18 SSP sections and submit them for ISSM review (`compliance_write_ssp_section`)
- **Track SSP completeness** — Monitor overall SSP readiness percentage (`compliance_ssp_completeness`)
- **Manage narrative governance** — View version history, diff changes, roll back edits, and submit narratives for ISSM approval (`compliance_narrative_history`, `compliance_narrative_diff`, `compliance_rollback_narrative`, `compliance_submit_narrative`, `compliance_batch_submit_narratives`)
- **Manage HW/SW inventory** — Register hardware and software, auto-seed from boundary, check completeness, and export to eMASS Excel (`inventory_add_item`, `inventory_auto_seed`, `inventory_completeness`, `inventory_export`)
- **Upload compliance evidence** — Navigate to a control narrative in the dashboard and click **Attach Evidence** to upload screenshots, scan results, or configuration exports. Use the **Evidence** page to browse all evidence for a system, search and filter by category, and review file details with integrity hashes

---

## What's Next

- [Full ISSO Guide](../personas/isso.md) — Complete Implement/Assess/Monitor workflows
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase details
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable ISSO cheat sheet

## Common First-Day Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| "What systems am I assigned to?" returns empty | ISSM has not yet assigned you as ISSO | Ask your ISSM to run `compliance_assign_rmf_role` for the target system |
| "Access denied: Compliance.Analyst cannot invoke watch_dismiss_alert" | ISSOs cannot dismiss alerts — only officers (ISSM) can | Escalate to your ISSM to dismiss false positive alerts |
| AI narrative suggestions unavailable | Air-gapped environment without LLM endpoint | Write narratives manually using `compliance_write_narrative`; inherited narratives still auto-populate from the embedded catalog |

---

## POA&M Management (Feature 039)

ISSOs manage day-to-day POA&M operations:

- **Create Items**: Add POA&M entries for new findings via the dashboard or `compliance_create_poam`
- **Lifecycle Management**: Transition items through Ongoing → Completed/Delayed/Risk Accepted with appropriate validation
- **Component Linkage**: Link POA&M items to affected system components for traceability
- **Remediation Sync**: Create or link remediation tasks and track bidirectional status
- **Milestones**: Add and track milestones with target dates for each POA&M item

---

## SAR Contributions (Feature 041)

As an ISSO, you contribute to the Security Assessment Report:

- **Review findings**: SAR sections are auto-populated from assessment findings you manage
- **Edit sections**: Use `compliance_edit_sar_section` to update findings and recommendations
- **Evidence linkage**: Ensure evidence artifacts are linked to controls for inclusion in the authorization package
