# Implementation Plan: CI/CD Pipeline Hardening

**Branch**: `053-cicd-hardening` | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/053-cicd-hardening/spec.md`

## Summary

Add four missing CI job groups and fix one misconfigured job input in `.github/workflows/ci.yml` so that every PR exercises the full test suite — integration tests, VS Code extension tests, M365 extension tests, Playwright E2E smoke tests, and the ATO Compliance Gate — in addition to the three jobs that already run (Build + Unit Tests, VS Code Compile, ATO Compliance Gate). No source code is changed. No database schema is changed. No new packages are introduced. The implementation is a single YAML file edit that wires together test commands already validated in local developer workflows.

## Technical Context

**Language/Version**: YAML (GitHub Actions workflow syntax); shell (bash, used in multi-line `run:` steps); no C# or TypeScript changes.
**Primary Dependencies**: `ubuntu-latest` GitHub-hosted runner (already used); `xvfb` system package (available via `apt-get`); `docker compose` (available on `ubuntu-latest`); `npx playwright` (already declared in `src/Ato.Copilot.Dashboard/package.json`); `@vscode/test-electron` (already declared in `extensions/vscode/package.json`); `dotnet test` (already used in the existing build job).
**Storage**: Ephemeral SQLite file per integration-test job run (`$RUNNER_TEMP/ato-copilot-test.db`); ephemeral docker-compose volumes per E2E job run (destroyed by `docker compose down -v`). No persistent storage added.
**Testing**: Each new CI job is self-verifying — a failing test in the job fails the PR. Correctness of the YAML is verified by running each job locally with `act` or by opening a draft PR and observing the Actions tab. See [quickstart.md](./quickstart.md) for step-by-step local verification.
**Target Platform**: `ubuntu-latest` GitHub-hosted runner for all five job groups. Existing jobs are not migrated from their current runner targets.
**Project Type**: Existing monorepo — single file edit to `.github/workflows/ci.yml`. No new projects, no new directories, no new packages.
**Performance Goals**: Integration tests < 5 min (FR-001); VS Code tests < 3 min (FR-002); E2E smoke < 8 min (FR-004). All five job groups run in parallel — total wall-clock time for a PR check is bounded by the slowest job, not the sum.
**Constraints**:

- **Single file** — `.github/workflows/ci.yml` is the only file modified (T006 may add `appsettings.Test.json` if absent — that is the only permitted exception).
- **Parallel jobs** — all five job groups are independent and run concurrently. No sequential dependencies between new jobs.
- **Existing job preserved** — the current Build + Unit Tests job is not modified. The VS Code Compile job is not modified. The ATO Compliance Gate job receives only an `mcp-server-url` input fix (FR-005).
- **SQLite only** — no SQL Server service container in CI.
- **Reuse `docker-compose.mcp.yml`** — no new compose file created.

**Scale/Scope**:

- **Surfaces touched**: `.github/workflows/ci.yml` only (plus `appsettings.Test.json` if T002 requires it).
- **Code surfaces NOT touched**: all C# source, all TypeScript source, all test files, all database migrations, all API contracts, all Docker images, all documentation outside this spec folder.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle / Standard | Verdict | Evidence in spec / plan |
|---|---|---|---|
| I | Documentation as Source of Truth | PASS | Spec exists at [spec.md](./spec.md); this plan + [research.md](./research.md) + [data-model.md](./data-model.md) + [contracts/ci-jobs.md](./contracts/ci-jobs.md) + [quickstart.md](./quickstart.md) cover every decision. |
| II | Simplicity | PASS | Single YAML file change. No new abstractions. No new packages. Reuse of `docker-compose.mcp.yml` (existing), `xvfb-run` (system tool), `npm test` (existing script hook). Five jobs, each a direct invocation of an already-validated local command. |
| III | YAGNI | PASS | Every new job is driven by an FR with a GitHub issue (FR-001 → #136, FR-002 → #137, FR-003 → #138, FR-004 → #139, FR-005 → #140). No speculative jobs, no feature flags, no matrix strategies beyond what the FRs require. |
| IV | Single Responsibility Principle | PASS | Each CI job does one thing: run one test suite. `dotnet-integration-tests` runs integration tests only. `vscode-extension-test` runs VS Code extension tests only. Etc. Jobs do not share steps or side-effects. |
| V | BaseAgent / BaseTool Architecture | N/A | No agents or tools modified. |
| VI | Test-Driven Development (NON-NEGOTIABLE) | PASS — by construction | Each new job is itself the test gate. A broken test fails the job; the job failing blocks the PR. The jobs run the existing test suites — no new test code is authored as part of this feature. TDD applies to the test suites themselves, which were written under their respective features. |
| VII | Observability & Structured Logging | N/A | No new application telemetry. GitHub Actions provides job-level logs, timing, and pass/fail status natively. |
| — | Azure Government & Compliance | PASS | No new Azure resources. No change to data residency or identity. |
| — | Security: Zero-Trust + Tenant Isolation | PASS | No secrets introduced beyond what docker-compose requires locally. No new environment variables exposed to PR builds from forks (GitHub Actions fork security model is unchanged). |
| — | Security: Secrets / Transport | PASS | No new secrets. The integration test job uses SQLite (no credentials). The E2E job uses docker-compose with existing dev credentials already in `docker-compose.mcp.yml`. |
| — | Local Type-Checking Parity (NON-NEGOTIABLE) | N/A | No TypeScript changes. |
| — | DevOps: CI/CD Zero Warnings | PASS | Standard gate; the new YAML jobs introduce no warnings (no deprecated Actions syntax, no pinned-but-unpinned action versions beyond existing practice). |
| — | DevOps: GitHub Issue Discipline (NON-NEGOTIABLE) | PASS | GitHub Epic #119. Tasks #136–#140 exist, one per FR. Tracked in [Implementation Phasing](#implementation-phasing) below. |
| — | Complexity Justification | NOT APPLICABLE | No §II or §III deviation. |

**Gate result**: **PASS** — proceed to implementation.

---

## Project Structure

### Documentation (this feature)

```text
specs/053-cicd-hardening/
├── plan.md                  # This file (/speckit.plan output)
├── spec.md                  # Feature specification (already exists)
├── tasks.md                 # Task list — T001–T006 (already exists)
├── research.md              # Phase 0 output — 5 architecture decisions
├── data-model.md            # Phase 1 output — no new tables (CI-only change)
├── contracts/
│   └── ci-jobs.md           # Phase 1 — CI job interface contracts
├── quickstart.md            # Phase 1 — local verification recipe
└── checklists/
    └── requirements.md      # Spec quality checklist
```

### Source Code (repository root)

```text
.github/
└── workflows/
    └── ci.yml               # MODIFY: add 4 jobs, fix 1 job input

tests/
└── Ato.Copilot.Tests.Integration/   # READ ONLY — 80+ test files, already exist

extensions/
├── vscode/
│   └── test/runTests.ts             # READ ONLY — already exists
└── m365/
    └── package.json                 # READ ONLY — npm test script already exists

src/Ato.Copilot.Dashboard/
└── e2e/playwright.config.ts         # READ ONLY — already exists

docker-compose.mcp.yml               # READ ONLY — reused as-is
```

---

## Implementation Phasing

The five functional requirements map to five independent, PR-sized increments. Each increment touches only `ci.yml` (and possibly `appsettings.Test.json`) and can be reviewed, merged, and reverted independently.

```text
Phase 1 (T001, T002) ── dotnet-integration-tests job
Phase 2 (T003)       ── vscode-extension-test job
Phase 3 (T004)       ── m365-extension-test job
Phase 4 (T005)       ── dashboard-e2e-smoke job
Phase 5 (T006)       ── ATO Compliance Gate mcp-server-url fix
```

All five phases target the same branch (`053-cicd-hardening`) and the same file (`ci.yml`). They are sequenced to allow incremental validation on the PR, but they may be combined into a single commit if the author prefers.

| Phase | Task(s) | GitHub Issue | Deliverable | Acceptance |
|---|---|---|---|---|
| 1 — Integration Tests | T001, T002 | #136 | `dotnet-integration-tests` job in `ci.yml` | All 80+ integration test files execute; job exits green in < 5 min on `ubuntu-latest` |
| 2 — VS Code Tests | T003 | #137 | `vscode-extension-test` job in `ci.yml` | All 16 VS Code test files pass via xvfb-run in < 3 min |
| 3 — M365 Tests | T004 | #138 | `m365-extension-test` job in `ci.yml` | All 15 Mocha suites in `extensions/m365/` pass via `npm test` |
| 4 — Playwright E2E | T005 | #139 | `dashboard-e2e-smoke` job in `ci.yml` | Tests 01–04 pass against docker-compose stack in < 8 min |
| 5 — Compliance Gate Fix | T006 | #140 | `mcp-server-url` corrected in existing job | ATO Compliance Gate passes with docker-compose health endpoint |

**GitHub Issue Discipline (NON-NEGOTIABLE)**: Epic #119 and task issues #136–#140 exist. Tasks T001–T006 in [tasks.md](./tasks.md) have checkbox entries referencing the corresponding issues.

---

## Complexity Tracking

> No deviation from Simplicity (§II) or YAGNI (§III). Table left intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| _(none)_ | _(n/a)_ | _(n/a)_ |

---

## Phase 0 / Phase 1 Outputs

- **Phase 0**: [research.md](./research.md) — 5 architecture decisions resolved (SQLite provider swap, xvfb strategy, npm test delegation, docker-compose stack spin-up, compliance gate URL fix).
- **Phase 1**: [data-model.md](./data-model.md) (no new tables), [contracts/ci-jobs.md](./contracts/ci-jobs.md), [quickstart.md](./quickstart.md).

---

## Post-Design Constitution Re-Check

Re-evaluated after Phase 1 artifacts were produced. No new violations introduced.

| # | Principle / Standard | Phase 1 Verdict | Notes |
|---|---|---|---|
| II | Simplicity | PASS | Phase 1 confirms single-file scope. No new test infrastructure beyond `xvfb` system package and existing docker-compose file. |
| III | YAGNI | PASS | Contracts define exactly the five jobs driven by FRs. No extra jobs, no matrix strategies, no caching beyond what the `ubuntu-latest` runner provides by default. |
| IV | SRP | PASS | Each job in [contracts/ci-jobs.md](./contracts/ci-jobs.md) has a single defined responsibility and a single pass/fail criterion. |
| VI | TDD | PASS — by construction | The jobs execute existing test suites. No new test code added under this feature. |
| VII | Observability | PASS | GitHub Actions native job logs, timing annotations, and PR check status provide full observability without additional instrumentation. |

**Final gate result**: **PASS**. Ready for implementation (T001–T006).
