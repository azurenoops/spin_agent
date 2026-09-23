# NIST 800-53 Control Coverage

> Control coverage by family — automated scanning vs. manual attestation.

---

## Coverage Overview

Security Posture Intelligence Navigator supports the complete NIST SP 800-53 Rev. 5 control catalog with coverage through automated Azure resource scanning and manual SSP narrative attestation.

### Baseline Sizes

| Baseline | Controls | Typical DoD IL |
|----------|----------|---------------|
| Low | 152 | IL2 |
| Moderate | 329 | IL4 |
| High | 400 | IL5 / IL6 |

---

## Coverage by Family

| Family | Code | Controls | Automated Scan | Manual Attestation | Notes |
|--------|------|----------|---------------|-------------------|-------|
| Access Control | AC | 25 | ✓ Azure AD, RBAC, NSG | ✓ Policy narratives | AC-2 accounts, AC-3 enforcement, AC-17 remote access |
| Awareness and Training | AT | 6 | | ✓ Full | Organizational policy — no Azure resource mapping |
| Audit and Accountability | AU | 16 | ✓ Diagnostic settings, Log Analytics | ✓ Policy context | AU-2 event types, AU-6 review, AU-12 generation |
| Assessment, Authorization, and Monitoring | CA | 9 | | ✓ Full | CA-2 assessment, CA-5 POA&M, CA-6 authorization |
| Configuration Management | CM | 14 | ✓ Azure Policy, Defender | ✓ Baseline docs | CM-2 baseline, CM-6 settings, CM-8 inventory |
| Contingency Planning | CP | 13 | ✓ Backup, ASR | ✓ Plan docs | CP-2 plan, CP-9 backup, CP-10 recovery |
| Identification and Authentication | IA | 12 | ✓ Azure AD, MFA | ✓ Policy context | IA-2 MFA, IA-5 authenticator management |
| Incident Response | IR | 10 | ✓ Sentinel, Defender alerts | ✓ Plan docs | IR-2 training, IR-4 handling, IR-6 reporting |
| Maintenance | MA | 6 | | ✓ Full | Physical/logical maintenance — org policy |
| Media Protection | MP | 8 | ✓ Disk encryption | ✓ Policy context | MP-2 access, MP-4 storage, MP-5 transport |
| Physical and Environmental Protection | PE | 20 | | ✓ Full | Inherited from CSP for cloud-hosted systems |
| Planning | PL | 11 | | ✓ Full | PL-2 security plan (SSP), PL-4 rules of behavior |
| Program Management | PM | 16 | | ✓ Full | Organizational program management |
| Personnel Security | PS | 9 | | ✓ Full | PS-2 position risk, PS-3 screening |
| PII Processing and Transparency | PT | 8 | | ✓ Full | Privacy controls |
| Risk Assessment | RA | 7 | ✓ Defender scores | ✓ Methodology docs | RA-3 risk assessment, RA-5 vulnerability scanning |
| System and Services Acquisition | SA | 22 | | ✓ Full | SA-2 resource allocation, SA-4 acquisition process |
| System and Communications Protection | SC | 28 | ✓ TLS, encryption, NSGs | ✓ Architecture docs | SC-7 boundary, SC-8 transmission, SC-28 at-rest |
| System and Information Integrity | SI | 16 | ✓ Defender, updates | ✓ Policy context | SI-2 flaw remediation, SI-3 malicious code, SI-4 monitoring |
| Supply Chain Risk Management | SR | 12 | | ✓ Full | SR-2 supply chain plan |

---

## Automated Scan Coverage

### Azure Resource Types Scanned

| Resource Type | Controls Assessed | Method |
|--------------|-------------------|--------|
| Virtual Machines | AC-3, AU-2, CM-6, IA-2, SC-28, SI-2 | Azure Resource Graph + Defender |
| Storage Accounts | AC-3, MP-4, SC-8, SC-28 | Encryption, access keys, network rules |
| SQL Databases | AC-2, AU-2, SC-28, SI-2 | TDE, auditing, vulnerability assessment |
| Key Vaults | AC-3, IA-5, SC-12, SC-28 | Access policies, key rotation |
| Network Security Groups | AC-3, SC-7 | Inbound/outbound rules analysis |
| App Services | AC-17, CM-6, SC-8 | HTTPS, TLS version, auth settings |
| Azure AD / Entra ID | AC-2, IA-2, IA-5 | MFA, conditional access, role assignments |

### Scan Sources

| Source | Controls | Description |
|--------|----------|-------------|
| Azure Resource Graph | Infrastructure | Resource configuration queries |
| Azure Policy | CM family | Compliance state per policy assignment |
| Microsoft Defender for Cloud | Multiple | Security recommendations and scores |
| Azure AD / Entra ID | AC, IA | Identity and access configuration |

---

## Inheritance Coverage

Controls inherited from FedRAMP-authorized Cloud Service Providers (CSPs) are tracked via `ControlInheritance` entities:

| Inheritance Type | Description | SSP Narrative |
|-----------------|-------------|---------------|
| Inherited | Fully satisfied by CSP | Auto-populated template |
| Shared | CSP + customer responsibility | Template + customer narrative required |
| Customer | Entirely customer responsibility | Full narrative required |

Typical inheritance coverage for Azure Government:
- **Low baseline:** ~60% inheritable
- **Moderate baseline:** ~45% inheritable
- **High baseline:** ~35% inheritable

---

## Assessment Methods

Per NIST SP 800-53A, three methods map to tool usage:

| Method | Implementation | Tools Used |
|--------|---------------|------------|
| **Test** | Automated resource scanning, configuration validation | `compliance_assess`, `compliance_assess_control` |
| **Examine** | Document review, SSP narrative analysis, evidence collection | `compliance_collect_evidence`, `compliance_verify_evidence` |
| **Interview** | Manual attestation via narrative input | `compliance_write_narrative`, `compliance_assess_control` |
