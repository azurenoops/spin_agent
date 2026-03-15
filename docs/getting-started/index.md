# Getting Started

> First-time setup and orientation for all ATO Copilot users.

---

## Choose Your Role

ATO Copilot is organized by persona. Select your role below to see prerequisites, first-time setup, and your first 3 commands:

| Persona | Role | Getting Started Guide |
|---------|------|----------------------|
| **ISSM** | Information System Security Manager | [ISSM Getting Started](issm.md) |
| **ISSO** | Information System Security Officer | [ISSO Getting Started](isso.md) |
| **SCA** | Security Control Assessor | [SCA Getting Started](sca.md) |
| **AO** | Authorizing Official | [AO Getting Started](ao.md) |
| **Engineer** | Platform Engineer / System Owner | [Engineer Getting Started](engineer.md) |

---

## General Prerequisites

Before using ATO Copilot in any role, ensure you have:

| Requirement | Details |
|------------|---------|
| **CAC Enrollment** | Common Access Card enrolled with ATO Copilot — mapped by thumbprint, Azure AD group, or Azure RBAC |
| **Azure Subscription** | Access to the Azure Government subscription(s) being assessed |
| **Interface Access** | At least one supported interface: VS Code, Microsoft Teams, MCP API, or CLI |

## How RBAC Works

Your role is automatically resolved from your CAC certificate through a 4-tier chain:

1. **Explicit mapping** — Your CAC thumbprint is directly mapped to a role
2. **Azure AD group** — You belong to an Azure AD group assigned a role
3. **Azure RBAC** — Your Azure RBAC permissions on the subscription determine your role
4. **Default** — If no mapping is found, you are assigned `Compliance.PlatformEngineer`

To check your current role:

```
"What role am I logged in as?"
```

---

## Supported Interfaces

### VS Code (GitHub Copilot Chat)

**Best for**: Engineers, ISSOs

Type `@ato` in the GitHub Copilot Chat panel, then use slash commands:

| Command | Purpose |
|---------|---------|
| `/compliance` | Compliance scanning, assessment, remediation |
| `/knowledge` | NIST, STIG, RMF, FedRAMP knowledge queries |
| `/config` | Server configuration and connection settings |

### Microsoft Teams (M365 Bot)

**Best for**: ISSMs, AOs, SCAs

Message the ATO Copilot bot in Teams. Responses use Adaptive Cards for dashboards, assessments, and approvals.

### MCP Server API

**Best for**: Automation, scripting, CI/CD

All tools are accessible via REST, Server-Sent Events (SSE), or stdio transport. Authentication uses CAC certificates.

### CLI

**Best for**: Engineers, ISSOs who prefer command-line workflows

Direct MCP tool invocations for scripting and automation.

---

## What's Next

After completing your persona-specific getting-started guide:

- [Persona Guides](../personas/index.md) — Full workflow reference for your role
- [RMF Phase Reference](../rmf-phases/index.md) — Step-by-step walkthrough of all 7 RMF phases
- [NL Query Reference](../guides/nl-query-reference.md) — Natural language query examples
- [Quick Reference Cards](../reference/quick-reference-cards.md) — Printable cheat sheets

---

## Dashboard Setup

The Visual Compliance Dashboard provides a web-based view of your compliance posture.

### Prerequisites

- Node.js 18+ and npm
- MCP server running (provides the REST API)

### Quick Start

```bash
cd src/Ato.Copilot.Dashboard
cp .env.example .env.local
npm install
npm run dev
```

Open `http://localhost:5173` in your browser.

### Configuration

Edit `.env.local` to point to your MCP server:

```
VITE_API_BASE_URL=http://localhost:5000/api/dashboard
VITE_POLL_INTERVAL_MS=15000
```

See the [Dashboard Guide](../guides/compliance-dashboard.md) for full feature documentation.
