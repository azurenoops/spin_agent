# Quickstart: 021 — PIA Service + System Interconnections

**Date**: 2026-03-07 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## What This Feature Adds

Feature 021 fills the two highest-priority gaps in the RMF Prepare phase:

- **Privacy Threshold Analysis (PTA)** — Auto-detect PII from system information types and determine if a full PIA is required
- **Privacy Impact Assessment (PIA)** — Generate OMB M-03-22 compliant PIA documents with AI-assisted narratives and ISSM review lifecycle
- **System Interconnections** — Register, track, and validate external system-to-system data flows
- **ISA/MOU Management** — AI-draft Interconnection Security Agreements, track agreement lifecycle and expiration
- **RMF Gate Enforcement** — Block Prepare→Categorize advancement until privacy and interconnection requirements are satisfied
- **Cross-Service Integration** — SSP §10 generation, authorization pre-checks, ConMon ISA expiration monitoring

## Prerequisites

- Security Posture Intelligence Navigator running (Feature 015+ deployed)
- At least one registered system with security categorization and information types defined
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

### 1. Run Privacy Threshold Analysis (auto-detect)

```
@ato Does "ACME Portal" need a Privacy Impact Assessment?
```

Or via MCP tool directly:
```json
{
  "tool": "compliance_create_pta",
  "arguments": {
    "system_id": "<system-id>"
  }
}
```

**Expected output**: PTA determination showing whether PII was detected in the system's information types, which SP 800-60 categories triggered it, and whether a full PIA is required.

### 2. Generate PIA (if required)

```
@ato Generate a PIA for "ACME Portal"
```

**Expected output**: Full PIA document with 8 sections, pre-populated where data exists (system info, PII categories, security safeguards from control baseline), AI-drafted narrative.

### 3. Register an interconnection

```
@ato Add an interconnection from "ACME Portal" to DISA SIPR Gateway via VPN, bidirectional, Secret classification
```

**Expected output**: New interconnection registered with Proposed status.

### 4. Generate ISA

```
@ato Generate an ISA for the DISA SIPR Gateway interconnection
```

**Expected output**: AI-drafted ISA document with system details, connection specifics, security controls, and signature blocks pre-populated from system data.

### 5. Validate agreements

```
@ato Are all interconnection agreements current for "ACME Portal"?
```

**Expected output**: Validation report showing each active interconnection, its agreement status (compliant, expiring soon, missing, or expired), and overall gate satisfaction.

### 6. Check privacy compliance

```
@ato Show me the privacy compliance status for "ACME Portal"
```

**Expected output**: Dashboard showing PTA determination, PIA status, interconnection count, agreement coverage, gate satisfaction, and overall compliance status.

## Verification Checklist

- [ ] PTA auto-detection correctly identifies PII info types (D.8.x, D.17.x, D.28.x)
- [ ] PTA with no PII returns `PiaNotRequired`
- [ ] PIA generates 8 OMB M-03-22 sections with pre-populated data
- [ ] PIA review → Approve sets expiration to +1 year
- [ ] PIA review → RequestRevision resets to Draft with deficiencies
- [ ] Interconnection CRUD works (add, list, update)
- [ ] ISA generation produces NIST 800-47 structured document
- [ ] Agreement validation detects missing, expired, and expiring agreements
- [ ] Gate 3 (privacy) blocks advancement when PIA required but not approved
- [ ] Gate 4 (interconnections) blocks when active interconnections lack signed ISAs
- [ ] `HasNoExternalInterconnections` certification satisfies Gate 4
- [ ] SSP §10 populates with interconnection data
- [ ] ConMon detects ISA approaching expiration
- [ ] RBAC: Only SecurityLead can approve PIAs and generate ISAs

## Key Files to Know

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/PrivacyModels.cs` | PTA, PIA, PiaSection entities + enums + DTOs |
| `src/Ato.Copilot.Core/Models/Compliance/InterconnectionModels.cs` | Interconnection, Agreement entities + enums + DTOs |
| `src/Ato.Copilot.Core/Interfaces/Compliance/IPrivacyService.cs` | Privacy service interface |
| `src/Ato.Copilot.Core/Interfaces/Compliance/IInterconnectionService.cs` | Interconnection service interface |
| `src/Ato.Copilot.Agents/Compliance/Services/PrivacyService.cs` | PTA analysis, PIA generation/review |
| `src/Ato.Copilot.Agents/Compliance/Services/InterconnectionService.cs` | Interconnection CRUD, ISA generation, validation |
| `src/Ato.Copilot.Agents/Compliance/Tools/PrivacyTools.cs` | 4 privacy MCP tools |
| `src/Ato.Copilot.Agents/Compliance/Tools/InterconnectionTools.cs` | 5 interconnection MCP tools |
| `src/Ato.Copilot.Agents/Compliance/Services/RmfLifecycleService.cs` | Gates 3 + 4 enforcement |
