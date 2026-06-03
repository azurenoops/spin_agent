# Spec: API Mismatch Fixes

**Feature**: 054-api-mismatch-fixes
**Branch**: `054-api-mismatch-fixes`
**Status**: Ready for implementation
**Priority**: P0 — Critical (ship-blocking)

## Problem Statement

5 critical wiring failures where the Dashboard UI silently breaks at the HTTP layer. None produce compile errors.

| Gap | Frontend Call | Backend Route | Symptom |
|-----|--------------|---------------|---------|
| GAP-001 | `POST /api/dashboard/systems/{id}/inheritance/apply-profile` | **MISSING** | 404 on CRM Profile Apply |
| GAP-002 | `POST /systems/{id}/inheritance/import/preview` + `/apply` | **MISSING** | 404 on CRM Import Dialog |
| GAP-003 | `PUT /api/dashboard/remediation/poam/bulk-status` | `POST /api/dashboard/poam/bulk-status` | Wrong verb + missing prefix |
| GAP-004 | `PUT /systems/{systemId}/poam/{poamId}/status` | `/api/dashboard/poam/{poamId}/status` | Missing systemId prefix |
| GAP-014 | `sendMessage(attachments?: File[])` | Never included in request | Silent file drop |

## Functional Requirements

- FR-001: `POST /api/dashboard/systems/{id}/inheritance/apply-profile` returns 200
- FR-002: `POST /systems/{id}/inheritance/import/preview` returns preview data
- FR-003: `POST /systems/{id}/inheritance/import/apply` returns 200
- FR-004: Bulk POAM status uses PUT verb + `/remediation/poam/bulk-status` path — consistent frontend-to-backend
- FR-005: Single POAM status path includes `/systems/{systemId}` prefix
- FR-006: Chat file attachments included as FormData multipart

## Constraints

- Fix backend routes to match frontend (not vice versa) — frontend paths include systemId for tenancy scoping
- Do NOT break existing working POAM endpoints
- FormData multipart for file attachments (not JSON)
- Integration test for each fixed route

## GitHub Issues

- Epic: #120
- Tasks: #141 (apply-profile), #142 (import preview+apply), #143 (bulk POAM), #144 (single POAM status), #145 (file attachments)

## Files

- `src/Ato.Copilot.Mcp/Server/DashboardEndpoints.cs`
- `src/Ato.Copilot.Dashboard/src/api/inheritance.ts`
- `src/Ato.Copilot.Dashboard/src/api/remediation.ts`
- `src/Ato.Copilot.Dashboard/src/hooks/useChat.ts`
- `src/Ato.Copilot.Dashboard/src/hooks/useSseStream.ts`
