# Getting Started

> Set up, build, and run the Security Posture Intelligence Navigator MCP server for NIST 800-53 compliance on Azure Government.

!!! tip "Looking for per-persona onboarding?"
    If you are an ISSM, ISSO, SCA, AO, or Engineer looking for role-specific getting-started guidance, see the [Getting Started hub](getting-started/index.md).

## Table of Contents

- [Prerequisites](#prerequisites)
- [Clone & Build](#clone--build)
- [Configuration](#configuration)
  - [Azure Credentials](#azure-credentials)
  - [Database](#database)
  - [Environment Variables](#environment-variables)
- [Run Modes](#run-modes)
  - [HTTP Mode](#http-mode)
  - [Stdio Mode (GitHub Copilot / Claude Desktop)](#stdio-mode-github-copilot--claude-desktop)
  - [Docker](#docker)
- [Verify Installation](#verify-installation)
- [First Assessment](#first-assessment)
- [Next Steps](#next-steps)

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | 9.0+ | C# 13 target |
| Azure CLI | Latest | `az login` for credential flow |
| Azure subscription | — | Azure Government preferred |
| Docker (optional) | Latest | For containerized deployment |
| Git | Latest | Source control |

---

## Clone & Build

```bash
git clone <repo-url> ato-copilot
cd ato-copilot

# Restore and build
dotnet build Ato.Copilot.sln

# Run tests to verify
dotnet test Ato.Copilot.sln
```

The solution contains six projects:

| Project | Purpose |
|---|---|
| `Ato.Copilot.Core` | Domain models, EF Core DbContext, configuration |
| `Ato.Copilot.State` | In-memory state management |
| `Ato.Copilot.Agents` | Agent framework, ComplianceAgent, 65+ tools |
| `Ato.Copilot.Mcp` | MCP server (HTTP + stdio), middleware |
| `Ato.Copilot.Tests.Unit` | Unit tests (xUnit + FluentAssertions + Moq) |
| `Ato.Copilot.Tests.Integration` | Integration tests (TestServer) |

---

## Configuration

### Azure Credentials

Security Posture Intelligence Navigator needs Azure AD credentials to access your subscriptions. Edit `src/Ato.Copilot.Mcp/appsettings.json` or use environment variables:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.us/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "CloudEnvironment": "AzureUSGovernment"
  },
  "Gateway": {
    "Azure": {
      "TenantId": "your-tenant-id",
      "SubscriptionId": "your-subscription-id",
      "CloudEnvironment": "AzureGovernment",
      "UseManagedIdentity": true
    }
  }
}
```

For local development, the system uses `DefaultAzureCredential` which supports:
- Managed Identity (production)
- Azure CLI (`az login`)
- Environment variables
- Visual Studio credentials

### Database

The system defaults to **SQLite** for development:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ato-copilot.db"
  },
  "Database": {
    "Provider": "SQLite"
  }
}
```

For production, switch to **SQL Server**:

```json
{
  "Database": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-server;Database=AtoCopilot;Trusted_Connection=true;"
  }
}
```

The database is **auto-migrated** at startup — no manual migration commands needed.

### Azure OpenAI (Optional)

Enable LLM-powered natural language processing in agents. When configured and enabled, agents use Azure OpenAI to understand user intent, select tools via function-calling, and return natural language responses. **When unconfigured or disabled, agents behave exactly as before** — deterministic keyword routing and raw tool output.

```json
{
  "Gateway": {
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.com",
      "ApiKey": "your-api-key",
      "ChatDeploymentName": "gpt-4o",
      "AgentAIEnabled": true,
      "MaxToolCallRounds": 5,
      "Temperature": 0.3
    }
  }
}
```

For Azure Government with Managed Identity:

```json
{
  "Gateway": {
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.us",
      "UseManagedIdentity": true,
      "ChatDeploymentName": "gpt-4o",
      "AgentAIEnabled": true
    }
  }
}
```

| Setting | Type | Default | Description |
|---|---|---|---|
| `Endpoint` | string | `""` | Azure OpenAI resource endpoint. Empty = AI disabled. |
| `ApiKey` | string | `""` | API key (ignored when `UseManagedIdentity` is true) |
| `ChatDeploymentName` | string | `""` | Chat model deployment name (e.g., `gpt-4o`) |
| `AgentAIEnabled` | bool | `false` | Feature flag — must be `true` to enable AI processing |
| `MaxToolCallRounds` | int | `5` | Max LLM tool-call rounds before returning |
| `Temperature` | double | `0.3` | LLM temperature (0.0–1.0) |
| `UseManagedIdentity` | bool | `false` | Use `DefaultAzureCredential` instead of API key |

**Degraded mode**: If `Endpoint` is empty or `AgentAIEnabled` is `false`, all agents fall back to deterministic processing with zero overhead. No Azure OpenAI calls are made.

### Environment Variables

All configuration can be overridden with environment variables prefixed with `ATO_`:

| Variable | Description |
|---|---|
| `ATO_RUN_MODE` | `http` or `stdio` |
| `ATO_AZURE_AD__TENANT_ID` | Azure AD tenant |
| `ATO_AZURE_AD__CLIENT_ID` | App registration client ID |
| `ATO_AZURE_AD__CLIENT_SECRET` | App registration secret |
| `ATO_GATEWAY__AZURE__SUBSCRIPTION_ID` | Default Azure subscription |
| `ATO_GATEWAY__AZURE__CLOUD_ENVIRONMENT` | `AzureGovernment` or `AzurePublic` |
| `ATO_CONNECTION_STRINGS__DEFAULT_CONNECTION` | Database connection string |

---

## Run Modes

### HTTP Mode

Start a REST API server on port 3001:

```bash
cd src/Ato.Copilot.Mcp
dotnet run -- --http
```

**Endpoints:**

| Route | Method | Description |
|---|---|---|
| `/` | GET | Server info and version |
| `/health` | GET | Health check with agent status |
| `/mcp/tools` | GET | List all available MCP tools |
| `/mcp/chat` | POST | Natural language compliance chat |
| `/mcp` | POST | MCP JSON-RPC endpoint |

**Test the server:**

```bash
# Health check
curl http://localhost:3001/health

# List tools
curl http://localhost:3001/mcp/tools

# Chat
curl -X POST http://localhost:3001/mcp/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What NIST control families are available?"}'
```

### Stdio Mode (GitHub Copilot / Claude Desktop)

For use as an MCP server in AI coding assistants:

```bash
cd src/Ato.Copilot.Mcp
dotnet run -- --stdio
```

In stdio mode, the server reads JSON-RPC messages from stdin and writes responses to stdout. Console output is suppressed; logs go to `logs/ato-copilot-*.log`.

**Claude Desktop configuration** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "ato-copilot": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/src/Ato.Copilot.Mcp", "--", "--stdio"]
    }
  }
}
```

**GitHub Copilot (VS Code)** — add to `.vscode/mcp.json`:

```json
{
  "servers": {
    "ato-copilot": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/Ato.Copilot.Mcp", "--", "--stdio"]
    }
  }
}
```

### Docker

```bash
# Copy and edit environment file
cp .env.example .env
# Edit .env with your Azure credentials

# Build and run
docker compose -f docker-compose.mcp.yml up --build
```

The container:
- Runs as a non-root user (`atocopilot`)
- Exposes port 3001
- Persists data to `/data` volume (SQLite database)
- Persists logs to `/app/logs` volume
- Includes health check (30s interval)

---

## Verify Installation

After starting in HTTP mode:

```bash
# 1. Check health
curl -s http://localhost:3001/health | jq .

# Expected: { "status": "Healthy", "agents": [...] }

# 2. List available tools
curl -s http://localhost:3001/mcp/tools | jq '.[] | .name'

# 3. Test chat
curl -s -X POST http://localhost:3001/mcp/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What compliance frameworks are supported?"}' | jq .
```

---

## First Assessment

Once configured with Azure credentials:

```text
"Run a FedRAMP Moderate assessment on subscription <your-subscription-id>"
```

Or via HTTP:

```bash
curl -X POST http://localhost:3001/mcp/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Run a quick compliance scan on subscription abc-123"}'
```

The assessment will:
1. Query Azure Resource Graph for resource configuration
2. Evaluate Azure Policy compliance state
3. Check Microsoft Defender for Cloud recommendations
4. Generate findings with severity, control mappings, and affected resources
5. Store results in the database for tracking

---

## Next Steps

- [Architecture](architecture/overview.md) — System design, project structure, and patterns
- [Development](dev/contributing.md) — Contributing, testing, and code conventions
- [Deployment](deployment.md) — Production deployment, security, and operations
- [Compliance Watch](guides/compliance-watch.md) — Continuous monitoring and alerting
- [Remediation Kanban](guides/remediation-kanban.md) — Track and resolve compliance findings
