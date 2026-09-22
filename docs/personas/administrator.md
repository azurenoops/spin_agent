# Administrator Guide

> Security Posture Intelligence Navigator infrastructure management — templates, configuration, and separation of duties.

---

## Role Overview

- **Full Title**: Security Posture Intelligence Navigator Administrator
- **Abbreviation**: Admin
- **RBAC Role**: `Compliance.Administrator`
- **Primary Interface**: MCP API, VS Code
- **Key Responsibility**: Manage Security Posture Intelligence Navigator server configuration, document templates, and infrastructure settings. This role is explicitly separated from the Authorizing Official role to enforce DoD separation of duties.

!!! warning "Separation of Duties"
    The `Compliance.Administrator` role **cannot** issue authorization decisions or accept risk. These capabilities are exclusive to `Compliance.AuthorizingOfficial`. This separation is required by DoDI 8510.01 to prevent conflicts of interest.

---

## Permissions

| Capability | Allowed | Tool |
|-----------|---------|------|
| Upload document templates | ✅ | `compliance_upload_template` |
| List document templates | ✅ | `compliance_list_templates` |
| Update document templates | ✅ | `compliance_update_template` |
| Delete document templates | ✅ | `compliance_delete_template` |
| View system details | ✅ | `compliance_get_system`, `compliance_list_systems` |
| Issue authorization decisions | ❌ | `compliance_issue_authorization` — AO only |
| Accept risk | ❌ | `compliance_accept_risk` — AO only |
| Assess controls | ❌ | `compliance_assess_control` — SCA only |
| Dismiss alerts | ❌ | `watch_dismiss_alert` — ISSM only |

---

## Template Management

The Administrator manages custom document templates used by Security Posture Intelligence Navigator to generate SSPs, SARs, POA&Ms, and RARs.

### Upload a Template

> **"Upload our organization's SSP template"**

```
Tool: compliance_upload_template
Parameters:
  name: "Organization SSP Template v2.1"
  document_type: "SSP"
  content: <DOCX file content>
```

### List Templates

> **"List all uploaded document templates"**

```
Tool: compliance_list_templates
Parameters:
  document_type: "SSP"  (optional filter)
```

### Update a Template

> **"Update template {id} with new file content"**

```
Tool: compliance_update_template
Parameters:
  template_id: "<template-guid>"
  name: "Organization SSP Template v2.2"
  content: <updated DOCX file content>
```

### Delete a Template

> **"Delete template {id}"**

```
Tool: compliance_delete_template
Parameters:
  template_id: "<template-guid>"
```

---

## Template Merge Fields

Templates use `{{field_name}}` placeholders that are automatically populated with system data during document generation:

### System Information

| Merge Field | Data Source |
|-------------|------------|
| `{{system_name}}` | Registered system name |
| `{{system_acronym}}` | System acronym |
| `{{system_type}}` | MajorApplication, GeneralSupportSystem, etc. |

### Security Categorization

| Merge Field | Data Source |
|-------------|------------|
| `{{categorization}}` | FIPS 199 overall level (Low/Moderate/High) |
| `{{impact_level}}` | DoD Impact Level (IL2–IL6) |
| `{{fips_notation}}` | Full FIPS 199 notation |

### Compliance Content

| Merge Field | Data Source |
|-------------|------------|
| `{{control_implementations}}` | All control narratives grouped by family |
| `{{baseline_summary}}` | Baseline level, control count, tailoring summary |
| `{{findings_summary}}` | Finding counts by CAT severity |
| `{{poam_items}}` | All POA&M items with status and milestones |
| `{{risk_acceptances}}` | Active risk acceptances from the AO |

---

## Supported Document Types

| Type | Description | Default Format |
|------|-------------|---------------|
| **SSP** | System Security Plan | Markdown / DOCX |
| **SAR** | Security Assessment Report | Markdown |
| **RAR** | Risk Assessment Report | Markdown |
| **POA&M** | Plan of Action & Milestones | Markdown |
| **CRM** | Customer Responsibility Matrix | Markdown |
| **ConMon** | Continuous Monitoring Report | Markdown |

---

## Infrastructure Configuration

### Azure Subscription Connection

Configure the Azure subscription used by Security Posture Intelligence Navigator for compliance assessments and evidence collection:

> **"Configure the Azure subscription connection"**

### Proxy Configuration (Air-Gapped)

For disconnected or air-gapped environments:

> **"Set up proxy configuration for air-gapped environment"**

!!! info "Air-Gapped Considerations"
    In air-gapped environments:
    
    - AI narrative suggestions (`compliance_suggest_narrative`) are unavailable
    - Watch event-driven monitoring requires local policy cache only
    - eMASS export generates files locally — manual transfer via removable media
    - Template management works fully offline (all operations are local)

---

## Cross-Persona Handoffs

| From | To | Trigger | Data |
|------|----|---------|------|
| Administrator → ISSM | Templates uploaded | Template IDs, document types available |
| ISSM → Administrator | Template update needed | Change request with document type and requirements |

---

## See Also

- [Persona Overview](index.md) — All personas and RACI matrix
- [Deployment Guide](../deployment.md) — Server deployment and configuration
- [Architecture Overview](../architecture/overview.md) — System architecture
