# Quickstart: Mission System Details

**Feature**: 046-mission-system-details  
**Date**: 2026-03-26

## Prerequisites

- .NET 9.0 SDK installed
- Node.js 18+ and npm (for dashboard)
- Project builds: `dotnet build Ato.Copilot.sln`
- Existing seed data with at least one `RegisteredSystem` and `RmfRoleAssignment`

## Build & Test

```bash
# Build entire solution
dotnet build Ato.Copilot.sln

# Run unit tests
dotnet test tests/Ato.Copilot.Tests.Unit/ --filter "FullyQualifiedName~SystemProfile"

# Run integration tests
dotnet test tests/Ato.Copilot.Tests.Integration/ --filter "FullyQualifiedName~SystemProfile"

# Build dashboard
cd src/Ato.Copilot.Dashboard && npm run build
```

## Database Migration

After adding new entities to `AtoCopilotContext`:

```bash
cd src/Ato.Copilot.Core
dotnet ef migrations add AddSystemProfileEntities --context AtoCopilotContext
dotnet ef database update --context AtoCopilotContext
```

## Smoke Test: MCP Tools

### 1. Get profile (empty)

```
Tool: compliance_get_system_profile
Args: { "system_id": "MySystem" }
Expected: 6 sections all in NotStarted status, 0% completeness
```

### 2. Save mission & purpose

```
Tool: compliance_save_profile_section
Args: {
  "system_id": "MySystem",
  "section_type": "MissionAndPurpose",
  "content": {
    "missionStatement": "This system provides...",
    "businessPurpose": "Supports the mission of...",
    "operationalJustification": "Required for...",
    "businessFunctions": "Enables..."
  }
}
Expected: Section saved as Draft, completionPercentage > 0
```

### 3. Submit for review

```
Tool: compliance_submit_profile_section
Args: { "system_id": "MySystem", "section_types": ["MissionAndPurpose"] }
Expected: Section transitions to UnderReview
```

### 4. ISSM approves

```
Tool: compliance_review_profile_section
Args: {
  "system_id": "MySystem",
  "section_type": "MissionAndPurpose",
  "decision": "approve"
}
Expected: Section transitions to Approved
```

### 5. Check completeness

```
Tool: compliance_get_profile_completeness
Args: { "system_id": "MySystem" }
Expected: 1/5 approved (20%), 4 incomplete mandatory sections listed
```

## Smoke Test: Dashboard UI

### 6. Sidebar nav items

```
Action: Log in as Mission Owner, navigate to /systems/{id}
Expected: Left sidebar SYSTEM PROFILE group shows 6 profile section links (Mission & Purpose, Users & Access, Environment, Data Types, Ports & Protocols, Leveraged Auth) each with a governance status badge
```

### 7. Profile section form

```
Action: Click "Mission & Purpose" in sidebar
Expected: Form loads with fields for mission statement, business purpose, operational justification. Save and Submit buttons visible. Status badge shows "Not Started".
Action: Fill in mission statement, click Save
Expected: Success toast, status badge changes to "Draft", sidebar badge updates
```

### 8. Profile Readiness card

```
Action: Navigate to system overview (/systems/{id})
Expected: Metric cards row includes "Profile Readiness" card showing "0/5 approved" and "0%"
Action: Approve 3 mandatory sections via MCP tool, refresh page
Expected: Card updates to "3/5 approved" and "60%"
```

### 9. To Do panel — profile tasks

```
Action: Log in as Mission Owner, navigate to system overview
Expected: To Do panel shows "YOUR PROFILE TASKS" section listing incomplete sections and controls flagged for business context
Action: Log in as ISSO, navigate to same system
Expected: YOUR PROFILE TASKS section is NOT visible (role-filtered)
```

### 10. Business-context side panel on Narratives

```
Action: As Mission Owner, save a business context draft for AC-1 (via MCP tool smoke test)
Action: Log in as ISSO, navigate to Narratives page, expand AC-1 row
Expected: Business Context side panel shows the MO draft with author, date, status badge, and "Copy to Narrative" button
Action: Click "Copy to Narrative"
Expected: Business context text appears in the ISSO's technical narrative textarea
```

### 11. Missing Mission Owner advisory

```
Action: Register a system with no MO assigned, wait (or adjust registration date to 31+ days ago)
Action: Log in as ISSM, navigate to system overview
Expected: "No Mission Owner Assigned" banner appears with "Assign Mission Owner" action
Action: Click "Assign Mission Owner"
Expected: Navigates to role management page
```

### 12. Role switcher — quick role change

```
Action: Open the dashboard, locate the role-switcher widget in the top navigation bar
Expected: Dropdown shows roles: ISSM, ISSO, Mission Owner, Engineer, SCA, AO. A "DEV" badge is visible.
Action: Select "Mission Owner"
Expected: Dashboard re-renders immediately without page reload. "YOUR PROFILE TASKS" appears in To Do panel. Profile sections show edit capabilities.
Action: Switch to "ISSO"
Expected: "YOUR PROFILE TASKS" disappears. Profile sections become read-only. Narratives page shows business-context side panel.
Action: Switch to "ISSM"
Expected: Review/approve buttons appear on UnderReview profile sections. "Assign Mission Owner" action visible if MO not assigned. Batch-approve available.
Action: Switch to "Engineer"
Expected: Profile sections are read-only. Remediation and findings content at full prominence.
Action: Close browser, reopen dashboard
Expected: Previously selected role is restored from localStorage.
```

### 13. Role-aware API header

```
Action: Open browser dev tools (Network tab), select "Mission Owner" in role switcher
Action: Navigate to any system page
Expected: All API requests include header `X-Simulated-Role: MissionOwner`
Action: Switch to "ISSM"
Expected: Subsequent API requests include header `X-Simulated-Role: ISSM`
```

### 14. Withdrawal from UnderReview

```
Tool: compliance_submit_profile_section
Args: { "system_id": "MySystem", "section_types": ["MissionAndPurpose"] }
Expected: Section transitions to UnderReview

Tool: compliance_submit_profile_section
Args: { "system_id": "MySystem", "action": "withdraw", "section_types": ["MissionAndPurpose"] }
Expected: Section transitions back to Draft, audit entry created with action "Withdrawn"

Dashboard: As Mission Owner, navigate to a section in UnderReview status
Expected: "Withdraw" button visible. Click it. Section returns to Draft and becomes editable.
```

### 15. Mission Owner assignment notification

```
Action: As ISSM, assign MissionOwner role to a user for a system
Expected: 
  1. To Do panel for that user shows "Complete System Profile for [System Name]" task on next dashboard visit
  2. Email notification sent to the assigned user's email address with system name and link to profile page
```

## Implementation Order

1. **Models & DbContext** — New entities + migration
2. **Service interface & implementation** — ISystemProfileService + SystemProfileService (incl. withdrawal)
3. **Notification service** — INotificationService (To Do + email on MO assignment)
4. **MCP tools** — 7 tools registered in ComplianceAgent
5. **Unit tests** — Service layer tests (including withdrawal + notification)
6. **Integration tests** — MCP tool endpoint tests
7. **Dashboard types** — Extend types/dashboard.ts with profile and business-context types
8. **Dashboard API modules** — systemProfile.ts + businessContext.ts
9. **Dashboard components** — ProfileSectionForm (incl. Withdraw button), ProfileReadinessCard
10. **Dashboard page** — SystemProfile.tsx with 6 section forms
11. **Dashboard layout** — SystemLayout sidebar nav + System Details tab (5 mandatory / 1 optional)
12. **Dashboard TodoPanel** — YOUR PROFILE TASKS section
13. **Dashboard overview** — ProfileIncompleteBanner, MissingMissionOwnerBanner, metric card placement
14. **Role switcher** — RoleSwitcher.tsx widget + useSettings role extension + X-Simulated-Role header
15. **Role-aware views** — Wire all role-dependent components (incl. Withdraw button visibility)
16. **Narratives page** — Business-context side panel, MO Input tag (FR-030, FR-031)
17. **Documentation** — data-model.md, personas, RACI updates
