# Package Boundary Map — Issue #2683

## TypeScript Starter Lib vs Per-Project Boundaries

**Date:** 2026-08-17
**Author:** F.R.I.D.A.Y. (codebase inspection)
**Branch:** 2437-traceability-panel
**Status:** Draft — for architecture review

---

## 1. Current State: Four Isolated TypeScript Projects

The repo ships four independent TypeScript projects. There is **no monorepo workspace** and **no shared package**. Every project compiles independently.

```
spin_agent/
├── src/Ato.Copilot.Chat/ClientApp/     # React 18 SPA, CRA (react-scripts), target: es5
├── src/Ato.Copilot.Dashboard/          # React 19 SPA, Vite, target: ES2022
├── extensions/vscode/                  # VS Code extension, tsc/commonjs, target: ES2022
└── extensions/m365/                    # Teams bot (Express), tsc/commonjs, target: ES2022
```

### Build toolchain matrix (verified from tsconfig + package.json)

| Project | Bundler | Module format | Target | package name |
|---|---|---|---|---|
| Chat ClientApp | CRA (react-scripts) | ESNext | es5 | clientapp |
| Dashboard | Vite | ESNext (bundler) | ES2022 | ato-copilot-dashboard |
| VS Code ext | tsc | CommonJS | ES2022 | ato-copilot-vscode |
| M365 Teams bot | tsc | CommonJS | ES2022 | ato-copilot-m365 |

---

## 2. Cross-Project Import Graph

**Finding: zero cross-project imports.** All four projects are fully self-contained.

```
extensions/vscode/src  ──►  (no imports from other TS projects)
extensions/m365/src    ──►  (no imports from other TS projects)
Chat/ClientApp/src     ──►  (no imports from other TS projects)
Dashboard/src          ──►  (no imports from other TS projects)
```

Each project imports only from its own `src/`, its `node_modules/`, or standard Node/DOM APIs.

---

## 3. Duplicated Code — Verified Evidence

### 3a. SSE Client (highest duplication, most complex)

**Three independent SSE client implementations** with identical parsing logic and retry strategy:

| File | Lines (approx) | Differences |
|---|---|---|
| `extensions/vscode/src/services/sseClient.ts` | ~200 | `SseEvent.event: string`, `data: string`; fallback to sync `/mcp/chat`; AbortController |
| `extensions/m365/src/services/sseClient.ts` | ~220 | `SseEvent.type: string`, `data: Record<string,unknown>`; wraps `McpResponse`; `baseUrl` in constructor |
| `src/Ato.Copilot.Dashboard/src/services/chatService.ts` | inline | Comment says "ported from extensions/vscode/src/services/sseClient.ts" |

All three share: `parseSseChunk`, exponential-backoff retry loop, `ReadableStream` reader, `TextDecoder`, `\n\n`-separated block splitting.

**Root divergence:** the VS Code client uses `event: string` (raw field name); M365 client uses `type: string` (renamed). This is a naming inconsistency that a shared lib would enforce away.

### 3b. MCP API response types

**Three independent `ToolExecution` / `ErrorDetail` / `McpResponse` type definitions:**

| File | Type name |
|---|---|
| `extensions/vscode/src/services/mcpClient.ts` | `ToolExecution`, `ErrorDetail`, `McpChatResponse` |
| `extensions/m365/src/services/atoApiClient.ts` | `ToolExecution`, `ErrorDetail`, `McpResponse` |
| `src/Ato.Copilot.Chat/ClientApp/src/types/chat.ts` | `ToolExecutionResult` (fields differ) |
| `src/Ato.Copilot.Dashboard/src/types/chat.ts` | `ToolExecution` (matches M365 shape) |

The `ToolExecution` type is structurally identical in M365 and Dashboard (`toolName`, `success`, `executionTimeMs`). VS Code adds `resultSummary`. Chat ClientApp uses a different shape (`result`, `parameters`, `executedAt`, `duration`).

### 3c. Chat conversation/message types

**Two divergent `Conversation` + `Message` type systems:**

| Project | Type style | MessageRole | MessageStatus |
|---|---|---|---|
| Chat ClientApp (`types/chat.ts`) | C#-mirroring enums (`MessageRole.User`, `MessageStatus.Sent`) | enum | enum |
| Dashboard (`types/chat.ts`) | union literals (`'user'`, `'assistant'`, `'sending'`, `'streaming'`) | type union | type union |

These represent the **same backend model** (`ChatModels.cs`) but in incompatible TypeScript styles. A shared lib would normalise this to one representation.

### 3d. Axios HTTP client setup

**Two axios wrapper instances with identical interceptor patterns:**

| File | Auth source | Base URL env var |
|---|---|---|
| `src/Ato.Copilot.Chat/ClientApp/src/services/chatApi.ts` | `REACT_APP_API_BASE_URL` (CRA) | local token |
| `src/Ato.Copilot.Dashboard/src/api/client.ts` | `VITE_API_BASE_URL` (Vite), MSAL interceptor | MSAL bearer |

Both wrap axios with error-envelope normalisation. The Dashboard version is more complete (MSAL silent-renewal, 401-retry, simulated-role header).

### 3e. Test utilities

**mocha + chai + sinon** installed independently in both `extensions/vscode` and `extensions/m365` at identical versions (`mocha ^10.2.0`, `chai ^4.3.10`, `sinon ^17.0.1`).

---

## 4. Dependency Overlap Map

```
Package          | Chat | Dashboard | VSCode | M365
-----------------|------|-----------|--------|-----
axios            |  ✓   |     ✓     |        |  ✓
@microsoft/signalr|  ✓  |     ✓     |        |
react            |  ✓   |     ✓     |        |
react-markdown   |  ✓   |     ✓     |        |
mocha            |      |           |  ✓     |  ✓
chai             |      |           |  ✓     |  ✓
sinon            |      |           |  ✓     |  ✓
typescript       |  ✓   |     ✓     |  ✓     |  ✓
```

---

## 5. Recommended Package Separation Strategy

### Option A: Minimal shared lib — `packages/ato-shared` (Recommended)

Create a single `packages/ato-shared` package in a **pnpm workspace** monorepo. Move only the highest-ROI duplicates.

**Proposed contents of `@ato-copilot/shared`:**

```
packages/ato-shared/
├── src/
│   ├── sse/
│   │   ├── SseClient.ts          # Unified SSE client (single implementation)
│   │   ├── parseSseChunk.ts      # Extracted pure parser function
│   │   └── types.ts              # SseEvent (canonical field names)
│   ├── mcp/
│   │   ├── types.ts              # ToolExecution, ErrorDetail, McpResponse (canonical)
│   │   └── conversationId.ts    # generateConversationId() (dedup M365 + VSCode logic)
│   └── index.ts
├── package.json                  # name: "@ato-copilot/shared", private: true
└── tsconfig.json                 # target: ES2022, module: ESNext + commonjs via build
```

**Migration cost:** Low. Each consuming project replaces ~3 import paths. No runtime behaviour change.

**Risk:** The SSE client field-name divergence (`event` vs `type`) must be resolved — pick one canonical name and update all event handlers in all four projects. This is a one-time breaking rename, not ongoing work.

### Option B: Monorepo with feature packages

Split by domain: `@ato-copilot/shared`, `@ato-copilot/chat-ui`, `@ato-copilot/auth`. Higher effort, higher long-term benefit. Appropriate after Option A lands and the team has monorepo tooling familiarity.

### Option C: Status quo

Keep four isolated projects. Cheapest now, most expensive over time as divergence compounds. The SSE field-name inconsistency already exists — it will spread to more event handlers.

**Recommendation: Option A.** Delivers 80% of the value at 20% of the cost. The SSE client deduplication alone eliminates the three-way divergence and the naming inconsistency bug.

---

## 6. Build Tooling Gap: CRA End-of-Life

`src/Ato.Copilot.Chat/ClientApp` uses `react-scripts` (Create React App), which is **end-of-life** since April 2023 and has open CVEs in its build toolchain.

**Recommendation:** Migrate Chat ClientApp to Vite (matching the Dashboard). This is independent of the shared-lib work but unblocks upgrading to React 19 (Dashboard already uses it; Chat is on 18).

**Effort estimate:** Medium (1–2 days). The component tree is small (9 source files + 6 tests).

---

## 7. Implementation Order

1. **Resolve SSE field-name divergence** — decide `event` vs `type` and update all handlers (1 day, zero shared-lib tooling needed)
2. **Bootstrap pnpm workspace** — add `pnpm-workspace.yaml`, update `bootstrap.sh`, update `.devcontainer` (0.5 days)
3. **Extract `@ato-copilot/shared`** — move SSE client + MCP types, wire up consumers (1 day)
4. **Migrate Chat ClientApp to Vite** — optional, decoupled (1–2 days)

Total for steps 1–3: ~2.5 days of focused effort.

---

## Appendix: File Evidence References

- SSE duplication: `extensions/vscode/src/services/sseClient.ts`, `extensions/m365/src/services/sseClient.ts`, `src/Ato.Copilot.Dashboard/src/services/chatService.ts` (comment: "ported from extensions/vscode/src/services/sseClient.ts")
- MCP types: `extensions/vscode/src/services/mcpClient.ts:1-60`, `extensions/m365/src/services/atoApiClient.ts:1-60`
- Chat types divergence: `src/Ato.Copilot.Chat/ClientApp/src/types/chat.ts`, `src/Ato.Copilot.Dashboard/src/types/chat.ts`
- Axios duplication: `src/Ato.Copilot.Chat/ClientApp/src/services/chatApi.ts`, `src/Ato.Copilot.Dashboard/src/api/client.ts`
- tsconfig per project: all four `tsconfig.json` files (verified above)
- package.json per project: all four `package.json` files (verified above)
