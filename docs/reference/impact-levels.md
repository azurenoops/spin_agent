# DoD Impact Levels Reference

> Impact Level definitions (IL2–IL6), data classifications, security requirements, and Azure region mappings.

---

## Overview

Department of Defense (DoD) Impact Levels classify information systems by the sensitivity of data processed, stored, or transmitted. Each level imposes progressively stricter security requirements aligned with NIST 800-53 baselines and FedRAMP authorization levels.

Security Posture Intelligence Navigator models impact levels through the `ImpactLevel` entity, which associates data classification rules, security requirements, and Azure-specific implementation guidance.

---

## Impact Level Matrix

| Level | Name | Data Classification | FedRAMP Baseline | NIST Baseline |
|-------|------|-------------------|-----------------|--------------|
| **IL2** | Public / Non-CUI | Publicly releasable DoD data, non-sensitive | FedRAMP Low / Moderate | Low / Moderate |
| **IL4** | CUI / FOUO | Controlled Unclassified Information, For Official Use Only, Privacy Act, HIPAA | FedRAMP Moderate (DoD) | Moderate |
| **IL5** | CUI / Mission Critical | Higher-sensitivity CUI, National Security Systems, mission-critical data | FedRAMP High (DoD) | High |
| **IL6** | Classified (SECRET) | Classified National Security Information up to SECRET | N/A (classified overlay) | High + classified overlay |

> **Note:** IL3 was retired and merged into IL2 by DISA. IL1 is no longer used.

---

## Security Requirements by Level

### IL2 — Public / Non-CUI

| Category | Requirement |
|----------|-------------|
| **Encryption in Transit** | TLS 1.2+ |
| **Encryption at Rest** | AES-256 (platform-managed keys acceptable) |
| **Network** | Internet-accessible, standard Azure Commercial |
| **Personnel** | No clearance required; background check per contract |
| **Physical Security** | Standard commercial data center (SOC 2 Type II) |

### IL4 — CUI / FOUO

| Category | Requirement |
|----------|-------------|
| **Encryption in Transit** | TLS 1.2+ with FIPS 140-2 Level 1 validated modules |
| **Encryption at Rest** | AES-256 with FIPS 140-2 Level 1 validated modules |
| **Network** | Azure Government region; logical separation from commercial tenants |
| **Personnel** | National Agency Check with Inquiries (NACI) or equivalent |
| **Physical Security** | FedRAMP Moderate physical controls; US-based data centers |

### IL5 — CUI / Mission Critical

| Category | Requirement |
|----------|-------------|
| **Encryption in Transit** | TLS 1.2+ with FIPS 140-2 Level 2 validated modules |
| **Encryption at Rest** | AES-256 with FIPS 140-2 Level 2; customer-managed keys recommended |
| **Network** | Azure Government with dedicated virtual networks; no public endpoints |
| **Personnel** | IT-II or IT-III position sensitivity; favorably adjudicated T3/T5 investigation |
| **Physical Security** | FedRAMP High physical controls; CONUS facilities only |

### IL6 — Classified (SECRET)

| Category | Requirement |
|----------|-------------|
| **Encryption in Transit** | NSA-approved (Suite B / CNSA) cryptography |
| **Encryption at Rest** | NSA-approved Type 1 or Suite B encryption |
| **Network** | Azure Government Secret; air-gapped from commercial/government networks |
| **Personnel** | SECRET clearance minimum |
| **Physical Security** | SCIF or equivalent classified processing environment |

---

## Azure Region Mapping

| Impact Level | Azure Environment | Regions |
|-------------|------------------|---------|
| IL2 | Azure Commercial | Any US region |
| IL4 | Azure Government | US Gov Virginia, US Gov Arizona, US Gov Texas |
| IL5 | Azure Government | US Gov Virginia, US Gov Arizona (IL5-approved) |
| IL6 | Azure Government Secret | US Gov Secret regions (air-gapped) |

---

## Control Baseline Implications

Each impact level maps to a NIST 800-53 baseline with additional DoD overlays:

| Impact Level | NIST Baseline | Approximate Control Count | Additional Overlays |
|-------------|--------------|--------------------------|-------------------|
| IL2 | Low / Moderate | 170–325 | FedRAMP Low/Moderate |
| IL4 | Moderate (enhanced) | 325+ | CNSSI 1253 CUI overlay, FedRAMP Moderate |
| IL5 | High | 421+ | CNSSI 1253 High overlay, FedRAMP High |
| IL6 | High + Classified | 500+ | CNSSI 1253 Classified overlay, NSS controls |

### Tailoring Impact

Higher impact levels restrict tailoring options:
- **IL2**: Liberal tailoring allowed; compensating controls accepted broadly
- **IL4**: Moderate tailoring; CUI-specific controls cannot be tailored out
- **IL5**: Restrictive tailoring; AO approval required for each tailored control
- **IL6**: Minimal tailoring; classified overlay controls are non-negotiable

---

## Tool Integration

### Setting Impact Level During Categorization

Impact level is derived from FIPS 199 categorization via `compliance_categorize_system`:

| FIPS 199 High-Water Mark | Mapped Impact Level |
|--------------------------|-------------------|
| Low | IL2 |
| Moderate | IL4 |
| High | IL5 |

IL6 requires explicit classification authority and is set via system registration metadata.

### Impact Level in Dashboard

The `compliance_multi_system_dashboard` tool displays impact level per system, allowing portfolio managers to filter and sort by sensitivity.

### Impact Level in Authorization

Authorization decisions via `compliance_issue_authorization` consider impact level when determining:
- Required authorization artifacts (IL5+ requires additional documentation)
- ATO duration recommendations (higher levels may have shorter authorization periods)
- Conditions and constraints specific to the impact level

---

## Data Model

```csharp
record ImpactLevel(
    string Level,              // "IL2", "IL4", "IL5", "IL6"
    string Name,               // Display name
    string DataClassification,  // Data classification description
    SecurityRequirements SecurityRequirements,
    AzureImpactGuidance AzureImplementation,
    List<string> AdditionalControls);

record SecurityRequirements(
    string Encryption,         // Encryption standard
    string Network,            // Network boundary requirements
    string Personnel,          // Clearance requirements
    string PhysicalSecurity);  // Physical security requirements

record AzureImpactGuidance(
    string Region,             // Required Azure region
    string Network,            // Azure network guidance
    string Identity,           // Azure identity guidance
    string Encryption,         // Azure encryption guidance
    List<string> Services);    // Recommended Azure services
```
