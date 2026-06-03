# SDK Gap Report: Epic 050 — CSP-Inherited Capability Lifecycle

**Reviewer**: Hermes Agent (automated)  
**Date**: 2026-06-03  
**Branch**: `050-csp-capability-lifecycle`  
**Source dir**: `specs/050-csp-capability-lifecycle/`

---

## 1. File-level Status Table

| SDK File | Present? | Complete? | Gaps Found |
|---|---|---|---|
| `checklists/requirements.md` | ✅ Yes | ⚠️ Partial | Covers spec-level QA only; no SDK-artifact completeness checks (tasks, contracts, data-model, quickstart). Substantive but narrowly scoped. |
| `tasks.md` | ✅ Yes | ⚠️ Partial | Does NOT reference child issue numbers #158–#161. T001 is deferred/blanked ("DEFERRED — requires explicit user approval"). All 5 user stories are mapped to tasks; issue number discipline is missing. |
| `data-model.md` | ✅ Yes | ⚠️ Partial | Entity fully specified; EF Core config and migration SQL present and correct. Indexes documented. **No SQL Server Row-Level Security (RLS) predicate documentation** — only application-layer `TenantId` filter approach is specified. |
| `contracts/http-api.md` | ✅ Yes | ✅ Yes | All 3 endpoints documented with full request/response shapes, error tables, OpenAPI fragments, and idempotency notes. |
| `contracts/internal-services.md` | ✅ Yes | ✅ Yes | `ReparentCapabilityAsync` fully documented with C# signature, XML doc, exception contract, and implementation outline. `ICapabilityHistoryService` also fully specified. |
| `contracts/frontend-types.md` | ✅ Yes | ✅ Yes | TypeScript types for history (`capabilityHistory.ts`) and move (`capabilityMove.ts`); component prop contracts; API client surface; accessibility and testing cross-references. |
| `quickstart.md` | ✅ Yes | ✅ Yes | 10 feature-specific verification scenarios; curl smoke tests; troubleshooting table; Constitution gates checklist. Actionable and specific. |
| `plan.md` | ✅ Yes | ✅ Yes | Eight clearly delineated phases (Setup → Foundational → US1 → US2 → US3 → US4 → US5 → Polish) with explicit checkpoints, cross-references, and a Constitution Check section. |
| `research.md` | ✅ Yes | ✅ Yes | 11 documented decisions (R1–R11) with rationale, alternatives rejected, and consequences. Every trade-off traceable to a clarification Q&A or a verified fact about `main`. |

---

## 2. Gap Detail

### GAP-1 (SIGNIFICANT): Issue references #158–#161 absent from `tasks.md`

**File**: `tasks.md`  
**Severity**: Significant — blocks Constitution § DevOps Issue Discipline compliance.

The task file contains exactly zero references to GitHub issue numbers. The only relevant task is:

```
- [ ] T001 Create GitHub issues … DEFERRED — requires explicit user approval before external GitHub writes.
```

No issues `#158`, `#159`, `#160`, or `#161` are mentioned anywhere in the file. The Constitution requires that every user story sub-issue exists and is linked to the parent feature issue before commits merge.

**Exact addition needed**:

Update T001 to pin the assigned issue numbers once user approves, and add issue refs to each phase header:

```markdown
- [ ] T001 Create GitHub issues per Constitution § DevOps Issue Discipline:
  - Feature 050 parent issue: **#157** (or confirm assigned number)
  - US1 (manually-added capabilities vetted by default): **#158**
  - US2 (move capability to different component): **#159**
  - US3 (capability detail drawer shows audit trail): **#160**
  - US4 (remap gated behind Advanced sub-menu): **#161**
  - US5 (picker reflects review state): **#162** (if applicable)
  Link all sub-issues to parent with `closes #157` or GitHub parent-issue field.
```

And in each phase header (Phase 3–7) add the issue ref:

```markdown
## Phase 3: User Story 1 — … (Priority: P1) 🎯 MVP  <!-- GitHub: #158 -->
## Phase 4: User Story 2 — … (Priority: P1)          <!-- GitHub: #159 -->
## Phase 5: User Story 3 — … (Priority: P1)          <!-- GitHub: #160 -->
## Phase 6: User Story 4 — … (Priority: P2)          <!-- GitHub: #161 -->
```

---

### GAP-2 (MINOR): `data-model.md` — No SQL Server RLS predicate documentation

**File**: `data-model.md`  
**Severity**: Minor — tenant isolation is correctly implemented via application-layer `TenantId` filtering and a leading-`TenantId` composite index, but the explicit RLS predicate approach (if adopted elsewhere in the codebase) is not addressed.

Section 1.8 documents the composite index `(TenantId, CapabilityId, OccurredAt DESC)` as the primary read guard, and section 1.7 specifies the EF Core FK behaviors. However, if the project uses SQL Server Row-Level Security predicates for defense-in-depth (as some Feature 048 tables may), this table's RLS predicate is unspecified.

**Exact addition needed** (add to `data-model.md` after section 1.8):

```markdown
### 1.8a RLS predicates (SQL Server — if applicable)

`CapabilityHistoryEvents` does **not** use a SQL Server Row-Level Security
predicate in this feature. Tenant isolation is enforced exclusively at the
application layer via the leading `TenantId` column in all queries and
the composite index in § 1.8. If the project later adopts DB-level RLS
globally, the predicate for this table would be:

```sql
-- Example only — not applied in this migration
CREATE SECURITY POLICY CapabilityHistoryTenantPolicy
ADD FILTER PREDICATE dbo.fn_TenantIsolation(TenantId)
ON dbo.CapabilityHistoryEvents
WITH (STATE = ON);
```

This is out of scope for Feature 050; document here so the decision is
explicit rather than accidental.
```

---

### GAP-3 (MINOR): `checklists/requirements.md` — Scope limited to spec-level QA only

**File**: `checklists/requirements.md`  
**Severity**: Minor — the checklist is substantive but validates only the spec document quality, not the SDK artifact set (contracts, data-model, tasks, quickstart).

All 16 items in the checklist address whether `spec.md` is well-formed. No checklist item covers:
- Are all contracts present and internally consistent?
- Does `tasks.md` reference GitHub issues?
- Does `data-model.md` cover the migration SQL?
- Does `quickstart.md` have actionable steps?

**Exact addition needed** (append new section to `checklists/requirements.md`):

```markdown
## SDK Artifact Completeness

- [x] `data-model.md` — entity fields, EF Core config, migration SQL, indexes present
- [x] `contracts/http-api.md` — all 3 new/extended endpoints documented with request/response shapes
- [x] `contracts/internal-services.md` — `ReparentCapabilityAsync` and `ICapabilityHistoryService` specified
- [x] `contracts/frontend-types.md` — TypeScript types for history and move present
- [x] `quickstart.md` — actionable feature-specific dev setup and verification steps present
- [x] `plan.md` — phased implementation with clear phase boundaries and checkpoints
- [x] `research.md` — trade-off decisions R1–R11 documented
- [ ] `tasks.md` — GitHub child issue numbers #158–#161 referenced (GAP-1; pending T001 approval)
- [ ] `data-model.md` — RLS predicate decision documented (GAP-2; added as explicit out-of-scope note)
```

---

## 3. User Story Coverage Check

All 5 user stories from `spec.md` are represented in `tasks.md`:

| User Story | Priority | Tasks.md Phase | Tasks Present |
|---|---|---|---|
| US1 — Manually-added capabilities vetted by default | P1 | Phase 3 | T011–T018 ✅ |
| US2 — Move capability to different component | P1 | Phase 4 | T019–T028 ✅ |
| US3 — Capability detail drawer shows audit trail | P1 | Phase 5 | T029–T041 ✅ |
| US4 — Remap gated behind "Advanced" sub-menu | P2 | Phase 6 | T042–T045 ✅ |
| US5 — Picker reflects review state | P2 | Phase 7 | T046–T047 ✅ |

All user stories are covered. ✅

---

## 4. Issue Ref Check

**Issue refs #158–#161 in `tasks.md`**: ❌ **NOT PRESENT**

```
grep for "#158", "#159", "#160", "#161" → 0 matches
```

T001 is the only task that mentions GitHub issues and it is explicitly deferred with no numbers assigned. This is GAP-1 above.

---

## 5. Endpoint Coverage Check

| Endpoint | Documented? | Request Shape? | Response Shape? | Error Table? |
|---|---|---|---|---|
| `POST /capabilities` with `markMappedImmediately` | ✅ | ✅ | ✅ (both `false`/`true` variants) | ✅ |
| `POST /capabilities/{id}/move` | ✅ | ✅ (body + `If-Match` header) | ✅ | ✅ |
| `GET /capabilities/{id}/history` | ✅ | ✅ (query params) | ✅ (paginated) | ✅ |

All 3 endpoints fully documented. ✅

---

## 6. Overall Verdict

### **MINOR GAPS**

The spec SDK is comprehensive and well-structured. Two minor gaps and one significant gap exist:

| Gap | Severity | File | Action Required |
|---|---|---|---|
| GAP-1 | **Significant** | `tasks.md` | Add GitHub issue numbers #158–#161 to T001 and phase headers once user approves T001 |
| GAP-2 | Minor | `data-model.md` | Add explicit RLS-out-of-scope note to § 1.8a |
| GAP-3 | Minor | `checklists/requirements.md` | Add SDK artifact completeness section |

The overall rating is **MINOR GAPS** because GAP-1, while marked Significant in isolation, is blocked on external user approval (not a spec authoring failure) and all substantive implementation guidance is present and complete. The feature is implementable as-is; the gaps are process/discipline items, not missing design decisions.

---

*Report generated by Hermes Agent gap-review run. Commit this file and resolve GAP-1 before merging any feature-050 PR.*
