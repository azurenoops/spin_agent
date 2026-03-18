# Deviation Management Guide

Deviation management handles false positives, risk acceptances, and control waivers through a governed lifecycle with audit trails.

## Overview

A **deviation** is a formal record that documents why a compliance finding, POA&M item, or control requirement is being excepted from normal enforcement. Three deviation types are supported:

| Type | Purpose | Example |
|------|---------|---------|
| **False Positive** | Scanner incorrectly flagged a finding | STIG check fails but control is actually implemented |
| **Risk Acceptance** | AO accepts residual risk in lieu of remediation | Legacy system cannot be patched without breaking mission capability |
| **Waiver** | Control requirement excluded from a boundary's scope | Shared infrastructure provides equivalent protection |

## Deviation Lifecycle

```
Created (Pending) → ISSM Review → AO Review (CAT I only) → Approved/Denied
                                                              ↓
                                              Approved → Expired (auto) / Revoked (manual)
```

### Status Flow

- **Pending** — Awaiting reviewer action
- **Approved** — Active; linked findings marked FalsePositive/RiskAccepted, POA&Ms marked RiskAccepted
- **Denied** — Reviewer rejected the request
- **Expired** — Past expiration date; linked entities revert to Open/Ongoing automatically
- **Revoked** — Manually revoked; linked entities revert

### CAT I Two-Step Approval

CAT I severity deviations require both ISSM recommendation and AO final decision. The ISSM records their recommendation (Approve/Deny) while the deviation stays in Pending status. The AO then renders the final approval or denial.

## Dashboard Pages

### Deviations Page

Navigate to **Systems → [System] → Deviations** to view all deviations for a system. The page includes:

- **Summary cards** — Counts by status (Pending, Approved, Denied, Expired, Revoked)
- **Filterable table** — Filter by type, status, severity, and search text
- **Detail drawer** — Full deviation details with Review, Revoke, and Extend actions

### Cross-Page Indicators

Deviation information surfaces on other dashboard pages:

- **System Detail** — "Active Deviations" metric card with count and link
- **Remediation** — "View Deviation" link on risk-accepted POA&M items in the detail drawer
- **Assessments** — Purple deviation badges (False Positive / Risk Accepted) on flagged findings
- **Documents** — Waiver count badge in the SSP Sections header
- **Gap Analysis** — "Waived" column and purple badges on control families with active waivers

## Chat Commands (MCP Tools)

Five MCP tools are available for chat-driven deviation management:

### `compliance_request_deviation`

Create a new deviation request.

**Parameters:**
- `system_id` (required) — Target system ID
- `control_id` (required) — NIST 800-53 control ID (e.g., "AC-2")
- `deviation_type` (required) — `FalsePositive`, `RiskAcceptance`, or `Waiver`
- `justification` (required) — Reason for the deviation
- `cat_severity` (required) — `CatI`, `CatII`, or `CatIII`
- `expiration_date` (required) — ISO 8601 date (max 365 days out)
- `compensating_controls` — Description of mitigating measures
- `finding_id` — Link to a specific compliance finding
- `poam_entry_id` — Link to a specific POA&M item
- `boundary_id` — Scope to a specific authorization boundary (for waivers)

### `compliance_review_deviation`

Approve or deny a pending deviation.

**Parameters:**
- `deviation_id` (required) — Deviation to review
- `decision` (required) — `Approve` or `Deny`
- `reviewer_role` — `ISSM` (default) or `AO`
- `comments` — Reviewer comments

### `compliance_list_deviations`

List deviations with filters.

**Parameters:**
- `system_id` (required) — Target system ID
- `status` — Filter by status
- `type` — Filter by deviation type
- `page` / `page_size` — Pagination

### `compliance_revoke_deviation`

Revoke an active deviation.

**Parameters:**
- `deviation_id` (required) — Deviation to revoke
- `reason` (required) — Reason for revocation

### `compliance_extend_deviation`

Extend the expiration date of an approved deviation.

**Parameters:**
- `deviation_id` (required) — Deviation to extend
- `new_expiration_date` (required) — New ISO 8601 date (max 365 days out)
- `justification` — Updated justification

## VS Code Integration

### Request False Positive

Right-click on an ATO Copilot diagnostic in the editor and select **"Request False Positive"** from the context menu. This creates a `FalsePositive` deviation linked to the specific finding.

## Notifications

- **Creation** — Requestor receives confirmation when deviation is submitted
- **Review** — Requestor notified when deviation is approved or denied
- **ISSM Recommendation** — Requestor notified when ISSM records a CAT I recommendation
- **Revocation** — Requestor notified when deviation is revoked
- **Extension** — Requestor notified when expiration is extended
- **Expiration Warnings** — 30-day and 7-day warnings sent automatically
- **Expiration** — Requestor notified when deviation expires
- **Daily Digest** — Pending reviews and upcoming expirations included in the daily compliance digest

## Export Integration

### eMASS POA&M Export

Risk-accepted POA&M items include three additional columns in the eMASS Excel export:
- **Deviation Justification** — The approval justification text
- **Deviation Type** — FalsePositive, RiskAcceptance, or Waiver
- **Deviation Expiration** — Expiration date (yyyy-MM-dd)

### OSCAL SSP Export

The OSCAL SSP JSON export includes deviation data in two locations:
- **Control implementation** — `deviation-type` prop on implemented requirements with active deviations
- **Back-matter resources** — Each approved deviation appears as a resource with severity, expiration, reviewer, and compensating controls metadata

## Todo Panel Items

The deviation system generates actionable todo items:

- **Pending reviews** — "Review N pending deviation requests"
- **Expiring deviations** — "Renew N expiring deviations" (within 30 days)
- **CAT I approvals** — "N CAT I deviations require your approval"
- **Missing evidence** — "N deviations without evidence" (outstanding-info category)
