# Quickstart: 019 — Prisma Cloud Scan Import

**Date**: 2026-03-05 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## What This Feature Adds

Feature 019 enables Security Posture Intelligence Navigator to ingest Prisma Cloud CSPM compliance scan data:

- **CSV import** — Parse Prisma Cloud compliance CSV exports, auto-resolve subscriptions, create compliance findings and control effectiveness records
- **API JSON import** — Parse Prisma Cloud API JSON with enhanced remediation guidance, CLI scripts, and alert history
- **Multi-subscription support** — A single CSV with alerts from multiple Azure subscriptions is auto-split into per-system imports
- **NIST control mapping** — Direct mapping from Prisma `complianceMetadata` to NIST 800-53 controls (no CCI intermediate step)
- **Policy catalog** — Browse unique Prisma policies with NIST mappings and finding counts
- **Trend analysis** — Compare scan cycles to track remediation progress (new/resolved/persistent findings)

## Prerequisites

- Security Posture Intelligence Navigator running (Feature 017+ deployed — scan import framework exists)
- At least one registered system with a control baseline selected
- Azure subscription mapped to the registered system (via `compliance_define_boundary` or `AzureSubscriptionProfile`)
- Prisma Cloud CSV export file or API JSON response
- .NET 9.0 SDK, Docker

## Build & Run (After Implementation)

```bash
# From repo root
cd src/Ato.Copilot.Mcp
dotnet build
dotnet run

# Or via Docker
docker compose -f docker-compose.mcp.yml up --build
```

## Quick Validation

After the feature is implemented, verify the core workflows:

### 1. Import a Prisma CSV (dry-run first)

```
@ato Import this Prisma Cloud scan for "ACME Portal" in dry-run mode
```

Or via MCP tool directly:
```json
{
  "tool": "compliance_import_prisma",
  "arguments": {
    "file_content": "<base64-encoded-csv>",
    "file_name": "prisma-alerts-2026-03-05.csv",
    "system_id": "<system-id>",
    "dry_run": true,
    "conflict_resolution": "skip"
  }
}
```

**Expected output**: Import summary showing alert counts (open, resolved, dismissed), NIST controls affected, unmapped policies, and any subscription resolution warnings.

### 2. Run actual CSV import

```
@ato Import this Prisma Cloud scan for "ACME Portal"
```

**Expected output**: Import completed — findings created, effectiveness records upserted for each NIST control, evidence attached with SHA-256 hash.

### 3. Auto-resolve subscription (no system_id)

```json
{
  "tool": "compliance_import_prisma",
  "arguments": {
    "file_content": "<base64-encoded-csv>",
    "file_name": "prisma-alerts.csv",
    "conflict_resolution": "skip"
  }
}
```

**Expected output**: Subscription auto-resolved from CSV `Account ID` column via `ISystemSubscriptionResolver`. Import assigned to the matched registered system.

### 4. Import API JSON with remediation context

```json
{
  "tool": "compliance_import_prisma_api",
  "arguments": {
    "file_content": "<base64-encoded-json>",
    "file_name": "prisma-api-export.json",
    "system_id": "<system-id>"
  }
}
```

**Expected output**: Import with enhanced remediation — CLI scripts extracted, policy recommendations stored as remediation guidance, alert history captured for audit trail.

### 5. View Prisma policy catalog

```
@ato Show Prisma Cloud policies for "ACME Portal"
```

```json
{
  "tool": "compliance_list_prisma_policies",
  "arguments": {
    "system_id": "<system-id>"
  }
}
```

**Expected output**: List of unique Prisma policies with NIST control mappings, open/resolved counts per policy, and affected resource types.

### 6. Run trend analysis

```
@ato Show Prisma Cloud compliance trend for "ACME Portal"
```

```json
{
  "tool": "compliance_prisma_trend",
  "arguments": {
    "system_id": "<system-id>",
    "group_by": "nist_control"
  }
}
```

**Expected output**: Comparison of the last two Prisma imports showing new findings, resolved findings, persistent findings, remediation rate, and breakdown by NIST control.

### 7. Verify downstream artifacts include Prisma data

```
@ato Generate a SAR for "ACME Portal"
```

**Expected output**: SAR includes Prisma-sourced findings with `Source = "Prisma Cloud"` in the Assessment Findings section.

## Verification Checklist

- [ ] CSV import creates `ComplianceFinding` records with `ScanSource = Cloud`, `Source = "Prisma Cloud"`
- [ ] Multi-row CSV alerts (same Alert ID) grouped into one finding with multiple NIST control effectiveness records
- [ ] NIST controls extracted from `Compliance Standard` column (filtered for "NIST 800-53")
- [ ] Severity mapping: critical/high → CatI, medium → CatII, low → CatIII, informational → no CAT
- [ ] Status mapping: open → Open, resolved → Remediated, dismissed → Accepted, snoozed → Open
- [ ] Subscription auto-resolution via `ISystemSubscriptionResolver`
- [ ] Multi-subscription CSV auto-split into per-system import records
- [ ] Unresolved subscriptions produce actionable error with `compliance_register_system` guidance
- [ ] Non-Azure alerts skipped with warning in auto-resolve mode, accepted when `system_id` explicit
- [ ] `ControlEffectiveness` records use aggregate evaluation (ANY open → OtherThanSatisfied)
- [ ] Evidence created with `EvidenceType = "CloudScanResult"`, SHA-256 hash, `CollectionMethod = "Automated"`
- [ ] Dry-run shows accurate preview without persisting
- [ ] Conflict resolution (Skip/Overwrite/Merge) works on `PrismaAlertId` matching key
- [ ] File size limit enforced at 25MB after base64 decode
- [ ] API JSON import extracts remediation guidance and CLI scripts
- [ ] API JSON import captures alert history for audit trail
- [ ] Trend analysis compares two imports: new/resolved/persistent with remediation rate
- [ ] `compliance_list_imports` returns Prisma imports with correct `ImportType`
- [ ] SAR, SSP, POA&M, SAP generation includes Prisma-sourced findings

## Key Files to Know

| File | Purpose |
|------|---------|
| `specs/019-prisma-cloud-import/spec.md` | Feature specification (707 lines, 13 parts) |
| `specs/019-prisma-cloud-import/plan.md` | Implementation plan |
| `specs/019-prisma-cloud-import/data-model.md` | Entity extensions and DTOs |
| `specs/019-prisma-cloud-import/contracts/mcp-tools.md` | MCP tool contracts |
| `specs/019-prisma-cloud-import/tasks.md` | Task breakdown (56 tasks, T001–T056) |
| `src/Ato.Copilot.Core/Models/Compliance/ScanImportModels.cs` | Entities and enums (to be extended) |
| `src/Ato.Copilot.Core/Interfaces/Compliance/IScanImportService.cs` | Service interface (to be extended) |
| `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/ScanImportService.cs` | Import orchestration (to be extended) |
| `src/Ato.Copilot.Agents/Compliance/Services/SystemSubscriptionResolver.cs` | Subscription resolver (existing, reused) |
