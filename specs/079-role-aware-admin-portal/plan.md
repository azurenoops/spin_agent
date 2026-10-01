# Implementation Plan: Role-Aware SPIN Admin Portal

**Branch**: `079-role-aware-admin-portal` | **Date**: 2026-10-01 | **Spec**: [spec.md](./spec.md)

## Summary

Add a server-evaluated scoped access model, enforce it at backend resources, and
compose the existing organization/CSP/platform administration services in a
shared SPIN shell based on the approved administration mock set.

## Technical Context

**Language/Version**: C# 13 / .NET 9; TypeScript 5.7 / React 19
**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core 9, React Router 7, Axios, Tailwind CSS
**Storage**: Existing SQLite/SQL Server `AtoCopilotContext`; no schema change
**Testing**: xUnit, FluentAssertions, Moq, WebApplicationFactory, Vitest, Testing Library, Playwright
**Target Platform**: Azure Government-hosted Linux containers and evergreen browsers
**Project Type**: Web application
**Performance Goals**: Effective-access p95 under 200 ms for 100 destinations
**Constraints**: Zero trust, tenant isolation, no client authorization, no FAST implementation
**Scale/Scope**: Organization, provider, platform, and system destinations

## Constitution Check

- Documentation first: PASS - spec, research, model, contract, tasks precede product code.
- Simplicity/YAGNI: PASS - one evaluator and one contract; reuse existing services.
- TDD: PASS - test tasks precede every production task.
- Zero trust/tenant isolation: PASS - server resource authorization and scoped queries.
- Local type-check parity: dashboard typecheck script will be added if absent.
- Issue discipline: PENDING - issue body must be previewed before external creation.
- UX consistency: PASS - standard envelope and approved mock composition.

## Project Structure

```text
specs/079-role-aware-admin-portal/
src/Ato.Copilot.Core/Authorization/
src/Ato.Copilot.Mcp/Authorization/
src/Ato.Copilot.Mcp/Endpoints/Auth/
src/Ato.Copilot.Dashboard/src/features/access/
src/Ato.Copilot.Dashboard/src/features/admin-portal/
tests/Ato.Copilot.Tests.Unit/Authorization/
tests/Ato.Copilot.Tests.Integration/Authorization/
```

## Architecture

1. Resolve verified claim and persisted assignment sources.
2. Produce immutable workspace destinations with explicit actions.
3. Enforce actions through resource-aware backend authorization.
4. Revalidate route destinations before mounting data providers.
5. Abort and generation-guard requests across context changes.
6. Reuse existing pages/services inside the shared shell.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
No constitutional complexity exceptions are required. Existing linked people
and role assignments represent organization administration, while explicit
provider-to-customer system sharing is deferred and fails closed rather than
introducing a speculative grant schema.
