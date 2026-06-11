# Plan — 074: Policy + Technical Narrative Split & Evidence Classification

---

## Overview

7-phase plan. Each phase has a hard checkpoint: `dotnet build` must pass before the next
phase begins. All phases target branch `074-policy-technical-narrative`.

---

## Phase 1 — Data Model (Sprint 1, Days 1–2)

**Goal:** New columns and enum defined in C#. No migration yet.

**Tasks:** T001, T002, T003

**Work:**
1. Add `EvidenceNarrativeType` enum to `ComplianceModels.cs` (after `EvidenceCategory`)
2. Add `PolicyNarrative`, `TechnicalNarrative`, `MigratedFromLegacy` to `ControlImplementation`
3. Add `NarrativeType`, `AutoTagRationale`, `ManuallyTaggedBy` to `EvidenceArtifact`
4. Create `NarrativeDualDtos.cs` with `PatchDualNarrativeRequest`, `DualNarrativeResponse`,
   `EvidenceArtifactSummary` records (T010)

**Checkpoint 1:** `dotnet build Ato.Copilot.sln` passes with zero errors. No migration
applied yet.

---

## Phase 2 — EF Core Migration (Sprint 1, Days 2–3)

**Goal:** Schema change applied to dev database. Back-fill verified.

**Tasks:** T004, T005

**Work:**
1. Run `dotnet ef migrations add Feature052_PolicyTechnicalNarrativeSplit`
2. Review generated migration file — confirm all six `AddColumn` calls and no unexpected changes
3. Add back-fill SQL to migration `Up()` (T005)
4. Apply migration: `dotnet ef database update`
5. Spot-check with SQLite query: `SELECT Id, Narrative, TechnicalNarrative, MigratedFromLegacy FROM ControlImplementations LIMIT 5`

**Checkpoint 2:** Migration file exists. `dotnet ef database update` succeeds on dev SQLite.
Existing rows have `TechnicalNarrative = Narrative` and `MigratedFromLegacy = 1` where
`Narrative IS NOT NULL`. New `EvidenceArtifact` rows get `NarrativeType = 3` (Unclassified);
existing rows get `NarrativeType = 2` (Combined) from back-fill.

---

## Phase 3 — HTTP Endpoints (Sprint 1, Days 3–5)

**Goal:** GET and PATCH endpoints functional with correct auth and audit logging.

**Tasks:** T006, T007, T008, T009, T010

**Work:**
1. Create `NarrativeDualEndpoints.cs` with stub route handlers
2. Register `MapNarrativeDualEndpoints()` in startup
3. Implement `GetDualNarrativeAsync` — EF query + evidence split projection
4. Implement `PatchDualNarrativeAsync` — partial update + role gate + audit log
5. Create `EvidenceClassifyEndpoint.cs` + `MapEvidenceClassifyEndpoint()` (T014)

**Checkpoint 3:** `dotnet build` passes. `GET /api/systems/{id}/controls/AC-2/narrative`
returns 200 with `policyNarrative`, `technicalNarrative`, and split evidence lists.
`PATCH` with only `policyNarrative` leaves `technicalNarrative` unchanged. PlatformEngineer
JWT receives 403 on Policy write.

---

## Phase 4 — MCP Tools (Sprint 2, Days 1–2)

**Goal:** Three new MCP tools registered and callable by agents.

**Tasks:** T011, T012, T013, T014

**Work:**
1. Create `NarrativePolicyTool.cs` — `narrative_set_policy`
2. Create `NarrativeTechnicalTool.cs` — `narrative_set_technical`
3. Create `EvidenceClassifyTool.cs` — `evidence_classify`
4. Register all three tools in the MCP agent DI registration (search for existing
   `NarrativeHistoryTool` registration to find the right file)

**Checkpoint 4:** All three tools appear in the agent's tool manifest. `narrative_set_policy`
can be invoked via the MCP test harness and returns success envelope.

---

## Phase 5 — Auto-Tagger Service (Sprint 2, Days 2–3)

**Goal:** Classifier service implemented; bulk-tag job runnable.

**Tasks:** T015, T016

**Work:**
1. Create `IEvidenceNarrativeClassifier` interface
2. Implement `EvidenceNarrativeClassifier` with source-based rules + filename regex
3. Create `EvidenceNarrativeBulkClassifierJob` for one-time backfill
4. Register classifier in DI

**Checkpoint 5:** Unit test T021 passes (200 test artifacts tagged correctly). Bulk job
produces summary log line.

---

## Phase 6 — SSP Export Integration (Sprint 2, Days 3–5)

**Goal:** OSCAL and DOCX exports render both halves with canonical labels.

**Tasks:** T017, T018

**Work:**
1. Locate OSCAL export service — search `_smt.` or `implemented-requirements` in codebase
2. Add `_smt.policy` + `_smt.technical` statement entries (Policy first, Technical second)
3. Locate DOCX/QuestPDF export service — search `Implementation Statement` in codebase
4. Insert two labeled subsections per control; read placeholder from `appsettings.json`

**Checkpoint 6:** Integration test T022 passes. OSCAL JSON has both `statement-id` values.
DOCX has two labeled subsections. Missing halves render placeholder string.

---

## Phase 7 — Tests & Cleanup (Sprint 3, Days 1–2)

**Goal:** All acceptance scenarios pass; spec files merged.

**Tasks:** T019, T020, T021, T022

**Work:**
1. Write and run integration test T019 (migration)
2. Write and run integration test T020 (endpoint PATCH/GET + role gate)
3. Confirm unit test T021 (classifier)
4. Write and run integration test T022 (OSCAL export)
5. PR review, address feedback, merge to `main`

**Checkpoint 7 (Final):** `dotnet test Ato.Copilot.sln` passes with zero failures.
All Definition of Done checklist items in `spec.md` checked.
