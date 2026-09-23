# Security Posture Intelligence Navigator — M365 Extension for Teams

Express.js webhook server that integrates Security Posture Intelligence Navigator with Microsoft Teams, delivering compliance assessment results as Adaptive Cards.

## Features

- **Teams Bot** — Chat with Security Posture Intelligence Navigator directly in Microsoft Teams
- **Adaptive Cards** — Rich, intent-routed card responses for compliance, infrastructure, cost, deployment, and resource discovery
- **Azure Government** — "View in Azure Portal" links to `portal.azure.us`
- **Follow-Up Prompts** — Interactive quick-reply buttons for missing information
- **M365 Copilot Plugin** — Plugin manifest for M365 Copilot integration

## Setup

### Prerequisites

- Node.js 20 LTS or later
- Security Posture Intelligence Navigator MCP Server running

### Installation

```bash
cd extensions/m365
npm install
```

### Configuration

Set required environment variables:

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ATO_API_URL` | Yes | — | MCP Server base URL |
| `ATO_API_KEY` | No | `""` | API key if required |
| `PORT` | No | `3978` | Express server port |
| `BOT_ID` | Yes* | — | Azure Bot registration app ID |
| `BOT_PASSWORD` | Yes* | — | Azure Bot registration password |

\* Required for Bot Framework authentication. Logged as warning if missing.

### Running

```bash
# Development
ATO_API_URL=http://localhost:3001 npm run dev

# Production
npm run build
ATO_API_URL=http://localhost:3001 npm start
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/messages` | Teams webhook — receives messages, returns Adaptive Cards |
| GET | `/health` | Health check |
| GET | `/openapi.json` | OpenAPI 3.0 specification |
| GET | `/ai-plugin.json` | M365 Copilot plugin descriptor |

## Authentication

Feature 051 (US6) wires Teams SSO + OAuthPrompt fallback into the bot.
The auth dispatcher decides per-message whether to attempt an SSO
token-exchange or fall back to the Bot Framework `OAuthPrompt`. Once
an identity is linked the link is **Teams-tenant-wide** — one
sign-in satisfies mobile, desktop, and web for the same Teams user.

### `AUTH_TEAMS_SSO_MODE` — three deployment-wide modes

The mode is a **deployment-wide** setting (no per-tenant override) per
the clarification recorded in
[`specs/051-login/spec.md`](https://github.com/azurenoops/ato-copilot/blob/051-login/specs/051-login/spec.md)
Q1. Set it via the env var `AUTH_TEAMS_SSO_MODE` (or its config
equivalent `Auth:TeamsSso:Mode` on the .NET side).

| Mode | Meaning | Manifest requirement |
| --- | --- | --- |
| `Required` | SSO is mandatory. The bot attempts a silent token exchange via Bot Framework SSO on every unlinked message. If exchange fails the user sees a hard error — no OAuthPrompt fallback. | The Teams app manifest **MUST** declare a `webApplicationInfo` block with the SSO app id + resource. The manifest validator (Phase 9.1 / T112–T113) **fails startup** with a clear log line when the entry is missing. |
| `Optional` (default) | The bot attempts SSO first. If SSO is unavailable (the manifest has no `webApplicationInfo`, the user has not consented to the app, etc.) the bot **falls back** to OAuthPrompt and asks the user to sign in. | Optional — the bot tolerates either manifest shape. |
| `Disabled` | The bot **always** uses OAuthPrompt; SSO is never attempted. | Not required. |

### Setting the mode

```bash
# Production — Teams SSO required, manifest must include webApplicationInfo
AUTH_TEAMS_SSO_MODE=Required npm start

# Default — try SSO, fall back to OAuthPrompt
AUTH_TEAMS_SSO_MODE=Optional npm start

# Force OAuthPrompt regardless of manifest capability
AUTH_TEAMS_SSO_MODE=Disabled npm start
```

### First-mention experience

When an unlinked Teams user `@mentions` the bot for the first time:

- In `Required` / `Optional` mode the bot attempts an SSO token
  exchange via Bot Framework. On success the link is bound silently.
- On failure (or in `Disabled` mode) the bot replies with an
  **Adaptive Card** containing a "Sign in" button — never a
  free-text "please sign in" message. The button triggers an
  `OAuthPrompt` flow.
- After sign-in the bot stores the identity link in `IIdentityLinkStore`
  keyed by `(teams-tenant-id, oid)` so the same identity covers every
  Teams client (mobile, desktop, web).

### Tenant disabled error

If the linked user's impersonated tenant is later set to `Disabled`
(Feature 048), the next message returns the standard "Tenant disabled"
adaptive card — not a Bot Framework stack trace.

## Adaptive Card Types

| Intent | Card | Description |
|--------|------|-------------|
| `compliance` | Compliance Assessment | Score with color thresholds, control counts |
| `infrastructure` | Infrastructure Result | Resource details, Azure Portal link |
| `cost` | Cost Estimate | Monthly cost breakdown |
| `deployment` | Deployment Result | Status, logs |
| `resource_discovery` | Resource List | Name, type, status table |
| (default) | Generic | Plain text response |
| (error) | Error | Error message with help text |
| (followUp) | Follow-Up | Missing fields with quick-reply buttons |

### Score Color Thresholds

- **≥80%** — Green (Good)
- **≥60%** — Orange (Warning)
- **<60%** — Red (Attention)

## Teams App Registration

1. Register a bot in the [Azure Bot Service](https://portal.azure.us/#create/Microsoft.AzureBot)
2. Set `BOT_ID` and `BOT_PASSWORD` environment variables
3. Deploy the manifest from `src/manifest/` to your Teams tenant

## Development

```bash
npm install
npm run build
npm test
```

### Testing

```bash
# Run all tests
npm test

# Health check
curl http://localhost:3978/health

# Send a test message
curl -X POST http://localhost:3978/api/messages \
  -H "Content-Type: application/json" \
  -d '{"text": "Run compliance scan", "conversation": {"id": "test-1"}, "from": {"id": "user-1"}}'
```

## License

MIT
