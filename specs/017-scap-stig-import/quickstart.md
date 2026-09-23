# Quickstart: 017 — SCAP/STIG Viewer Import

**Date**: 2026-03-01 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## What This Feature Adds

Feature 017 enables Security Posture Intelligence Navigator to ingest real-world STIG assessment data:

- **CKL import** — Parse DISA STIG Viewer checklist files, create compliance findings and control effectiveness records
- **XCCDF import** — Parse SCAP Compliance Checker results with automated evidence chains
- **CKL export** — Generate CKL files from assessment data for eMASS upload
- **Import management** — Track import history, view summaries, detect unmatched rules
- **Automatic NIST mapping** — STIG → CCI → NIST 800-53 control resolution on import

## Prerequisites

- Security Posture Intelligence Navigator running (Feature 015+ deployed)
- At least one registered system with a control baseline selected
- STIG Viewer CKL file or SCAP SCC XCCDF results file
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

After the feature is implemented, verify the core import workflow:

### 1. Import a CKL file (dry-run first)

```
@ato Import this STIG Viewer checklist for "ACME Portal" in dry-run mode
```

Or via MCP tool directly:
```json
{
  "tool": "compliance_import_ckl",
  "arguments": {
    "system_id": "<system-id>",
    "file_content": "<base64-encoded-ckl>",
    "file_name": "Windows_Server_2022_STIG_V1R1.ckl",
    "dry_run": true,
    "conflict_resolution": "skip"
  }
}
```

**Expected output**: Import summary showing finding counts (Open, NotAFinding, N/A, Not_Reviewed), NIST controls affected, and any unmatched rules.

### 2. Run actual import

```
@ato Import this CKL file for "ACME Portal"
```

**Expected output**: Import completed — findings created, effectiveness records upserted, evidence attached with SHA-256 hash.

### 3. Import XCCDF results

```
@ato Import these SCAP SCC results for "ACME Portal"
```

### 4. View import history

```
@ato Show import history for "ACME Portal"
```

### 5. Export CKL for eMASS

```
@ato Export a CKL file for "ACME Portal" using the Windows Server 2022 STIG
```

## Verification Checklist

- [ ] CKL import creates `ComplianceFinding` records with `StigFinding = true`
- [ ] STIG findings map to NIST controls via CCI chain
- [ ] `ControlEffectiveness` records created for each affected NIST control
- [ ] Evidence created with SHA-256 hash of source file
- [ ] Dry-run shows accurate preview without persisting
- [ ] Conflict resolution (Skip/Overwrite/Merge) works on re-import
- [ ] XCCDF import captures compliance score
- [ ] CKL export produces valid XML re-importable by STIG Viewer
- [ ] Import history queryable by system and benchmark

## Key Files to Know

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/ScanImportModels.cs` | New entities, enums, DTOs |
| `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/` | Parser and import service implementations |
| `src/Ato.Copilot.Agents/Compliance/Tools/ScanImportTools.cs` | 5 MCP tools |
| `src/Ato.Copilot.Agents/Compliance/Resources/cci-nist-mapping.json` | CCI→NIST mapping (existing, 7,575 entries) |
| `src/Ato.Copilot.Agents/Compliance/Resources/stig-controls.json` | STIG knowledge base (existing, 880 entries) |
| `specs/017-scap-stig-import/` | Spec, plan, research, data model, contracts |

## Reference

- [Spec](spec.md) — Full feature specification with CKL/XCCDF format documentation
- [Plan](plan.md) — Phased implementation plan with 60 tasks
- [Research](research.md) — CKL/XCCDF format deep dive, integration point analysis
- [Data Model](data-model.md) — Entity definitions, enums, DTOs, EF Core configuration
- [MCP Tool Contracts](contracts/mcp-tools.md) — All 5 tool input/output contracts
