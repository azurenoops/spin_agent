# Research: SSP Document Export

**Feature**: 037-ssp-document-export  
**Date**: 2026-03-17  
**Status**: Complete

## R1: SSP Content Assembly Pipeline

**Question**: How does the existing `SspService.GenerateSspAsync` work and can it serve as the content source for Word/PDF/OSCAL export?

**Decision**: Yes — use `GenerateSspAsync` as the canonical content source for Word and PDF exports. For OSCAL, use the separate `OscalSspExportService.ExportAsync()`.

**Rationale**: `GenerateSspAsync` already loads the complete entity graph (system metadata, categorization, baseline, narratives with approved versions, roles, interconnections, boundaries, inventory, components, SSP sections, contingency plan, tailorings) and renders all 13 NIST 800-18 sections in order. It returns an `SspDocument` object with structured Markdown content including YAML front-matter. This is the richest data assembly available and covers all FR requirements (FR-001, FR-013, FR-014, FR-015). The method accepts `IProgress<string>` for progress reporting and `CancellationToken` for cancellation — both required for async job execution.

**Alternatives considered**:
- Build a separate data loader for export: Rejected — duplicates all entity loading logic already in `SspService`
- Render directly from entities to DOCX without Markdown intermediate: Rejected — Markdown intermediate enables consistent content across formats

## R2: DOCX Generation Approach

**Question**: How should the Word document be generated — custom OpenXml assembly or template-based mail-merge?

**Decision**: Use the existing `DocumentTemplateService.RenderDocxAsync()` for template-based DOCX generation with the SSP merge-field schema (17 fields). For systems without a custom template, use a built-in default template that ships with the application.

**Rationale**: `DocumentTemplateService` already implements DOCX mail-merge using ZipArchive + `word/document.xml` text replacement with `{{FieldName}}` merge fields. It validates templates on upload, extracts merge fields via regex, and supports the SSP document type schema with 17 fields (SystemName, SystemAcronym, SecurityCategorization, BaselineLevel, ControlNarratives, AuthorizationBoundary, etc.). Custom templates are validated against the SSP schema. The approach handles org-wide template storage via `ConcurrentDictionary<string, StoredTemplate>`.

**Gap**: Current `StoredTemplate` is in-memory only (`ConcurrentDictionary`). For the new feature we need persistent template storage (database + filesystem). The `SspTemplate` entity will persist metadata in SQL Server and template files on the local filesystem.

**Alternatives considered**:
- Generate DOCX from scratch using DocumentFormat.OpenXml SDK: Rejected — complex, requires building paragraph/table/style structures manually; template approach is proven
- Use a third-party library like DocX or Aspose: Rejected — additional dependency; existing ZipArchive approach works

## R3: PDF Generation Approach

**Question**: How should PDF export work?

**Decision**: Use the existing `DocumentTemplateService.RenderPdfAsync()` which uses QuestPDF 2025.7.0 (Community Edition, MIT license). The PDF pipeline will: (1) get the SSP Markdown content from `GenerateSspAsync`, (2) parse the Markdown sections, (3) render using QuestPDF's `Document.Create()` fluent API with headers, footers, page numbers, and table of contents.

**Rationale**: QuestPDF is already a project dependency with Community Edition licensing. `RenderPdfAsync` already exists in `DocumentTemplateService` with multi-page support, headers, footers, and page numbering. The "table of contents" requirement (US2 acceptance scenario 2) requires a two-pass render: first pass collects section titles and page numbers, second pass renders the TOC. QuestPDF supports this via the `SkipOnce` and `ShowOnce` page layout features.

**Alternatives considered**:
- Convert DOCX to PDF via LibreOffice CLI: Rejected — requires LibreOffice installation in Docker, heavy dependency
- Use iTextSharp/iText7: Rejected — AGPL license incompatible with the project

## R4: OSCAL JSON Export

**Question**: Can the existing `OscalSspExportService` be reused for the dashboard OSCAL export?

**Decision**: Yes — call `OscalSspExportService.ExportAsync()` directly from the new `SspExportService`. It already produces a valid OSCAL SSP JSON string with system metadata, control implementations, and component inventory. Serialize to a `.json` file and store alongside Word/PDF exports.

**Rationale**: `OscalSspExportService` loads the same entity graph as `SspService`, builds OSCAL-compliant JSON with NIST baseline profile URIs, includes `system-implementation.components`, and returns warnings for data gaps. The `OscalExportResult.OscalJson` property contains the complete JSON string ready for file storage.

**Alternatives considered**:
- Build a new OSCAL serializer from the Markdown content: Rejected — OSCAL requires structured data, not parsed Markdown

## R5: Async Job Execution Pattern

**Question**: What pattern should be used for async SSP export with real-time notification?

**Decision**: Use a `Channel<SspExportJob>` producer-consumer pattern with a `BackgroundService` consumer. The API endpoint enqueues a job and returns immediately with a job ID. The background service dequeues, generates the export, stores the file, persists metadata, and pushes a SignalR notification via `NotificationHub`.

**Rationale**: The codebase already uses `BackgroundService` extensively (ComplianceWatch, DigestScheduler, EscalationService, RetentionCleanup, OverdueScan — all follow the same pattern). `Channel<T>` is the standard .NET approach for bounded producer-consumer queues. The `NotificationHub` already supports `NewNotification` events to named user groups (`user:{userId}`), and the frontend `useNotifications` hook auto-reconnects.

**Implementation**:
1. `SspExportBackgroundService : BackgroundService` — reads from `Channel<SspExportJob>`, processes one at a time per system
2. `SspExportJob` record — contains systemId, format, templateId, userId, jobId
3. When complete: persist `SspExport` entity, push `SspExportReady` event via `IHubContext<NotificationHub>`
4. When failed: push `SspExportFailed` event with error message

**Alternatives considered**:
- Hangfire: Rejected — additional infrastructure dependency; `BackgroundService` + `Channel<T>` is lightweight and already the project pattern
- Synchronous with HTTP timeout: Rejected — SC-001 allows 60s which is close to typical HTTP timeouts; async is safer

## R6: File Storage Strategy

**Question**: Where should generated SSP files and uploaded templates be stored?

**Decision**: Local filesystem under a configurable `exports/` directory inside the container's data volume. Template files stored alongside in a `templates/` directory. Path validation via existing `PathSanitizationService`.

**Rationale**: The project does not currently use Azure Blob Storage or any external object store. Current template storage is in-memory (`ConcurrentDictionary`). For persistence across container restarts, files will be stored on a Docker volume. File metadata (path, size, hash) persisted in SQL Server via `SspExport` and `SspTemplate` entities. `PathSanitizationService` already exists for directory traversal prevention.

**Directory structure**:
```
/app/data/
├── exports/
│   └── {systemId}/
│       └── {exportId}.{docx|pdf|json}
└── templates/
    └── {templateId}.docx
```

**Alternatives considered**:
- Azure Blob Storage: Rejected — adds Azure dependency; local storage sufficient for initial release per spec assumptions
- Database BLOBs: Rejected — SSP documents can be 10-50 MB; SQL Server not optimal for large binary storage

## R7: Retention and Cleanup

**Question**: How should export file retention and automatic cleanup work?

**Decision**: Add a `RetentionCleanupHostedService` (if not already covering exports) that runs daily, queries `SspExport` records where `ExpiresAt < DateTime.UtcNow`, deletes the corresponding files, and removes the database records. Default retention: 30 days from generation.

**Rationale**: The project already has `RetentionCleanupHostedService` for session cleanup. The same pattern applies — periodic timer, scoped DI, query-then-delete. The `SspExport.ExpiresAt` column provides the retention boundary.

## R8: Role-Based Access Control

**Question**: How to implement export RBAC in the dashboard API?

**Decision**: The dashboard currently does not enforce per-endpoint RBAC (all dashboard endpoints are accessible to any authenticated user). For this feature, add role checking in the endpoint handlers by reading the user's role claims from the JWT token. ISSM/ISSO/AO can export; ISSM/Administrator can manage templates.

**Rationale**: The MCP server has a full `ComplianceRoles` enum and RBAC enforcement, but the dashboard API layer (`DashboardEndpoints.cs`) currently has no role checks. This feature introduces the first dashboard-level RBAC, which should be implemented as a reusable pattern (middleware or extension method) for future features.

**Alternatives considered**:
- Defer RBAC to a future feature: Rejected — SSP documents contain CUI; spec requires RBAC per clarification Q1
- Use MCP-layer RBAC: Rejected — dashboard endpoints are separate from MCP tools
