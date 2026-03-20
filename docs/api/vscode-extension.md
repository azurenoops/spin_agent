# VS Code Extension Reference

> Chat commands, diagnostics, code actions, webview panels, and configuration.

---

## Table of Contents

- [Overview](#overview)
- [Chat Participant](#chat-participant)
- [Commands](#commands)
- [Webview Panels](#webview-panels)
- [Diagnostics & Code Actions](#diagnostics--code-actions)
- [Configuration](#configuration)
- [Architecture](#architecture)

---

## Overview

The ATO Copilot VS Code extension provides an `@ato` chat participant for inline compliance workflows, webview panels for RMF visualization, and IaC compliance diagnostics.

---

## Chat Participant

### `@ato`

Invoke the ATO Copilot agent in VS Code Chat:

```
@ato scan my Terraform files for compliance issues
@ato show compliance status
@ato register a new system called "My App"
@ato what controls apply to my system?
```

The `@ato` participant routes messages to the MCP server's ComplianceAgent and renders responses with structured formatting.

### Supported Intents

| Pattern | Tool Invoked | Description |
|---------|-------------|-------------|
| `scan`, `assess` | `compliance_assess` | Run compliance assessment |
| `register system` | `compliance_register_system` | Register new system |
| `categorize` | `compliance_categorize_system` | FIPS 199 categorization |
| `select baseline` | `compliance_select_baseline` | NIST baseline selection |
| `write narrative` | `compliance_write_narrative` | SSP narrative authoring |
| `generate ssp` | `compliance_generate_ssp` | SSP document generation |
| `remediate`, `fix` | `compliance_remediate` | Execute remediation |
| `show findings` | `compliance_status` | Compliance status |
| `pim activate` | `pim_activate_role` | PIM elevation |
| `show dashboard` | `compliance_multi_system_dashboard` | Portfolio dashboard |
| `generate package` | `compliance_generate_package` | eMASS authorization package |
| `package status` | `compliance_package_status` | Package generation status |
| `validate package` | `compliance_validate_package` | Pre-submission readiness check |
| `package history` | `compliance_list_packages` | List generated packages |
| `validate oscal` | `compliance_validate_oscal_schema` | OSCAL schema validation |
| `generate sar` | `compliance_generate_sar` | Security Assessment Report |
| `edit sar` | `compliance_edit_sar_section` | Edit SAR section |
| `review sar` | `compliance_review_sar` | SAR review/approval |

---

## Commands

| Command | Description |
|---------|-------------|
| `ATO Copilot: Show RMF Overview` | Open RMF overview webview panel |
| `ATO Copilot: Scan Current File` | Scan active editor file for compliance |
| `ATO Copilot: Show Compliance Status` | Display compliance posture summary |

---

## Webview Panels

### RMF Overview Panel

A rich webview panel showing the system's RMF lifecycle status with interactive visualization.

**Activation:** Command palette → `ATO Copilot: Show RMF Overview`

**Features:**

1. **RMF Lifecycle Stepper** — Visual 7-step stepper showing:
   - ✅ Completed steps (green)
   - 🔵 Current step (blue, highlighted)
   - ⬜ Upcoming steps (gray)
   - Step icons: 📋 Prepare, 📊 Categorize, 🎯 Select, 🔧 Implement, 🔍 Assess, ✅ Authorize, 📡 Monitor
   - Click any step to navigate

2. **System Details Grid** — Two-column grid:
   - System Type, Hosting Environment
   - Mission Criticality, System Status
   - Active Alerts count

3. **Security Categorization** — FIPS 199 display:
   - C/I/A impact levels with color-coded badges (High=red, Moderate=yellow, Low=green)
   - FIPS category label

4. **Control Baseline Progress** — Progress bar:
   - Implemented/total controls ratio
   - Baseline name
   - Percentage indicator

5. **ATO Status** — Authorization badge:
   - Status (Authorized, Expired, Pending, None)
   - Expiration date
   - Color-coded urgency

6. **Actions Section** — Quick action buttons:
   - View Compliance Details
   - Generate SSP
   - Run Assessment

**Message Handling:**

| Message Type | Action |
|-------------|--------|
| `viewStep` | Navigate to specific RMF step |
| `viewCompliance` | Open compliance details |
| `refresh` | Reload panel data |

**Data Interface:**

```typescript
interface RmfSystemOverview {
  systemName: string;
  acronym?: string;
  systemType?: string;
  hostingEnvironment?: string;
  currentRmfStep?: string;
  rmfStepNumber?: number;
  missionCriticality?: string;
  impactLevel?: string;
  complianceScore?: number;
  activeAlerts?: number;
  isActive?: boolean;
  atoStatus?: string;
  atoExpiration?: string;
  categorization?: {
    fipsCategory?: string;
    confidentialityImpact?: string;
    integrityImpact?: string;
    availabilityImpact?: string;
  };
  controlBaseline?: {
    baselineName?: string;
    totalControls?: number;
    implementedControls?: number;
  };
}
```

**Theming:** uses VS Code CSS variables (`--vscode-editor-background`, `--vscode-editor-foreground`, etc.) for consistent theming in light and dark modes.

---

## Diagnostics & Code Actions

### IaC Compliance Scanning

The extension scans Infrastructure-as-Code files for compliance issues:

**Supported File Types:**
- Terraform (`.tf`, `.tfvars`)
- ARM Templates (`.json` with `$schema` containing `deploymentTemplate`)
- Bicep (`.bicep`)

**Diagnostic Severity Mapping:**

| CAT Level | VS Code Severity | Description |
|-----------|-----------------|-------------|
| CAT I | Error | Critical — must fix before deployment |
| CAT II | Warning | Significant — should fix |
| CAT III | Information | Advisory — recommended fix |

### Code Actions

When diagnostics are present, the extension offers code actions:

| Action | Description |
|--------|-------------|
| `Fix: Apply Remediation` | Auto-fix the compliance finding |
| `Explain Control` | Show NIST control details |
| `Accept Risk` | Mark as accepted risk (requires AO approval) |
| `View STIG Details` | Show related STIG rule |

---

## Configuration

### Extension Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `atoCopilot.mcpServer.url` | string | `http://localhost:3001` | MCP server URL |
| `atoCopilot.mcpServer.mode` | string | `http` | Transport mode (`http` or `stdio`) |
| `atoCopilot.autoScan` | boolean | false | Auto-scan IaC files on save |
| `atoCopilot.showInlineHints` | boolean | true | Show inline compliance hints |
| `atoCopilot.catSeverityFilter` | string | `CatI,CatII` | Minimum CAT level to show |

---

## Architecture

```
VS Code Extension
├── src/
│   ├── extension.ts          — Activation, command registration
│   ├── chat/                 — @ato chat participant handler
│   ├── panels/
│   │   ├── rmfOverviewPanel.ts   — Webview lifecycle (vscode API)
│   │   └── rmfOverviewHtml.ts    — Pure HTML builder (no vscode dep)
│   ├── diagnostics/          — IaC compliance scanner
│   └── providers/            — Code action providers
├── test/
│   └── suite/
│       └── rmfPanel.test.ts  — 38 unit tests (mocha + chai)
└── package.json              — Extension manifest
```

### Design Decisions

- **Pure HTML builder separation** — `rmfOverviewHtml.ts` has no `vscode` import, enabling unit testing with standard mocha/chai without VS Code test runner
- **VS Code CSS variables** — All theming uses CSS custom properties for automatic light/dark mode support
- **XSS escaping** — All user-provided text is HTML-escaped via `escapeHtml()` before rendering
- **Message-based communication** — Webview ↔ extension communication via `postMessage` / `onDidReceiveMessage`

---

## Boundary-Aware Chat Queries (Feature 033)

The `@ato` chat participant supports boundary-aware queries:

| Query Pattern | Description |
|---------------|-------------|
| `@ato list boundary definitions for [system]` | List all boundaries |
| `@ato create a logical boundary named "Production"` | Create boundary |
| `@ato delete boundary [name/id]` | Delete boundary (reassigns to Primary) |
| `@ato run boundary gap analysis for [system]` | Gap analysis with comparison |
| `@ato gap analysis for Production boundary` | Filtered to one boundary |
| `@ato compare boundary coverage` | Compare coverage across boundaries |
