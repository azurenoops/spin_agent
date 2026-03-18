# Implementation Plan: SSP Document Export

**Branch**: `037-ssp-document-export` | **Date**: 2026-03-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/037-ssp-document-export/spec.md`

## Summary

Add one-click SSP document export from the Dashboard in Word (.docx), PDF, and OSCAL JSON formats. The system compiles all narratives, People/Places/Things components, boundary definitions, control implementations, roles, and system metadata into downloadable SSP documents. Custom .docx template upload is supported. Exports run as async background jobs with SignalR real-time notifications. All exports are audit-logged with SHA-256 content hashes.

**Technical approach**: Leverage the existing `SspService.GenerateSspAsync()` Markdown assembly pipeline as the content source. Use the existing `DocumentTemplateService` for DOCX rendering (mail-merge) and `QuestPDF` for PDF rendering. Use `OscalSspExportService` for OSCAL JSON output. Add new `SspExportService` to orchestrate async job execution, file storage, and notification delivery via the existing `NotificationHub` SignalR infrastructure.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0 (backend), TypeScript 5.x / React (frontend)  
**Primary Dependencies**: QuestPDF 2025.7.0 (PDF), DocumentFormat.OpenXml via ZipArchive (DOCX), ClosedXML 0.104.2 (Excel), System.Text.Json (OSCAL), SignalR (real-time), axios (frontend HTTP)  
**Storage**: SQL Server (entity metadata via EF Core 9.0), local filesystem (generated export files and uploaded templates)  
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration)  
**Target Platform**: Linux Docker containers (ASP.NET 9.0), nginx-served React SPA  
**Project Type**: Web application (ASP.NET Minimal API backend + React dashboard frontend)  
**Performance Goals**: SSP export < 60s for 500 controls; File download < 2s for cached exports  
**Constraints**: Template upload max 10 MB; Export files max 50 MB; 512 MB memory budget; Export file retention 30 days  
**Scale/Scope**: 1-10 concurrent export jobs per system; Organization-wide template library; ~325 controls (Moderate) to ~421 controls (High) typical workload

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Feature follows `/docs/` guidance; spec committed to `/specs/037-ssp-document-export/` |
| II. BaseAgent/BaseTool Architecture | N/A | Feature does not add agents or tools; uses existing `SspService` and `DocumentTemplateService` |
| III. Testing Standards | PASS | Plan includes unit tests for export service, integration tests for API endpoints, boundary-value tests for file size limits |
| IV. Azure Government & Compliance First | PASS | No new Azure interactions; OSCAL export uses NIST-standard URIs; file storage is local (no cloud blob) |
| V. Observability & Structured Logging | PASS | FR-021 requires audit logging with SHA-256 hash; all export operations logged via Serilog |
| VI. Code Quality & Maintainability | PASS | Single-responsibility services; DI for all dependencies; XML docs on public types |
| VII. User Experience Consistency | PASS | Export dialog follows existing UI patterns; progress indicator via SignalR matches existing notification pattern |
| VIII. Performance Requirements | PASS | SC-001 targets 60s for 500 controls; async job prevents HTTP timeout; `CancellationToken` on all async paths |

**Gate result**: PASS — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/037-ssp-document-export/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (API contracts)
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/Ato.Copilot.Core/
├── Models/Compliance/
│   ├── SspExport.cs               # NEW — Export metadata entity
│   └── SspTemplate.cs             # NEW — Custom template entity
├── Dtos/Dashboard/
│   └── SspExportDtos.cs           # NEW — Request/response DTOs
└── Interfaces/Compliance/
    └── ISspExportService.cs       # NEW — Export service interface

src/Ato.Copilot.Agents/
└── Compliance/Services/
    └── SspExportService.cs        # NEW — Export orchestration, file storage, async job

src/Ato.Copilot.Mcp/
├── Endpoints/
│   └── DashboardEndpoints.cs      # MODIFIED — Add SSP export and template endpoints
├── Hubs/
│   └── NotificationHub.cs         # EXISTING — Used for export-ready notifications
└── Program.cs                     # MODIFIED — Register new services, EnsureSchemaAdditions

src/Ato.Copilot.Dashboard/
├── src/api/
│   └── exports.ts                 # NEW — SSP export and template API client
├── src/components/
│   ├── ExportSspDialog.tsx        # NEW — Format/template selection dialog
│   └── TemplateManagementDialog.tsx # NEW — Template CRUD dialog
└── src/pages/
    └── Documents.tsx              # MODIFIED — Add Export SSP button, export history, download links

tests/Ato.Copilot.Tests.Unit/
└── SspExportServiceTests.cs       # NEW — Unit tests for export service

tests/Ato.Copilot.Tests.Integration/
└── SspExportEndpointTests.cs      # NEW — Integration tests for export API
```

**Structure Decision**: Follows the existing multi-project web application layout. New entities in `Core/Models`, new service in `Agents/Services`, new endpoints in `Mcp/Endpoints`, new UI components in `Dashboard`. No new projects required — all additions fit within existing project boundaries.
