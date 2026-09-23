# STIG Coverage Reference

> STIG IDs in the library, NIST control mapping, CAT severity levels, and technology applicability.

---

## Overview

Security Posture Intelligence Navigator includes a STIG (Security Technical Implementation Guide) library parsed from DISA XCCDF benchmarks. STIGs are mapped to NIST 800-53 controls via CCI (Control Correlation Identifier) cross-references.

### Data Model

```
StigBenchmark (1) ──── (*) StigControl
CciMapping            ──── Maps CCI → NIST Control ID
StigCrossReference    ──── Links STIG Rule → CCI → NIST Control
```

---

## CAT Severity Levels

DISA assigns CAT (Category) severity to each STIG rule:

| CAT Level | Severity | Impact | CI/CD Gate |
|-----------|----------|--------|-----------|
| **CAT I** | Critical | Directly affects confidentiality, integrity, or availability. Exploitation could cause mission failure. | **Blocks** deployment |
| **CAT II** | Significant | Potential for system compromise. May lead to degradation of mission capability. | **Blocks** deployment |
| **CAT III** | Low | Administrative or documentation gaps. Minimal direct security impact. | Warning only |

### CAT-to-FindingSeverity Mapping

| CAT Level | FindingSeverity | Risk Level |
|-----------|----------------|------------|
| CAT I | Critical / High | Unacceptable without mitigation |
| CAT II | Medium | Requires POA&M or remediation |
| CAT III | Low / Informational | Document and track |

---

## STIG-to-NIST Mapping

The `compliance_show_stig_mapping` tool reveals control-to-STIG relationships:

### Mapping Chain

```
NIST 800-53 Control (e.g., AC-2)
    ↕ (via CCI)
CCI Identifier (e.g., CCI-000015)
    ↕ (via XCCDF metadata)
STIG Rule (e.g., V-12345)
    ↕ (via benchmark)
STIG Benchmark (e.g., Windows Server 2022 STIG)
```

### Common Mappings

| NIST Family | Example Controls | STIG Technologies |
|------------|-----------------|-------------------|
| AC (Access Control) | AC-2, AC-3, AC-6 | Windows, Linux, Active Directory |
| AU (Audit) | AU-2, AU-3, AU-6 | Windows, Linux, SQL Server |
| CM (Configuration) | CM-2, CM-6, CM-7 | Windows, Linux, Network Devices |
| IA (Identification) | IA-2, IA-5, IA-8 | Windows, Active Directory, PKI |
| SC (Sys & Comm) | SC-7, SC-8, SC-28 | Network Devices, TLS, Encryption |
| SI (Sys & Info Integrity) | SI-2, SI-3, SI-4 | Windows, Linux, Antivirus |

---

## Technology Coverage

### Supported STIG Benchmarks

| Technology | Benchmark | Version Track | Typical CAT I Count |
|-----------|-----------|---------------|-------------------|
| Windows Server 2022 | MS/DC | Quarterly | 8-12 |
| Windows Server 2019 | MS/DC | Quarterly | 10-15 |
| Windows 11 | STIG | Quarterly | 5-8 |
| Red Hat Enterprise Linux 8/9 | STIG | Quarterly | 10-15 |
| Ubuntu 22.04 | STIG | Quarterly | 8-12 |
| SQL Server 2019/2022 | Instance/Database | Quarterly | 5-8 |
| IIS 10.0 | Server/Site | Quarterly | 3-5 |
| .NET Framework 4.0 | STIG | Semi-annual | 2-4 |
| Apache Tomcat | STIG | Semi-annual | 3-5 |
| Cisco IOS-XE | NDM/RTR | Quarterly | 5-8 |
| Azure Cloud | SRG | Semi-annual | 10-15 |

### STIG Data Properties

| Property | Description |
|----------|-------------|
| `StigId` | DISA rule identifier (e.g., V-12345) |
| `Title` | Rule title / requirement |
| `Description` | Full rule description |
| `CatSeverity` | CAT I, II, or III |
| `FixText` | Remediation guidance |
| `CheckContent` | Verification procedure |
| `BenchmarkId` | Parent benchmark identifier |

---

## Tool Integration

### Viewing STIG Mappings

```
Tool: compliance_show_stig_mapping
Parameters:
  system_id: "<system-guid>"
  control_ids: "AC-2,IA-2,CM-6"
```

Returns per-control list of applicable STIGs with:
- STIG ID and title
- CAT severity
- Benchmark source
- Fix text summary

### IaC Scanning with STIG Rules

The VS Code extension maps IaC compliance findings to STIG rules where applicable, showing:
- STIG rule ID in diagnostic message
- CAT severity as VS Code diagnostic severity
- Fix text as code action description

### STIG Search Tools

| Tool | Description |
|------|-------------|
| `compliance_search_stigs` | Search STIG library by keyword |
| `compliance_explain_stig` | Explain a specific STIG rule with NIST mapping |

---

## CI/CD Integration

The GitHub Actions compliance gate (`ato-compliance-gate`) uses CAT severity to determine pass/fail:

| CAT Level | Gate Behavior |
|-----------|-------------|
| CAT I | **Blocks** PR merge |
| CAT II | **Blocks** PR merge |
| CAT III | Warning annotation only |

Risk acceptances for specific findings bypass the gate block for accepted controls.

---

## STIG Import & Export

> Feature 017: SCAP/STIG Viewer Import

Security Posture Intelligence Navigator can import and export STIG assessment data in industry-standard formats:

### Supported Import Formats

| Format | Source | Tool | Collection Method |
|--------|--------|------|-------------------|
| **CKL** | DISA STIG Viewer | `compliance_import_ckl` | Manual |
| **XCCDF** | SCAP Compliance Checker | `compliance_import_xccdf` | Automated |

### Import Processing Pipeline

```
CKL/XCCDF File
  ↓ Parse & validate
STIG Rule Resolution
  ↓ Match VulnId/RuleId → StigControl
CCI/NIST Mapping
  ↓ Cross-reference StigControl → CCI → NIST 800-53
Finding Creation
  ↓ ComplianceFinding with CAT severity
Effectiveness Update
  ↓ Aggregate per-control effectiveness
Evidence Capture
  ↓ SHA-256 hashed import evidence
```

### Status Mapping

#### CKL Status → Finding Status

| CKL Status | Finding Status |
|------------|----------------|
| Open | Open |
| NotAFinding | Remediated |
| Not_Applicable | Accepted |
| Not_Reviewed | Open |

#### XCCDF Result → Finding Status

| XCCDF Result | Finding Status |
|--------------|----------------|
| fail | Open |
| pass | Remediated |
| notapplicable | Accepted |
| error | Open |
| unknown | Open |
| notchecked | Open |

### Export Format

| Format | Target | Tool |
|--------|--------|------|
| **CKL** | DISA STIG Viewer / eMASS | `compliance_export_ckl` |

Exported CKL files follow the DISA STIG Viewer CHECKLIST XML schema with ASSET, STIG_INFO, and VULN elements.

### Conflict Resolution

When re-importing files that contain findings already present in the database:

| Strategy | Behavior |
|----------|----------|
| **Skip** | Keep existing finding unchanged (default) |
| **Overwrite** | Replace existing finding with imported data |
| **Merge** | Keep whichever finding has the higher severity |

### Duplicate Detection

Imports use SHA-256 file hashing to detect and warn about duplicate file imports.
