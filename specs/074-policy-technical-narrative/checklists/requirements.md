# Requirements Checklist — 074: Policy + Technical Narrative Split & Evidence Classification

---

## 1. SDK Artifact Completeness

| Artifact | File | Status |
|----------|------|--------|
| Spec (background, stories, FRs, NFRs, test plan, DoD) | `spec.md` | ✅ Present |
| Tasks (phased, file paths, issue refs) | `tasks.md` | ✅ Present |
| Data model (entity extensions, enum, migration, back-fill SQL, rollback) | `data-model.md` | ✅ Present |
| Research (6 decision records R1–R6) | `research.md` | ✅ Present |
| Plan (7-phase implementation plan with checkpoints) | `plan.md` | ✅ Present |
| Quickstart (migration verify, curl examples, role-gate test, pitfalls) | `quickstart.md` | ✅ Present |
| HTTP API contract (3 endpoints, all req/resp/errors) | `contracts/http-api.md` | ✅ Present |
| MCP tools contract (3 tools, parameters, responses, registration) | `contracts/mcp-tools.md` | ✅ Present |
| Requirements checklist | `checklists/requirements.md` | ✅ Present |

**SDK artifact verdict: COMPLETE (9/9)**

---

## 2. Functional Requirements Coverage

| ID | Requirement | Covered In | Verdict |
|----|-------------|------------|---------|
| FR-001 | `PolicyNarrative` + `TechnicalNarrative` additive nullable columns | data-model.md §1, tasks.md T002, T004 | ✅ |
| FR-002 | `EvidenceNarrativeType` enum + `NarrativeType` column on `EvidenceArtifact` | data-model.md §2, tasks.md T001, T003 | ✅ |
| FR-003 | Legacy `Narrative` back-filled to `TechnicalNarrative`; `MigratedFromLegacy = true` | data-model.md §4 back-fill SQL, tasks.md T005, spec.md US1 AC4 | ✅ |
| FR-004 | `PATCH /api/systems/{id}/controls/{controlId}/narrative` partial update | contracts/http-api.md §2, tasks.md T009 | ✅ |
| FR-005 | `GET /api/systems/{id}/controls/{controlId}/narrative` returns both halves + evidence split | contracts/http-api.md §1, tasks.md T008 | ✅ |
| FR-006 | MCP tools `narrative_set_policy`, `narrative_set_technical`, `evidence_classify` | contracts/mcp-tools.md, tasks.md T011-T013 | ✅ |
| FR-007 | SSP OSCAL + DOCX exports render both halves with canonical labels | spec.md FR-007, tasks.md T017, T018 | ✅ |
| FR-008 | Auto-tagging classifier + bulk job for legacy evidence | spec.md FR-008, tasks.md T015, T016, research.md R3 | ✅ |
| FR-009 | Role gate: PE/SCA → 403 on `policyNarrative` write | contracts/http-api.md §2, tasks.md T009, research.md R5 | ✅ |
| FR-010 | Staleness rules: Policy stale on `OrgDefaultUpdated`, Technical event-based | spec.md FR-010, contracts/http-api.md §1 response | ✅ |

**Functional requirements verdict: COMPLETE (10/10)**

---

## 3. Non-Functional Requirements Coverage

| ID | Requirement | Covered In | Verdict |
|----|-------------|------------|---------|
| NFR-001 | Additive migration — zero data loss; rollback drops only new columns | data-model.md §4 Down(), research.md R1 | ✅ |
| NFR-002 | PATCH p99 < 200 ms | spec.md NFR-002 | ✅ |
| NFR-003 | All new columns nullable (no NOT NULL on existing tables) | data-model.md §4 migration, tasks.md T004 | ✅ |
| NFR-004 | `EvidenceNarrativeType` defaults correct at DB level | data-model.md §4 defaultValue annotations + back-fill | ✅ |
| NFR-005 | Legacy `Narrative` in API response as `legacyNarrative` | contracts/http-api.md §1, data-model.md §3 DualNarrativeResponse | ✅ |
| NFR-006 | Classifier < 1 s per 1 000 rows | spec.md NFR-006, tasks.md T015 | ✅ |
| NFR-007 | MCP tools follow `BaseTool` envelope | contracts/mcp-tools.md, tasks.md T011-T013 | ✅ |

**Non-functional requirements verdict: COMPLETE (7/7)**

---

## 4. User Story Coverage

| Story | Acceptance Scenarios | Test Task | Verdict |
|-------|---------------------|-----------|---------|
| US1 — Two first-class editors | AC1–AC4 | T019, T020 | ✅ |
| US2 — Evidence tagged Policy/Technical | AC1–AC2 | T020, T021 | ✅ |
| US3 — Role-based write gates | PE → 403 | T020 | ✅ |
| US4 — SSP export both halves canonical | OSCAL + DOCX | T022 | ✅ |
| US5 — MCP agent authoring | Tool invocation | contracts/mcp-tools.md | ✅ |

**User story verdict: COMPLETE (5/5)**

---

## 5. Research Decision Records

| ID | Question | Answered? |
|----|----------|-----------|
| R1 | Additive columns vs. new `ControlNarrative` table | ✅ |
| R2 | Legacy `Narrative` rename vs. preserve | ✅ |
| R3 | `EvidenceNarrativeType` default for new uploads | ✅ |
| R4 | Back-fill legacy narrative to Technical vs. Legacy kind | ✅ |
| R5 | Hard 403 vs. draft suggestion for PE on Policy | ✅ |
| R6 | OSCAL statement IDs — `_smt.a/_smt.b` vs. `_smt.policy/_smt.technical` | ✅ |

**Research verdict: COMPLETE (6/6)**

---

## 6. Open Questions (for `/clarify` before implementation)

| # | Question | Default in Spec |
|---|----------|-----------------|
| Q1 | Approval ordering — must Policy be approved before Technical? | No gating (independent approvals) |
| Q2 | Coherence-check trigger — auto on each Technical save, or only on demand? | On demand / final-approval gate |
| Q3 | Both-half evidence in appendices — show in both appendices or separate Appendix C? | Show in both with footnote |
| Q4 | CSP-inherited capabilities — always seed Policy only, or allow marking Technical too? | Policy only |
| Q5 | Family-level cadence overrides — per deployment or per organization? | Per deployment (appsettings) |

---

**Overall checklist verdict: READY FOR IMPLEMENTATION**
