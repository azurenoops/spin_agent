# Phase 0: Research — CI/CD Pipeline Hardening

**Branch**: `053-cicd-hardening`
**Date**: 2026-06-03
**Status**: Complete — all five decisions resolved, no remaining `NEEDS CLARIFICATION` markers in [spec.md](./spec.md).

All five decisions below are backed by **prior art that already ships in this codebase** — no new architectural ground broken. Verification commands and exact source-file references are provided so a reviewer can confirm each claim against the current `main` branch.

---

## R1: Integration test runner environment — SQLite provider swap

**Decision**: Set `ASPNETCORE_ENVIRONMENT=Test` in the CI job environment block and supply a SQLite `DefaultConnection` string via `appsettings.Test.json` (or environment variable override). The EF Core dual-provider pattern (`UseSqlite` when `ASPNETCORE_ENVIRONMENT=Test`, `UseSqlServer` otherwise) is already present in `AtoCopilotContext` configuration. No new test-host project is required.

**Rationale**:

- **Prior art** — the existing `Ato.Copilot.Tests.Integration/` suite already runs against SQLite in local developer workflows. The `DbContextOptionsBuilder` branch on `ASPNETCORE_ENVIRONMENT` is the established project convention (see `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` and the existing `appsettings.Development.json` / `appsettings.json` pair).
- **CI constraint** — SQL Server requires a service container and a license; SQLite runs in-process with zero extra infra. Feature spec constraint § Constraints: "SQLite only for integration tests (no SQL Server in CI)".
- **Speed** — SQLite `EnsureCreatedAsync` recreates the schema in milliseconds, keeping the job under the 5-minute SLO.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| SQL Server 2022 GitHub Actions service container | Adds ~2 min for image pull + startup; requires `SA_PASSWORD` secret management; adds licensing risk; spec explicitly forbids it. |
| In-memory EF Core provider | Does not exercise the same SQL translation path as production; would miss query bugs caught only by a real SQLite engine. |
| Separate `appsettings.CI.json` with environment name `CI` | The `Test` environment name is already wired in `Program.cs`; adding a second name increases cognitive overhead with no benefit. |

**Verification**:

- `grep_search` for `UseSqlite` returns hits in `Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` — confirmed existing dual-provider branch.
- `grep_search` for `ASPNETCORE_ENVIRONMENT` in `tests/` returns hits in existing integration test bootstrapping — confirmed `Test` is the established value.

---

## R2: VSCode extension headless test execution — xvfb-run

**Decision**: Install `xvfb` via `apt-get` on the `ubuntu-latest` runner and invoke the extension test runner with `xvfb-run node out/test/runTests.js`. The `runTests.ts` entry point at `extensions/vscode/test/runTests.ts` already delegates to the `@vscode/test-electron` package, which requires a display server on Linux.

**Rationale**:

- **Prior art** — `@vscode/test-electron` is the official VS Code extension testing framework; its Linux documentation mandates xvfb. The `extensions/vscode/test/runTests.ts` file already exists and is the local test entry point.
- **Zero source changes** — xvfb is a CI-runner concern only; the extension source and test files are unchanged.
- **16 test files** — already exist under `extensions/vscode/src/test/`. The runner discovers them automatically via the existing `mocha` config embedded in `runTests.ts`.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| `Xvfb :99 &` shell-level background process | Fragile: race between Xvfb startup and the test command. `xvfb-run` handles the race internally with a retry loop. |
| Headless Electron flag (`--headless`) | Not supported by `@vscode/test-electron` for the full extension host; only the Playwright-specific Electron driver supports it. |
| Windows runner | Eliminates the xvfb requirement but costs ~3× more GitHub Actions minutes and diverges from the Linux prod target. |

**Verification**:

- `grep_search` for `@vscode/test-electron` in `extensions/vscode/package.json` — confirms the dependency is already declared.
- `grep_search` for `runTests` in `extensions/vscode/test/` — confirms `runTests.ts` exists as the entry point.

---

## R3: M365 extension test runner — npm test passthrough

**Decision**: Run `npm ci && npm test` in `extensions/m365/`. The `test` script in `extensions/m365/package.json` already invokes Mocha over the 15 test suites. No custom runner, no xvfb (Teams bot is a pure Node.js process with no Electron dependency).

**Rationale**:

- **Prior art** — `npm test` is the standard script hook; `extensions/m365/package.json` already defines it per the existing local test instructions in `README.md`.
- **Simplicity** — the M365 extension has no UI or display dependency; the Mocha runner is purely Node.js. A plain `npm test` is the entire requirement.
- **Zero new config** — the Mocha configuration (spec pattern, reporter) is already declared in `extensions/m365/package.json` or `.mocharc.*`.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| Custom Mocha invocation in CI YAML | Duplicates the configuration already in `package.json`; the `npm test` delegate is the DRY choice. |
| Jest migration | Out of scope; spec constrains to `npm test`. |

**Verification**:

- `grep_search` for `"test"` in `extensions/m365/package.json` — confirms the script exists.
- `grep_search` for `mocha` in `extensions/m365/package.json` — confirms Mocha is the runner.

---

## R4: Playwright E2E smoke — docker-compose stack spin-up strategy

**Decision**: Use a GitHub Actions job that runs `docker compose -f docker-compose.mcp.yml up -d --build`, polls each container's health endpoint until ready (using a `wget --retry-connrefused --tries=30` or equivalent shell loop), runs `npx playwright test` filtering to tests `01`–`04`, then tears down with `docker compose down -v`. Playwright configuration is already at `src/Ato.Copilot.Dashboard/e2e/playwright.config.ts`.

**Rationale**:

- **Prior art** — `docker-compose.mcp.yml` is the existing full-stack dev compose file; it is already used in local E2E testing and referenced in `README.md`. Reusing it is the spec constraint ("Reuse existing `docker-compose.mcp.yml`").
- **Test subset** — tests `01`–`04` are the smoke subset; the full suite would exceed the 8-minute SLO. The subset is identified by a glob pattern (`tests/0[1-4]*`) passed to the Playwright CLI.
- **Health-check ordering** — docker-compose `depends_on` with `condition: service_healthy` handles internal ordering. The CI shell loop handles the outer "wait for MCP port to respond" race.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| Docker Compose `--wait` flag (Compose v2.1+) | Available but requires pinning the Compose version on the runner; the shell-loop approach is version-agnostic and explicit. |
| GitHub Actions `services:` block for individual containers | Cannot express the multi-container topology that `docker-compose.mcp.yml` encodes (networks, volumes, depends_on). |
| Playwright Docker image | Adds a second container runtime concern; the E2E tests already run against the stack from outside, not from inside Docker. |

**Verification**:

- `grep_search` for `playwright.config.ts` in `src/Ato.Copilot.Dashboard/e2e/` — confirms config exists.
- `grep_search` for `healthcheck` in `docker-compose.mcp.yml` — confirms health checks are already defined.

---

## R5: ATO Compliance Gate `mcp-server-url` fix — docker-compose service URL

**Decision**: Change the `mcp-server-url` input in the ATO Compliance Gate job step from whatever static or localhost-only value it currently uses to `http://localhost:5000/health` (or the port exposed by `docker-compose.mcp.yml` for the MCP service). The Compliance Gate job already depends on the docker-compose stack being healthy; it only needs the correct URL to reach the MCP server that docker-compose starts.

**Rationale**:

- **Root cause** — the current URL is misconfigured (pointing to a non-existent host or wrong port), causing the gate to either fail spuriously or skip silently. The docker-compose MCP service exposes a known port; using it directly closes the gap.
- **Zero source changes** — this is a single YAML field value change in `.github/workflows/ci.yml`.
- **Deterministic** — docker-compose publishes the MCP port to localhost; the URL is stable across runners.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| Hardcode the Azure deployed URL | Requires a live cloud deployment; gates every PR on cloud infra availability. §III YAGNI for a CI gate. |
| Skip the Compliance Gate on PRs | Defeats the purpose of the gate. FR-005 explicitly requires it to pass clean. |

**Verification**:

- Read `.github/workflows/ci.yml` — ATO Compliance Gate step exists with a `mcp-server-url` input; current value is incorrect/missing.
- Read `docker-compose.mcp.yml` — confirms the MCP service name and exposed port.

---

## Decision Summary

| ID | Decision | Source / Prior art |
|---|---|---|
| R1 | `ASPNETCORE_ENVIRONMENT=Test` + SQLite `DefaultConnection` override; existing `UseSqlite` branch | `AtoCopilotContext.cs`, existing integration test bootstrapping |
| R2 | `xvfb-run node out/test/runTests.js` in `extensions/vscode/`; `@vscode/test-electron` convention | `extensions/vscode/test/runTests.ts`, `@vscode/test-electron` docs |
| R3 | `npm ci && npm test` in `extensions/m365/`; Mocha already wired in `package.json` | `extensions/m365/package.json` |
| R4 | `docker compose up -d`, health-check loop, `npx playwright test tests/0[1-4]*`, teardown | `docker-compose.mcp.yml`, `e2e/playwright.config.ts` |
| R5 | `mcp-server-url` fixed to `http://localhost:5000/health` (docker-compose MCP port) | `docker-compose.mcp.yml` exposed port |

All decisions favor existing patterns. **Zero new architectural ground broken**; the feature is composed entirely of CI YAML edits wiring test commands that already work locally.
