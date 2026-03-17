# Implementation Plan: Dashboard Chat Side Panel

**Branch**: `034-dashboard-chat` | **Date**: 2026-03-16 | **Spec**: `specs/034-dashboard-chat/spec.md`
**Input**: Feature specification from `/specs/034-dashboard-chat/spec.md`

## Summary

Add an AI chat side panel to the React dashboard that connects to the existing MCP Server's SSE streaming endpoint (`POST /mcp/chat/stream`). The panel overlays the dashboard content, supports multi-turn conversations persisted in local storage, renders Markdown responses with tool execution evidence, includes dashboard context (page, system, entity) in every request, and supports file attachments for CKL/XCCDF/Prisma/Nessus imports. Authentication uses Azure AD / Entra ID in production with dev bypass.

## Technical Context

**Language/Version**: TypeScript 5.7 / React 19 / C# 13 (.NET 9.0 — backend, no changes expected)
**Primary Dependencies**: React 19, react-router-dom 7, axios 1.7, react-markdown (new), remark-gfm (new), react-syntax-highlighter (new)
**Storage**: Browser localStorage (conversations); no server-side DB changes
**Testing**: Vitest 3.0 + @testing-library/react 16 (frontend); existing xUnit/Moq for backend
**Target Platform**: Modern browsers (Chrome, Edge, Firefox — latest 2 versions); responsive ≥768px full-width
**Project Type**: Web application (React SPA dashboard) — frontend-only feature
**Performance Goals**: First token <5s (SC-001); panel open <300ms (SC-002); 50 conversations in localStorage without degradation (SC-004)
**Constraints**: Overlay mode (no layout reflow); 320–600px panel width; 20-message history depth per request; 10 MB max file attachment
**Scale/Scope**: Single-page panel added to existing dashboard; ~15 new React components; ~5 new service modules; ~4 new hooks

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | PASS | Feature spec and plan follow `/docs/` conventions; no conflicting guidance |
| II | BaseAgent/BaseTool Architecture | N/A | No backend agent/tool changes — this is a frontend-only feature consuming existing endpoints |
| III | Testing Standards | PASS | Vitest unit tests for all components/hooks/services; @testing-library for integration; coverage target 80%+ |
| IV | Azure Government & Compliance | PASS | Bearer token auth with dev bypass; no hardcoded credentials; MSAL deferred to separate cross-cutting feature |
| V | Observability & Structured Logging | PASS | Console logging for SSE events, errors, retry attempts; no backend logging changes |
| VI | Code Quality & Maintainability | PASS | Single-responsibility components; DI via React context; TypeScript strict mode; no magic values |
| VII | User Experience Consistency | PASS | MCP response envelope (success, data, metadata) honored; actionable error messages with retry; progress feedback on streaming |
| VIII | Performance Requirements | PASS | First-token <5s; panel open <300ms; cancellation via AbortController; bounded history (20 messages) |

**Gate Result**: PASS — No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/034-dashboard-chat/
├── plan.md              # This file
├── research.md          # Phase 0: Technology decisions
├── data-model.md        # Phase 1: TypeScript interfaces and state model
├── quickstart.md        # Phase 1: Development setup guide
├── contracts/           # Phase 1: SSE event contract, ChatContext schema
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/Ato.Copilot.Dashboard/src/
├── components/
│   ├── chat/                    # NEW — All chat panel components
│   │   ├── ChatPanel.tsx        # Root panel container (overlay, resize, toggle)
│   │   ├── ChatHeader.tsx       # Panel header (title, new conversation, close)
│   │   ├── ChatMessages.tsx     # Message list with auto-scroll
│   │   ├── ChatInput.tsx        # Text input, file attachment, send button
│   │   ├── ChatBubble.tsx       # Single message bubble (user or assistant)
│   │   ├── MarkdownRenderer.tsx # react-markdown with syntax highlighting
│   │   ├── ToolEvidence.tsx     # Collapsible tool execution details
│   │   ├── SuggestionCards.tsx  # Clickable follow-up suggestion chips
│   │   ├── ProgressSteps.tsx    # Streaming progress indicators
│   │   ├── ConversationList.tsx # Conversation selector dropdown
│   │   ├── WelcomeMessage.tsx   # First-open welcome with example questions
│   │   ├── FileAttachment.tsx   # Drag-and-drop / file picker for imports
│   │   ├── ErrorMessage.tsx     # Error display with retry button
│   │   └── ChatToggle.tsx       # Header icon button to open/close panel
│   └── layout/
│       └── PageLayout.tsx       # MODIFIED — inject ChatPanel as sibling overlay
├── hooks/
│   ├── useChat.ts               # NEW — Chat state management hook
│   ├── useSseStream.ts          # NEW — SSE streaming hook with AbortController
│   ├── useChatContext.ts        # NEW — Dashboard context extraction hook
│   └── useLocalStorage.ts       # NEW — Typed localStorage hook (conversations, panel state)
├── services/
│   └── chatService.ts           # NEW — SSE client, message sending, file upload
├── types/
│   └── chat.ts                  # NEW — Chat TypeScript interfaces
└── api/
    └── client.ts                # MODIFIED — Add MCP base URL configuration

tests/ (src/Ato.Copilot.Dashboard/)
├── components/chat/             # NEW — Component unit tests
├── hooks/                       # NEW — Hook unit tests
└── services/                    # NEW — Service unit tests
```

**Structure Decision**: Frontend-only feature. All new code lives under `src/Ato.Copilot.Dashboard/src/`. Chat components are grouped in `components/chat/` to avoid polluting the existing component tree. The SSE client is a standalone service (`chatService.ts`) that can be tested independently. No backend changes required — the existing MCP Server endpoints are sufficient.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
