# Research: 021 — PIA Service + System Interconnections

**Date**: 2026-03-07 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

---

## R1: SP 800-60 PII Info Type Detection Strategy

**Decision**: Use a static mapping of known-PII info type family prefixes to classify SP 800-60 info types. Known-PII families: D.8.x (Personnel Records), D.17.x (Health/Medical), D.28.x (Financial). Ambiguous types flagged as `PendingConfirmation`.

**Rationale**: The existing `InformationType` entity stores `Sp80060Id` (e.g., "D.8.1") and `Name`. No pre-seeded reference table of all SP 800-60 types exists — data is user-provided during categorization. Rather than importing the full 800-60 catalog, PTA auto-detection cross-references info type IDs against known-PII prefixes, which is deterministic and testable.

**Alternatives considered**:
- Import full SP 800-60 Vol II catalog as seed data → Rejected: adds ~400 entries with ongoing maintenance burden, and the PII classification still requires human judgment for many types.
- AI-based PII classification from info type names → Rejected: non-deterministic, creates compliance risk (PTA is a legal determination).
- Delegate entirely to manual mode → Rejected: misses the auto-detection value proposition that makes Security Posture Intelligence Navigator faster than manual PTA worksheets.

---

## R2: PIA Document Structure (OMB M-03-22 Questionnaire Sections)

**Decision**: Model 8 PIA sections as `PiaSection` owned entities (JSON column on PIA), aligned with OMB M-03-22 required content areas:

| Section ID | Title |
|-----------|-------|
| 1.1 | System Information |
| 2.1 | Information Collected |
| 2.2 | Purpose of Collection |
| 3.1 | Information Sharing |
| 4.1 | Notice and Consent |
| 5.1 | Individual Access and Correction |
| 6.1 | Security Safeguards |
| 7.1 | Records Retention and Disposal |

**Rationale**: OMB M-03-22 specifies 6 required content areas; DoDI 5400.11 adds system description and retention. The 8-section model covers all requirements. Stored as JSON rather than normalized table to keep the PIA as a single queryable document.

**Alternatives considered**:
- Normalized PiaQuestion + PiaAnswer tables → Rejected: over-normalized for a document that's always read/written as a whole.
- Free-form markdown only (no structured sections) → Rejected: loses pre-population capability and section-level completeness tracking.

---

## R3: ISA Document Template Structure (NIST SP 800-47 Rev 1)

**Decision**: AI-generated ISA follows NIST SP 800-47 Rev 1 recommended structure with 7 sections:

1. Introduction & Purpose
2. System Descriptions (both parties)
3. Interconnection Details (type, protocols, ports, classification)
4. Security Controls (encryption, authentication, monitoring)
5. Roles and Responsibilities (from RmfRoleAssignments)
6. Agreement Terms (effective date, duration, termination)
7. Signatures

**Rationale**: SP 800-47 Rev 1 provides a recommended ISA template. Pre-populating from interconnection record + system metadata reduces manual drafting. AI drafts narrative prose from structured data.

**Alternatives considered**:
- Fixed template with fill-in-the-blank → Rejected: less useful than AI-generated narrative.
- Custom organizational template support → Deferred: can be added later with template upload feature.

---

## R4: Gate Extension Pattern in RmfLifecycleService

**Decision**: Extend existing `CheckPrepareToCategorize` method with two additional `yield return` statements for Gate 3 (privacy readiness) and Gate 4 (interconnection documentation). The method uses `RegisteredSystem` navigation properties — new gates will use the added `PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `SystemInterconnections`, and `HasNoExternalInterconnections` properties.

**Rationale**: The existing gate pattern is a static method yielding `GateCheckResult` records. Adding gates follows the identical pattern — no architectural changes needed. The service must `Include()` the new navigation properties when loading `RegisteredSystem` for gate evaluation.

**Alternatives considered**:
- Separate gate evaluation service → Rejected: gate logic is co-located in `RmfLifecycleService` for all transitions; splitting would fragment the pattern.
- Database-driven gate configuration → Rejected: over-engineering for 6 total gates across the application.

---

## R5: SSP §10 Integration with SspService

**Decision**: Add a new section generator method in `SspService` for "System Interconnections" (§10 per NIST 800-18 Rev 1 §3.2). The section produces a markdown table of all active interconnections with target system, connection type, data flow, classification, agreement status, and security measures.

**Rationale**: `SspService.GenerateSspAsync` currently generates 4 sections with a `sections` parameter allowing selective generation. Adding §10 follows the same pattern — a new case in the section generation switch/conditional.

**Alternatives considered**:
- Separate interconnection report tool → Rejected: SSP is the authoritative document that includes interconnection data; a separate report duplicates information.

---

## R6: ConMon Integration for ISA/Agreement Expiration

**Decision**: Add an `ISA/MOU Expiration Check` to the existing ConMon monitoring cycle. Check `InterconnectionAgreements` for:
- Approaching expiration (≤90 days) → advisory alert
- Expired agreements → `SignificantChange` record with `ChangeType = "New Interconnection"`

**Rationale**: ConMonService already has `CheckExpirationAsync` for ATO expirations with the exact pattern needed. ISA expiration follows the same alert escalation (Info@90d → Warning@60d → Urgent@30d → Critical@expired). Expired ISAs create `SignificantChange` records using the existing `ReportChangeAsync` method.

**Alternatives considered**:
- Separate background service for ISA monitoring → Rejected: duplicates existing ConMon infrastructure.
- Webhook/timer-based monitoring → Rejected: ConMon already runs on a monitoring cycle.

---

## R7: PTA Invalidation on SecurityCategorization Changes

**Decision**: When `CategorizationService` updates a system's information types, trigger PTA invalidation. The PTA is deleted (or re-created) and any approved PIA is set to `UnderReview`. Implementation: add a call from `CategorizationService.CategorizeSystemAsync` to `PrivacyService.InvalidatePtaAsync`.

**Rationale**: PTA determination is derived from information types. If the source data changes, the determination may be wrong. Invalidation forces re-analysis. Setting PIA to `UnderReview` (not deleted) preserves work while signaling review is needed.

**Alternatives considered**:
- Event-driven invalidation via domain events → Rejected: no event infrastructure exists in the project; direct service call is simpler and consistent with existing patterns.
- Soft-invalidation (flag PTA as stale but keep determination) → Rejected: stale PTA could incorrectly satisfy the privacy gate.

---

## R8: AI-Assisted PIA Narrative Drafting

**Decision**: Use the existing `IChatCompletionService` (Azure OpenAI) to draft PIA narrative sections. Provide system context (description, info types, safeguards from control baseline) as structured data in the prompt. Each section gets a targeted prompt with the relevant data.

**Rationale**: The project already uses Azure OpenAI via `Azure.AI.OpenAI` (2.1.0) and `Microsoft.Extensions.AI.OpenAI`. PIA narrative drafting is a text generation task that benefits from AI assistance. The structured questionnaire sections provide clear input/output contracts.

**Alternatives considered**:
- Template-only generation (no AI) → Rejected: produces generic text that requires significant manual editing.
- Full-document AI generation (single prompt) → Rejected: section-by-section drafting allows per-section pre-population and targeted prompts.

---

## R9: Authorization Pre-check Extension

**Decision**: Extend `AuthorizationService` to include PIA + ISA validation alongside existing authorization pre-checks. Add two checks: (1) PIA is Approved if PTA = PiaRequired, (2) all active interconnections have signed agreements.

**Rationale**: Authorization pre-checks exist to catch issues before ATO submission. Privacy and interconnection compliance are mandatory for ATO — adding them to the pre-check catches gaps early.

**Alternatives considered**:
- Separate privacy pre-check → Rejected: authorization pre-check is the single entry point for ATO readiness validation.
