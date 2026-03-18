# Implementation Plan: Evidence Repository

**Branch**: `038-evidence-repository` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/038-evidence-repository/spec.md`

## Summary

Add a unified evidence management system to the ATO Copilot Dashboard. Users can upload evidence artifacts (screenshots, scan results, config exports) and attach them to control implementations or security capabilities. A new Evidence Repository page provides a centralized, searchable view of all evidence for a system. The feature also surfaces the existing `EvidenceStorageService` automated collection via a dashboard "Collect Evidence" button. File storage uses an abstracted provider interface (local filesystem default, Azure Blob Storage optional) configurable via server-side settings (`appsettings.json` / environment variables).

## Technical Context

**Language/Version**: C# / .NET 9.0 (backend), TypeScript / React 18+ (frontend)
**Primary Dependencies**: ASP.NET Core Minimal APIs, Entity Framework Core 9, Axios, React, Tailwind CSS, Heroicons
**Storage**: SQL Server (metadata via EF Core), Local Filesystem / Azure Blob Storage (files via abstracted `IFileStorageProvider`)
**Testing**: xUnit + FluentAssertions + Moq (backend), Vitest (frontend)
**Target Platform**: Docker containers (nginx + ASP.NET), Azure Government compatible
**Project Type**: Full-stack web application (React SPA + .NET API)
**Performance Goals**: Evidence upload < 30s end-to-end (SC-001), Repository page load < 3s (SC-002), search/filter < 1s (SC-003), automated collection < 10s (SC-008)
**Constraints**: 25 MB max file size, file type allowlist with extension + content-type validation, 365-day default version retention
**Scale/Scope**: ~70 DbSets in AtoCopilotContext, 16+ dashboard pages, 2 new entities + 2 new enums, 1 new page, 1 new API service, ~20 new endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | PASS | Feature follows `/specs/` convention; docs will be updated |
| II | BaseAgent/BaseTool Architecture | N/A | No new agents or tools — feature uses existing `EvidenceStorageService` via dashboard endpoint |
| III | Testing Standards | PASS | Unit tests for service/model, integration tests for endpoints planned |
| IV | Azure Government & Compliance First | PASS | Azure Blob Storage provider supports Gov regions; no new Azure SDK dependencies beyond Azure.Storage.Blobs |
| V | Observability & Structured Logging | PASS | All new services will use `ILogger<T>` structured logging |
| VI | Code Quality & Maintainability | PASS | DI-based, single-responsibility services, XML docs on public members |
| VII | User Experience Consistency | PASS | Standard dashboard patterns (Axios client, Tailwind, inline panels) |
| VIII | Performance Requirements | PASS | Pagination on evidence list, CancellationToken on all async ops, response time targets defined |

**GATE RESULT: PASS** — No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/038-evidence-repository/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── evidence-api.md  # REST API contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
# Backend — new files
src/Ato.Copilot.Core/
├── Models/Compliance/
│   └── EvidenceArtifactModels.cs        # EvidenceArtifact, EvidenceVersion, ArtifactCategory, CollectionMethod enums
├── Interfaces/Compliance/
│   └── IEvidenceArtifactService.cs      # Service interface for upload/download/list/delete
└── Interfaces/Storage/
    └── IFileStorageProvider.cs          # Abstracted file storage interface

src/Ato.Copilot.Mcp/
├── Configuration/
│   └── EvidenceOptions.cs                    # Strongly-typed config for Evidence:* settings
└── Services/
    ├── EvidenceVersionPurgeService.cs        # Background service for version file purging
    └── Storage/
        ├── LocalFileStorageProvider.cs       # Local filesystem implementation
        └── AzureBlobStorageProvider.cs       # Azure Blob Storage implementation

src/Ato.Copilot.Dashboard/
└── Endpoints/
    └── (additions to DashboardEndpoints.cs)  # Evidence CRUD + upload/download endpoints

# Backend — modified files
src/Ato.Copilot.Core/Data/Context/
└── AtoCopilotContext.cs                 # Add DbSet<EvidenceArtifact>, DbSet<EvidenceVersion>

src/Ato.Copilot.Mcp/
└── Program.cs                           # Register IFileStorageProvider, IEvidenceArtifactService

# Frontend — new files
src/Ato.Copilot.Dashboard/src/
├── api/
│   └── evidence.ts                      # Evidence API service (Axios)
├── pages/
│   └── EvidenceRepository.tsx           # Evidence Repository page
├── components/
│   ├── EvidenceUploadDialog.tsx         # Upload dialog with category/method selection
│   ├── EvidenceDetailPanel.tsx          # Inline slide-over detail panel
│   └── EvidenceSection.tsx              # Evidence list for control narrative views
└── types/
    └── evidence.ts                      # TypeScript types for evidence entities

# Frontend — modified files
src/Ato.Copilot.Dashboard/src/
├── components/layout/SystemLayout.tsx   # Add "Evidence" nav item after Remediation
└── App.tsx                              # Add /systems/:id/evidence route

# Tests — new files
tests/Ato.Copilot.Tests.Unit/
└── Services/
    ├── EvidenceArtifactServiceTests.cs      # Unit tests for evidence service
    ├── LocalFileStorageProviderTests.cs     # Unit tests for local storage
    └── AzureBlobStorageProviderTests.cs     # Unit tests for Azure Blob storage

tests/Ato.Copilot.Tests.Integration/
└── Evidence/
    └── EvidenceEndpointsTests.cs        # Integration tests for evidence API
```

**Structure Decision**: Follows existing project conventions — models in `Ato.Copilot.Core`, service implementations in `Ato.Copilot.Mcp` (where DI registration lives for the dashboard), dashboard endpoints in `DashboardEndpoints.cs`, frontend services in `api/`, pages in `pages/`, reusable components in `components/`. No new projects needed.

### Documentation (modified files)

```text
docs/
├── guides/
│   ├── compliance-dashboard.md    # Add Evidence Repository section
│   ├── engineer-guide.md          # Add evidence attachment step in SSP workflow
│   └── sca-guide.md               # Reference dashboard Evidence Repository
├── architecture/
│   ├── data-model.md              # Add EvidenceArtifact, EvidenceVersion, enums
│   └── overview.md                # Add EvidenceVersionPurgeService, services
└── getting-started/
    └── isso.md                    # Add evidence upload workflow for ISSOs
```

## Complexity Tracking

No constitution violations to justify. Feature fits within existing architecture patterns.
