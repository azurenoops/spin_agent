# Deployment Guide

> Production deployment, container configuration, security hardening, and operational guidance.

## Table of Contents

- [Deployment Options](#deployment-options)
- [Docker Deployment](#docker-deployment)
  - [Build the Image](#build-the-image)
  - [Docker Compose](#docker-compose)
  - [Environment Variables](#environment-variables)
- [Azure Deployment](#azure-deployment)
  - [Azure Container Apps](#azure-container-apps)
  - [Azure App Service](#azure-app-service)
  - [Managed Identity](#managed-identity)
- [Database Configuration](#database-configuration)
  - [SQLite (Single Instance)](#sqlite-single-instance)
  - [SQL Server (Production)](#sql-server-production)
  - [Migrations](#migrations)
- [Security Configuration](#security-configuration)
  - [Azure AD / Entra ID](#azure-ad--entra-id)
  - [CAC/PIV Authentication](#cacpiv-authentication)
  - [Azure Key Vault](#azure-key-vault)
  - [CORS](#cors)
- [Compliance Framework Settings](#compliance-framework-settings)
- [Monitoring & Observability](#monitoring--observability)
  - [Health Checks](#health-checks)
  - [Logging](#logging)
  - [Structured Log Output](#structured-log-output)
- [Data Retention](#data-retention)
- [Rate Limiting](#rate-limiting)
- [Backup & Recovery](#backup--recovery)
- [Operational Runbook](#operational-runbook)

---

## Deployment Options

| Method | Best For | Database |
|---|---|---|
| **Docker Compose** | Single-server, dev/staging | SQLite (file volume) |
| **Azure Container Apps** | Production, auto-scaling | SQL Server |
| **Azure App Service** | Production, PaaS | SQL Server |
| **dotnet publish** | Bare-metal / VM | Either |

---

## Docker Deployment

### Build the Image

Multi-stage Dockerfile builds a minimal runtime image:

```bash
docker build -t ato-copilot:latest .
```

The image:
- Uses `mcr.microsoft.com/dotnet/aspnet:9.0` runtime base
- Runs as non-root user (`atocopilot`)
- Includes `curl` for health checks
- Exposes port 3001
- Entry point: `dotnet Ato.Copilot.Mcp.dll --http`

### Docker Compose

```bash
cp .env.example .env
# Edit .env with your credentials
docker compose -f docker-compose.mcp.yml up --build
```

**Compose features:**
- Port mapping: `${ATO_SERVER_PORT:-3001}:3001`
- Persistent volumes: `ato-data` (database), `ato-logs` (logs)
- Health check: 30s interval, 10s timeout, 3 retries
- Restart policy: `unless-stopped`
- Isolated network: `ato-network`

### Environment Variables

All configuration can be overridden via `ATO_`-prefixed environment variables:

**Required:**

| Variable | Description |
|---|---|
| `ATO_AZURE_AD__TENANT_ID` | Azure AD tenant ID |
| `ATO_AZURE_AD__CLIENT_ID` | App registration client ID |
| `ATO_AZURE_AD__CLIENT_SECRET` | App registration secret |
| `ATO_GATEWAY__AZURE__SUBSCRIPTION_ID` | Default Azure subscription |

**Optional:**

| Variable | Default | Description |
|---|---|---|
| `ATO_RUN_MODE` | `http` | Server mode (`http` / `stdio`) |
| `ASPNETCORE_ENVIRONMENT` | `Production` | ASP.NET environment |
| `ATO_AZURE_AD__CLOUD_ENVIRONMENT` | `AzureUSGovernment` | Azure cloud |
| `ATO_GATEWAY__AZURE__CLOUD_ENVIRONMENT` | `AzureGovernment` | Azure ARM cloud |
| `ATO_CONNECTION_STRINGS__DEFAULT_CONNECTION` | `Data Source=/data/ato-copilot.db` | Database connection |
| `ATO_DATABASE__PROVIDER` | `SQLite` | `SQLite` or `SqlServer` |
| `ATO_SERVER_PORT` | `3001` | HTTP listen port |

---

## Azure Deployment

### Azure Container Apps

```bash
# Create resource group
az group create -n rg-ato-copilot -l usgovvirginia

# Create container app environment
az containerapp env create \
  -n ato-copilot-env \
  -g rg-ato-copilot \
  -l usgovvirginia

# Deploy
az containerapp create \
  -n ato-copilot \
  -g rg-ato-copilot \
  --environment ato-copilot-env \
  --image your-acr.azurecr.us/ato-copilot:latest \
  --target-port 3001 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 3 \
  --env-vars \
    ATO_RUN_MODE=http \
    ATO_DATABASE__PROVIDER=SqlServer \
    ATO_GATEWAY__AZURE__USE_MANAGED_IDENTITY=true
```

### Azure App Service

```bash
az webapp create \
  -n ato-copilot \
  -g rg-ato-copilot \
  --plan ato-copilot-plan \
  --runtime "DOTNETCORE:9.0"

az webapp config appsettings set \
  -n ato-copilot \
  -g rg-ato-copilot \
  --settings \
    ATO_RUN_MODE=http \
    ATO_DATABASE__PROVIDER=SqlServer
```

### Managed Identity

For production, use **Managed Identity** instead of client secrets:

```json
{
  "Gateway": {
    "Azure": {
      "UseManagedIdentity": true,
      "ClientId": "",
      "ClientSecret": ""
    }
  }
}
```

Assign the managed identity these Azure RBAC roles:
- **Reader** on target subscriptions (for compliance assessment)
- **Security Reader** (for Defender for Cloud integration)
- **Resource Policy Contributor** (if using policy-based remediation)
- **Key Vault Secrets User** (if using Key Vault configuration)

---

## Database Configuration

### SQLite (Single Instance)

Default for development and single-server deployments:

```json
{
  "Database": { "Provider": "SQLite" },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/ato-copilot.db"
  }
}
```

SQLite is suitable for deployments with a single application instance. The database file is created automatically.

### SQL Server (Production)

For multi-instance or high-availability deployments:

```json
{
  "Database": {
    "Provider": "SqlServer",
    "CommandTimeoutSeconds": 30,
    "MaxRetryCount": 5,
    "MaxRetryDelay": 30
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-server.database.usgovcloudapi.net;Database=AtoCopilot;Authentication=Active Directory Managed Identity;"
  }
}
```

SQL Server includes retry-on-failure (5 retries, 30s max delay) for transient fault handling.

### Migrations

Database migrations are **applied automatically at startup**. The migration process:
1. Creates a scoped `AtoCopilotContext`
2. Calls `Database.MigrateAsync()` with a 30-second timeout
3. On failure: logs `Critical`, sets exit code 1, application shuts down (fail-fast)

No manual migration commands are required for deployment.

---

## Security Configuration

### Azure AD / Entra ID

```json
{
  "AzureAd": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "loaded-from-key-vault",
    "CloudEnvironment": "AzureUSGovernment",
    "RequireCac": false
  }
}
```

| Setting | Description |
|---|---|
| `TenantId` | Azure AD tenant (GUID) |
| `ClientId` | Application registration client ID |
| `ClientSecret` | Client secret — load from Key Vault in production, leave blank in source |
| `CloudEnvironment` | `AzureCloud` or `AzureUSGovernment` (drives token authority) |
| `RequireCac` | Require CAC/PIV certificate authentication |

### CAC/PIV Authentication

For DoD environments requiring CAC/PIV:

```json
{
  "AzureAd": { "RequireCac": true },
  "CacAuth": {
    "DefaultSessionTimeoutHours": 8,
    "MaxSessionTimeoutHours": 24
  }
}
```

The `CacAuthenticationMiddleware` validates the JWT `amr` claim for CAC/PIV authentication methods.

### CAC Simulation Mode (Development Only)

For local development without a physical smart card, add simulation config to `appsettings.Development.json`:

```json
{
  "CacAuth": {
    "SimulationMode": true,
    "SimulatedIdentity": {
      "UserPrincipalName": "dev.user@dev.mil",
      "DisplayName": "Dev User (Simulated)",
      "CertificateThumbprint": "ABC123DEF456",
      "Roles": ["Global Reader", "ISSO"]
    }
  }
}
```

| Setting | Description |
|---|---|
| `SimulationMode` | Enables simulated CAC identity injection (Development only) |
| `SimulatedIdentity.UserPrincipalName` | UPN for the simulated user (required) |
| `SimulatedIdentity.DisplayName` | Display name (required) |
| `SimulatedIdentity.CertificateThumbprint` | Optional certificate thumbprint claim |
| `SimulatedIdentity.Roles` | Array of compliance roles assigned to the simulated user |

!!! warning "Production safety"
    Simulation mode is ignored in non-Development environments. Never add `SimulationMode` or `SimulatedIdentity` keys to `appsettings.json`.

### Azure Key Vault

In non-Development environments, secrets can be loaded from Azure Key Vault:

```json
{
  "KeyVault": {
    "VaultUri": "https://your-vault.vault.usgovcloudapi.net/"
  }
}
```

The Key Vault configuration provider is added automatically when `VaultUri` is set and the environment is not Development. Uses `DefaultAzureCredential` with Azure Government authority.

### CORS

Configure allowed origins for HTTP mode:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://your-frontend.azurewebsites.us"
    ]
  }
}
```

---

## Compliance Framework Settings

```json
{
  "Agents": {
    "Compliance": {
      "Enabled": true,
      "DefaultFramework": "NIST800-53",
      "DefaultBaseline": "FedRAMPHigh",
      "EnableAutomatedRemediation": false,
      "HighRiskFamilies": ["AC", "IA", "SC"],
      "NistControls": {
        "BaseUrl": "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json",
        "CacheDurationHours": 24,
        "EnableOfflineFallback": true
      },
      "Boundary": {
        "ValidateAzureResources": false
      }
    }
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch for the compliance agent |
| `DefaultFramework` | `"NIST800-53"` | Default compliance framework |
| `DefaultBaseline` | `"FedRAMPHigh"` | Default control baseline |
| `EnableAutomatedRemediation` | `false` | Allow auto-remediation globally |
| `HighRiskFamilies` | `["AC","IA","SC"]` | Families that require manual approval prereqs during remediation; empty/missing falls back to defaults |
| `NistControls.BaseUrl` | NIST OSCAL URL | Online OSCAL catalog source |
| `NistControls.CacheDurationHours` | `24` | Catalog cache window |
| `NistControls.EnableOfflineFallback` | `true` | Use embedded catalog if network unavailable |
| `Boundary.ValidateAzureResources` | `false` | Validate ARM resource IDs at boundary import |

### Database & EF Core retry

```json
{
  "Database": {
    "Provider": "SqlServer",
    "EnableSensitiveDataLogging": false,
    "CommandTimeoutSeconds": 30,
    "MaxRetryCount": 5,
    "MaxRetryDelay": 30
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Provider` | `"SQLite"` | `SQLite` (dev) or `SqlServer` (prod) — auto-detected from connection string when omitted |
| `EnableSensitiveDataLogging` | `false` | Log parameter values (development only — never enable in prod) |
| `CommandTimeoutSeconds` | `30` | EF Core command timeout |
| `MaxRetryCount` | `5` | SQL Server transient-fault retry attempts |
| `MaxRetryDelay` | `30` | Max delay (seconds) between retries |

### HTTP resilience (Polly)

```json
{
  "Resilience": {
    "Pipelines": [
      {
        "Name": "default",
        "MaxRetryAttempts": 3,
        "BaseDelaySeconds": 2.0,
        "UseJitter": true,
        "RequestTimeoutSeconds": 30
      }
    ]
  }
}
```

The pipeline named `"default"` is applied to every `IHttpClientFactory` client by `AddCoreServices`. Add additional named pipelines and reference them from named clients.

---

## Monitoring & Observability

### Health Checks

The `/health` endpoint returns JSON with agent status:

```bash
curl -s https://your-server:3001/health | jq .
```

```json
{
  "status": "Healthy",
  "agents": [
    { "name": "compliance-agent", "status": "Healthy", "description": "" }
  ],
  "totalDurationMs": 12.5
}
```

Use this for load balancer health checks (30s interval recommended).

### Logging

Serilog writes to two sinks:

| Sink | Target | Retention |
|---|---|---|
| Console | stdout (HTTP mode only, suppressed in stdio) | — |
| Rolling File | `logs/ato-copilot-YYYY-MM-DD.log` | 14 days |

**Configuration:**

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### Structured Log Output

Logs include structured properties:
- `Application`: `"ATO Copilot"`
- `CorrelationId`: Unique per-request ID
- `AgentName`: Which agent handled the request
- `ToolName`: Which tool was executed
- `UserId`: Redacted user identifier

Sensitive data is automatically redacted by `SensitiveDataDestructuringPolicy`.

---

## Data Retention

Automatic cleanup runs daily via `RetentionCleanupHostedService`:

```json
{
  "Retention": {
    "AssessmentRetentionDays": 1095,
    "AuditLogRetentionDays": 2555,
    "EnableAutomaticCleanup": true,
    "CleanupIntervalHours": 24
  }
}
```

| Data Type | Default Retention | Notes |
|---|---|---|
| Assessments | 1,095 days (3 years) | Findings cascade-deleted |
| Audit logs | 2,555 days (7 years) | NIST AU-11 compliance |
| Compliance alerts | 730 days (2 years) | Only resolved/dismissed alerts |
| Daily snapshots | 90 days | Compliance trend data |
| Weekly snapshots | 730 days (2 years) | Long-term trend data |

---

## Rate Limiting

Per-endpoint rate limiting is configured under the `RateLimiting` section (Feature 029). The MCP server applies these limits via ASP.NET Core's `System.Threading.RateLimiting` middleware. Adjust the per-policy windows and permit counts to balance Azure API throttling against responsiveness.

For outbound HTTP resilience (retries, jitter, request timeout) configure the `Resilience:Pipelines` section — see [Database & EF Core retry](#database--ef-core-retry) above. The pipeline named `"default"` is applied to every `IHttpClientFactory` client.

---

## Backup & Recovery

### SQLite

Back up the database file directly:

```bash
# Stop the service first (or use SQLite online backup)
cp /data/ato-copilot.db /backup/ato-copilot-$(date +%Y%m%d).db
```

### SQL Server

Use standard SQL Server backup strategies (full, differential, log). The schema is managed by EF Core migrations — restoring a backup and restarting the application will apply any pending migrations automatically.

---

## Operational Runbook

### Startup Sequence

1. Determine run mode (HTTP or stdio)
2. Configure Serilog logging
3. Register all services (Core → State → Agents → MCP)
4. Apply database migrations (30s timeout, fail-fast)
5. Start middleware pipeline (HTTP) or stdio loop
6. Start hosted services (monitoring, escalation, overdue scan, cleanup)

### Common Issues

| Symptom | Cause | Resolution |
|---|---|---|
| Exit code 1 on startup | Database migration failure | Check DB connection string, verify network access |
| `AUTH_REQUIRED` on all requests | Missing Azure AD config | Set `ATO_AZURE_AD__*` environment variables |
| `PIM_ELEVATION_REQUIRED` | Tool requires PIM role | Activate PIM role first: "Activate Reader role" |
| Slow assessments | Rate limiting | Adjust permit windows under `RateLimiting` or relax `Resilience:Pipelines[default].RequestTimeoutSeconds` |
| No notifications | Channels not configured | Enable and configure Email/Teams/Slack in settings |
| High memory usage | Large assessment results | Reduce `MaxPageSize` or `MemoryBudgetMb` |

### Graceful Shutdown

The server handles `SIGTERM` gracefully:
1. Stops accepting new requests
2. Completes in-flight operations
3. Flushes Serilog buffers (`Log.CloseAndFlush()`)
4. Stops hosted services

In Docker: `docker compose down` sends SIGTERM with a default 10s grace period.

### Log Analysis

```bash
# Recent errors
grep -i "error\|fatal" logs/ato-copilot-$(date +%Y-%m-%d).log

# Tool execution times
grep "ToolMetrics" logs/ato-copilot-*.log | jq -r '.ExecutionTimeMs'

# Failed auth attempts
grep "AUTH_REQUIRED\|PIM_ELEVATION" logs/ato-copilot-*.log
```

---

## Feature 015: RMF Workflow Deployment Notes

### New Entities (Database)

Feature 015 adds 18 new EF Core entities. These are applied automatically via migrations at startup:

| Entity | Table | Purpose |
|--------|-------|---------|
| `RegisteredSystem` | `RegisteredSystems` | RMF system registration |
| `SecurityCategorization` | `SecurityCategorizations` | FIPS 199 categorization |
| `InformationType` | `InformationTypes` | SP 800-60 information types |
| `AuthorizationBoundary` | `AuthorizationBoundaries` | System boundary resources |
| `RmfRoleAssignment` | `RmfRoleAssignments` | Personnel role assignments |
| `ControlBaseline` | `ControlBaselines` | NIST 800-53 baselines |
| `ControlTailoring` | `ControlTailorings` | Baseline tailoring actions |
| `ControlInheritance` | `ControlInheritances` | Control inheritance designations |
| `ControlImplementation` | `ControlImplementations` | SSP narratives |
| `ControlEffectiveness` | `ControlEffectivenesses` | Assessment determinations |
| `AssessmentRecord` | `AssessmentRecords` | Immutable assessment snapshots |
| `AuthorizationDecision` | `AuthorizationDecisions` | ATO/IATT/DATO decisions |
| `RiskAcceptance` | `RiskAcceptances` | Accepted risks |
| `PoamItem` | `PoamItems` | Plan of Action & Milestones |
| `PoamMilestone` | `PoamMilestones` | POA&M milestone tracking |
| `ConMonPlan` | `ConMonPlans` | Continuous monitoring plans |
| `ConMonReport` | `ConMonReports` | Periodic compliance reports |
| `SignificantChange` | `SignificantChanges` | Change events |

### New RBAC Role

Feature 015 adds the `AuthorizingOfficial` role. This role is required for:
- `compliance_issue_authorization` — Issue ATO/IATT/DATO decisions
- `compliance_accept_risk` — Accept risk on specific findings

Ensure this role is provisioned in your identity provider and assigned to appropriate personnel.

### New NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| QuestPDF | 2024.12.3 | PDF document generation (SSP, SAR, POA&M) |
| ClosedXML | 0.104.2 | eMASS Excel import/export |
| Microsoft.Graph | 5.70.0 | PIM and Graph API integration |

### Additional Azure Service Principal Permissions

If using PIM tools with Microsoft Graph:
- **RoleManagement.ReadWrite.Directory** — For PIM role activation
- **PrivilegedAccess.ReadWrite.AzureADGroup** — For PIM group management

If using JIT VM Access:
- **Microsoft.Security/locations/jitNetworkAccessPolicies/\*** — For JIT operations

### Embedded Reference Data

Four JSON data files are embedded in the Agents assembly and loaded at startup:
- `nist-800-53-rev5-catalog.json` — NIST 800-53 control catalog
- `nist-800-53-baselines.json` — Low/Moderate/High baseline definitions
- `sp800-60-information-types.json` — SP 800-60 Vol. 2 information types
- `cnssi-1253-overlays.json` — CNSSI 1253 overlay controls by impact level

These files do not require external configuration.

### RMF Configuration

```json
{
  "Agents": {
    "Compliance": {
      "EnableAutomatedRemediation": false,
      "DefaultFramework": "NIST800-53",
      "DefaultBaseline": "FedRAMPHigh",
      "HighRiskFamilies": ["AC", "IA", "SC"]
    },
    "Kanban": {
      "OverdueScan": { "IntervalMinutes": 5 }
    }
  },
  "Pim": {
    "DefaultActivationDurationHours": 4,
    "MaxActivationDurationHours": 8,
    "RequireTicketNumber": false,
    "AutoDeactivateAfterRemediation": false
  }
}
```
