# Gap Analysis

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The Gap Analysis page shows which NIST 800-53 controls in your system's baseline are covered by Security Capabilities and which remain unmapped.

---

## Overview

Gap analysis answers the question: *"Which controls do we still need to address?"*

It compares the system's `ControlBaseline` (all applicable controls) against `CapabilityControlMapping` records (controls covered by security capabilities) to identify coverage gaps.

---

## Reading the Coverage Matrix

Navigate to `/systems/{systemId}/gaps` to view the gap analysis.

### Summary Metrics

Four metric cards at the top show:

- **Total Controls** — Total baseline controls for this system
- **Covered** — Controls with at least one capability mapping
- **Gaps** — Controls with no capability mapping
- **Coverage** — Overall coverage percentage

### Per-Family Breakdown

The coverage matrix table shows one row per NIST 800-53 control family:

| Column | Description |
|--------|-------------|
| **Family** | Control family name (e.g., "AC — Access Control") |
| **Total** | Number of controls in this family |
| **Covered** | Controls with capability mappings |
| **Gaps** | Controls without mappings |
| **Coverage** | Visual bar + percentage |

### Highlighting

- Families with **<50% coverage** are highlighted in **red** to draw attention
- The coverage bar uses green for covered portion and gray for gaps

---

## Expanding Family Details

Click the expand arrow on any family row to see the list of **unmapped controls** — these are the specific controls that need security capabilities assigned.

Each unmapped control shows:
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

When a system has multiple boundary definitions, the gap analysis page adds a **boundary selector** dropdown:

- **All Boundaries** (default): Shows combined coverage across all boundaries, plus a **Boundary Comparison Table** with color-coded per-boundary coverage percentages
- **Specific Boundary**: Filters gap results to show only controls covered by capabilities mapped to the selected boundary (including organization-wide/null-FK mappings)

The boundary comparison table uses color coding:
- 🟢 Green (≥80%): Strong coverage
- 🟡 Yellow (50–79%): Partial coverage  
- 🔴 Red (<50%): Needs attention
