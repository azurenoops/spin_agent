# ADR-068 — OSCAL Support Strategy for SPIN Agent

**Status:** PROPOSED  
**Date:** 2026-06-18  
**Author:** Oracle (Intelligence & Knowledge Management Lead)  
**Requested by:** Batman (Strategic Assessment, W10 Dispatch Queue)  
**Decision Required by:** W10 Scoping Lock  
**Spec cross-reference:** Gap #1 from Batman W10 Strategic Assessment; spec `022-ssp-full-oscal`

---

## Context

SPIN Agent is an AI-powered compliance copilot for DoD teams navigating the NIST Risk Management Framework. The platform generates SSP narratives, control implementations, SAP/SAR artifacts, and POA&M entries — all currently exported as Word/Excel documents for eMASS submission.

OSCAL (Open Security Controls Assessment Language) is NIST's machine-readable standard for expressing security controls, system implementations, assessment plans, results, and POA&M data in structured JSON, XML, or YAML formats.

### Regulatory Mandate — Source of Urgency

**OMB M-24-15** (Modernizing the Federal Risk and Authorization Management Program), issued July 25, 2024, contains hard compliance deadlines:

| Deadline | Requirement |
|----------|-------------|
| **180 days** (Jan 2025) | Federal agencies must update policy to align with the memo |
| **24 months** (July 2026) | All GRC and system-inventory tools must be capable of ingesting and producing machine-readable OSCAL artifacts |

Direct quote from M-24-15:
> *"Agencies must have the necessary procedures in place to produce, accept, and submit materials in machine-readable formats… FedRAMP should receive all authorization artifacts — SSP, SAP, SAR, and POA&M — as machine-readable data through APIs."*

**This is a legal compliance deadline, not a trend.** Any DoD compliance copilot serving federal agencies or FedRAMP-scoped customers that cannot produce OSCAL by July 2026 is non-compliant with federal acquisition requirements. 400+ CSPs are already converting.

### Competitive Landscape (Intelligence Summary)

| Competitor | OSCAL posture |
|------------|--------------|
| RegScale | OSCAL-native since 2023 — top differentiator in enterprise sales |
| GovRAMP | OSCAL adapter implemented; native pipeline roadmapped |
| Telos Xacta | OSCAL import/export, marketed as FedRAMP automation tool |
| SPIN Agent (current) | No OSCAL support — gap vs. all named competitors |

Batman's gap assessment: "OSCAL is the compliance automation language of the federal government. Not supporting it means we can't interoperate with the emerging ecosystem and can't claim to be a true ATO copilot."

### W10 Scoping Context

W10 is a bounded sprint window. Engineering capacity is allocated. The OSCAL decision is architecturally significant because:

1. **Option A** (native generation) requires OSCAL data model knowledge built into the compliance engine at the record level — not addable post-hoc without significant refactoring of `ControlImplementation`, `AssessmentRecord`, `PoamItem`, and `AuthorizationDecision` entities.
2. **Option B** (export adapter) can be implemented as a projection layer over existing entities without touching the core data model.
3. **Option C** (defer) risks:
   - Falling outside the July 2026 OMB M-24-15 deadline during W11 planning
   - Losing enterprise deals where OSCAL is now an RFP line item
   - Allowing RegScale's OSCAL-native positioning to harden in the market

---

## Decision

**Recommended Option: B — OSCAL Export Adapter for W10, with Option A as W12+ Strategic Initiative.**

This matches Batman's recommendation. The analysis below provides the evidence foundation for John's approval.

---

## Options Considered

### Option A: Native OSCAL Generation (High Effort, High Value)

**What it means:**  
Refactor the SPIN Agent data model and compliance engine to treat OSCAL as the authoritative internal format. All compliance artifacts (SSP, SAP, SAR, POA&M) are generated and stored as OSCAL-native structures. Word/Excel export becomes a rendering concern on top of OSCAL-native records.

**Architecture implications:**
- `ControlImplementation` narratives stored as OSCAL `implemented-requirement` elements
- `AssessmentRecord` maps to OSCAL `assessment-results` layer
- `PoamItem` maps to OSCAL `finding` / `risk` / `observation` model
- `AuthorizationDecision` maps to OSCAL `authorization` component
- New database schema for OSCAL component registry (catalogs, profiles)
- OSCAL catalog ingestion pipeline for NIST SP 800-53 Rev 5 + DoD overlays

**Pros:**
- True competitive differentiation vs. RegScale ("OSCAL-native" is a genuine enterprise differentiator)
- Machine-readable output enables FedRAMP automation workflows
- Future-proof: OMB M-24-15 compliance without structural debt
- Enables control catalog crosswalks (SP 800-53 ↔ CMMC ↔ IL5) natively
- Positions for DoD's long-term move toward "ATO-as-code"

**Cons:**
- High engineering effort: 8–12 weeks minimum for a correct OSCAL-native implementation
- W10 cannot absorb this without dropping 3–4 other P1 roadmap items (CMMC overlays, Tanium integration, P1 Marketplace, cATO Dashboard)
- Risk of incomplete implementation shipping with schema debt
- OSCAL schemas are complex — FedRAMP extensions add additional specificity beyond base NIST schemas
- Wrong time in product lifecycle: data model must be stable before OSCAL-native can be implemented cleanly

**Effort:** ~8–12 weeks | **Risk:** High | **W10 fit:** No

---

### Option B: OSCAL Export Adapter (Medium Effort, Satisfies Mandate) ✅ RECOMMENDED

**What it means:**  
Build a projection/export layer that transforms SPIN Agent's existing compliance record model into OSCAL-compliant JSON/XML outputs. The internal data model does not change. OSCAL is generated on-demand from `ControlImplementation`, `AssessmentRecord`, `PoamItem`, and `AuthorizationDecision` records at export time.

**Architecture:**
```
SPIN Agent Records (existing)
        ↓
OSCAL Projection Service
        ↓
OSCAL JSON/XML (SSP, SAP, SAR, POA&M)
        ↓
Export endpoint + eMASS / FedRAMP upload
```

**Projection scope (W10):**
- **SSP export:** `RegisteredSystem` + `SecurityCategorization` + `ControlImplementation` records → OSCAL System Security Plan (JSON)
- **POA&M export:** `PoamItem` + `PoamMilestone` records → OSCAL POA&M component
- **Control catalog mapping:** SP 800-53 Rev 5 control IDs → OSCAL catalog reference URIs
- **Phase 2 (W11 candidate):** SAP → OSCAL Assessment Plan; SAR → OSCAL Assessment Results

**What adapter does NOT require:**
- Schema changes to existing entities
- Disruption to current SSP narrative generation pipeline
- Changes to the eMASS Word/Excel export workflow (both coexist)
- Task #314 resolution (seed pipeline is independent)

**Pros:**
- W10-deliverable: 3–4 weeks for SSP + POA&M projection (two most critical artifacts per M-24-15)
- Satisfies the July 2026 OMB M-24-15 24-month mandate on schedule
- Zero regression risk on existing export workflows
- Gives enterprise customers an OSCAL checkbox before W12
- Establishes the projection pattern that W12 native migration can adopt selectively
- Unblocks higher-classification IL5 deals where OSCAL is now an RFP requirement

**Cons:**
- Not architecturally "pure" — OSCAL is a rendering concern, not a source of truth
- Projection layer will need maintenance as OSCAL schemas evolve (FedRAMP extensions update annually)
- Cannot advertise "OSCAL-native" — only "OSCAL-compatible" or "OSCAL export"
- Some fidelity loss: OSCAL structural richness is not fully captured by projection from flat records

**Effort:** ~3–4 weeks (SSP + POA&M scope) | **Risk:** Low | **W10 fit:** Yes

---

### Option C: Defer to W11+ (Low Risk Near-Term, High Risk Long-Term)

**What it means:**  
No OSCAL work in W10. Revisit in W11 scoping once market demand is clearer.

**Pros:**
- Preserves W10 capacity for P1–P5 items (CMMC overlays, Tanium, Marketplace, cATO, IL5 roadmap)
- No architectural commitment before data model is stable

**Cons:**
- **OMB M-24-15 risk:** The 24-month clock from July 2024 expires in July 2026. If W11 begins Q3 2026, Option C puts the platform at or past the compliance deadline — non-negotiable for federal customers.
- **Deal risk:** Enterprise RFPs increasingly include OSCAL as a line item. Competitors (RegScale, Telos) will capture deals where OSCAL is required.
- **Positioning risk:** Batman assessment explicitly flags OSCAL as "architecturally significant." Deferring signals the team underestimates the structural complexity — the later it starts, the more it will conflict with a maturing data model.
- **Compounding cost:** Every W10 entity added without OSCAL awareness increases the cost of native migration (W12+). Option B adapter in W10 actually *reduces* W12 native migration effort by force-validating the mapping.

**Effort:** 0 now | **Risk:** High (mandate timeline) | **W10 fit:** Yes (but inadvisable)

---

## Recommendation Rationale

The choice between B and C is not a trade-off of effort vs. features. It is a trade-off of **compliance deadline vs. capacity allocation**.

The OMB M-24-15 24-month deadline is July 2026. If W10 is the last sprint window before that deadline (or close to it), Option C is not a real option — it is a compliance gap masquerading as a prioritization call.

The choice between A and B in W10 is straightforward: Option A's 8–12 week scope crowds out the entire P1–P5 roadmap that Batman identified. The correct sequencing is:

```
W10: Option B (adapter) → closes mandate gap, earns OSCAL-compatible positioning
W12: Option A (native)  → when data model is stable, retire adapter, claim OSCAL-native
```

Option B is not a permanent compromise. It is the correct first move that:
1. Satisfies the compliance mandate on time
2. Preserves W10 roadmap capacity for CMMC, Tanium, Marketplace, cATO, and IL5 — all of which open near-term deals
3. Generates the control-to-OSCAL mapping work that makes W12 native migration faster and lower-risk
4. Immediately answers "do you support OSCAL?" with "yes, with export" rather than "not yet"

---

## Decision Record

| Dimension | Option A | **Option B ✅** | Option C |
|-----------|----------|-----------------|----------|
| W10 feasibility | ❌ Too large | ✅ 3–4 weeks | ✅ 0 weeks |
| OMB M-24-15 compliance | ✅ Exceeds | ✅ Satisfies | ⚠️ Risk if W11 is Q3 2026 |
| Data model impact | High refactor | None | None |
| Competitive positioning | OSCAL-native | OSCAL-compatible | No OSCAL |
| Roadmap cost (P1–P5) | 3–4 items dropped | Minor capacity | Unaffected |
| W12 native migration cost | N/A | Reduced | Increased |
| Enterprise deal risk | Low | Low | High |

---

## Implementation Scope (Option B — W10)

If approved, W10 OSCAL adapter scope:

### Phase 1 — SSP OSCAL Export (Required)
- New spec: `spec/022-ssp-full-oscal` review and gap-close
- `IOscalProjectionService` interface + `OscalSspProjector` implementation
- Maps: `RegisteredSystem` → `system-characteristics`; `SecurityCategorization` → `security-impact-level`; `ControlImplementation` → `implemented-requirement`; `AuthorizationBoundary` → `system-component`
- Export endpoint: `GET /api/dashboard/systems/{id}/oscal/ssp.json`
- NIST SP 800-53 Rev 5 catalog reference constants

### Phase 2 — POA&M OSCAL Export (Required)
- `PoamItem` + `PoamMilestone` → OSCAL POA&M model
- Export endpoint: `GET /api/dashboard/systems/{id}/oscal/poam.json`

### Phase 3 — Frontend Export Action (Required)
- "Download OSCAL" button on system detail → downloads SSP JSON + POA&M JSON
- Co-located with existing "Export to eMASS" action

### Out of Scope for W10 (W11 candidates):
- SAP → OSCAL Assessment Plan
- SAR → OSCAL Assessment Results
- OSCAL import / ingest
- OSCAL validation against FedRAMP schema extensions
- Native OSCAL internal storage (Option A — W12+)

---

## W12+ Strategic Initiative Note

Option A (native OSCAL generation) should be roadmapped explicitly for W12+ with the following prerequisites:
1. OSCAL schema stability for FedRAMP extensions (updated annually — target the stable Rev 5 FedRAMP profile)
2. SPIN Agent data model stability (post W10 CMMC overlays, post Tanium integration)
3. Option B adapter shipping and validated in production (real customer SSP exports confirm the mapping)
4. ADR update: retire adapter, adopt OSCAL-native with internal format migration

The path from B to A is: validate mapping in production (W10–W11) → freeze the mapping → migrate internal storage to OSCAL schema (W12).

---

## Approval Required

- [ ] **John** — W10 scoping lock: confirm Option B in scope, confirm W10 capacity allocation (~3–4 weeks engineering)
- [ ] **Batman** — Strategic alignment: confirm W12+ Option A roadmap slot
- [ ] **Cyborg** — Technical validation: confirm adapter approach does not conflict with eMASS export architecture (spec `041-emass-package`)

---

## Sources

| Source | Date | Relevance |
|--------|------|-----------|
| OMB M-24-15: Modernizing FedRAMP | July 25, 2024 | Hard 24-month OSCAL mandate for all federal GRC tools |
| DRTConfidence OSCAL Analysis | July 29, 2024 | M-24-15 compliance timeline breakdown; 400+ CSPs converting |
| Batman W10 Strategic Assessment | 2026-06-18 | Gap identification; Option A/B/C framing; W10 dispatch |
| NIST OSCAL Implementer's Guide (Workshop 32) | Feb 19, 2025 | Implementation best practices; common challenges |
| RegScale competitive positioning | Field intelligence | OSCAL-native as enterprise differentiator |

---

*ADR authored by Oracle — source-grounded, timestamped, citable.*  
*Decision authority: John Spinella (Product). Approval needed before W10 scoping lock.*
