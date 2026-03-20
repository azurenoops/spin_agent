# Getting Started: ISSM

> First-time setup and orientation for Information System Security Manager users.

---

## Prerequisites

| Requirement | Details |
|------------|---------|
| **Access** | CAC enrolled with `Compliance.SecurityLead` role (thumbprint mapped or Azure AD group membership) |
| **Tools** | Microsoft Teams (primary) or MCP API client |
| **Knowledge** | Familiarity with NIST RMF lifecycle, DoDI 8510.01, and FIPS 199 categorization |

## First-Time Setup

1. **Verify your identity and role**

    ```
    "What role am I logged in as?"
    ```

    Expected result: `Compliance.SecurityLead`

2. **Register your first system**

    Use the **System Intake Wizard** in the Compliance Dashboard for guided setup:

    1. Navigate to **Systems** and click **"+ Add System"**
    2. Complete the 7-step wizard (system details, capabilities, components, boundaries, roles, verification, categorization)
    3. See the [System Intake Wizard Guide](../guides/system-intake-wizard.md) for detailed instructions

    Alternatively, register via chat:

    ```
    "Register a new system called 'My System' as a Major Application
     in Azure Government"
    ```

    Expected result: System registered with a unique ID, current RMF phase set to Prepare.

3. **View the system you just created**

    ```
    "Show system details for {id}"
    ```

    Expected result: System summary showing name, type, hosting environment, RMF phase, and authorization boundary status.

## Your First 3 Commands

### 1. Register a System

> **"Register a new system called 'ACME Portal' as a Major Application with mission-critical designation in Azure Government"**

Expected result: System entity created with RMF phase = Prepare. You receive the system ID for all subsequent commands.

### 2. Identify System Components

Navigate to the system's **Components** page in the dashboard and add your system assets using the People, Places, and Things model. For Azure-hosted systems, click **Discover from Azure** to auto-import cloud resources.

> **"Show components for system {id}"**

Expected result: Component inventory listing all People, Places, and Things for the system.

!!! info "Why components first?"
    Per NIST SP 800-37 Rev 2 (Tasks P-16 → P-17), asset identification precedes boundary definition. Inventory your components first, then define the boundary around them.

### 3. Define the Authorization Boundary

> **"Define the authorization boundary for system {id} — add the production VMs, SQL database, and Key Vault"**

Expected result: Azure resource IDs added to the authorization boundary. ATO Copilot confirms the resource count.

### 4. Assign RMF Roles

> **"Assign Jane Smith as ISSO and Bob Jones as SCA for system {id}"**

Expected result: RMF roles assigned. You can verify with "What roles are assigned to system {id}?"

## What's Next

- [Full ISSM Guide](../guides/issm-guide.md) — Complete 29-step RMF lifecycle workflow
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase details
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable ISSM cheat sheet

## Common First-Day Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| "Role not recognized" | CAC certificate not mapped to any RBAC role | Contact Administrator to map your CAC thumbprint, or verify Azure AD group membership |
| "Cannot register system" | Missing `Compliance.SecurityLead` role | Verify your role with "What role am I logged in as?" — only SecurityLead can register systems |
| "Subscription not found" | Azure subscription not linked to ATO Copilot | Verify the subscription ID and ensure the ATO Copilot service principal has Reader access |

---

## eMASS Authorization Package (Feature 041)

As the ISSM, you are responsible for generating and submitting authorization packages:

1. **Validate readiness**: `compliance_validate_package` — ensure all artifacts pass pre-submission checks
2. **Generate package**: `compliance_generate_package` — creates a ZIP with SSP, POA&M, SAR, SAP, assessment results, and evidence
3. **Track progress**: `compliance_package_status` — monitor artifact-by-artifact generation in real time
4. **Download**: Download the completed ZIP from the Documents page
5. **Submit to eMASS**: Upload the package via eMASS portal

> "Generate an authorization package for [system name]"
