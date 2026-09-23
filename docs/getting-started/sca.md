# Getting Started: Security Control Assessor (SCA)

> First-time setup and orientation for Security Control Assessor users.

---

## Prerequisites

| Requirement | Details |
|------------|---------|
| **Access** | CAC enrolled with `Compliance.Auditor` role |
| **Independence** | Organizationally independent from the implementation team (per DoDI 8510.01) |
| **Tools** | Microsoft Teams (Adaptive Cards) or MCP API client |
| **Knowledge** | Assigned as SCA to one or more systems by the ISSM |

!!! warning "Read-Only Role"
    As SCA you have **read-only** access. You cannot modify narratives, fix findings, or issue authorization decisions. If you attempt a write operation, Security Posture Intelligence Navigator will return an RBAC denial with explanation.

## First-Time Setup

1. **Verify your identity and read-only role**

    ```
    "What role am I logged in as?"
    ```

    Expected result: `Compliance.Auditor`.

2. **Review the system's SSP before assessment**

    ```
    "Show narrative progress for system {id}"
    ```

    Expected result: Per-control-family narrative completion percentages — this tells you how much of the SSP is written.

    ```
    "Show the baseline for system {id}"
    ```

    Expected result: Applied security control baseline (Low/Moderate/High) with control count.

3. **Begin your first assessment**

    ```
    "Assess control AC-1 as Satisfied using the Examine method —
     policy document reviewed and current"
    ```

    Expected result: Assessment recorded with NIST method, result, and justification. Date/time stamped.

## Your First 3 Commands

### 1. Assess a Control

> **"Assess control AC-1 as Satisfied using the Examine method — policy document reviewed and current"**

Expected result: Assessment recorded with finding result (Satisfied/Other Than Satisfied), method (Test/Interview/Examine), and assessor justification.

### 2. Take a Compliance Snapshot

> **"Take a snapshot of system {id} before assessment begins"**

Expected result: Point-in-time snapshot captured. Use this to compare pre- vs. post-assessment state.

### 3. Generate the SAR

> **"Generate the Security Assessment Report for system {id}"**

Expected result: SAR document generated with all assessed controls, findings, risk levels, and supporting evidence references.

## What's Next

- [Full SCA Guide](../guides/sca-guide.md) — Complete assessment workflows and DoD CAT severity
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase details
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable SCA cheat sheet

## Common First-Day Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| "Access denied: write operation not permitted for Compliance.Auditor" | SCA is a read-only role; you attempted a write operation (e.g., edit narrative) | Assessment recording is the only write you can perform; all other writes require ISSO or ISSM role |
| "No systems found" | ISSM has not yet assigned you as SCA | Ask your ISSM to run `compliance_assign_rmf_role` with your identity |
| Snapshot comparison fails | No prior snapshot exists to compare against | Take a baseline snapshot first, then take another after your assessment round |
