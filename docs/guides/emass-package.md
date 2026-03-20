# eMASS Authorization Package Export

> Feature 041: eMASS Authorization Package Export — Complete workflow for generating, validating, and downloading authorization packages.

This guide covers the end-to-end process for generating an eMASS-compatible authorization package containing all required RMF artifacts as a single ZIP archive.

---

## Overview

The authorization package bundles six artifacts into a single ZIP file for eMASS submission:

| Artifact | Format | Description |
|----------|--------|-------------|
| OSCAL SSP | JSON | System Security Plan (NIST OSCAL 1.1.2) |
| OSCAL POA&M | JSON | Plan of Action & Milestones |
| OSCAL Assessment Results | JSON | Security assessment findings |
| OSCAL Assessment Plan | JSON | Security Assessment Plan (SAP) |
| SAR | DOCX | Security Assessment Report |
| Evidence | Files + manifest | Supporting evidence bundle |

---

## Prerequisites

- System registered with a FIPS 199 categorization and baseline selected
- Authorization boundary defined
- All SSP sections authored and approved
- Security Assessment Plan (SAP) finalized
- Security Assessment Report (SAR) created and approved
- POA&M items created for any open findings
- Evidence artifacts uploaded and linked to controls

---

## Readiness Check

Before generating a package, run the readiness validation to identify blocking issues:

> "Validate package readiness for [system name]"

The readiness check verifies:

1. **Authorization boundary** — boundary definition exists
2. **SSP sections** — all sections approved
3. **SAR status** — SAR exists and is approved
4. **SAP status** — SAP exists and is finalized
5. **POA&M items** — at least one POA&M item exists (warning if zero)
6. **Cross-artifact consistency** — POA&M control IDs match SSP controls
7. **OSCAL schema compliance** — all OSCAL artifacts pass schema validation
8. **Evidence coverage** — evidence exists for assessed controls (warning if incomplete)

!!! tip "Readiness Results"
    **Errors** are blocking — resolve them before generating. **Warnings** are non-blocking but should be reviewed.

---

## Generating a Package

### Via Dashboard

1. Navigate to **Documents** → **Authorization Package Generation**
2. Click **Generate Package**
3. The dialog runs a readiness check automatically
4. Select evidence mode:
    - **Embedded** — includes evidence files in the ZIP (larger file)
    - **Manifest Only** — includes evidence references only (smaller file)
5. Click **Generate Package** to start background generation
6. Track artifact-by-artifact progress in real time via SignalR
7. Download the ZIP when complete

### Via Chat / MCP

> "Generate an authorization package for [system name]"

Or use the MCP tool directly:

```
compliance_generate_package
  system_id: "my-system"
  evidence_mode: "embedded"  # or "manifest_only"
```

Check status with:

```
compliance_package_status
  package_id: "<returned-package-id>"
```

---

## SAR Lifecycle

The Security Assessment Report (SAR) is a required artifact in the authorization package.

### Creating a SAR

> "Generate a SAR for [system name]"

A new SAR is auto-populated with:

- Executive Summary — from assessment findings
- Methodology — standard assessment methodology
- Findings — all open and closed findings
- Recommendations — remediation guidance
- Conclusion & Risk Assessment — overall risk posture

### Editing SAR Sections

> "Edit the SAR findings section"

### Review and Approval

1. **Submit for review**: moves SAR from Draft to InReview
2. **Approve**: moves SAR to Approved (required for package generation)
3. **Reject**: sends back to Draft with reviewer comments

---

## OSCAL Schema Validation

All OSCAL artifacts are validated against NIST OSCAL 1.1.2 JSON schemas:

> "Validate OSCAL schema for [system name] SSP"

Supported models: `ssp`, `poam`, `assessment-results`, `assessment-plan`

!!! info "Schema Updates"
    OSCAL schemas are bundled with the application and sourced from the official NIST OSCAL releases. Schema updates are validated and included with application releases.

---

## Standalone OSCAL Exports

Individual OSCAL artifacts can be exported without generating a full package:

- **OSCAL SSP**: `compliance_export_oscal model=ssp`
- **OSCAL POA&M**: `compliance_export_oscal model=poam`
- **OSCAL Assessment Results**: `compliance_export_oscal model=assessment-results`
- **OSCAL Assessment Plan**: `compliance_export_oscal model=assessment-plan`

---

## Package History

View previously generated packages:

> "Show package history for [system name]"

Packages are retained for **90 days**. After expiration, metadata is preserved but the ZIP file is deleted and cannot be downloaded.

---

## Evidence Integration

### Embedded Mode

Evidence files are organized in the ZIP as:

```
evidence/
  AC-1/
    scan-report.pdf
    config-screenshot.png
  AC-2/
    user-access-review.xlsx
evidence-manifest.json
```

### Manifest Only Mode

The ZIP includes `evidence-manifest.json` with references to evidence artifacts stored in the system. No actual files are embedded.

!!! warning "Size Threshold"
    If total evidence exceeds 100 MB in embedded mode, the system automatically falls back to manifest-only mode.

---

## Troubleshooting

### Common eMASS Import Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| Schema validation failed | OSCAL artifact doesn't conform to schema | Run `compliance_validate_oscal_schema` and fix reported violations |
| Missing required field | SSP section or control narrative incomplete | Complete all SSP sections and control narratives |
| POA&M reference mismatch | POA&M references controls not in SSP | Ensure POA&M control IDs match the system's in-scope controls |
| SAR not approved | SAR is in Draft or InReview status | Submit and approve the SAR before generating |
| Package generation timeout | Generation exceeded 15-minute limit | Reduce evidence size or use manifest-only mode |

### Generation Failures

When a package generation fails, the error includes:

- **Failed artifact** — which artifact caused the failure
- **Error detail** — specific error message
- **Remediation** — suggested fix

Use `compliance_package_status` to view failure details for a specific package.
