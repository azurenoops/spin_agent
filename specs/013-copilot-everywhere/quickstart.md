# Quickstart: Copilot Everywhere — Multi-Channel Extensions & Channels Library

**Feature**: 013-copilot-everywhere  
**Date**: 2026-02-26

---

## Prerequisites

- Docker Compose stack running (MCP Server at `http://localhost:3001`, Chat App at `http://localhost:5001`)
- .NET 9.0 SDK installed
- Node.js 20 LTS + npm installed
- VS Code 1.90+ with GitHub Copilot Chat extension
- (Optional) Azure Bot registration for M365 extension

## What This Feature Adds

Three new components, all connecting to the existing MCP Server:

1. **Channels Library** (`Ato.Copilot.Channels`) — .NET class library with `IChannel`, `IChannelManager`, `IMessageHandler`, `IStreamingHandler` abstractions and `InMemoryChannel` implementation
2. **VS Code Extension** (`extensions/vscode/`) — `@ato` GitHub Copilot Chat participant with `/compliance`, `/knowledge`, `/config` commands
3. **M365 Extension** (`extensions/m365/`) — Express.js webhook returning Adaptive Cards for Teams

## Quick Verification

### 1. Channels Library — Unit Tests

```bash
# Build the solution
dotnet build Ato.Copilot.sln

# Run channel tests
dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~Channels"
```

**Expected**: All InMemoryChannel, ChannelManager, DefaultMessageHandler, and StreamContext tests pass.

### 2. Channels Library — In-Process Smoke Test

```csharp
// Register channels (test mode — no config binding)
services.AddInMemoryChannels();

// Register a connection
var manager = serviceProvider.GetRequiredService<IChannelManager>();
var conn = await manager.RegisterConnectionAsync("user-1");
// conn.ConnectionId is a GUID, conn.Conversations is empty

// Join a conversation
await manager.JoinConversationAsync(conn.ConnectionId, "conv-1");

// Send a message to the conversation
await manager.SendToConversationAsync("conv-1", new ChannelMessage
{
    Content = "Hello from Channels!",
    Type = MessageType.SystemNotification
});
// conn receives the message

// Start a stream
var streamHandler = serviceProvider.GetRequiredService<IStreamingHandler>();
await using var stream = await streamHandler.BeginStreamAsync("conv-1");
await stream.WriteAsync("Chunk 1...");   // seq=1
await stream.WriteAsync("Chunk 2...");   // seq=2
await stream.CompleteAsync();             // sends aggregated content
```

### 3. VS Code Extension

```bash
# Install dependencies
cd extensions/vscode
npm install

# Build
npm run compile

# Run tests
npm test

# Launch in VS Code (Extension Development Host)
# Press F5 in VS Code with the extension folder open
```

**Usage in VS Code**:
```
@ato How do I comply with AC-2?
@ato /compliance Run FedRAMP assessment on subscription 123
@ato /knowledge What is the NIST 800-53 control family for encryption?
@ato /config Show current settings
```

**Command Palette**:
- `Security Posture Intelligence Navigator: Check API Health`
- `Security Posture Intelligence Navigator: Configure Connection`
- `Security Posture Intelligence Navigator: Analyze Current File for Compliance` (open a `.bicep` file first)
- `Security Posture Intelligence Navigator: Analyze Workspace for Compliance`

### 4. M365 Extension

```bash
# Install dependencies
cd extensions/m365
npm install

# Build
npm run build

# Start server
npm start
# Server starts on http://localhost:3978

# Test health
curl http://localhost:3978/health
# {"name":"Security Posture Intelligence Navigator M365 Extension","version":"1.0.0","timestamp":"..."}

# Test message processing
curl -X POST http://localhost:3978/api/messages \
  -H "Content-Type: application/json" \
  -d '{
    "type": "message",
    "text": "Run compliance assessment for my subscription",
    "conversation": { "id": "test-1" },
    "from": { "id": "user-1", "name": "Test User" }
  }'
# Returns Adaptive Card JSON with compliance results
```

## Key Behaviors

| Scenario | Behavior |
|----------|----------|
| MCP Server running | All extensions connect, process, and return results |
| MCP Server down | VS Code shows warning with "Configure Connection" button; M365 returns error Adaptive Card |
| No slash command (`@ato free text`) | Sent without `targetAgent` — MCP Server routes by intent |
| `/compliance` command | Sent with `targetAgent: "ComplianceAgent"` |
| DefaultMessageHandler with AgentInvoker | Stores message → sends thinking → invokes agent → stores response |
| DefaultMessageHandler without AgentInvoker (Echo mode) | Returns user message as AgentResponse |
| Connection idle > 30 min | Cleanup service removes from all groups |
| Stream disposed without complete | Auto-completes with aggregated content (FR-016) |
| Stream aborted | Error message sent, stream locked from further writes |

## Build & Test

```bash
# Full .NET build
dotnet build Ato.Copilot.sln

# All .NET unit tests
dotnet test tests/Ato.Copilot.Tests.Unit

# Channel tests only
dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~Channels"

# VS Code extension
cd extensions/vscode && npm test

# M365 extension
cd extensions/m365 && npm test
```
