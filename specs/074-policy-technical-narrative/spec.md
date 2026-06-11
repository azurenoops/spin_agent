# Spec 074 — Policy + Technical Narrative Split & Evidence Classification

**Epic:** #64 — Feature 052: Evidence + Narrative Modeling (Policy + Technical)
**GitHub Issue:** #64
**Wave:** 9 — Core UX & Integrations
**Status:** Draft
**Branch:** `074-policy-technical-narrative`
**Feature Number:** 074

---

## Background

Every NIST 800-53 control implementation in ATO Copilot today has exactly one `ControlImplementation.Narrative`
field. In real RMF practice auditors expect **two** canonical halves:

| Half | Covers | Evidence | Reviewer |
|------|--------|----------|----------|
| **Policy** | Org-level governance: who owns the control, what the written policy says, review frequency, role assignments, policy document citations | Signed policy PDFs, meeting-minutes, training rosters, SOPs | ISSM |
| **Technical** | System-level implementation: Azure/cloud configuration, automated controls, scan output, Conditional Access rules, RBAC, CI/CD gates | Azure Policy exports, Defender snapshots, SCAP results, screenshots | ISSM or SCA |

Today ISSMs write one paragraph that blends both (e.g., "We have an account management policy
**and** Azure AD enforces lifecycle workflows") and evidence (a Word doc vs. an Azure Policy
JSON) files into one undifferentiated list. Feature 052 models the two halves as **first-class
duals** so every control has a Policy Narrative and a Technical Narrative with independent
authorship, review, staleness tracking, AI-generation prompts, and SSP export paths.

### Dependencies

| Feature | How it affects this spec |
|---------|--------------------------|
| 024 — narrative-governance | Version history, approval lifecycle, `NarrativeVersion` table extend to both halves |
| 038 — evidence-repository | `EvidenceArtifact` storage, retention, and versioning extend with `NarrativeType` tag |
| 022 — ssp-full-oscal | OSCAL export renders both halves as labeled `implemented-requirements.statements[]` entries |
| 037 — ssp-document-export | DOCX/PDF export renders two labelled subsections per control |
| 044 — org-control-inheritance | Org-default inheritance applies to Policy half; Technical is always per-system |
| 041 — emass-package | eMASS concatenates Policy + "\n\n" + Technical as the single implementation statement |

---

## Functional Requirements

### FR-001 — Two Nullable Narrative Columns (Additive Migration)
`ControlImplementation` gains `PolicyNarrative (string? nullable)` and
`TechnicalNarrative (string? nullable)` columns. The existing `Narrative` field is **not
renamed or removed** — it becomes the combined/legacy field. EF Core migration is purely
additive; no existing rows are altered except by the optional back-fill described in FR-003.

### FR-002 — `EvidenceNarrativeType` Enum on Evidence Artifacts
`EvidenceArtifact` gains a `NarrativeType` column typed `EvidenceNarrativeType`:
`Policy (0) | Technical (1) | Combined (2) | Unclassified (3)`. New uploads default to
`Unclassified`; callers may pass the value on upload. Existing rows default to `Combined`
(migration back-fill).

### FR-003 — Legacy Narrative Back-fill
An EF Core `HasData` seed or post-migration script sets `TechnicalNarrative = Narrative`
for all rows where `Narrative IS NOT NULL` and sets a `MigratedFromLegacy = true` flag so
the system can distinguish migrated vs. freshly-authored Technical narratives. Policy half
is initialized `NULL` (not authored) for all existing rows.

### FR-004 — PATCH Endpoint for Dual Narrative Update
New `PATCH /api/systems/{systemId}/controls/{controlId}/narrative` accepts body with
`policyNarrative` and/or `technicalNarrative` (both optional). Each provided half is saved
independently; an omitted half is left unchanged. Returns the full updated dual-narrative
response (see FR-005).

### FR-005 — GET Endpoint Returns Both Halves + Evidence Split
`GET /api/systems/{systemId}/controls/{controlId}/narrative` returns the Policy Narrative,
Technical Narrative, legacy Narrative, and evidence lists split by `NarrativeType`.

### FR-006 — MCP Tools: `narrative_set_policy`, `narrative_set_technical`, `evidence_classify`
Three new MCP tools for agent-driven authoring and evidence classification (see
`contracts/mcp-tools.md`).

### FR-007 — SSP Export Renders Both Halves
OSCAL JSON export adds `statement-id` entries `{controlId}_smt.policy` and
`{controlId}_smt.technical`. DOCX/PDF export renders `**Implementation Statement (Policy):**`
followed by `**Implementation Statement (Technical):**`. Missing halves render the configured
placeholder string (default: `[Not Authored]`).

### FR-008 — Auto-Tagging Classifier for Existing Evidence
A deterministic classifier tags legacy `EvidenceArtifact` rows by filename/source:
- Names matching `*Policy*`, `*Procedure*`, `*SOP*`, `*Plan*` → `Policy`
- Source = `AzurePolicy`, `Defender`, `SCAP`, `ACAS`, `PrismaCloud`, `NessusACASScan`,
  `AwsSecurityHub`, `GcpScc` → `Technical`
- Otherwise → `Combined` with `AutoTagRationale = "LowConfidence"`
Classifier writes a `AutoTagRationale` string field on each row. Manual overrides clear the
rationale and set `ManuallyTaggedBy`.

### FR-009 — Role-Based Write Gates
Policy Narrative: writable by `Analyst` role (ISSM/ISSO level). PlatformEngineer role receives
403 attempting a Policy write.
Technical Narrative: writable by `PlatformEngineer` and `Analyst` (ISSO/ISSM/SCA).

### FR-010 — Staleness Rules
Policy Narrative staleness is triggered by `OrgDefaultUpdated` events (Feature 044). Technical
Narrative staleness is event-based (no cadence). Both halves expose `IsStale` and `StaleReason`
in the GET response.

---

## Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | Migration is purely additive — zero data loss; rollback drops only the two new columns |
| NFR-002 | PATCH endpoint responds in < 200 ms at p99 under normal load |
| NFR-003 | All new DB columns are nullable; no NOT NULL constraint added to existing rows |
| NFR-004 | `EvidenceNarrativeType` defaults are applied at the DB level as well as in EF (`DEFAULT 3` for new artifacts, `DEFAULT 2` for migration back-fill) |
| NFR-005 | Legacy `Narrative` field remains in the API response under `legacyNarrative` for backward compatibility |
| NFR-006 | Auto-tagging classifier completes < 1 s per 1000 rows on SQLite (dev) |
| NFR-007 | MCP tools follow existing `BaseTool` response envelope pattern |

---

## User Stories

### User Story 1 — Two First-Class Narrative Editors Per Control (P1)
An ISSO authoring AC-2 opens the control and sees two editors: **Policy Narrative** and
**Technical Narrative**. Each saves independently, each has its own version history, and
the SSP export renders both as labeled subsections.

**Acceptance Scenarios:**
1. **Given** an existing system, **When** the user opens AC-2, **Then** both `policyNarrative`
   and `technicalNarrative` fields are returned in `GET /api/systems/{id}/controls/AC-2/narrative`.
2. **Given** only `policyNarrative` is PATCHed, **When** the save completes, **Then**
   `technicalNarrative` is unchanged.
3. **Given** both halves authored, **When** the SSP exports, **Then** both labeled subsections
   appear in the export document.
4. **Given** a legacy system, **When** migration runs, **Then** `technicalNarrative` equals
   the old `narrative` value; `policyNarrative` is null; `migratedFromLegacy = true`.

### User Story 2 — Evidence Tagged Policy or Technical (P1)
When evidence is uploaded, the caller declares `NarrativeType`. Each narrative editor returns
only evidence tagged for its half plus `Combined`. SSP appendices render two evidence indices.

**Acceptance Scenarios:**
1. **Given** an artifact uploaded with `narrativeType = "Policy"`, **When** the evidence list
   is fetched, **Then** it appears in the Policy evidence set.
2. **Given** a legacy artifact, **When** the classifier runs, **Then** it is tagged based on
   filename/source heuristic with a `autoTagRationale` explanation.

### User Story 3 — Role-Based Write Gates (P1)
A PlatformEngineer attempting to PATCH `policyNarrative` receives 403. An ISSM can write both.

### User Story 4 — SSP Export Both Halves Canonical (P1)
OSCAL JSON has `statement-id` suffixes `_smt.policy` and `_smt.technical`. DOCX export has
labeled subsections in Policy → Technical order. Missing halves get the configured placeholder.

### User Story 5 — MCP Agent Authoring (P2)
An agent can call `narrative_set_policy` and `narrative_set_technical` to author each half,
and `evidence_classify` to bulk-tag existing artifacts.

---

## Out of Scope

- More than two halves (no Operational/Managerial/Compensating kinds)
- Free-form `NarrativeKind` values — enum is closed: `Policy | Technical`
- Splitting AI narrative cache — old cache entries are invalidated on first use post-migration
- Per-half POA&Ms — Feature 039 POA&Ms remain at the control level
- Per-half evidence retention policies — both halves use Feature 038 defaults
- Renaming "Policy" / "Technical" labels — fixed for cross-deployment auditor consistency
- Multilingual narratives

---

## Test Plan

| Test | Method | Scope |
|------|--------|-------|
| Migration adds two nullable columns | EF Core integration test | `ControlImplementation` table |
| Migration back-fills `TechnicalNarrative` from `Narrative` | Integration test | Existing rows |
| PATCH saves each half independently | Integration test | Endpoint |
| GET returns split evidence lists | Integration test | Endpoint |
| Role gate: PE → 403 on Policy PATCH | Integration test | Auth middleware |
| Auto-tagger classifies 200 legacy rows correctly | Unit test | Classifier service |
| OSCAL export has `_smt.policy` + `_smt.technical` entries | Integration test | Export service |
| DOCX export has labeled subsections | Integration test | Export service |
| MCP tools return correct envelope | Unit test | Tool classes |

---

## Definition of Done

- [ ] `PolicyNarrative` and `TechnicalNarrative` columns exist in DB (EF migration applied)
- [ ] `EvidenceNarrativeType` enum + `NarrativeType` column on `EvidenceArtifact`
- [ ] `PATCH /api/systems/{id}/controls/{controlId}/narrative` endpoint live
- [ ] `GET /api/systems/{id}/controls/{controlId}/narrative` returns both halves + evidence split
- [ ] Role gates enforced: PE → 403 on Policy write
- [ ] MCP tools `narrative_set_policy`, `narrative_set_technical`, `evidence_classify` registered
- [ ] SSP OSCAL and DOCX exports render both halves with canonical labels
- [ ] Auto-tagging classifier service implemented and covered by tests
- [ ] All acceptance scenarios from US1–US4 pass
- [ ] Spec files merged to `main` via this PR
