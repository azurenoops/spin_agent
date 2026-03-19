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
