# Quickstart: eMASS Authorization Package Export

**Feature**: 041-emass-package | **Date**: 2026-03-19

## Prerequisites

1. **System with completed SSP**: All 13 SSP sections in `Approved` status (Feature 022)
2. **Assessment data**: `ControlEffectivenessRecord` entries for baseline controls
3. **POA&M items**: Active `PoamItem` entries for non-compliant controls (Feature 039)
4. **Finalized SAP**: `SecurityAssessmentPlan` in `Finalized` status (Feature 018)
5. **Evidence artifacts**: Uploaded via evidence repository and linked to controls (Feature 038)
6. **.NET 8 SDK** installed
7. **Node.js 18+** for dashboard frontend

## Build & Run

```bash
# Build the solution
dotnet build Ato.Copilot.sln

# Run migrations (adds AuthorizationPackage, SAR entities)
dotnet ef database update --project src/Ato.Copilot.Core --startup-project src/Ato.Copilot.Mcp

# Run unit tests
dotnet test tests/Ato.Copilot.Tests.Unit

# Run integration tests
dotnet test tests/Ato.Copilot.Tests.Integration

# Start the MCP server (includes dashboard API)
cd src/Ato.Copilot.Mcp
dotnet run
```

Dashboard:
```bash
cd src/Ato.Copilot.Dashboard
npm install
npm run dev
```

## Workflow: Generate Authorization Package

### Step 1 — Create a SAR

The SAR is the only new artifact that doesn't exist from prior features. Generate one from existing assessment data:

**Via MCP Tool**:
```
compliance_generate_sar system_id="<system-guid>"
```

**Via Dashboard API**:
```bash
curl -X POST http://localhost:5000/api/v1/systems/{systemId}/sar \
  -H "Content-Type: application/json" \
  -d '{"title": "Security Assessment Report — FY26 Q2"}'
```

This auto-populates findings from `ControlEffectivenessRecord` data and creates editable narrative sections.

### Step 2 — Edit SAR Narratives

Edit the executive summary and recommendations sections:

**Via MCP Tool**:
```
compliance_edit_sar_section sar_id="<sar-guid>" section_type="executive_summary" content="## Executive Summary\n\n..."
compliance_edit_sar_section sar_id="<sar-guid>" section_type="recommendations" content="## Recommendations\n\n..."
```

### Step 3 — Approve the SAR

Submit for review and approve:

```
compliance_review_sar sar_id="<sar-guid>" action="submit"
compliance_review_sar sar_id="<sar-guid>" action="approve"
```

### Step 4 — Validate Package Readiness

Check that all artifacts are ready:

```
compliance_validate_package system_id="<system-guid>"
```

This checks: artifact presence, OSCAL version consistency, cross-artifact control ID matching, SSP section status, POA&M reference integrity, evidence coverage.

### Step 5 — Generate the Package

```
compliance_generate_package system_id="<system-guid>" evidence_mode="embedded"
```

This enqueues a background job that:
1. Generates OSCAL SSP (1.1.2)
2. Generates OSCAL POA&M (1.1.2)
3. Generates OSCAL Assessment Results (1.1.2)
4. Generates OSCAL SAP (1.1.2)
5. Generates SAR Word document
6. Generates evidence manifest + bundles evidence files
7. Validates all OSCAL artifacts against NIST JSON schemas
8. Runs cross-artifact consistency checks
9. Assembles ZIP archive atomically
10. Sends SignalR notification on completion

### Step 6 — Monitor & Download

**Poll status**:
```
compliance_package_status package_id="<package-guid>"
```

**Download via API**:
```bash
curl -O http://localhost:5000/api/v1/systems/{systemId}/packages/{packageId}/download
```

## Standalone Exports

Export individual OSCAL artifacts without generating a full package:

**Dashboard UI**: Documents page → Exports section → new "OSCAL POA&M" and "OSCAL Assessment Results" buttons

**API**:
```bash
# OSCAL POA&M
curl -O http://localhost:5000/api/v1/systems/{systemId}/exports/oscal-poam

# OSCAL Assessment Results
curl -O http://localhost:5000/api/v1/systems/{systemId}/exports/oscal-assessment-results

# OSCAL SAP
curl -O http://localhost:5000/api/v1/systems/{systemId}/exports/oscal-sap
```

**MCP Tool** (existing, updated with `assessment-plan` model):
```
compliance_export_oscal system_id="<system-guid>" model="assessment-plan"
```

## OSCAL Schema Validation

Validate any OSCAL artifact against bundled NIST 1.1.2 JSON schemas:

```
compliance_validate_oscal_schema system_id="<system-guid>" model="ssp"
```

## Key Files

| Component | Path |
|-----------|------|
| Package service | `src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs` |
| Background worker | `src/Ato.Copilot.Agents/Compliance/Services/PackageBackgroundService.cs` |
| SAR service | `src/Ato.Copilot.Agents/Compliance/Services/SecurityAssessmentReportService.cs` |
| OSCAL SAP export | `src/Ato.Copilot.Agents/Compliance/Services/OscalSapExportService.cs` |
| JSON Schema validator | `src/Ato.Copilot.Agents/Compliance/Services/OscalSchemaValidationService.cs` |
| Package validation | `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs` |
| MCP tools | `src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs` |
| SAR tools | `src/Ato.Copilot.Agents/Compliance/Tools/SarTools.cs` |
| Dashboard API | `src/Ato.Copilot.Mcp/Controllers/PackageController.cs` |
| Entity models | `src/Ato.Copilot.Core/Models/Compliance/AuthorizationPackage.cs` etc. |
| OSCAL schemas | `src/Ato.Copilot.Core/Resources/oscal-schemas/` (embedded resources) |
| Unit tests | `tests/Ato.Copilot.Tests.Unit/` |
| Integration tests | `tests/Ato.Copilot.Tests.Integration/` |

## NuGet Dependencies (new)

| Package | Version | Purpose |
|---------|---------|---------|
| `JsonSchema.Net` | latest | NIST OSCAL JSON Schema Draft 2020-12 validation |
| `DocumentFormat.OpenXml` | latest | SAR Word document generation (may already be transitive) |
