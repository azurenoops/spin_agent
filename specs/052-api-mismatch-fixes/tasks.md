# Tasks: API Mismatch Fixes

**Feature**: 052-api-mismatch-fixes
**Branch**: `052-api-mismatch-fixes`
**Input artifacts**: [spec.md](./spec.md)

**Tests**: REQUIRED. Every fixed route needs an integration test.

---

## Phase 1: Backend Route Registration (DashboardEndpoints.cs)

- [ ] T001 Register `POST /api/dashboard/systems/{id}/inheritance/apply-profile` in `DashboardEndpoints.cs`. Wire to existing `IInheritanceService.ApplyProfileAsync`. GitHub Issue: #141
- [ ] T002 Register `POST /api/dashboard/systems/{id}/inheritance/import/preview` in `DashboardEndpoints.cs`. GitHub Issue: #142
- [ ] T003 Register `POST /api/dashboard/systems/{id}/inheritance/import/apply` in `DashboardEndpoints.cs`. GitHub Issue: #142
- [ ] T004 Fix bulk POAM status: change backend to `PUT` verb and path to `/api/dashboard/systems/{id}/remediation/poam/bulk-status` matching frontend. GitHub Issue: #143
- [ ] T005 Fix single POAM status: add `/systems/{systemId}` prefix to backend route. Verify systemId used for RLS tenant scoping. GitHub Issue: #144

## Phase 2: Frontend File Attachment Wire-Up

- [ ] T006 Include `attachments?: File[]` as FormData multipart in `useSseStream.ts` request builder. Update `ChatRequest` type. Verify MCP server receives file bytes. Unit test for FormData assembly. GitHub Issue: #145

## Phase 3: Integration Tests

- [ ] T007 Integration test: `apply-profile` route returns 200
- [ ] T008 Integration test: `import/preview` returns preview data
- [ ] T009 Integration test: `import/apply` returns 200
- [ ] T010 Integration test: bulk POAM PUT returns 200
- [ ] T011 Integration test: single POAM status with systemId returns 200
