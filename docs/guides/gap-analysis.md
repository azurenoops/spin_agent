# Gap Analysis API

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The gap-analysis API reports which NIST 800-53 controls in a system's baseline
are covered by Security Capabilities and which remain unmapped. The dedicated
dashboard page and sidebar link have been retired; use capability, assessment,
and remediation views for interactive workflows.

---

## Overview

Gap analysis answers the question: *"Which controls do we still need to address?"*

It compares the system's `ControlBaseline` (all applicable controls) against `CapabilityControlMapping` records (controls covered by security capabilities) to identify coverage gaps.

---

## Response Metrics

The response includes:

- **Total Controls** — Total baseline controls for this system
- **Covered** — Controls with at least one capability mapping
- **Gaps** — Controls with no capability mapping
- **Coverage** — Overall coverage percentage

### Per-Family Breakdown

The response contains one entry per NIST 800-53 control family:

| Column | Description |
|--------|-------------|
| **Family** | Control family name (e.g., "AC — Access Control") |
| **Total** | Number of controls in this family |
| **Covered** | Controls with capability mappings |
| **Gaps** | Controls without mappings |
| **Coverage** | Visual bar + percentage |

Each family entry lists its **unmapped controls**, including:
- Control ID (e.g., "AC-4")
- Control title

---

## How Coverage Is Computed

1. The system's `ControlBaseline.ControlIds` provides the full list of applicable controls
2. `CapabilityControlMapping` records scoped to this system (or org-wide with null scope) provide covered controls
3. Coverage = Covered Controls / Total Controls × 100

!!! tip "Improving Coverage"
    To address gaps, navigate to the [Security Capabilities Library](/guides/security-capabilities/) and create or update capabilities with mappings for the unmapped controls.

---

## Baseline Levels

Coverage varies significantly by baseline level:

| Baseline | Approximate Control Count |
|----------|--------------------------|
| **Low** | ~125 controls |
| **Moderate** | ~325 controls |
| **High** | ~421 controls |

---

## API Endpoint

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/dashboard/systems/{systemId}/gaps` | Get gap analysis for a system |

---

## Boundary-Scoped Gap Analysis (Feature 033)

Supply the optional `boundaryDefinitionId` query parameter to limit results to
controls covered by capabilities mapped to that boundary, including
organization-wide mappings. Without the parameter, the response includes
combined coverage and per-boundary comparison data.
