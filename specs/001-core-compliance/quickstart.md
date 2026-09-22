# Quickstart: Core Compliance Capabilities

**Branch**: `001-core-compliance` | **Date**: 2026-02-21

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Azure CLI (`az`) authenticated to an Azure subscription
- For Azure Government: `az cloud set --name AzureUSGovernment && az login`
- Required Azure RBAC roles on target subscription:
  - **Reader** (resource discovery)
  - **Policy Reader** (policy compliance queries) [optional: policy scans]
  - **Security Reader** (Defender for Cloud) [optional: DFC integration]

## Build & Run

```bash
# Build
dotnet build Ato.Copilot.sln

# Run tests
dotnet test

# Run in HTTP mode
cd src/Ato.Copilot.Mcp
dotnet run -- --http
# Server starts at http://localhost:3001

# Run in stdio mode (for GitHub Copilot / Claude Desktop)
cd src/Ato.Copilot.Mcp
dotnet run -- --stdio
```

## End-to-End Walkthrough

### Step 1: Configure Your Subscription

```
User: Set my subscription to abc-123-def-456
Agent: ✅ Default subscription set to abc-123-def-456

User: Set default framework to FedRAMP High
Agent: ✅ Default framework set to FedRAMP High (was NIST 800-53)

User: What's my current configuration?
Agent: Current configuration:
  - Subscription: abc-123-def-456
  - Framework: FedRAMP High
  - Baseline: High
  - Cloud Environment: Azure Government
  - Dry-run default: On
  - Scan type: Combined
```

### Step 2: Run a Compliance Assessment

```
User: Run compliance assessment
Agent: Running combined compliance assessment against subscription abc-123-def-456...
  Scanning storage accounts (15 found)...
  Scanning virtual machines (8 found)...
  Evaluating NIST 800-53 initiative...
  Evaluating custom compliance policies...

  ━━━ Assessment Complete ━━━
  Framework: FedRAMP High | Baseline: High | Scan: Combined
  Score: 85.5% | 360/421 controls passing

  By Family:
  ┌──────┬──────────────────────────┬──────┬──────┬────────┐
  │ AC   │ Access Control           │ 20   │ 5    │ ⚠ High │
  │ AU   │ Audit & Accountability   │ 14   │ 2    │ Medium │
  │ CM   │ Configuration Mgmt       │ 12   │ 2    │ Medium │
  │ SC   │ System & Communications  │ 45   │ 6    │ ⚠ High │
  └──────┴──────────────────────────┴──────┴──────┴────────┘

  Critical findings: 2 | High: 8 | Medium: 15
```

### Step 3: Drill Into a Control Family

```
User: Show me failing AC controls
Agent: Access Control (AC) — 5 failing controls:

  AC-2   Account Management         FAIL  Critical  [Resource: 3 non-compliant]
  AC-2.1 Account Management | Auto  FAIL  High      [Policy: 1 violation]
  AC-6   Least Privilege            FAIL  High      [Resource: 2 non-compliant]
  AC-6.5 Least Privilege | Priv     FAIL  Medium    [Resource: 1 non-compliant]
  AC-17  Remote Access              FAIL  Medium    [Policy: 1 violation]
```

### Step 4: Remediate a Finding

```
User: Fix AC-2
Agent: ⚠️ This remediation affects Access Control settings.
  Applying this could impact user access.

  Remediation Plan (DRY-RUN):
  1. Enable MFA for all privileged accounts (3 accounts)
  2. Disable inactive accounts older than 90 days (1 account)
  3. Assign Azure Policy for account management monitoring

  Type: 2 resource changes, 1 policy assignment
  Risk: HIGH

  Reply 'apply this remediation' to proceed.

User: Apply this remediation
Agent: Applying remediation...
  ✅ Step 1: MFA enabled for 3 privileged accounts
  ✅ Step 2: 1 inactive account disabled
  ✅ Step 3: Policy assignment created
  
  Remediation complete. Run 'validate AC-2' to verify.
```

### Step 5: Collect Evidence

```
User: Collect evidence for AC-2
Agent: Collecting evidence for AC-2 (Account Management)...
  ✅ Azure AD Conditional Access policies exported (JSON)
  ✅ Azure Policy compliance snapshot captured
  ✅ Resource inventory for IAM resources listed
  ✅ Activity log excerpts (last 30 days) collected

  4 evidence artifacts collected and stored.
```

### Step 6: Generate a Document

```
User: Generate SSP
Agent: What system name should I use for the SSP?

User: Security Posture Intelligence Navigator Platform
Agent: Generating System Security Plan for Security Posture Intelligence Navigator Platform...

  ✅ SSP generated (FedRAMP High template)
  Content includes:
  - System description and authorization boundary
  - 421 control implementations
  - 48 open findings with remediation plans
  - Evidence references for completed controls
```

### Step 7: Check History

```
User: Show compliance history for last 30 days
Agent: Compliance trend (last 30 days):
  Feb 01: 78.2% (combined) — 329/421 passing
  Feb 10: 82.1% (combined) — 346/421 passing
  Feb 21: 85.5% (combined) — 360/421 passing
  
  Trend: ↑ +7.3% improvement
  New findings resolved: 31
  New findings discovered: 5
```

## Verification Checklist

After implementation, verify these scenarios work end-to-end:

- [X] Configure subscription, framework, and baseline via natural language
- [X] Run resource-only scan and see findings grouped by resource type
- [X] Run policy-only scan and see findings grouped by policy initiative
- [X] Run combined scan and see correlated results
- [X] Drill into a control family and see individual control status
- [X] Remediate a single finding in dry-run mode
- [X] Apply remediation after confirmation
- [X] Batch remediate by severity or family
- [X] High-risk remediation shows additional warning
- [X] Collect evidence for a specific control
- [X] Generate SSP, SAR, and POA&M documents
- [X] Query compliance history and see trend data
- [X] Query audit log and see all actions logged
- [X] Auditor role cannot run remediation
- [X] Missing subscription prompts user to configure
- [X] Azure API failure shows actionable error message
