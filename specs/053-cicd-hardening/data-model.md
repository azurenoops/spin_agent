# Phase 1: Data Model — CI/CD Pipeline Hardening

**Branch**: `053-cicd-hardening`
**Date**: 2026-06-03
**Status**: Final

## Summary

This feature introduces **no new tables, no new migrations, no new C# entities, no new TypeScript types, and no new database columns**. The entire change surface is confined to `.github/workflows/ci.yml`.

No EF Core context changes. No API contract changes. No frontend changes. No test fixture schema changes.

## Entity Inventory

| Entity / Artifact | Touch | Change |
|---|---|---|
| `.github/workflows/ci.yml` | **MODIFY** | Add 4 new jobs; fix 1 existing job input |
| All C# source | No change | None |
| All TypeScript source | No change | None |
| All database tables | No change | None |
| All EF Core migrations | No change | None |
| All API contracts | No change | None |

## Why There Is No Data Model

CI/CD pipeline hardening is a pure **DevOps configuration** change. The feature adds GitHub Actions job definitions (YAML) that invoke test commands already exercised in local developer workflows. No application data is created, read, updated, or deleted as a result of this feature.

The five new or modified job definitions in `ci.yml` orchestrate:

1. **`dotnet-integration-tests`** — runs `dotnet test` against `tests/Ato.Copilot.Tests.Integration/` with an ephemeral SQLite database scoped to the CI runner's temp directory. The SQLite file is created at job start and discarded at job end. It is not committed, not persisted, and not shared between jobs.

2. **`vscode-extension-test`** — runs the VS Code extension test runner (`xvfb-run node out/test/runTests.js`) in the `extensions/vscode/` directory. No database; no network calls. Pure TypeScript/Node.js process.

3. **`m365-extension-test`** — runs `npm test` in `extensions/m365/`. No database; no persistent state.

4. **`dashboard-e2e-smoke`** — spins up `docker-compose.mcp.yml`, which starts the MCP server with its own ephemeral SQLite volume. That volume is destroyed by `docker compose down -v` at job end. No data persists beyond the job boundary.

5. **ATO Compliance Gate** (modified, not new) — changes a single `mcp-server-url` input value. No schema impact.

## Constraints Confirmed

- **No migration files** added or modified.
- **No `AtoCopilotContext`** changes.
- **No `appsettings.json`** changes in source — any `ASPNETCORE_ENVIRONMENT=Test` override lives in the CI job's `env:` block, not in committed application config files (unless `appsettings.Test.json` is absent and must be added — see T002).
- **SQLite ephemeral scope** — the integration test job's SQLite database file lives in `$RUNNER_TEMP` and is automatically cleaned up. It is never pushed to the repository.

## Schema Diff (visualization)

```text
Database tables:      no change
EF Core migrations:   no change
C# entity classes:    no change
TypeScript types:     no change
API response shapes:  no change

CI/CD:
  .github/workflows/ci.yml:
+   dotnet-integration-tests job   [NEW]
+   vscode-extension-test job      [NEW]
+   m365-extension-test job        [NEW]
+   dashboard-e2e-smoke job        [NEW]
~   ato-compliance-gate job        [MODIFY: mcp-server-url input fixed]
```

This is the **entirety** of the change surface for Feature 053.
