# ATO Copilot

AI-powered compliance copilot that guides DoD teams through every step of the NIST Risk Management Framework (RMF) — from system registration through continuous monitoring and ATO authorization.

Built on the Model Context Protocol (MCP) with Azure OpenAI function calling, 130 compliance tools, and multi-channel delivery (VS Code, web chat, stdio).

ATO Copilot is the first tool that:

- Covers all 7 steps in a single conversational interface
- Uses AI to automate the hardest part (control narrative writing)
- Integrates with Azure for automated compliance evidence
- Exports to eMASS so it fits into the existing DoD workflow instead of replacing it

ATO Copilot is where you DO the work, eMASS is where you SUBMIT the work. 

## Features

### RMF Lifecycle Automation

- **Prepare** — Register systems, define authorization boundaries, assign ISSO/ISSM/AO roles
- **Categorize** — FIPS 199 impact levels with NIST SP 800-60 information type mapping
- **Select** — Baseline selection, control tailoring, CRM inheritance, STIG cross-reference
- **Implement** — Control narratives, batch SSP population, IaC compliance scanning
- **Assess** — Automated compliance assessment, evidence collection, SAR generation
- **Authorize** — ATO/IATT/DATO decisions, risk acceptance, POA&M management, authorization packages
- **Monitor** — Continuous monitoring plans, drift detection, ConMon reports, expiration alerts

### AI-Powered Intelligence

- **Azure OpenAI Function Calling** — GPT-4o with intelligent tool selection (72/130 tools per request)
- **Multi-Turn Conversations** — Conversational context across turns with automatic tool execution
- **System Name Resolution** — Natural language system references resolved to UUIDs automatically
- **Contextual Suggestions** — Follow-up action buttons based on conversation context

### Enterprise Security

- **CAC/PIV Authentication** — DoD smart card authentication with certificate role mapping
- **Privileged Identity Management** — Azure PIM integration with JIT role activation
- **RBAC Enforcement** — Viewer, Operator, Administrator, Auditor, AuthorizingOfficial roles
- **Audit Logging** — Full correlation-tracked audit trail with 7-year retention

### Multi-Channel Delivery

- **VS Code Extension** — GitHub Copilot Chat participant with `/compliance`, `/knowledge`, `/config` commands
- **Web Chat** — React + Tailwind SPA with SignalR streaming and suggestion buttons
- **Stdio Mode** — Direct MCP integration for GitHub Copilot and Claude Desktop
- **HTTP REST API** — SSE streaming endpoint for custom integrations

### Document Generation & Interoperability

- **SSP, POA&M, SAR, RAR** — QuestPDF and ClosedXML document generation
- **eMASS Export** — Controls, POA&M, and OSCAL format export
- **Template Engine** — Customizable document templates with save/reuse

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) — pinned by [`global.json`](global.json)
- [Docker](https://www.docker.com/) (recommended for full deployment)
- Node.js 20 LTS (for VS Code/M365 extensions and React SPAs)
- Azure subscription (Azure Government preferred)

### Set up your dev machine

**macOS / Linux**

```bash
./scripts/bootstrap.sh           # installs prerequisites + restores all dependencies
./scripts/bootstrap.sh --check   # verify prerequisites without installing
```

**Windows (PowerShell)**

```powershell
.\scripts\bootstrap.ps1
.\scripts\bootstrap.ps1 -Check
```

**Codespaces / Dev Container** — open the repo in VS Code and choose
**"Reopen in Container"**. The container ([`.devcontainer/devcontainer.json`](.devcontainer/devcontainer.json))
runs `bootstrap.sh` automatically and pre-installs the recommended extensions.

The bootstrap script verifies/installs .NET 9 SDK, Node 20, Docker, Azure CLI,
Python 3.11, and `dotnet-ef`, then runs `dotnet restore` and `npm ci` for every
sub-project. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for details.

### Docker (Recommended)

```bash
cp .env.example .env
# Edit .env with your Azure credentials and OpenAI settings
docker compose -f docker-compose.mcp.yml up --build
```

This starts five services:

| Service | Port | Description |
|---------|------|-------------|
| `ato-copilot-sql` | 1433 | SQL Server 2022 database |
| `ato-copilot-redis` | 6379 | Redis (session throttle counters) |
| `ato-copilot-mcp` | 3001 | MCP server with 130+ compliance tools |
| `ato-copilot-chat` | 5001 | Web chat application |
| `ato-copilot-dashboard` | 5173 | Compliance dashboard (React / nginx) |

### Build & Test

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln    # 3,164 tests
```

### Run Locally (HTTP mode)

```bash
cd src/Ato.Copilot.Mcp
dotnet run -- --http
```

Server starts at `http://localhost:3001`:

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check with capability report |
| `GET /mcp/tools` | List all 130 available tools |
| `POST /mcp/chat/stream` | SSE streaming chat with AI function calling |
| `POST /mcp/chat` | Synchronous chat endpoint |
| `POST /mcp` | MCP JSON-RPC (tools/list, tools/call) |

### Run Locally (Stdio mode)

```bash
cd src/Ato.Copilot.Mcp
dotnet run -- --stdio
```

## MCP Tools (130)

### RMF Lifecycle Tools

| Category | Tools | Examples |
|----------|-------|---------|
| **Registration** (8) | System registration, boundaries, roles | `compliance_register_system`, `compliance_define_boundary`, `compliance_assign_role` |
| **Categorization** (3) | FIPS 199, information types | `compliance_categorize_system`, `compliance_add_info_types` |
| **Baseline Selection** (6) | Baselines, tailoring, inheritance, STIG | `compliance_select_baseline`, `compliance_tailor_baseline`, `compliance_show_stig_mapping` |
| **SSP Authoring** (5) | Narratives, batch populate, SSP generation | `compliance_write_narrative`, `compliance_batch_populate`, `compliance_generate_ssp` |
| **Assessment** (6) | Control assessment, evidence, SAR | `compliance_assess_control`, `compliance_record_effectiveness`, `compliance_generate_sar` |
| **Authorization** (7) | ATO decisions, risk, POA&M, packages | `compliance_issue_authorization`, `compliance_create_poam`, `compliance_bundle_authorization_package` |
| **Continuous Monitoring** (7) | ConMon plans, reports, reauthorization | `compliance_create_conmon_plan`, `compliance_generate_conmon_report`, `compliance_reauthorization_workflow` |
| **Compliance Scanning** (11) | Assessments, remediation, evidence, audit | `compliance_assess`, `compliance_remediate`, `compliance_collect_evidence` |
| **Templates** (4) | Document template management | `compliance_list_templates`, `compliance_generate_from_template` |
| **eMASS/OSCAL** (3) | Interoperability exports | `compliance_emass_export_controls`, `compliance_emass_export_oscal` |

### Platform Tools

| Category | Tools | Examples |
|----------|-------|---------|
| **Compliance Watch** (23) | Monitoring, alerts, drift, auto-remediation | `watch_enable_monitoring`, `watch_detect_drift`, `watch_manage_alerts` |
| **Kanban** (21) | Remediation task boards | `kanban_create_task`, `kanban_update_status`, `kanban_get_board` |
| **Auth & PIM** (15) | CAC auth, PIM roles, JIT access | `cac_authenticate`, `pim_activate_role`, `jit_request_access` |
| **Knowledge Base** (7) | NIST, STIG, RMF, FedRAMP guidance | `compliance_explain_nist_control`, `compliance_search_stigs` |
| **IaC Scanning** (1) | Infrastructure-as-Code compliance | `compliance_iac_scan` |
| **Configuration** (1) | Settings management | `configuration_manage` |
| **Chat** (1) | Open-ended compliance interaction | `compliance_chat` |

## Project Structure

```
ato-copilot/
├── Ato.Copilot.sln
├── src/
│   ├── Ato.Copilot.Core/              # Domain models, EF Core (40 entities), interfaces
│   │   ├── Data/Context/              # AtoCopilotContext — SQL Server / SQLite
│   │   ├── Models/Compliance/         # RMF, assessment, authorization models
│   │   └── Interfaces/Compliance/     # Service contracts
│   ├── Ato.Copilot.Agents/            # AI agents with 130 tool implementations
│   │   ├── Common/                    # BaseAgent (AI + keyword routing), BaseTool
│   │   └── Compliance/
│   │       ├── Agents/                # ComplianceAgent, ConfigurationAgent, KnowledgeBaseAgent
│   │       ├── Tools/                 # 25 tool files across RMF lifecycle
│   │       ├── Services/              # Business logic (40+ service implementations)
│   │       └── Prompts/               # AI system prompts
│   ├── Ato.Copilot.Mcp/              # MCP server (stdio + HTTP + SSE streaming)
│   │   ├── Server/                    # McpServer, McpHttpBridge, McpStdioService
│   │   ├── Middleware/                # CAC auth, RBAC, audit logging, correlation
│   │   └── Prompts/                   # Prompt registry
│   ├── Ato.Copilot.Chat/             # Web chat application
│   │   ├── Controllers/              # Chat API endpoints
│   │   ├── Hubs/                     # SignalR real-time streaming
│   │   └── ClientApp/                # React + Tailwind CSS SPA
│   └── Ato.Copilot.State/            # In-memory state management
├── extensions/
│   └── vscode/                        # VS Code extension (Chat participant + diagnostics)
├── tests/
│   └── Ato.Copilot.Tests.Unit/        # 3,164 unit tests (xUnit + FluentAssertions + Moq)
├── docs/                              # MkDocs Material documentation site
├── Dockerfile
└── docker-compose.mcp.yml             # 3-service deployment
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        MCP Clients                                  │
│  VS Code Extension │ Web Chat (React) │ Stdio (Copilot/Claude)     │
└────────┬────────────────────┬───────────────────┬───────────────────┘
         │                    │                   │
         ▼                    ▼                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Ato.Copilot.Mcp — MCP Server (HTTP :3001 + stdio)                 │
│  ├── SSE Streaming (POST /mcp/chat/stream)                         │
│  ├── JSON-RPC (POST /mcp)                                          │
│  ├── Middleware: CAC Auth → RBAC → Audit Logging → Correlation     │
│  └── Agent Router: Intent classification → agent dispatch           │
└────────────────────────────────┬────────────────────────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         ▼                       ▼                       ▼
┌─────────────────┐  ┌────────────────────┐  ┌────────────────────┐
│ Knowledge Base  │  │ Compliance Agent   │  │ Configuration      │
│ Agent           │  │ (130 tools)        │  │ Agent              │
│ (7 tools)       │  │                    │  │ (1 tool)           │
│                 │  │ AI Path:           │  │                    │
│ NIST, STIG,     │  │  Azure OpenAI      │  │ Settings           │
│ RMF, FedRAMP    │  │  GPT-4o function   │  │ management         │
│ guidance        │  │  calling           │  │                    │
│                 │  │                    │  │                    │
│                 │  │ Keyword Path:      │  │                    │
│                 │  │  40+ route rules   │  │                    │
│                 │  │  w/ conversational │  │                    │
│                 │  │  fallback          │  │                    │
└─────────────────┘  └────────┬───────────┘  └────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
┌──────────────────┐ ┌──────────────┐ ┌────────────────┐
│ Core Services    │ │ EF Core      │ │ Azure SDKs     │
│ ├── 40+ services │ │ ├── 40 DbSets│ │ ├── Resource    │
│ ├── RMF workflow │ │ ├── SQL Server│ │ │    Graph       │
│ ├── AI prompts   │ │ └── SQLite   │ │ ├── Policy      │
│ └── Doc gen      │ │              │ │ ├── Defender     │
│    (QuestPDF,    │ └──────────────┘ │ ├── PIM          │
│     ClosedXML)   │                  │ └── Key Vault    │
└──────────────────┘                  └────────────────┘
```

## Configuration

### Environment Variables

```bash
# Server mode
ATO_RUN_MODE=http                                        # stdio | http

# Azure AD / Entra ID
ATO_AZURE_AD__TENANT_ID=your-tenant-id
ATO_AZURE_AD__CLIENT_ID=your-client-id

# Azure Government
ATO_GATEWAY__AZURE__SUBSCRIPTION_ID=your-sub-id
ATO_GATEWAY__AZURE__CLOUD_ENVIRONMENT=AzureGovernment

# Azure OpenAI (AI-powered tool calling)
ATO_GATEWAY__AZUREOPENAI__ENDPOINT=https://your-endpoint.openai.azure.us/
ATO_GATEWAY__AZUREOPENAI__APIKEY=your-api-key
ATO_GATEWAY__AZUREOPENAI__CHATDEPLOYMENTNAME=gpt-4o
ATO_GATEWAY__AZUREOPENAI__AGENTAIENABLED=true

# Database
ATO_CONNECTIONSTRINGS__DEFAULTCONNECTION="Server=localhost,1433;..."
```

### Key Configuration Sections

| Section | Description |
|---------|-------------|
| `Server` | Kestrel `Urls` binding (HTTP mode) |
| `Gateway:AzureOpenAI` | Azure OpenAI endpoint, model, temperature, max tool rounds |
| `AzureAi` | Foundry/OpenAI client wiring (tenant, deployment, run timeout) |
| `AzureAd` | Azure AD / Entra ID (`TenantId`, `ClientId`, `ClientSecret`, `CloudEnvironment`, `RequireCac`) |
| `Gateway:Azure` | Subscription, managed identity, Gov cloud, request timeouts |
| `ConnectionStrings` | SQLite (dev) / SQL Server (prod) — `DefaultConnection`, `ChatDb` |
| `Database` | EF Core `Provider`, `CommandTimeoutSeconds`, `MaxRetryCount`, `MaxRetryDelay`, `EnableSensitiveDataLogging` |
| `Resilience` | Polly HTTP pipelines (`Name`, `MaxRetryAttempts`, `BaseDelaySeconds`, `UseJitter`, `RequestTimeoutSeconds`) |
| `RateLimiting` | Per-endpoint rate-limit policies (Feature 029) |
| `Caching` | `IMemoryCache` defaults |
| `Pagination` / `Streaming` | Default page sizes and SSE flush intervals |
| `OpenTelemetry` | OTLP exporter + Prometheus toggle |
| `Cors` | Allowed origins for Chat + Dashboard |
| `Serilog` | Structured logging — bound directly via `ReadFrom.Configuration` |
| `Agents:Compliance` | Compliance agent (`DefaultFramework`, `DefaultBaseline`, `HighRiskFamilies`, `NistControls`, `Boundary`, `EnableAutomatedRemediation`) |
| `Agents:KnowledgeBaseAgent` | Token limits, confidence threshold |
| `Agents:Kanban` | `OverdueScan:IntervalMinutes` (hosted-service scan cadence) |
| `Pim` | Activation durations, high-privilege role definitions |
| `CacAuth` | Session timeout (8h / 24h max), simulation mode |
| `Retention` | Assessments 3yr, audit logs 7yr |
| `KeyVault` | `VaultUri` (non-Dev secrets provider) |
| `Onboarding` | First-run bootstrap configuration |

## Compliance Frameworks

| Framework | Support Level |
|-----------|--------------|
| NIST 800-53 Rev 5 | Full |
| FedRAMP High | Full |
| FedRAMP Moderate | Full |
| DoD IL2 | Supported |
| DoD IL4 | Supported |
| DoD IL5 | Supported |

## VS Code Extension

The VS Code extension integrates as a GitHub Copilot Chat participant:

```
@ato register a new system called Eagle Eye
@ato /compliance assess my system
@ato /knowledge explain AC-2
@ato /config set framework FedRAMP High
```

Features:
- Chat participant with RMF workflow commands
- IaC compliance diagnostics with CAT severity mapping
- Code actions for STIG remediation suggestions
- RMF Overview webview panel
- Follow-up suggestion buttons

Install from `extensions/vscode/` — see the extension README for details.

## Testing

```bash
# Run all 3,164 tests
dotnet test Ato.Copilot.sln

# Run unit tests only
dotnet test tests/Ato.Copilot.Tests.Unit/
```

Test coverage spans:
- **Tools** (32 files) — All 130 tool implementations
- **Services** (40+ files) — Business logic, alert pipelines, PIM
- **Agents** (20+ files) — Routing, AI path, prompt handling
- **Middleware** (5 files) — CAC auth, RBAC, audit, correlation
- **Scanners** (12 files) — All NIST control family scanners
- **Evidence Collectors** (12 files) — Per-family evidence collection
- **Models** (6 files) — Domain model validation
- **MCP/Server** (6 files) — SSE streaming, error handling, intent routing
- **Chat** (9 files) — Chat service, SignalR hub, message mapping

## Documentation

Full documentation is built with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/):

```bash
pip install mkdocs-material
mkdocs serve
```

Sections: Getting Started (6 role-specific guides), Personas (ISSM, ISSO, SCA, AO, Platform Engineer), RMF Phases (Prepare through Monitor), Reference (tool catalog, API, configuration).

## License

Proprietary. All rights reserved.
