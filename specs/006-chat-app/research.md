# Research: Security Posture Intelligence Navigator Chat Application

**Feature**: 006-chat-app | **Date**: 2026-02-23

## Topic 1: ASP.NET Core 9.0 SignalR Hub — Architecture & DI

### Hub Structure Alongside REST Controllers

- **Decision**: Thin `ChatHub : Hub` class alongside REST controllers. Hub handles real-time events only (MessageProcessing, MessageReceived, MessageError, TypingNotification). REST controllers handle CRUD (conversation create/list/search/delete, message list/create, file upload).
- **Rationale**: SignalR Hubs are transient — new instance per invocation. They should delegate to injected services, matching the existing pattern where `McpHttpBridge` delegates to `McpServer`. REST endpoints are better suited for request/response CRUD with pagination.
- **Alternatives considered**: Hub-only architecture (rejected — not designed for CRUD), Minimal API only (viable but controllers better organize 6+ endpoints).

### Hub Dependency Injection

- **Decision**: Register `IChatService` as Scoped. Inject `IServiceScopeFactory` into Hub constructor. Create scope per Hub method call.
- **Rationale**: Hubs are transient and cannot directly consume scoped services like `DbContext`. `IServiceScopeFactory` is the official Microsoft recommendation. Matches the existing pattern where `AtoCopilotContext` is registered as Scoped via `AddDbContext` in `CoreServiceExtensions.cs`.
- **Alternatives considered**: Inject IChatService directly as transient (unsafe if it depends on scoped DbContext), register everything as Singleton (defeats EF Core per-request tracking).

### Group-Based Messaging

- **Decision**: Use SignalR `Groups.AddToGroupAsync` keyed by `conversationId`. Clients call `JoinConversation(conversationId)` on selection; broadcasts use `Clients.Group(conversationId)`.
- **Rationale**: Spec FR-015 requires conversation-scoped groups. Built-in group management is in-memory (sufficient for single-server), auto-cleans on disconnect, and scales with Redis backplane if needed later.
- **Alternatives considered**: User-based groups (too coarse — user may have multiple tabs), manual ConnectionId tracking (over-engineers built-in functionality).

## Topic 2: React 18 + @microsoft/signalr Integration

### Connection Management

- **Decision**: `ChatContext.tsx` with `useRef` for `HubConnection` instance, `useReducer` for state, `React.createContext` for distribution. Initialize connection in `useEffect` with cleanup.
- **Rationale**: `useRef` holds the connection without triggering re-renders. `useReducer` provides predictable state transitions. Context makes connection available to all components without prop drilling.
- **Alternatives considered**: Global singleton module (harder to test), Zustand/Redux (unnecessary dependency for ~6 actions).

### Reconnection

- **Decision**: `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])` — immediate retry, then 2s, 5s, 10s, 30s. Register `onreconnecting`, `onreconnected`, `onclose` handlers.
- **Rationale**: Meets spec SC-002 (<10s reconnection). Graduated backoff prevents server hammering. After all retries exhaust, `onclose` fires and UI shows disconnected state.
- **Alternatives considered**: Manual reconnection loop (duplicates built-in functionality), no reconnection (poor UX, violates FR-016).

### Stale Closure Avoidance

- **Decision**: Store dispatch in `useRef`, update on every render. SignalR `.on()` handlers read from ref instead of closing over state.
- **Rationale**: `.on()` handlers are registered once and persist for connection lifetime. Closing over React state captures initial values. `useRef` ensures handlers always access latest dispatch.
- **Alternatives considered**: Re-register handlers on every render (duplicate invocations, memory leaks), `off()`+`on()` in useEffect deps (race-condition-prone).

### Optimistic UI Updates

- **Decision**: Immediately dispatch `ADD_MESSAGE` with `status: 'Sending'` on send. Server confirms via `MessageReceived` event → update to `Completed`. On error → update to `Error`.
- **Rationale**: Spec SC-003 requires processing indicator within 1s. Client-generated GUID enables matching optimistic entry to server confirmation.
- **Alternatives considered**: Wait for server confirmation (violates <1s requirement), separate optimistic/confirmed lists (over-complicated).

## Topic 3: EF Core Dual-Provider (SQLite + SQL Server)

### Separate ChatDbContext

- **Decision**: Create new `ChatDbContext` in `Ato.Copilot.Chat` with its own dual-provider registration. Use `Database:Provider` config key (matching existing pattern) with connection string auto-detection fallback.
- **Rationale**: Chat app has its own data (conversations, messages, attachments). Sharing `AtoCopilotContext` would couple chat schema to compliance schema migrations. Spec FR-032 requires auto-detection; FR-033 requires `EnsureCreatedAsync` (not migrations), which is appropriate for V1.
- **Alternatives considered**: Extend AtoCopilotContext (violates single-responsibility, couples deployments), separate database file/server (supported by this approach via override connection string).
- **Existing pattern**: `CoreServiceExtensions.cs` lines 62-90 — `Database:Provider` switch with `UseSqlServer()` / `UseSqlite()`.

### JSON Column Storage

- **Decision**: `HasConversion` with `ValueConverter<T, string>` using `System.Text.Json`. Exact match of existing pattern in `AtoCopilotContext.cs`.
- **Rationale**: EF Core 9.0 native `ToJson()` only works with SQL Server/PostgreSQL, NOT SQLite. `HasConversion` works identically across both providers. The existing codebase uses this pattern for 8+ properties (List<string>, complex types).
- **Alternatives considered**: `ToJson()` owned entities (SQLite-incompatible), separate metadata tables (query complexity), `JsonDocument` (immutable, requires disposal).

**Converter patterns needed**:
- `ValueConverter<List<string>, string>` — for Tags, Tools
- `ValueConverter<Dictionary<string, object>, string>` — for Metadata fields
- `ValueConverter<ToolExecutionResult, string>` — for nullable tool result

## Topic 4: SPA Hosting with ASP.NET Core 9.0

### Production Hosting

- **Decision**: `UseStaticFiles()` for React build output from `wwwroot/`, then `MapFallbackToFile("index.html")` as catch-all for client-side routing. API (`/api`) and Hub (`/hubs`) endpoints take precedence via endpoint routing.
- **Rationale**: `MapFallbackToFile` is the modern .NET 9.0 approach. `UseSpa()` is legacy terminal middleware from ASP.NET Core 2.x that breaks endpoint routing. `MapFallbackToFile` integrates cleanly with endpoint routing and respects route precedence.
- **Alternatives considered**: `UseSpa()` for everything (legacy, terminal middleware), separate hosting via Nginx/CDN (more complex, not aligned with single-container Docker model), YARP reverse proxy (over-engineering).

### Development Proxy

- **Decision**: Use `Microsoft.AspNetCore.SpaProxy` with `SpaProxyServerUrl` in `.csproj` to proxy to React dev server at `http://localhost:3000`.
- **Rationale**: Enables hot module replacement during development while keeping API calls routed through the .NET backend. Matches the existing CORS configuration pattern in MCP's `Program.cs`.
- **Alternatives considered**: Manual CORS + separate terminals (requires managing two processes independently), containerized development (heavier setup for local dev).

### Middleware Pipeline Order

```
1. UseSerilogRequestLogging()
2. UseSwagger() / UseSwaggerUI() (dev only)
3. UseHttpsRedirection()
4. UseStaticFiles()
5. UseRouting()
6. UseCors("AllowAll")
7. MapControllers()
8. MapHub<ChatHub>("/hubs/chat")
9. MapHealthChecks("/health")
10. MapFallbackToFile("index.html") (SPA catch-all — MUST be last)
```

## Summary of Decisions

| Topic | Decision | Key Rationale |
|-------|----------|---------------|
| Hub structure | Thin Hub alongside REST controllers | Matches McpHttpBridge delegation pattern; Hub is transient |
| Hub DI | Scoped services via `IServiceScopeFactory` | DbContext is scoped; official MS recommendation |
| Group messaging | `Groups.AddToGroupAsync` keyed by conversationId | Built-in, auto-cleans, per-conversation granularity |
| React SignalR | `useRef` for connection, `useReducer` for state | Avoids stale closures, predictable state |
| Reconnection | `withAutomaticReconnect([0,2000,5000,10000,30000])` | Meets <10s target, graduated backoff |
| Stale closures | `useRef` to hold dispatch | Handlers registered once, always latest values |
| Optimistic UI | Immediate dispatch with status transitions | <1s feedback requirement |
| Dual-provider DB | Separate `ChatDbContext`, same config pattern | Matches `CoreServiceExtensions`, decoupled schema |
| JSON columns | `HasConversion` with `ValueConverter` | Cross-provider (SQLite + SQL Server) compatible |
| SPA hosting (prod) | `UseStaticFiles()` + `MapFallbackToFile()` | Modern .NET 9.0 approach |
| SPA hosting (dev) | SPA proxy to CRA dev server | Hot reload, single-port development |
