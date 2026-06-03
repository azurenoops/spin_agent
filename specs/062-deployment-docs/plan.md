# Implementation Plan: Deployment Docs + Operations Runbook (Epic 062)

## Phase 1 — Runbook (Week 1)

**Goal**: On-call engineers have structured incident response procedures.

### Day 1–2
- **T062-01**: Write `docs/runbook.md`
  - Use `contracts/runbook-template.md` structure
  - Write all 5 failure mode sections (research.md § 6 has the analysis)
  - Write database recovery section (SQLite + SQL Server — research.md § 4–5)
  - Add link from `docs/deployment.md`

**Phase 1 exit criteria**: `docs/runbook.md` covers all 5 failure modes + DB recovery; linked
from deployment guide; tech review passed.

---

## Phase 2 — Smoke Test (Week 1)

**Goal**: Operators have a single-command post-deploy health check.

### Day 2–3
- **T062-02**: Write `scripts/smoke-test.sh`
  - 3 checks: health, auth middleware, authenticated API
  - Document in `contracts/smoke-test.md`
  - Test locally against dev instance

**Phase 2 exit criteria**: script exits 0 on healthy instance, exits 1 on broken instance.

---

## Phase 3 — CI Artifact + Deployment Guide Updates (Week 2)

**Goal**: VS Code extension distributable in CI; M365 bot linked in deployment guide.

### Day 4–5
- **T062-03**: Add `vscode-package` CI job (path-filtered, 90-day retention on main)
- **T062-04**: Add M365 bot section to `docs/deployment.md`
  - Note: depends on spec 061 T061-06 (`docs/deployment-m365.md`) existing first
  - If not yet available: add placeholder section with `[TODO: link to deployment-m365.md]`

---

## Phase 4 — Backup/Restore Documentation (Week 2)

**Goal**: Backup and restore procedures are documented for both DB backends.

### Day 5
- **T062-05**: Expand `docs/runbook.md` database recovery section
  - SQLite: `.backup` command + cron entry + restore procedure
  - SQL Server: verify automated backup + point-in-time restore CLI

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Spec 061 `deployment-m365.md` not ready when T062-04 starts | Medium | Low | Add placeholder section with TODO; update link when 061 ships |
| `vsce package` fails due to missing `package.json` fields | Medium | Low | Pre-check fields in the CI step; add missing fields if needed |
| Runbook diagnosis steps are incorrect (tested against wrong infra) | Low | High | Technical review by engineer who has run the system in production |
| SQLite backup command blocked by WAL mode | Low | Medium | Use `.backup` command (WAL-safe); document WAL checkpoint step |

---

## Milestone Summary

| Milestone | Tasks | Target | Exit Criteria |
|---|---|---|---|
| M1: Runbook | T062-01, T062-05 | End of Week 1 | `docs/runbook.md` covers 5 failure modes + DB recovery |
| M2: Smoke Test | T062-02 | End of Week 1 | Script exits correctly on healthy/broken instances |
| M3: CI + Docs | T062-03, T062-04 | End of Week 2 | `.vsix` in CI; M365 linked in deployment guide |
| **Epic Close** | All P1 | **Week 1** | US1 + US2 AC met |
