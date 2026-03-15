# ato-copilot Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-21

## Active Technologies
- C# 13 / .NET 9.0 + Azure.Identity 1.13, Azure.ResourceManager 1.13, Microsoft.EntityFrameworkCore 9.0, Serilog 4.2, xUnit 2.9, FluentAssertions 7.0, Moq 4.20 (002-remediation-kanban)
- SQLite (dev) / SQL Server (prod) via EF Core — `AtoCopilotContext` extended with 4 new DbSets; new migration (002-remediation-kanban)
- C# 13 / .NET 9.0 + Microsoft.Identity.Client (MSAL.NET) 4.68+, Microsoft.Identity.Web 3.5+, Microsoft.Graph 5.70+, Microsoft.AspNetCore.Authentication.JwtBearer 9.0.0 (existing), Azure.ResourceManager.SecurityCenter 1.2.0-beta.6 (existing for JIT VM), EF Core 9.0 (existing) (003-cac-auth-pim)
- SQLite (dev) / SQL Server (prod) — extending existing AtoCopilotContext with CacSession, JitRequestEntity, CertificateRoleMapping entities (003-cac-auth-pim)
- C# 13 / .NET 9.0 + Microsoft.SemanticKernel 1.34, Microsoft.Identity.Web 3.5, Microsoft.Graph 5.70, Azure.ResourceManager.SecurityCenter, EF Core 9.0, Serilog, xUnit 2.9.3, FluentAssertions 7.0.0, Moq 4.20.72 (003-cac-auth-pim)
- SQLite (dev) / SQL Server (prod) via EF Core; Azure Key Vault (prod secrets) (003-cac-auth-pim)
- C# 13 / .NET 9.0 + Microsoft.Extensions.AI 9.4.0-preview, Microsoft.Identity.Web 3.5.0, Serilog, Entity Framework Core 9.0.0 (004-kanban-user-context)
- SQLite (development), SQL Server (production) via EF Core (004-kanban-user-context)
- C# 13 / .NET 9.0 + EF Core 9.0, Azure.ResourceManager, Serilog, Microsoft.Extensions.Hosting, System.Threading.Channels (005-compliance-watch)
- SQLite (development) / SQL Server (production) via `AtoCopilotContext`; existing patterns — `IDbContextFactory<AtoCopilotContext>` for Singleton services (005-compliance-watch)
- C# 13 / .NET 9.0 (backend), TypeScript 4.9 / React 18.2 (frontend) + ASP.NET Core SignalR, EF Core 9.0, Serilog, SPA Services (backend); React, react-markdown, @microsoft/signalr, Tailwind CSS, Axios (frontend) (006-chat-app)
- SQLite (local dev) / SQL Server (Docker/production) — dual-provider, auto-detected from connection string (006-chat-app)
- [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION] (007-nist-controls)
- [if applicable, e.g., PostgreSQL, CoreData, files or N/A] (007-nist-controls)
- C# 13 / .NET 9.0 + Azure.ResourceManager (1.13.2), Azure.ResourceManager.PolicyInsights (1.2.0), Azure.ResourceManager.SecurityCenter (1.2.0-beta.6), Azure.ResourceManager.Resources (1.9.0), Azure.ResourceManager.ResourceGraph (1.1.0), Azure.Identity (1.13.2), Microsoft.EntityFrameworkCore (9.0.0), Microsoft.Extensions.Caching.Memory (9.0.0), Microsoft.Extensions.AI (9.4.0-preview), Serilog (4.2.0), Microsoft.Graph (5.70.0) (008-compliance-engine)
- EF Core with SQLite (dev) / SQL Server (prod) via `IDbContextFactory<AtoCopilotContext>`; Azure Blob Storage via `IEvidenceStorageService` (008-compliance-engine)
- C# 13 / .NET 9.0 + Azure.ResourceManager (1.13.2), Azure.Identity (1.13.2), Microsoft.Extensions.AI (9.4.0-preview), Microsoft.EntityFrameworkCore (9.0.x), Moq, xUnit, FluentAssertions (009-remediation-engine)
- EF Core InMemory (unit tests), SQL Server via AtoCopilotContext (runtime), in-memory Dictionary + List for remediation tracking (009-remediation-engine)
- C# 13 / .NET 9.0 + Microsoft.Extensions.AI, Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.DependencyInjection, System.Text.Json, xUnit 2.9.3, Moq 4.20.72, FluentAssertions 7.0.0 (010-knowledgebase-agent)
- JSON data files on disk (9 files loaded into `IMemoryCache`); `IAgentStateManager` (in-memory) for agent state (010-knowledgebase-agent)
- C# 13 / .NET 9.0 (`net9.0` across all projects) (011-azure-openai-agents)
- SQLite (dev) / SQL Server (prod) via EF Core. Chat has separate `ChatDbContext` for conversation persistence. No new storage for this feature — conversation history already managed in `AgentConversationContext.MessageHistory` (in-memory per request) and `ChatDbContext` (persistent in Chat service). (011-azure-openai-agents)
- C# 13 / .NET 9.0 + `IRemediationEngine` (AI → NIST fallback), `IAiRemediationPlanGenerator` (Azure OpenAI via `IChatClient`), `IScriptSanitizationService`, `INistRemediationStepsService`, `IKanbanService`, EF Core 9 (012-task-enrichment)
- SQL Server 2022 via EF Core (existing `RemediationTasks` table; `RemediationScript` varchar(8000) and `ValidationCriteria` varchar(2000) columns already exist; new `RemediationScriptType` varchar(20) column added) (012-task-enrichment)
- C# 13 / .NET 9.0 (Channels library), TypeScript 5.x (VS Code extension), TypeScript 5.x / Node.js 20 LTS (M365 extension) + `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `System.Text.Json` (Channels); `@vscode/chat` API, `axios` (VS Code); `express`, `adaptivecards`, `axios` (M365) (013-copilot-everywhere)
- In-memory `ConcurrentDictionary` collections (Channels library); `IConversationStateManager` from `Ato.Copilot.State` (message persistence) (013-copilot-everywhere)
- C# 13 / .NET 9.0 (server), TypeScript 5.3 (VS Code & M365 extensions) + Microsoft.Extensions.AI 9.4.0-preview, Azure.Identity 1.13.2, Azure.ResourceManager.* 1.x, Microsoft.EntityFrameworkCore 9.0, Microsoft.Graph 5.70.0, Serilog 4.2.0, System.Text.Json 9.0.5 (C#); axios 1.6.5, adaptivecards 3.0.1, express 4.18.2 (TS) (014-agent-ui-enrichment)
- EF Core 9.0 dual-provider (SQLite dev / SQL Server prod); two DbContexts (`AtoCopilotContext` for compliance, `ChatDbContext` for chat); in-memory caching via `Microsoft.Extensions.Caching.Memory` (014-agent-ui-enrichment)
- Markdown + MkDocs (Python-based static site generator) + MkDocs, mkdocs-material theme, mkdocs-search plugin (016-user-documentation)
- N/A (static Markdown files in `docs/`) (016-user-documentation)
- C# 13 / .NET 9.0 (`net9.0`) + EF Core 9.0, QuestPDF 2024.12.3, System.Text.Json 9.0.5, Serilog 4.2.0, ClosedXML 0.104.2 (018-sap-generation)
- EF Core InMemory (dev/test), SQLite/SQL Server (prod); `EnsureCreatedAsync()` — no migrations (018-sap-generation)
- C# 13 / .NET 9.0 + `System.Text.Json` (JSON parsing), `string.Split` + quote-aware CSV logic (CSV parsing), existing `IScanImportService`, `ISystemSubscriptionResolver`, `IAssessmentArtifactService`, `IBaselineService` (019-prisma-cloud-import)
- EF Core 9.0 InMemory (dev/test). Extends existing `ScanImportRecords` and `ScanImportFindings` DbSets — NO new DbSets, NO new migrations. `EnsureCreatedAsync()`. (019-prisma-cloud-import)
- N/A — documentation-only feature (manual test scripts) + Requires all 118 MCP tools from Features 001–019 to be deployed and functional (020-persona-test-cases)
- N/A — no data model changes (020-persona-test-cases)
- C# 13 / .NET 9.0 + EF Core 9.0.0, Azure.AI.OpenAI 2.1.0, Microsoft.Extensions.AI.OpenAI 9.4.0-preview, Serilog 4.2.0 (021-pia-interconnections)
- EF Core with SQL Server (Docker, production) / SQLite (development). Adds 4 new DbSets (`PrivacyThresholdAnalyses`, `PrivacyImpactAssessments`, `SystemInterconnections`, `InterconnectionAgreements`). (021-pia-interconnections)
- C# 13 / .NET 9.0 (`net9.0`, nullable enabled, implicit usings) + EF Core 9.0.0 (SQLite + SQL Server), System.Text.Json 9.0.5, Azure.AI.OpenAI 2.1.0, Serilog 4.2.0 (022-ssp-full-oscal)
- EF Core with SQLite (dev) / SQL Server (prod); InMemory provider for tests (022-ssp-full-oscal)
- Markdown (MkDocs Material theme) + MkDocs with Material theme, pymdownx extensions (admonition, superfences, tabbed, tasklist) (023-feature-docs-update)
- Static markdown files in `docs/` directory, served via MkDocs (023-feature-docs-update)
- C# 13 / .NET 9.0 + ASP.NET Core, EF Core 9.0.0, Serilog, DiffPlex (new — MIT, .NET Standard 2.0) (024-narrative-governance)
- SQLite (dev) / SQL Server (prod) via EF Core dual-provider (024-narrative-governance)
- C# 13 / .NET 9.0 + EF Core 9.0.0 (SQLite dev / SQL Server prod), ClosedXML (Excel I/O), System.Text.Json, FluentAssertions + xUnit + Moq (tests) (025-hw-sw-inventory)
- EF Core — `AtoCopilotContext` with `DbSet<InventoryItem>`. SQLite for dev, SQL Server for prod. (025-hw-sw-inventory)
- C# / .NET 8 + `System.Xml.Linq` (XDocument parser), Entity Framework Core, xUnit + FluentAssertions + Moq (testing) (026-acas-nessus-import)
- Azure Cosmos DB (via EF Core) — extends existing `ScanImportRecord`/`ScanImportFinding` entities (026-acas-nessus-import)
- C# 13 / .NET 9.0 + ASP.NET Core, Entity Framework Core, Serilog, Azure.Identity, Microsoft.Graph (027-cac-simulation-mode)
- SQL Server via EF Core (`IDbContextFactory<AtoCopilotContext>` pattern) (027-cac-simulation-mode)
- C# 13 / .NET 9.0 + `Azure.AI.Projects` (new — Foundry SDK), `Azure.AI.OpenAI` 2.1.0 (existing), `Microsoft.Extensions.AI` 9.4.x (existing), EF Core 9.0, ASP.NET Core (028-foundry-agents)
- SQL Server (containerized), SQLite (dev/test) — no schema changes for this feature (028-foundry-agents)
- C# 13 / .NET 9.0 + Azure.AI.Agents.Persistent 1.1.0, Azure.AI.OpenAI 2.1.0, Microsoft.Extensions.AI 9.4.x, Azure.Identity 1.13.2, EF Core 9.0, Serilog (028-foundry-agents)
- SQLite (dev) / SQL Server (prod) via EF Core; Foundry threads are server-side (ephemeral, no local persistence) (028-foundry-agents)
- C# 13 / .NET 9.0 + ASP.NET Core 9.0, Polly 8.4.2 (`Microsoft.Extensions.Http.Resilience` 9.0.0), `System.Threading.RateLimiting`, `System.Diagnostics.Metrics`, Serilog, Entity Framework Core 9.0, OpenTelemetry SDK (new) (029-enterprise-mcp-hardening)
- SQL Server / SQLite via EF Core (existing); `IMemoryCache` for response caching; embedded OSCAL JSON resource (029-enterprise-mcp-hardening)

- C# 13 / .NET 9.0 + Azure.Identity 1.13, Azure.ResourceManager 1.13, Microsoft.Extensions.AI 9.4-preview, Microsoft.EntityFrameworkCore 9.0, Serilog 4.2, xUnit 2.9, FluentAssertions 7.0, Moq 4.20 (001-core-compliance)

## Project Structure

```text
src/
tests/
```

## Commands

dotnet build Ato.Copilot.sln [ONLY COMMANDS FOR ACTIVE TECHNOLOGIES][ONLY COMMANDS FOR ACTIVE TECHNOLOGIES] dotnet test

## Code Style

C# .NET 9: Follow standard conventions

## Recent Changes
- 029-enterprise-mcp-hardening: Added C# 13 / .NET 9.0 + ASP.NET Core 9.0, Polly 8.4.2 (`Microsoft.Extensions.Http.Resilience` 9.0.0), `System.Threading.RateLimiting`, `System.Diagnostics.Metrics`, Serilog, Entity Framework Core 9.0, OpenTelemetry SDK (new)
- 028-foundry-agents: Added C# 13 / .NET 9.0 + Azure.AI.Agents.Persistent 1.1.0, Azure.AI.OpenAI 2.1.0, Microsoft.Extensions.AI 9.4.x, Azure.Identity 1.13.2, EF Core 9.0, Serilog
- 028-foundry-agents: Added C# 13 / .NET 9.0 + `Azure.AI.Projects` (new — Foundry SDK), `Azure.AI.OpenAI` 2.1.0 (existing), `Microsoft.Extensions.AI` 9.4.x (existing), EF Core 9.0, ASP.NET Core


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
