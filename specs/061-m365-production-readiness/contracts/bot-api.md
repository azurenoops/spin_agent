# Contract: Bot API (Epic 061)

This document specifies the HTTP API surface of the M365 Teams bot Express.js server and the
SSE streaming contract used internally between the bot and the ATO Copilot API.

---

## Endpoints

### POST /api/messages

The primary Bot Framework messaging endpoint. Teams calls this for every user message, Adaptive
Card action invoke, and auth token event.

#### Request

```
POST /api/messages
Content-Type: application/json
Authorization: Bearer <Bot Framework JWT>
```

**Body** — Bot Framework `Activity` object (partial, most relevant fields):

```typescript
interface Activity {
  type: 'message' | 'invoke' | 'event' | 'installationUpdate';
  id: string;                        // Activity ID
  timestamp: string;                 // ISO 8601
  serviceUrl: string;                // Bot Framework service URL (used for proactive messages)
  channelId: 'msteams';
  from: {
    id: string;                      // Teams user AAD object ID
    name: string;                    // Display name
    aadObjectId: string;             // Same as id for Teams
  };
  conversation: {
    id: string;                      // Conversation ID (used as identity store partition key)
    tenantId: string;                // AAD tenant ID
    isGroup: boolean;
  };
  recipient: {
    id: string;                      // Bot App ID (= BOT_ID)
    name: string;
  };
  text?: string;                     // User message text (present for type=message)
  value?: Record<string, unknown>;   // Adaptive Card action data (present for type=invoke)
  name?: string;                     // Event name (present for type=event)

  // Teams-specific channel data
  channelData?: {
    tenant: { id: string };
    teamsChannelId?: string;
    teamsTeamId?: string;
  };
}
```

#### Response — Standard

```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "type": "message",
  "attachments": [
    {
      "contentType": "application/vnd.microsoft.card.adaptive",
      "content": { /* Adaptive Card JSON — see Card Payloads below */ }
    }
  ]
}
```

#### Response — Proactive ACK (SSE timeout path)

When an assessment will take >25 seconds, the bot immediately returns:

```
HTTP/1.1 202 Accepted
Content-Type: application/json

{}
```

The bot then sends a proactive message to the conversation when the assessment completes.

#### Response — Auth Required

When `AUTH_TEAMS_SSO_MODE=Required` and no valid token is available:

```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "type": "message",
  "attachments": [
    {
      "contentType": "application/vnd.microsoft.card.oauth",
      "content": {
        "text": "Please sign in to use ATO Copilot.",
        "connectionName": "<AUTH_TEAMS_SSO_CONNECTION_NAME>",
        "buttons": [{ "type": "signin", "title": "Sign In", "value": "<sign-in URL>" }]
      }
    }
  ]
}
```

#### Response — Error

```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "type": "message",
  "text": "An error occurred. Please try again or contact your administrator."
}
```

> **Note**: Bot Framework requires HTTP 200 even for error responses. The bot must not return
> 4xx/5xx to Bot Framework except for authentication failures (401).

---

### GET /health

Liveness probe endpoint. Checked by Azure Container Apps and Azure Bot Service.

#### Request

```
GET /health
```

No authentication required.

#### Response

```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "ok",
  "version": "1.0.0",         // from package.json
  "timestamp": "2026-06-03T00:00:00.000Z"
}
```

**Failure response** (e.g., ATO API unreachable — optional deep health check):

```
HTTP/1.1 503 Service Unavailable
Content-Type: application/json

{
  "status": "degraded",
  "checks": {
    "atoApi": "unreachable"
  }
}
```

---

### GET /openapi.json

Returns the OpenAPI 3.0 specification for the bot's AI plugin interface.

#### Response

```
HTTP/1.1 200 OK
Content-Type: application/json

{ /* OpenAPI 3.0 JSON */ }
```

---

### GET /ai-plugin.json

Returns the Copilot AI plugin manifest (used by Microsoft 365 Copilot to discover the plugin).

#### Response

```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "schema_version": "v1",
  "name_for_human": "ATO Copilot",
  "name_for_model": "ato_copilot",
  "description_for_human": "...",
  "description_for_model": "...",
  "api": { "type": "openapi", "url": "/openapi.json" },
  "auth": { "type": "oauth" }
}
```

---

## Adaptive Card Payloads

All Adaptive Card content is rendered by card factory functions in `extensions/m365/src/cards/`.

### Common Card Envelope

```typescript
interface AdaptiveCardAttachment {
  contentType: 'application/vnd.microsoft.card.adaptive';
  content: AdaptiveCard;
}

interface AdaptiveCard {
  type: 'AdaptiveCard';
  version: '1.5';
  body: CardElement[];
  actions?: Action[];
  $schema: 'http://adaptivecards.io/schemas/adaptive-card.json';
}
```

### Card Types

| Card | Function | Trigger |
|---|---|---|
| `authorizationCard` | ATO authorization status and action items | User asks about authorization |
| `complianceCard` | Compliance posture summary | User asks about compliance |
| `dashboardCard` | System overview dashboard | Default / welcome |
| `kanbanBoardCard` | Task board for remediation items | User asks about open tasks |
| `interimAssessmentCard` | "Assessment in progress" placeholder | SSE timeout at 25 s |
| *(+16 more)* | Various compliance workflow cards | Context-dependent |

### Interim Assessment Card (SSE Timeout)

Sent when the upstream assessment SSE stream exceeds `SSE_TIMEOUT_MS`:

```json
{
  "type": "AdaptiveCard",
  "version": "1.5",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "body": [
    {
      "type": "TextBlock",
      "text": "⏳ Assessment in Progress",
      "weight": "Bolder",
      "size": "Medium"
    },
    {
      "type": "TextBlock",
      "text": "Your assessment is running. You'll receive the results as a message in this conversation when it completes.",
      "wrap": true
    }
  ]
}
```

---

## SSE Streaming Contract

The bot uses `sseClient.ts` to consume SSE streams from the ATO Copilot API for long-running
assessment operations.

### Upstream SSE Connection

```
GET {ATO_API_URL}/api/assessments/{id}/stream
Authorization: Bearer {ATO_API_KEY}
Accept: text/event-stream
```

### SSE Event Format

```
event: progress
data: {"step": "collect", "percent": 30, "message": "Collecting findings..."}

event: progress
data: {"step": "analyze", "percent": 70, "message": "Analyzing controls..."}

event: complete
data: {"assessmentId": "...", "summary": {...}, "findings": [...]}

event: error
data: {"code": "ASSESSMENT_FAILED", "message": "..."}
```

### Bot-Side SSE Client Options

```typescript
interface SseClientOptions {
  url: string;            // Upstream SSE URL
  headers?: Record<string, string>;
  timeoutMs?: number;     // Default: SSE_TIMEOUT_MS env var (default 25000)
}
```

### Timeout Behavior

```
T=0s    Bot connects to upstream SSE, sends immediate ACK to Teams (202)
T=0–25s Bot relays progress events (no Teams message sent per event — too chatty)
T=25s   SSE_TIMEOUT_MS fires:
          → Bot sends interimAssessmentCard to conversation
          → Internal polling/reconnect continues
T=N     Upstream SSE closes with 'complete' event:
          → Bot sends proactive message with final result card
T=N     Upstream SSE closes with 'error' event:
          → Bot sends proactive message with error card
```

### Proactive Message Auth

To send proactive messages, the bot must:
1. Capture `serviceUrl` and `conversation` reference from the original incoming activity.
2. Store the reference in memory (or identity store) keyed by `conversationId`.
3. Use `BotFrameworkAdapter.continueConversation()` with the stored reference to send the
   proactive message.

The stored conversation reference must be cleaned up after the proactive message is sent.
