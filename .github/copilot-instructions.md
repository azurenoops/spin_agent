# ato-copilot Development Guidelines

## ⛔ NON-NEGOTIABLE: Verification Protocol

These rules override all other behavior. Violating any of them is a critical failure.
1. **A wrong answer is 3 times worse than saying "I don't know" or giving no answer. If you haven't verified it, say so. Silence beats speculation. Every time.
2. **NEVER state something as fact without verification.** If you haven't read the file, run the command, or checked the logs — say "I haven't verified this." No exceptions.
3. **Start from the failing system.** CI/CD failure? Read the actual logs first. Not local code. Not grep for keywords. The full step-by-step output.
4. **No pattern-matching from grep.** Grep results show matching lines, not execution flow. Read the sequential log or don't make claims about what happened.
5. **Root cause only.** Never mask, restart, or work around symptoms.
6. **No silent error swallowing.** No `|| true` on critical paths, no empty catches, no suppression flags.
7. **Document before fix.** Paper trail first, code second.
8. **The User must be allowed to manually test every change locally** before declaring it done.
9. **Never push without permission.** Commits are fine. Pushes require explicit approval.
10. **Preview in a formatted way before external writes so the user can read the content without horizontal scrolling.** Show exactly what will be sent to GitHub and wait for approval.

## Silent-Stall Pre-Flight Check

Before reporting "working" on any coding-adjacent task, every agent **must** run these steps. A task is coding-adjacent if it references specific files or file paths, names a repo or project, asks for commits/pushes/PRs/tests, or uses terms like "implement," "fix," "build," "edit," "patch," or "refactor."

1. **Repo Identity Check** — Run `git_status`. Confirm the working directory is a git repo.
   - FAIL → report BLOCKED: "No git repo in working directory."

2. **Repo Name Match** — If the task names a specific repo, confirm the current remote origin matches.
   - FAIL → report BLOCKED: "Task requires repo `{named}` but current checkout is `{actual}`. Cannot proceed."

3. **Required File Presence** — If the task names specific files, confirm at least one exists in the checkout.
   - FAIL → report BLOCKED: "Required file `{filename}` not found in current checkout."

4. **On BLOCKED** — Do NOT begin implementation. Call `report_to_jarvis` (significance=`high`) with: agent name, task summary, which step failed, expected vs. found. Await re-dispatch.


Auto-generated from all feature plans. Last updated: 2026-02-21

## Active Technologies
- C# 13 / .NET 9.0 + Azure.Identity 1.13, Azure.ResourceManager 1.13, Microsoft.EntityFrameworkCore 9.0, Serilog 4.2, xUnit 2.9, FluentAssertions 7.0, Moq 4.20 (002-remediation-kanban)
- SQLite (dev) / SQL Server (prod) via EF Core — `AtoCopilotContext` extended with 4 new DbSets; new migration (002-remediation-kanban)
- C# 13 / .NET 9.0 + Microsoft.Identity.Client (MSAL.NET) 4.68+, Microsoft.Identity.Web 3.5+, Microsoft.Graph 5.70+, Microsoft.AspNetCore.Authentication.JwtBearer 9.0.0, Azure.ResourceManager.SecurityCenter 1.2.0-beta.6, Microsoft.SemanticKernel 1.34, EF Core 9.0 (003-cac-auth-pim)
- SQLite (dev) / SQL Server (prod) via EF Core; Azure Key Vault (prod secrets); extending existing AtoCopilotContext with CacSession, JitRequestEntity, CertificateRoleMapping entities (003-cac-auth-pim)
- C# 13 / .NET 9.0 + Microsoft.Extensions.AI 9.4.0-preview, Microsoft.Identity.Web 3.5.0, Serilog, Entity Framework Core 9.0.0 (004-kanban-user-context)
- SQLite (development), SQL Server (production) via EF Core (004-kanban-user-context)
- C# 13 / .NET 9.0 + EF Core 9.0, Azure.ResourceManager, Serilog, Microsoft.Extensions.Hosting, System.Threading.Channels (005-compliance-watch)
- SQLite (development) / SQL Server (production) via `AtoCopilotContext`; existing patterns — `IDbContextFactory<AtoCopilotContext>` for Singleton services (005-compliance-watch)
- C# 13 / .NET 9.0 (backend), TypeScript 4.9 / React 18.2 (frontend) + ASP.NET Core SignalR, EF Core 9.0, Serilog, SPA Services (backend); React, react-markdown, @microsoft/signalr, Tailwind CSS, Axios (frontend) (006-chat-app)
- SQLite (local dev) / SQL Server (Docker/production) — dual-provider, auto-detected from connection string (006-chat-app)
- C# 13 / .NET 9.0 + EF Core 9.0, Serilog, xUnit (007-nist-controls)
- SQLite (dev) / SQL Server (prod) via EF Core — NIST 800-53 Rev 5 control catalog (007-nist-controls)
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
- C# 13 / .NET 9.0 + `Azure.AI.Projects` (Foundry SDK), `Azure.AI.Agents.Persistent` 1.1.0, `Azure.AI.OpenAI` 2.1.0, `Microsoft.Extensions.AI` 9.4.x, Azure.Identity 1.13.2, EF Core 9.0, Serilog (028-foundry-agents)
- SQLite (dev/test) / SQL Server (prod, containerized) via EF Core; Foundry threads are server-side (ephemeral, no local persistence) (028-foundry-agents)
- C# 13 / .NET 9.0 + ASP.NET Core 9.0, Polly 8.4.2 (`Microsoft.Extensions.Http.Resilience` 9.0.0), `System.Threading.RateLimiting`, `System.Diagnostics.Metrics`, Serilog, Entity Framework Core 9.0, OpenTelemetry SDK (new) (029-enterprise-mcp-hardening)
- SQL Server / SQLite via EF Core (existing); `IMemoryCache` for response caching; embedded OSCAL JSON resource (029-enterprise-mcp-hardening)
- C# 13 / .NET 9.0 (backend); TypeScript 5 (frontend) + ASP.NET Core 9.0, EF Core 9.0, Serilog (backend); React 19, Vite 6, Recharts 2, Tailwind CSS 3, Axios 1, React Router 7 (frontend) (030-compliance-dashboard)
- SQL Server via EF Core (AtoCopilotContext) (030-compliance-dashboard)
- C# 13 / .NET 9.0 + ASP.NET Core 9.0, EF Core 9.0 (SQL Server), Azure OpenAI (effort estimation), React 19, TypeScript 5, Recharts 2, Tailwind CSS 3 (031-implementation-roadmap)
- SQL Server via `AtoCopilotContext` (EF Core, `EnsureCreatedAsync` model) (031-implementation-roadmap)
- TypeScript 5.7 (frontend), C# 13 / .NET 9.0 (backend — no changes expected) + React 19, Tailwind CSS 3.4, Vite 6.0, React Router 7.0 (032-dashboard-documentation)
- N/A (help content is static, embedded in component code) (032-dashboard-documentation)
- C# 13 / .NET 9.0 (backend), TypeScript 5.7 (dashboard) + ASP.NET Core, Entity Framework Core, Azure.Identity, Azure.ResourceManager.ResourceGraph (new for US8), React 19, Vite 6.0, Tailwind CSS 3.4 (033-boundary-scoped-model)
- SQL Server (production), SQLite (development) via EF Core (033-boundary-scoped-model)
- TypeScript 5.7 / React 19 / C# 13 (.NET 9.0 — backend, no changes expected) + React 19, react-router-dom 7, axios 1.7, react-markdown (new), remark-gfm (new), react-syntax-highlighter (new) (034-dashboard-chat)
- Browser localStorage (conversations); no server-side DB changes (034-dashboard-chat)
- C# 13 / .NET 9.0 (backend), TypeScript 5 / React 19 (dashboard), TypeScript 5 / Node.js (M365 Teams + VS Code extensions) + EF Core 9.0, ASP.NET Core Minimal APIs, Serilog, SignalR, Recharts (frontend), @microsoft/signalr (frontend) (035-deviation-management)
- SQLite (dev) / Azure SQL (prod) via EF Core — existing `AtoCopilotContext` (035-deviation-management)
- C# 9 / .NET 8 (backend), TypeScript / React 18 (dashboard) + EF Core 8 (SQL Server), ASP.NET Minimal APIs, Vite, TailwindCSS (036-risk-solutions)
- SQL Server (Docker, EnsureCreated + EnsureSchemaAdditions pattern) (036-risk-solutions)
- C# 12 / .NET 9.0 (backend), TypeScript 5.x / React (frontend) + QuestPDF 2025.7.0 (PDF), DocumentFormat.OpenXml via ZipArchive (DOCX), ClosedXML 0.104.2 (Excel), System.Text.Json (OSCAL), SignalR (real-time), axios (frontend HTTP) (037-ssp-document-export)
- SQL Server (entity metadata via EF Core 9.0), local filesystem (generated export files and uploaded templates) (037-ssp-document-export)
- C# / .NET 9.0 (backend), TypeScript / React 18+ (frontend) + ASP.NET Core Minimal APIs, Entity Framework Core 9, Axios, React, Tailwind CSS, Heroicons (038-evidence-repository)
- SQL Server (metadata via EF Core), Local Filesystem / Azure Blob Storage (files via abstracted `IFileStorageProvider`) (038-evidence-repository)
- C# / .NET 9.0 (backend); TypeScript 5.7 / React 19 (frontend) + EF Core 9.0 (SqlServer + SQLite), Azure.Identity 1.13.2, Azure.AI.OpenAI 2.1.0, Serilog 4.2.0, QuestPDF 2025.7.0 (backend); React Router 7.0, Axios 1.7, Recharts 2.15, Tailwind CSS 3.4, Vite 6.0 (frontend) (039-poam-management)
- SQL Server (production) / SQLite (dev) via `AtoCopilotContext`; Key Vault for ticketing credentials (039-poam-management)
- C# / .NET 8 + EF Core 8, Azure.ResourceManager, Azure.ResourceManager.ResourceGraph, ASP.NET Core Minimal APIs, React 18 + TypeScript (Vite dashboard) (040-component-centric-boundary)
- SQLite (dev), SQL Server (prod) via EF Core (040-component-centric-boundary)
- C# / .NET 8.0 + ASP.NET Core, Entity Framework Core, ClosedXML, System.Text.Json, System.IO.Compression (ZIP), SignalR, JsonSchema.Net (OSCAL JSON Schema validation), DocumentFormat.OpenXml (SAR Word generation) (041-emass-package)
- SQLite (dev) / PostgreSQL (prod) via EF Core, local filesystem + Azure Blob (IFileStorageProvider) for exports and evidence (041-emass-package)
- TypeScript 5.7 (frontend), C# .NET 8 (backend) + React 19, React Router 7, Vite 6, Tailwind CSS 3, Axios (frontend); EF Core, Serilog (backend) (042-system-intake-wizard)
- SQL Server via Entity Framework Core (existing `AtoCopilotContext`) (042-system-intake-wizard)
- C# 13 / .NET 9.0 (backend), TypeScript 5 / React 19 (frontend) + EF Core 9.0, ASP.NET Core Minimal APIs, ClosedXML 0.104.2 (Excel I/O), Serilog (logging); React 19, Vite 6, Tailwind CSS 3, Axios, React Router 7 (frontend) (043-control-inheritance)
- SQLite (dev) / SQL Server (prod) via EF Core — existing `AtoCopilotContext` (043-control-inheritance)
- C# 13 / .NET 9, TypeScript 5 / React 19 + EF Core 9, ASP.NET Core Minimal APIs, Vite 6, Tailwind CSS 3, Axios (044-org-control-inheritance)
- SQL Server (EF Core migrations, `AtoCopilotContext`) (044-org-control-inheritance)
- C# 13 / .NET 9.0 (backend), TypeScript / React 18 (frontend) + ASP.NET Core, EF Core 9.0.0, Serilog, xUnit, FluentAssertions, Moq, Axios, Tailwind CSS (046-mission-system-details)
- SQLite (dev) / SQL Server (prod) via EF Core dual-provider; `AtoCopilotContext` (046-mission-system-details)
- C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard); TypeScript 5 / Node.js 20 LTS (VS Code + M365 extensions — touched only for MCP-tool description string updates per FR-025) + ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (`Microsoft.EntityFrameworkCore` + `.SqlServer` + `.Sqlite`), `Microsoft.Extensions.Hosting` (`IHostedService` for FR-028 worker), `System.Threading.Channels` (in-process propagation intent queue), `System.Diagnostics.Metrics` (`Meter` + `Counter<long>` + `Histogram<double>` per Feature 029 patterns), `Microsoft.Identity.Web` 3.5+, Serilog 4.2 (structured logs per FR-028), xUnit 2.9.3 + FluentAssertions 7.0 + Moq 4.20 (tests); React 19, React Router 7, Axios 1.7, Tailwind CSS 3, Vite 6 (dashboard) (049-unified-rmf-role-assignments)
- EF Core dual-provider — SQLite (dev) / SQL Server (prod) via the existing `AtoCopilotContext`. **No new tables. No new migrations.** Three new values appended to the existing `OrganizationRole` enum (`MissionOwner`, `AuthorizingOfficial`, `SystemOwner`); the column is stored as `nvarchar` (string-serialized enum), so no schema column change is required. (049-unified-rmf-role-assignments)
- C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard) + ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (050-csp-capability-lifecycle)
- EF Core dual-provider — SQLite (dev) / SQL Server (prod) via the (050-csp-capability-lifecycle)
- C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard); TypeScript 5 / Node 20 LTS (VS Code + M365 extensions) + ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (SqlServer + Sqlite), Microsoft.Identity.Web 3.5+, Microsoft.Extensions.Caching.StackExchangeRedis 9.0 (NEW), Serilog 4.2; @azure/msal-browser 3.x + @azure/msal-react 2.x (NEW — dashboard); @azure/msal-node 2.x (NEW — VS Code); botbuilder-dialogs (M365) (051-login)
- SQLite (dev) / SQL Server (prod) via EF Core (`AtoCopilotContext`); one new table (`LoginAuditEvents`) shipped via `EnsureSchemaAdditions` module; Azure Storage append-blob (prod) + local filesystem (dev) for cold archive; Redis (prod) / in-memory (dev) for throttle counter; HMAC-signed first-party cookie for "remember tenant" (no server mirror) (051-login)

- C# 13 / .NET 9.0 + Azure.Identity 1.13, Azure.ResourceManager 1.13, Microsoft.Extensions.AI 9.4-preview, Microsoft.EntityFrameworkCore 9.0, Serilog 4.2, xUnit 2.9, FluentAssertions 7.0, Moq 4.20 (001-core-compliance)

## Project Structure

```text
src/
  Ato.Copilot.Core/         # Domain models, EF Core context, interfaces
  Ato.Copilot.Agents/       # AI agents + 130+ tool implementations (BaseAgent / BaseTool)
  Ato.Copilot.Mcp/          # MCP server (stdio + HTTP + SSE)
  Ato.Copilot.Chat/         # Web chat (ASP.NET Core + React SPA)
  Ato.Copilot.Dashboard/    # React dashboard (Vite + TS)
  Ato.Copilot.Channels/     # Multi-channel routing library
  Ato.Copilot.State/        # In-memory state management
  Ato.Copilot.Cli/          # ato-cli (System.CommandLine)
extensions/
  vscode/                   # VS Code extension (TypeScript)
  m365/                     # Teams bot (TypeScript, Adaptive Cards)
tests/
  Ato.Copilot.Tests.Unit/         # xUnit + FluentAssertions + Moq
  Ato.Copilot.Tests.Integration/  # WebApplicationFactory
  Ato.Copilot.Tests.Manual/       # Documented manual scenarios
specs/                      # Feature specs (NNN-feature-name)
docs/                       # MkDocs Material documentation
.specify/                   # Spec-kit templates, scripts, memory
```

## Commands

```bash
# .NET
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln

# TypeScript type-checking parity (per Constitution § Local Type-Checking Parity)
cd extensions/vscode              && npm ci && npm run compile
cd extensions/m365                && npm ci && npm run build
cd src/Ato.Copilot.Dashboard      && npm ci && npm run typecheck

# Full stack (MCP + Web Chat + SQL Server)
docker compose -f docker-compose.mcp.yml up --build
```

## Code Style

- **C# 13 / .NET 9**: Standard .NET conventions, nullable reference types enabled, implicit usings
- **TypeScript**: strict mode enabled in all extensions and the dashboard
- **TDD required (NON-NEGOTIABLE)**: Red-Green-Refactor; failing test before production code (Constitution §VI)
- **AAA pattern**: All tests use `// Arrange`, `// Act`, `// Assert` comment markers
- **Coverage**: 80% minimum on modified paths; CI enforces
- **Containers**: No `:latest` tags in production; immutable, tagged images only
- **Secrets**: Never in source, `appsettings.*.json`, or images — Azure Key Vault or env only
- **Logging**: Structured Serilog; redact sensitive tool parameters; no PII/CUI in logs

## Recent Changes
- 051-login: Added C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard); TypeScript 5 / Node 20 LTS (VS Code + M365 extensions) + ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (SqlServer + Sqlite), Microsoft.Identity.Web 3.5+, Microsoft.Extensions.Caching.StackExchangeRedis 9.0 (NEW), Serilog 4.2; @azure/msal-browser 3.x + @azure/msal-react 2.x (NEW — dashboard); @azure/msal-node 2.x (NEW — VS Code); botbuilder-dialogs (M365)
- 050-csp-capability-lifecycle: Added C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard) + ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0
- 049-unified-rmf-role-assignments: Added C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard); TypeScript 5 / Node.js 20 LTS (VS Code + M365 extensions — touched only for MCP-tool description string updates per FR-025) + ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (`Microsoft.EntityFrameworkCore` + `.SqlServer` + `.Sqlite`), `Microsoft.Extensions.Hosting` (`IHostedService` for FR-028 worker), `System.Threading.Channels` (in-process propagation intent queue), `System.Diagnostics.Metrics` (`Meter` + `Counter<long>` + `Histogram<double>` per Feature 029 patterns), `Microsoft.Identity.Web` 3.5+, Serilog 4.2 (structured logs per FR-028), xUnit 2.9.3 + FluentAssertions 7.0 + Moq 4.20 (tests); React 19, React Router 7, Axios 1.7, Tailwind CSS 3, Vite 6 (dashboard)


<!-- MANUAL ADDITIONS START -->

## Engineering Principles

### Holistic Thinking
Before writing any code, trace the impact through the full stack:

- backend (`src/Ato.Copilot.{Core,Agents,Mcp,Chat,Channels,State}`) → database
  (`AtoCopilotContext` / `ChatDbContext`) → MCP tool surface → frontend
  (Dashboard, Web Chat, VS Code extension, M365 Teams bot) → tests → docs → specs
- Verify contract alignment against the **current** implementation, not historical
  assumptions. The Active Technologies section above can lag reality — read the code.
- Update specs (`specs/NNN-*/`) and docs (`docs/`) whenever architecture or contract
  reality changes.
- For any tool change, ask: does this affect the MCP envelope schema (Constitution
  § UX Standards)? Does it affect tenant isolation (Feature 048)?

### Senior Developer Mindset

- Act like a senior developer / tech lead, not a checklist executor.
- Audit every diff as if you were the reviewer who has to defend it at an ATO
  audit.
- Anticipate edge cases and contract mismatches before they land.
- Flag design tension and drift explicitly — don't hide it under `TODO` comments.

### Root Cause Only

- Fix root causes, not symptoms.
- If a restart hides the issue, the issue is not understood yet.
- No silent suppression: no `|| true` on critical paths, no empty catches, no
  `#pragma warning disable` without a justification comment (Constitution § Code
  Quality Standards).

### Document Before Fix

- Create or update the paper trail before implementing the change.
- Keep specs (`specs/NNN-*/spec.md`, `plan.md`, `tasks.md`), docs (`docs/`), and
  GitHub issues synchronized with actual architecture.
- Per Constitution § DevOps GitHub Issue Discipline (NON-NEGOTIABLE): every Feature
  and User Story in spec-kit MUST have a corresponding GitHub issue with proper
  parent linkage.

## Spec-Kit Workflow

This repo uses [Spec-Kit](https://github.com/sstjean/spec-kit). Workflow:

1. **Specify**: `specs/NNN-feature-name/spec.md` — what + why (no implementation)
2. **Plan**: `specs/NNN-feature-name/plan.md` — tech stack, file layout, Constitution
   Check, Complexity Tracking
3. **Tasks**: `specs/NNN-feature-name/tasks.md` — dependency-ordered, parallel-marked
4. **Implement**: TDD cycle per task (failing test → green → refactor)

After planning a new feature, **always** run:

```bash
.specify/scripts/bash/update-agent-context.sh copilot
```

This regenerates the auto-generated portions above. Manual additions in this block
survive regeneration.

## Constitution Compliance

Every change MUST pass the Constitution Check gate
([.specify/memory/constitution.md](../.specify/memory/constitution.md), v2.0.0).
Key non-negotiables to remember while coding:

| Principle | Quick check |
|---|---|
| §VI TDD | Did you write the failing test first? Is AAA marked? |
| §V BaseAgent/BaseTool | Does the new agent extend `BaseAgent`? New tool extend `BaseTool`? |
| § Security: Zero-Trust | Is the request authenticated AND authorized server-side? |
| § Security: Tenant Isolation | Does the query filter by tenant on every path? |
| § Local Type-Checking Parity | Did `tsc --noEmit` run on every TS project you touched? |
| § DevOps: GitHub Issue Discipline | Is the Feature → User Story sub-issue linkage intact? |
| § Complexity Justification | If you deviated from §II Simplicity / §III YAGNI, did you fill the Complexity Tracking table in `plan.md`? |

## Session Procedures

When the user says **"Start up"** or **"Shutdown"**, follow the procedures in
[`.specify/memory/session-procedures.md`](../.specify/memory/session-procedures.md).
Read that file immediately and execute the steps.

## Editing Discipline

- **Never edit `.github/copilot-instructions.md` between the auto-generated
  section markers** unless explicitly fixing a generation bug. The script will
  rewrite Recent Changes and Last Updated on every run.
- **Always edit between `<!-- MANUAL ADDITIONS START -->` and
  `<!-- MANUAL ADDITIONS END -->` for free-form notes** — that block is
  preserved.
- **`AGENTS.md` is the cross-tool source.** When adding rules that should reach
  Cursor, Claude Code, Codex CLI, etc., update `AGENTS.md` first and let the
  spec-kit script propagate.

<!-- MANUAL ADDITIONS END -->
