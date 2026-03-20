# System Intake Wizard

> A guided 7-step wizard for registering new systems and completing initial RMF setup in the Compliance Dashboard.

---

## Overview

The System Intake Wizard walks users through creating a new Registered System and completing all foundational setup tasks required before entering the RMF lifecycle. Instead of navigating multiple pages, the wizard consolidates the entire intake process into a single modal flow accessible from the Portfolio Dashboard.

Systems created through the wizard that have not completed all setup steps display a **"Setup Incomplete"** badge in the Portfolio view.

## Starting the Wizard

1. Navigate to the **Systems** page in the Compliance Dashboard
2. Click the **"+ Add System"** button
3. The wizard opens as a full-screen modal overlay

## Wizard Steps

### Step 1: System Registration

Register the new system with required metadata.

| Field | Required | Description |
|-------|----------|-------------|
| **System Name** | Yes | Unique name (max 200 characters). Duplicate names are rejected. |
| **Acronym** | No | Short identifier (max 20 characters) |
| **System Type** | Yes | Major Application, Enclave, or Platform IT |
| **Mission Criticality** | Yes | Mission Critical, Mission Essential, or Mission Support |
| **Hosting Environment** | Yes | Where the system is hosted (e.g., Azure Government, On-Premises) |
| **Description** | No | Free-text description (max 2000 characters). AI-assisted generation available. |

After completing this step, the system is created and assigned a unique ID used by all subsequent steps.

### Step 2: Security Capabilities

Link existing security capabilities (e.g., firewalls, SIEM, MFA) to the system.

- **Search** capabilities by name, category, or provider
- **Select** one or more capabilities to link
- **Remove** linked capabilities before proceeding
- This step can be **skipped** and completed later from the system's detail page

### Step 3: System Components

Add Person, Place, or Thing components that make up the system.

- **Person**: Named individuals (with optional email)
- **Place**: Physical or logical locations
- **Thing**: Hardware, software, or other technical assets

Each component has a name, type, optional sub-type, description, and owner.

### Step 4: Authorization Boundaries

Define authorization boundaries for the system.

| Field | Required | Description |
|-------|----------|-------------|
| **Boundary Name** | Yes | Unique name within the system |
| **Boundary Type** | Yes | Physical, Logical, or Hybrid |
| **Description** | No | Description of the boundary scope |
| **Primary** | No | Mark one boundary as the primary boundary |

### Step 5: Assign RMF Roles

Assign personnel to the five standard RMF roles:

1. **Authorizing Official (AO)**
2. **Information System Security Manager (ISSM)**
3. **Information System Security Officer (ISSO)**
4. **Security Control Assessor (SCA)**
5. **System Owner**

Role assignments select from existing **Person** components in the organization. This step can be skipped.

### Step 6: Verify Roles

Review a read-only summary of all role assignments made in Step 5:

- Role name, assigned person, and assignment date
- Navigate **Back** to Step 5 to make corrections
- If Step 5 was skipped, shows "No roles assigned" message

### Step 7: Set Security Categorization

Set the system's FIPS 199 security categorization using SP 800-60 information types.

1. **Search** and select applicable SP 800-60 information types
2. Review auto-populated **Confidentiality, Integrity, and Availability** impact levels
3. **Override** any recommended impact level if needed (Low, Moderate, High)
4. The overall **FIPS 199 category** is computed as the high-water mark across all selected types
5. Optionally mark as a **National Security System** and provide **justification**

Clicking **Finish** saves the categorization and displays the Completion Summary.

## Navigation

| Action | Behavior |
|--------|----------|
| **Next** | Validates and persists current step data, advances to next step |
| **Back** | Returns to the previous step; data is preserved |
| **Skip** | Available on Steps 2–6. Advances without saving data for that step. |
| **Cancel** | Discards unsaved data for the current step and closes the wizard |
| **Step Indicator** | Click a completed step to navigate back to it |

> Forward-skipping to unreached steps is not allowed. You can only navigate backward to steps you have already visited.

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| **Duplicate system name** | Step 1 shows an inline validation error. Choose a different name. |
| **Network failure** | Error is displayed inline. Retry the operation. |
| **Session expiry** | Unsaved data for the current step is lost. Previously saved steps are preserved. |
| **Setup Incomplete badge** | Appears on the Portfolio view when a system is missing boundaries, roles, or categorization. |
| **Resuming setup** | Navigate to the system's detail page to complete skipped steps individually. |

## Setup Completion Criteria

A system is considered **setup complete** when all three conditions are met:

1. At least one **authorization boundary** is defined
2. At least one **active RMF role** is assigned
3. A **security categorization** is saved

Until all three are met, the system displays the "Setup Incomplete" badge.
