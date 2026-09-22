# M365 Extension Contract: Copilot Everywhere — Teams Declarative Agent

**Feature**: 013-copilot-everywhere  
**Date**: 2026-02-26  
**Status**: Complete

---

## Server Identity

| Field | Value |
|-------|-------|
| Name | Security Posture Intelligence Navigator M365 Extension |
| Version | 1.0.0 |
| Runtime | Node.js 20 LTS |
| Framework | Express.js |
| Port | 3978 (default) |

---

## Endpoints

### `POST /api/messages` (FR-037)

Primary webhook endpoint for Teams messages.

**Request** (from Teams Bot Framework):
```json
{
  "type": "message",
  "text": "Run compliance assessment for subscription abc-123",
  "conversation": {
    "id": "19:abc123@thread.v2"
  },
  "from": {
    "id": "user-aad-id",
    "name": "John Doe"
  }
}
```

**Response** — Adaptive Card attachment:
```json
{
  "type": "message",
  "attachments": [
    {
      "contentType": "application/vnd.microsoft.card.adaptive",
      "content": { /* Adaptive Card v1.5 JSON */ }
    }
  ]
}
```

**Error** — HTTP 400 (no message text):
```json
{
  "type": "message",
  "attachments": [
    {
      "contentType": "application/vnd.microsoft.card.adaptive",
      "content": { /* Error Adaptive Card */ }
    }
  ]
}
```

### Internal MCP Server Call (FR-038)

```typescript
// ATOApiClient POST to MCP Server
interface McpRequest {
  conversationId: string;        // "m365-{timestamp}-{random9}"
  message: string;               // Teams message text
  conversationHistory: [];       // Empty (no multi-turn for v1)
  context: {
    source: "m365-copilot";
    platform: "M365";
    userId: string;
    userName: string;
  };
}

// MCP Server response
interface McpResponse {
  response: string;
  agentUsed?: string;
  intentType?: string;           // Routes to specific card builder
  data?: {
    complianceScore?: number;
    passedControls?: number;
    warningControls?: number;
    failedControls?: number;
    resourceId?: string;
    estimatedCost?: number;
    resources?: Array<{ name: string; type: string; status: string }>;
    deploymentStatus?: string;
  };
  requiresFollowUp?: boolean;
  followUpPrompt?: string;
  missingFields?: string[];
}
```

---

### `GET /health` (FR-045)

**Response** (200 OK):
```json
{
  "name": "Security Posture Intelligence Navigator M365 Extension",
  "version": "1.0.0",
  "timestamp": "2026-02-26T14:30:00.000Z"
}
```

### `GET /openapi.json` (FR-046)

OpenAPI 3.0 specification for the `/api/messages` endpoint.

### `GET /ai-plugin.json` (FR-046)

M365 Copilot plugin descriptor:
```json
{
  "schema_version": "v1",
  "name_for_human": "Security Posture Intelligence Navigator",
  "name_for_model": "ato_copilot",
  "description_for_human": "Compliance assessment and remediation for Azure Government",
  "description_for_model": "Use this plugin to run NIST 800-53 compliance assessments, generate remediation scripts, and manage ATO processes for Azure Government subscriptions.",
  "api": {
    "type": "openapi",
    "url": "/openapi.json"
  },
  "logo_url": "/icon.png"
}
```

---

## Adaptive Card Builders (FR-042)

All cards use Adaptive Card schema version 1.5.

### Intent Routing (FR-040)

| `intentType` | Card Builder | Description |
|--------------|-------------|-------------|
| `compliance` | `buildComplianceCard` | Assessment results with score |
| `infrastructure` | `buildInfrastructureCard` | Infrastructure provisioning result |
| `cost` | `buildCostCard` | Cost estimate breakdown |
| `deployment` | `buildDeploymentCard` | Deployment status and logs |
| `resource_discovery` | `buildResourceCard` | Resource list with details |
| (default) | `buildGenericCard` | Generic text response |
| (error) | `buildErrorCard` | Error with help text |
| (followUp) | `buildFollowUpCard` | Follow-up question with quick replies |

### Compliance Assessment Card (FR-043)

```json
{
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "type": "AdaptiveCard",
  "version": "1.5",
  "body": [
    {
      "type": "TextBlock",
      "text": "Security Posture Intelligence Navigator — Compliance Assessment",
      "weight": "Bolder",
      "size": "Large"
    },
    {
      "type": "TextBlock",
      "text": "Overall Compliance Score",
      "weight": "Bolder"
    },
    {
      "type": "TextBlock",
      "text": "${score}%",
      "size": "ExtraLarge",
      "color": "${scoreColor}",
      "weight": "Bolder"
    },
    {
      "type": "ColumnSet",
      "columns": [
        {
          "type": "Column",
          "items": [{ "type": "TextBlock", "text": "✅ Passed", "weight": "Bolder" },
                    { "type": "TextBlock", "text": "${passed}" }]
        },
        {
          "type": "Column",
          "items": [{ "type": "TextBlock", "text": "⚠️ Warning", "weight": "Bolder" },
                    { "type": "TextBlock", "text": "${warning}" }]
        },
        {
          "type": "Column",
          "items": [{ "type": "TextBlock", "text": "❌ Failed", "weight": "Bolder" },
                    { "type": "TextBlock", "text": "${failed}" }]
        }
      ]
    }
  ],
  "actions": [
    { "type": "Action.OpenUrl", "title": "View Full Report", "url": "${reportUrl}" },
    { "type": "Action.Submit", "title": "Generate Remediation Plan", "data": { "action": "remediate" } }
  ]
}
```

**Score color thresholds**: ≥80% → "Good" (green), ≥60% → "Warning" (orange), <60% → "Attention" (red)

### Infrastructure Result Card (FR-044)

Includes "View in Azure Portal" button linking to `https://portal.azure.us/#resource/${resourceId}`.

### Error Card

```json
{
  "type": "AdaptiveCard",
  "version": "1.5",
  "body": [
    { "type": "TextBlock", "text": "⚠️ Error", "weight": "Bolder", "color": "Attention" },
    { "type": "TextBlock", "text": "${errorMessage}", "wrap": true },
    { "type": "TextBlock", "text": "💡 ${helpText}", "wrap": true, "isSubtle": true }
  ]
}
```

### Follow-Up Card (FR-041)

```json
{
  "type": "AdaptiveCard",
  "version": "1.5",
  "body": [
    { "type": "TextBlock", "text": "${followUpPrompt}", "wrap": true },
    { "type": "TextBlock", "text": "Missing information:", "weight": "Bolder" },
    {
      "type": "TextBlock",
      "text": "${numberedMissingFields}",
      "wrap": true
    }
  ],
  "actions": [
    { "type": "Action.Submit", "title": "${field1}", "data": { "quickReply": "${field1}" } },
    { "type": "Action.Submit", "title": "${field2}", "data": { "quickReply": "${field2}" } }
  ]
}
```

---

## ATOApiClient (FR-049)

```typescript
class ATOApiClient {
  private baseUrl: string;
  private timeout: number = 300_000;  // 300 seconds
  private headers = {
    "Content-Type": "application/json",
    "User-Agent": "ATO-Copilot-M365-Extension/1.0.0"
  };

  async sendMessage(text: string, conversationId: string, userId: string): Promise<McpResponse>;
  async checkHealth(): Promise<boolean>;
}
```

---

## Configuration (FR-048)

Environment variables validated on startup:

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ATO_API_URL` | yes | — | MCP Server base URL |
| `ATO_API_KEY` | no | `""` | API key if required |
| `PORT` | no | `3978` | Express server port |
| `BOT_ID` | yes* | — | Azure Bot registration app ID |
| `BOT_PASSWORD` | yes* | — | Azure Bot registration password |

\* Required for Bot Framework authentication. Logged as warning if missing.

---

## Teams App Manifest (FR-047)

```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/teams/v1.17/MicrosoftTeams.schema.json",
  "manifestVersion": "1.17",
  "version": "1.0.0",
  "id": "ato-copilot-m365",
  "name": { "short": "Security Posture Intelligence Navigator", "full": "Security Posture Intelligence Navigator for Microsoft 365" },
  "description": {
    "short": "Compliance assessment and remediation",
    "full": "Security Posture Intelligence Navigator brings NIST 800-53 compliance assessment, remediation planning, and ATO documentation directly into Microsoft Teams."
  },
  "developer": {
    "name": "Security Posture Intelligence Navigator Team",
    "websiteUrl": "https://ato-copilot.azurewebsites.us",
    "privacyUrl": "https://ato-copilot.azurewebsites.us/privacy",
    "termsOfUseUrl": "https://ato-copilot.azurewebsites.us/terms"
  },
  "icons": { "color": "color.png", "outline": "outline.png" },
  "bots": [
    {
      "botId": "${BOT_ID}",
      "scopes": ["personal", "team", "groupChat"],
      "commandLists": [
        {
          "scopes": ["personal"],
          "commands": [
            { "title": "Compliance Assessment", "description": "Run a compliance assessment" },
            { "title": "Remediation Plan", "description": "Generate a remediation plan" },
            { "title": "Control Lookup", "description": "Look up a NIST control" }
          ]
        }
      ]
    }
  ],
  "validDomains": ["ato-copilot.azurewebsites.us"]
}
```

---

## Graceful Shutdown (FR-050)

```typescript
process.on("SIGINT", () => gracefulShutdown("SIGINT"));
process.on("SIGTERM", () => gracefulShutdown("SIGTERM"));

async function gracefulShutdown(signal: string) {
  console.log(`Received ${signal}, shutting down gracefully...`);
  server.close(() => {
    console.log("Server closed");
    process.exit(0);
  });
  setTimeout(() => process.exit(1), 10_000);  // Force exit after 10s
}
```
