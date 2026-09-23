# Implementation Plan: Security Posture Intelligence Navigator Chat Application

**Branch**: `006-chat-app` | **Date**: 2026-02-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-chat-app/spec.md`

## Summary

Build a full-stack conversational chat application that serves as the primary user interface for the Security Posture Intelligence Navigator. The backend is an ASP.NET Core 9.0 host providing REST APIs for CRUD operations and a SignalR hub for real-time bidirectional messaging. The frontend is a React 18 + TypeScript SPA styled with Tailwind CSS. The chat app is a thin client: it persists conversations and messages in its own database, builds conversation history, and forwards user messages to the existing MCP Server at `/mcp/chat` for AI processing. Rich metadata (intent classification, tool results, multi-step progress, proactive suggestions) is rendered in the UI.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript 4.9 / React 18.2 (frontend)
**Primary Dependencies**: ASP.NET Core SignalR, EF Core 9.0, Serilog, SPA Services (backend); React, react-markdown, @microsoft/signalr, Tailwind CSS, Axios (frontend)
**Storage**: SQLite (local dev) / SQL Server (Docker/production) — dual-provider, auto-detected from connection string
**Testing**: xUnit + FluentAssertions + Moq (backend unit), WebApplicationFactory (integration); existing test infrastructure in Tests.Unit and Tests.Integration
**Target Platform**: Linux containers (Docker), Windows/macOS development; browser (Chrome, Edge, Firefox)
**Project Type**: Web application (full-stack SPA + API)
**Performance Goals**: Message response <30s typical, SignalR connection <3s, processing indicator <1s, search <2s, startup <10s
**Constraints**: 10 MB file upload limit, 180s AI processing timeout, 20-message context window, client-side debounce (no server-side rate limiting)
**Scale/Scope**: Single-user-per-session model, up to 1,000 conversations per user, optimistic UI updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | PASS | Plan creates docs artifacts; `/docs/chat-app.md` will be created during implementation per constitution gate |
| II | BaseAgent/BaseTool Architecture | N/A | Chat app does NOT contain agents or tools — it is a thin client forwarding to the MCP Server. No agent/tool code is introduced. |
| III | Testing Standards | PASS | Unit tests for ChatService, ChatHub, controllers; integration tests for API endpoints and SignalR; boundary tests for pagination, file size limits, empty inputs |
| IV | Azure Government & Compliance First | PASS | Azure Key Vault with managed identity for secrets, `DefaultAzureCredential`, no hardcoded credentials, environment variable overrides |
| V | Observability & Structured Logging | PASS | Serilog with console + file sinks (dev), Application Insights sink (production), structured request logging, connection lifecycle logging, message operation logging |
| VI | Code Quality & Maintainability | PASS | Constructor DI, named constants, XML docs on public types, single-responsibility services, warnings-as-errors, `Ato.Copilot.*` naming conventions |
| VII | User Experience Consistency | PASS | Categorized user-friendly error messages (not raw stack traces), connection status indicators, progress feedback within 1s, keyboard accessibility, ARIA labels |
| VIII | Performance Requirements | PASS | Health endpoint <200ms, startup <10s, pagination on all collections (default 50), `CancellationToken` on async operations |

**Gate Result**: PASS — no violations. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

\`\`\`text
specs/006-chat-app/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (REST API + SignalR contracts)
└── tasks.md             # Phase 2 output (/speckit.tasks command)
\`\`\`

### Source Code (repository root)

\`\`\`text
src/
├── Ato.Copilot.Chat/                 # NEW — ASP.NET Core 9.0 Web host
│   ├── Ato.Copilot.Chat.csproj       # Web SDK, SPA proxy, NuGet packages
│   ├── Program.cs                    # Application bootstrap, DI, middleware
│   ├── appsettings.json              # Configuration (connections, MCP URL, Serilog)
│   ├── Dockerfile                    # Multi-stage build (Node.js + .NET)
│   ├── Controllers/
│   │   └── ChatControllers.cs        # ConversationsController + MessagesController
│   ├── Data/
│   │   └── ChatDbContext.cs          # EF Core context with Fluent API config
│   ├── Hubs/
│   │   └── ChatHub.cs                # SignalR hub for real-time messaging
│   ├── Models/
│   │   └── ChatModels.cs             # Entities, DTOs, enums, metadata types
│   ├── Services/
│   │   ├── IChatService.cs           # Service interface
│   │   └── ChatService.cs            # Chat service with MCP integration
│   └── ClientApp/                    # React 18 + TypeScript SPA
│       ├── package.json
│       ├── tsconfig.json
│       ├── tailwind.config.js
│       ├── postcss.config.js
│       ├── public/
│       │   └── index.html
│       └── src/
│           ├── App.tsx               # Root component with ChatProvider
│           ├── index.tsx             # React entry point
│           ├── types/
│           │   └── chat.ts           # TypeScript interfaces/enums
│           ├── services/
│           │   └── chatApi.ts        # Axios-based REST API client
│           ├── contexts/
│           │   └── ChatContext.tsx    # React Context + useReducer + SignalR
│           ├── components/
│           │   ├── Header.tsx        # App header with settings modal
│           │   ├── ConversationList.tsx  # Sidebar conversation list
│           │   └── ChatWindow.tsx    # Main chat interface with rich metadata
│           └── styles/
│               └── App.css           # Custom animations and scrollbar styles
│
├── Ato.Copilot.Core/                 # EXISTING — shared models, interfaces, DB context
├── Ato.Copilot.Agents/               # EXISTING — agent framework (not modified)
├── Ato.Copilot.State/                # EXISTING — state management (not modified)
└── Ato.Copilot.Mcp/                  # EXISTING — MCP Server (target of /mcp/chat calls)

tests/
├── Ato.Copilot.Tests.Unit/           # EXISTING — add ChatService, ChatHub, controller tests
└── Ato.Copilot.Tests.Integration/    # EXISTING — add ChatController integration tests
\`\`\`

**Structure Decision**: The Chat application is a new `Ato.Copilot.Chat` project under `src/`, following the established `Ato.Copilot.*` naming convention. It references Core, Agents, and State (matching the MCP project pattern). The frontend lives inside `ClientApp/` as a co-located SPA served by the .NET host. No separate "Channels" project is created — the spec's reference to `Platform.Engineering.Copilot.Channels` is normalized to direct project references matching the existing naming convention. The SignalR hub and chat service are self-contained within the Chat project.

## Complexity Tracking

> No violations detected — table not required.

## Constitution Re-evaluation (Post-Phase 1 Design)

*Re-check after data model, contracts, and quickstart are finalized.*

| # | Principle | Status | Post-Design Notes |
|---|-----------|--------|-------------------|
| I | Documentation as Source of Truth | PASS | data-model.md, contracts/rest-api.md, contracts/signalr.md, quickstart.md all created. `/docs/chat-app.md` to be written during implementation. |
| II | BaseAgent/BaseTool Architecture | N/A | Confirmed: no agent or tool code in chat project. Chat forwards to MCP Server via HTTP. |
| III | Testing Standards | PASS | Contracts define testable surface: 7 REST endpoints + 4 hub methods. Each needs positive + negative tests. Boundary tests specified for file size (10 MB), message length (10,000 chars), pagination (skip/take). |
| IV | Azure Government & Compliance First | PASS | No new Azure service integrations; chat project inherits credential chain from Core. Connection strings via configuration, not hardcoded. |
| V | Observability & Structured Logging | PASS | Contracts include metadata fields (processingTimeMs, intentType, toolName) for structured logging. Hub lifecycle events logged. Application Insights sink configured for production via Serilog.Sinks.ApplicationInsights. |
| VI | Code Quality & Maintainability | PASS | Data model: 4 entities with clear SRP. Contracts: separate REST and SignalR specs. Hub uses IServiceScopeFactory (not service locator). XmlDoc required on all public types. |
| VII | User Experience Consistency | PASS | Error categories defined in both REST and SignalR contracts. MessageError event includes human-readable message + category. No stack traces exposed. Progress feedback via MessageProcessing event. |
| VIII | Performance Requirements | PASS | Health endpoint <200ms documented. Pagination on all collection endpoints (default 50). CancellationToken on all async operations. SignalR reconnection with graduated backoff. |

**Post-Design Gate Result**: PASS — all principles satisfied. Design is constitution-compliant.
