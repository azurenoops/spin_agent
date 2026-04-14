# Mission Owner Guide

> System mission authority — provides business context, user information, and system purpose details for RMF documentation.

---

## Role Overview

- **Full Title**: Mission Owner
- **Abbreviation**: MO
- **RBAC Role**: `MissionOwner`
- **Primary RMF Phases**: Categorize (Support), Implement (Support)
- **Key Responsibility**: Complete the System Profile with mission statement, user categories, data types, environment details, ports/protocols, and leveraged authorizations. Provide business-context narratives for flagged controls.
- **Reports to**: ISSM (for system security matters)
- **Primary Interface**: Dashboard (System Profile pages), VS Code (`@ato`)

---

## Permissions

| Capability | Allowed | Tool / UI |
|-----------|---------|-----------|
| View system profile | ✅ | `compliance_get_system_profile`, Dashboard Profile pages |
| Save profile section drafts | ✅ | `compliance_save_profile_section`, Dashboard Profile form |
| Submit sections for ISSM review | ✅ | `compliance_submit_profile_section`, Dashboard Submit button |
| Withdraw sections from review | ✅ | `compliance_submit_profile_section` (action=withdraw), Dashboard Withdraw button |
| View profile completeness | ✅ | `compliance_get_profile_completeness`, Dashboard completeness bar |
| Save business context drafts | ✅ | `compliance_save_business_context` |
| Review/approve profile sections | ❌ | ISSM only |
| Assign roles | ❌ | ISSM only |
| Write SSP narratives | ❌ | ISSO only |

---

## Typical Workflow

1. **Assignment**: ISSM assigns Mission Owner role for a specific system via `compliance_assign_rmf_role`
2. **Notification**: MO receives email notification and To Do task in dashboard
3. **Profile Completion**: MO fills in each profile section:
   - Mission & Purpose (mission statement, business purpose, operational justification, business functions)
   - Users & Access (access overview, authentication method, user categories)
   - Environment & Deployment (hosting model, network zones, DR posture)
   - Data Types (data overview, data type entries with sensitivity classifications)
   - Ports, Protocols & Services (PPS overview, PPS entries with justifications)
   - Leveraged Authorizations (optional — external authorization documentation)
4. **Submit for Review**: MO submits completed sections for ISSM review
5. **Address Feedback**: If ISSM requests revision, MO reviews comments and edits sections
6. **Business Context**: MO drafts business-context narratives for flagged controls (visible to ISSOs)

---

## Three-Tier Governance Model

| Tier | Description | Roles |
|------|-------------|-------|
| **One — Author** | Draft and edit system profile content | Mission Owner, System Owner |
| **Two — Review** | Approve or request revision of submitted content | ISSM |
| **Three — Incorporate** | Merge approved content into SSP narratives | ISSO |

---

## Dashboard Features

- **Your Profile Tasks** panel (visible only when role = MissionOwner)
- **Profile Completeness** progress bar (5 mandatory sections, Leveraged Auth is optional)
- **Governance Status** badges per section (NotStarted, Draft, UnderReview, Approved, NeedsRevision)
- **Role Switcher** in top nav for dev/test simulation
- **ProfileIncompleteBanner** on System Detail page when sections are incomplete
