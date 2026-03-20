# Getting Started: Platform Engineer / System Owner

> First-time setup and orientation for Platform Engineer and System Owner users.

---

## Prerequisites

| Requirement | Details |
|------------|---------|
| **Access** | CAC enrolled with `Compliance.PlatformEngineer` role (default if no explicit role mapping) |
| **Tools** | VS Code with GitHub Copilot Chat extension installed |
| **Infrastructure** | Azure subscription access for the system being authorized |

## First-Time Setup

1. **Verify your role**

    ```
    @ato "What role am I logged in as?"
    ```

    Expected result: `Compliance.PlatformEngineer`.

2. **Check your assigned tasks**

    ```
    @ato "Show my assigned remediation tasks"
    ```

    Expected result: Kanban board of assigned findings and remediation tasks with priorities and deadlines.

3. **Learn about your first control**

    ```
    @ato /knowledge "What does AC-2 mean for Azure?"
    ```

    Expected result: Plain-language explanation of the NIST control tailored to your Azure environment, with implementation guidance.

> **Tip:** System Owners can register new systems using the **System Intake Wizard** in the Compliance Dashboard (Systems → "+ Add System"). The wizard walks through 7 steps including component inventory, boundary definition, and role assignment. See the [System Intake Wizard Guide](../guides/system-intake-wizard.md).

## Your First 3 Commands

### 1. Explain a Control

> **@ato /knowledge "What does AC-2 mean for Azure?"**

Expected result: NIST control description translated into Azure-specific implementation steps — what services to configure, what settings to enable, and what evidence to collect.

### 2. Scan Your IaC for Compliance

> **@ato "Scan my Bicep file for compliance issues"**

Expected result: List of compliance findings in your Infrastructure as Code (Bicep/Terraform/ARM) with CAT severity levels and suggested fixes.

### 3. Write a Control Narrative

> **@ato "Suggest a narrative for control SC-7 on system {id}"**

Expected result: AI-generated draft narrative with a confidence score. Review, edit if needed, then commit with:

```
@ato "Write the narrative for SC-7: 'Network boundary protection is implemented
using Azure Firewall with default-deny rules...'"
```

## VS Code Integration

ATO Copilot integrates directly into your VS Code workflow:

- **IaC Diagnostics** — Compliance findings appear as squiggly underlines (CAT I/II → Error, CAT III → Warning)
- **Quick Fix** — Lightbulb Code Actions to apply suggested fixes from STIG findings
- **Hover Info** — Shows NIST control + STIG rule + CAT severity on hover
- **`@ato` Chat Participant** — Ask compliance questions in natural language from the Copilot Chat panel
- **Narrative Governance** — View narrative version history, compare changes, and submit narratives for ISSM review (`compliance_narrative_history`, `compliance_narrative_diff`, `compliance_submit_narrative`)
- **HW/SW Inventory** — Register deployed software components and keep versions current (`inventory_add_item`, `inventory_update_item`)

## What's Next

- [Full Engineer Guide](../guides/engineer-guide.md) — Complete Implement/Assess/Monitor workflows
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase details
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable Engineer cheat sheet

## CAC Simulation Mode (Local Development)

When developing locally without a physical CAC/PIV smart card, enable simulation mode to bypass hardware authentication.

### Enabling Simulation

In `appsettings.Development.json`, set:

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

### Switching Identities

Change the `Roles` array to test different personas:

| Persona | Roles |
|---------|-------|
| ISSO | `["ISSO", "Global Reader"]` |
| Platform Engineer | `["Platform Engineer"]` |
| SCA | `["SCA", "Global Reader"]` |
| AO | `["AO", "Global Reader"]` |

Restart the application after changing the identity configuration.

### CI/CD Usage

Integration tests use simulation mode automatically. Configure `CacAuthOptions` in test fixtures:

```csharp
builder.Services.Configure<CacAuthOptions>(o =>
{
    o.SimulationMode = true;
    o.SimulatedIdentity = new SimulatedIdentityOptions
    {
        UserPrincipalName = "test.isso@dev.mil",
        DisplayName = "Test ISSO",
        Roles = ["ISSO"]
    };
});
```

### Production Safety

Simulation mode **only activates in the Development environment**. In Production or Staging, the flag is ignored and a security warning is logged. The `appsettings.json` (production config) must never contain simulation keys.

### Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `InvalidOperationException: SimulatedIdentity configuration is required` | `SimulationMode` is true but no identity block | Add `SimulatedIdentity` section to config |
| `InvalidOperationException: UserPrincipalName` | UPN is empty or missing | Set a valid UPN like `"dev.user@dev.mil"` |
| Warning: "Simulation mode will be ignored" | `SimulationMode` is true in non-Development env | Expected behavior — remove the flag or switch to Development |

## Common First-Day Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| "No assigned tasks" returned | ISSO or ISSM has not yet created Kanban tickets for your system | Ask your ISSO to assign findings from the assessment or check that your system is in the Implement phase |
| IaC scan finds no issues on an empty file | Scanner requires actual resource definitions to analyze | Add Bicep/Terraform resource blocks before scanning |
| "Access denied" on authorization or assessment tools | `Compliance.PlatformEngineer` cannot assess controls or issue authorization | Assessment is done by the SCA; authorization by the AO — focus on implementation and remediation |

## AI Backend Configuration

ATO Copilot supports multiple AI providers for natural language processing. Configure the provider in `appsettings.json`:

### Provider Selection

Set `AzureAi:Provider` to choose the active AI provider and `AzureAi:Enabled` to enable AI processing:

| Provider Value | Description |
|----------------|-------------|
| `OpenAi` | Azure OpenAI GPT-4o via `IChatClient` — **default** |
| `Foundry` | Azure AI Foundry Agents with server-side orchestration |

When `AzureAi:Enabled` is `false` (or the `AzureAi` section is absent), all agents use deterministic tool routing only.

### Azure AI Foundry Configuration

When using `Foundry`, configure the unified `AzureAi` section in `appsettings.json`:

```json
{
  "AzureAi": {
    "Enabled": true,
    "Provider": "Foundry",
    "Endpoint": "https://<openai-endpoint>/",
    "DeploymentName": "gpt-4o",
    "FoundryProjectEndpoint": "https://<your-project>.services.ai.azure.us/",
    "RunTimeoutSeconds": 60,
    "MaxToolIterations": 10,
    "CloudEnvironment": "AzureGovernment"
  }
}
```

**Authentication**: The Foundry provider uses `DefaultAzureCredential` (Managed Identity in production, Azure CLI locally). No API key is needed.

**Fallback chain**: `Foundry` → `OpenAi` (IChatClient) → deterministic routing. If Foundry fails, the system automatically falls back.

---

## Enterprise Hardening Features (Feature 029)

### Resilience

All HTTP clients use Polly retry + circuit breaker + timeout pipelines. Configure via `Resilience:Pipelines` in `appsettings.json`.

### Cache Management

The `clear_cache` tool clears the in-memory response cache. Useful when testing or after data changes.

### Offline Mode

Set `Server:OfflineMode=true` for IL6 air-gapped operation. NIST control lookups, STIGs, and cached data remain available. AI-dependent operations return `OFFLINE_UNAVAILABLE`.

### Monitoring

Access Prometheus metrics at `/metrics` when `OpenTelemetry:EnablePrometheus=true`. Configure OTLP export via `OpenTelemetry:OtlpEndpoint`.

### Pagination

All collection responses are paginated (default 50, max 100). Add `?page=1&pageSize=25` to `/mcp/tools` or pass `page`/`pageSize` in the chat context.
