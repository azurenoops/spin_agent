# Dashboard UI Contracts: Mission System Details

**Feature**: 046-mission-system-details  
**Date**: 2026-03-26

This document defines the dashboard component changes, new components, TypeScript interfaces, and behavioral contracts for the 7 UI enhancement areas specified in User Story 12.

---

## 1. Left Sidebar Navigation — 6 New Profile Section Nav Items (FR-034)

**File**: `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`  
**Change type**: MODIFY existing `navGroups` array

### Contract

Add 6 new items to the existing `SYSTEM PROFILE` nav group (index 0 in `navGroups`), after "Boundaries":

```typescript
// Items to add to navGroups[0].items after "Boundaries"
{ path: 'profile/mission', label: 'Mission & Purpose', badge: governanceStatus, d: '...' },
{ path: 'profile/users', label: 'Users & Access', badge: governanceStatus, d: '...' },
{ path: 'profile/environment', label: 'Environment', badge: governanceStatus, d: '...' },
{ path: 'profile/data-types', label: 'Data Types', badge: governanceStatus, d: '...' },
{ path: 'profile/ports', label: 'Ports & Protocols', badge: governanceStatus, d: '...' },
{ path: 'profile/leveraged-auth', label: 'Leveraged Auth', badge: governanceStatus, d: '...' },
```

### Badge Extension

The existing `NavItem` interface adds an optional badge:

```typescript
interface NavItem {
  path: string;
  label: string;
  end?: boolean;
  d: string;
  badge?: ProfileSectionStatus;  // NEW — governance status badge
}
```

Badges render using the same `approvalVariant()` color mapping from the Narratives page:

| Status | Color | Badge Style |
|--------|-------|-------------|
| `NotStarted` | gray | `bg-gray-100 text-gray-500` |
| `Draft` | amber | `bg-amber-100 text-amber-700` |
| `UnderReview` | blue | `bg-blue-100 text-blue-700` |
| `Approved` | green | `bg-green-100 text-green-700` |
| `NeedsRevision` | red | `bg-red-100 text-red-700` |

Badge renders as a small dot or abbreviated text to the right of the nav label, consistent with the collapsed/expanded sidebar states.

### Data Source

Profile section statuses are fetched alongside the system detail polling. The `SystemDetailResponse` type is extended:

```typescript
interface SystemDetailResponse {
  // ... existing fields ...
  profileSections?: ProfileSectionSummary[];  // NEW
}

interface ProfileSectionSummary {
  sectionType: string;         // e.g., "MissionAndPurpose"
  governanceStatus: string;    // e.g., "Draft", "Approved"
  completionPercentage: number;
}
```

---

## 2. "System Details" Tab — Profile Completeness Summary (FR-035)

**File**: `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`  
**Change type**: MODIFY the `sidePanelTab === 'details'` section

### Contract

Insert BEFORE the existing "System Summary" card inside the "details" tab content:

```typescript
// NEW — Profile Summary Card (rendered above existing System Summary card)
interface ProfileSummaryCardProps {
  systemId: string;
  profileSections: ProfileSectionSummary[];
  missionOwnerName: string | null;
  userRole: string;                          // Current user's role for this system
  onSubmitAll: () => Promise<void>;          // Submit All for Review action
}
```

### Visual Layout

```
┌─────────────────────────────────┐
│ Profile Completeness            │
│ ████████░░░░░░░░ 50%            │  ← Progress bar
│                                 │
│ ✅ Mission & Purpose  Approved  │  ← Section checklist
│ 📝 Users & Access     Draft     │
│ 🔵 Environment        UnderReview  │
│ ⬜ Data Types          NotStarted│
│ ⬜ Ports & Protocols   NotStarted│
│ ⬜ Leveraged Auth      NotStarted│
│                                 │
│ [Submit All for Review]         │  ← Only visible for MissionOwner role
│                                 │
│ Mission Owner: Jane Smith       │  ← Shows assigned Mission Owner or "Not Assigned"
└─────────────────────────────────┘
┌─────────────────────────────────┐
│ Name          My System         │  ← Existing System Summary card (unchanged)
│ Acronym       SYS               │
│ ...                             │
└─────────────────────────────────┘
```

### Behavior

- Progress bar = (approved mandatory sections / 5) × 100
- Section checklist shows status badge using `approvalVariant()` color mapping (5 mandatory sections + Leveraged Auth shown separately)
- "Submit All for Review" button visible only when user has `MissionOwner` role AND at least one mandatory section is in `Draft` or `NeedsRevision`
- Button calls `POST /systems/{systemId}/profile/submit` with no `section_types` (submits all eligible)
- Mission Owner name populated from `GET /systems/{systemId}/profile/completeness` response

---

## 3. "To Do" Panel — YOUR PROFILE TASKS Section (FR-036)

**File**: `src/Ato.Copilot.Dashboard/src/components/cards/TodoPanel.tsx`  
**Change type**: MODIFY

### Contract

Add a profile tasks section above the existing phase-based todo items when the current user has `MissionOwner` role:

```typescript
// NEW — Data from GET /systems/{systemId}/profile/todos
interface ProfileTodoResponse {
  hasProfileTasks: boolean;                 // false → don't render section
  incompleteSections: ProfileTodoItem[];    // Sections in NotStarted/Draft
  revisionSections: ProfileTodoItem[];      // Sections in NeedsRevision (with ISSM feedback)
  flaggedControls: FlaggedControlItem[];    // Controls needing business context
}

interface ProfileTodoItem {
  sectionType: string;
  label: string;                            // Human-readable section name
  status: string;                           // Governance status
  reviewerComments?: string;                // ISSM feedback (for NeedsRevision)
  link: string;                             // Route to the section form
}

interface FlaggedControlItem {
  controlId: string;                        // e.g., "AC-1"
  controlTitle: string;                     // e.g., "Access Control Policy and Procedures"
  hasDraft: boolean;                        // Whether MO has already started a draft
  link: string;                             // Route to the narrative page for this control
}
```

### Visual Layout

```
┌─────────────────────────────────────┐
│ YOUR PROFILE TASKS                  │  ← Section header (amber background)
│                                     │
│ ⚠ Mission & Purpose     Not Started │  ← Click navigates to /profile/mission
│ ⚠ Data Types             Draft      │
│ 🔴 Environment           Revision   │  ← "View ISSM feedback" link
│                                     │
│ Business Context Needed:            │
│  • AC-1  Access Control Policy      │  ← Click navigates to narratives page
│  • PL-1  Planning Policy            │
│                                     │
├─────────────────────────────────────┤
│ Phase: Categorize → Select          │  ← Existing todo items (unchanged)
│ ▸ Complete security categorization  │
│ ▸ Define authorization boundary     │
└─────────────────────────────────────┘
```

### Behavior

- Section only renders when `hasProfileTasks === true` (user has MissionOwner role + tasks exist)
- NeedsRevision items show amber-left border + "View feedback" link to the section
- Flagged controls list shows only unfulfilled controls (no draft yet or draft in NeedsRevision)
- All items are clickable and navigate to the appropriate route
- When all profile tasks are complete, the section is hidden (FR-036)
- Existing todo items render beneath, completely unchanged (FR-040)

---

## 4. Metric Cards Row — Profile Readiness Card (FR-037)

**File**: `src/Ato.Copilot.Dashboard/src/components/cards/ProfileReadinessCard.tsx` (NEW)  
**Also**: `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx` (MODIFY — add to metrics row)

### Contract

```typescript
interface ProfileReadinessCardProps {
  approvedCount: number;     // Number of mandatory sections in Approved status
  totalCount: number;        // Total mandatory sections (always 5; Leveraged Auth is optional)
}
```

This component wraps the existing `MetricCard`:

```typescript
<MetricCard
  title="Profile Readiness"
  value={`${approvedCount}/${totalCount} approved`}
  subtitle={`${Math.round((approvedCount / totalCount) * 100)}%`}
  helpKey="profile-readiness"
/>
```

### Placement

Added to the existing metric cards row in `SystemDetail.tsx`, after the last existing metric card (e.g., after POA&Ms or Narrative Coverage). The card follows the same grid layout as existing cards.

### Data Source

Populated from the `profileSections` field on the extended `SystemDetailResponse`, or from `GET /systems/{systemId}/profile/completeness`.

---

## 5. Profile Incomplete Banner (FR-038, FR-041)

**File**: `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx` (MODIFY)

### Contract

```typescript
interface ProfileIncompleteBannerProps {
  incompleteSections: { sectionType: string; label: string; status: string }[];
  missionOwnerName: string | null;
  isCollapsed: boolean;
  onToggle: () => void;
}
```

### Visual Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│ ⚠ Profile Incomplete                                    [▾ Collapse]│
│                                                                     │
│ The following profile sections need attention:                      │
│ • Mission & Purpose (Not Started)                                   │
│ • Data Types (Draft)                                                │
│ • Ports & Protocols (Needs Revision)                                │
│                                                                     │
│ Assigned Mission Owner: Jane Smith                                  │
└─────────────────────────────────────────────────────────────────────┘
```

### Placement

Renders between the Phase Readiness section and the metric cards row in `SystemDetail.tsx` (FR-038).

### Behavior

- Hidden when all 5 mandatory sections are in `Approved` status
- Collapsible — default expanded, user can collapse/expand
- Shows section names with current status in parentheses (mandatory sections only; Leveraged Auth shown separately if incomplete)
- Shows "Mission Owner: Not Assigned" if no MO role exists, with amber text
- Visible to all roles in read-only mode (FR-041)
- Uses `bg-amber-50 border border-amber-200` styling consistent with existing warning patterns

---

## 6. "System Details" Tab Badge (FR-039)

**File**: `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`  
**Change type**: MODIFY the `sidePanelTabs` array rendering

### Contract

```typescript
// Extended tab definition
const sidePanelTabs = [
  { key: 'todo' as const, label: 'To do', badgeCount: 0 },
  { key: 'details' as const, label: 'System Details', badgeCount: profileAttentionCount },
];
```

Where `profileAttentionCount` = number of sections in `NotStarted`, `Draft`, or `NeedsRevision` status.

### Visual

```
┌──────────────┬──────────────────────┐
│   To do      │  System Details (3)  │  ← Badge count when > 0
└──────────────┴──────────────────────┘
```

### Behavior

- Badge shows as `(N)` appended to tab label text, styled with `text-xs font-medium text-blue-600`
- Badge hidden when count is 0
- Count = sections where status is NOT `Approved` and NOT `UnderReview`

---

## 7. "Missing Mission Owner" Advisory Banner (FR-019)

**File**: `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx` (MODIFY)

### Contract

```typescript
interface MissingMissionOwnerBannerProps {
  systemId: string;
  daysSinceRegistration: number;  // From completeness endpoint
  isIssmRole: boolean;            // Current user has ISSM role for this system
}
```

### Visual Layout

```
┌──────────────────────────────────────────────────────────────────┐
│ ⚠ No Mission Owner Assigned                                     │
│                                                                  │
│ This system was registered 45 days ago and has no Mission Owner. │
│ A Mission Owner is needed to provide system profile details.     │
│                                                                  │
│ [Assign Mission Owner]    ← Only visible to ISSM                 │
└──────────────────────────────────────────────────────────────────┘
```

### Placement

Renders at the top of `SystemDetail.tsx`, before Phase Readiness.

### Behavior

- Only renders when: (a) no MissionOwner role is assigned, AND (b) system was registered 30+ days ago (FR-019)
- "Assign Mission Owner" button only visible when `isIssmRole === true`
- Button navigates to the system's role management page (existing route pattern)
- Uses `bg-red-50 border border-red-200` styling for urgency
- Hidden once a MissionOwner is assigned (disappears on next poll cycle)

---

## New Page: SystemProfile

**File**: `src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx` (NEW)

### Contract

```typescript
interface SystemProfilePageProps {
  // Uses route params for systemId and sectionType
  // Uses useSystemContext() for system data
}
```

### Route

Added to `App.tsx` as a child of the `/systems/:id` layout:

```typescript
<Route path="profile/:sectionType" element={<SystemProfile />} />
```

### Behavior

- Renders the appropriate section form based on `sectionType` route param
- Section types map to tab-specific form configurations:
  - `mission` → Mission & Purpose form (text fields)
  - `users` → Users & Access form (text fields + UserCategory CRUD table)
  - `environment` → Environment & Deployment form (text fields + dropdowns)
  - `data-types` → Data Types form (text fields + DataTypeEntry CRUD table)
  - `ports` → Ports & Protocols form (PpsEntry CRUD table)
  - `leveraged-auth` → Leveraged Authorizations form (LeveragedAuthorization CRUD table)
- Shows governance status badge and action buttons based on current status:
  - `NotStarted` / `Draft` / `NeedsRevision` → "Save" + "Submit for Review" buttons
  - `UnderReview` → Read-only with "Under Review" indicator + "Withdraw" button (Mission Owner can retract before ISSM acts, FR-021a)
  - `Approved` → "Edit" button that creates a new Draft
- If `NeedsRevision`, shows ISSM feedback in a highlighted callout above the form
- Unsaved changes trigger browser beforeunload confirmation (FR-011)
- Character counters on text fields (FR edge case: max limits)

---

## New Component: ProfileSectionForm

**File**: `src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx` (NEW)

### Contract

```typescript
interface ProfileSectionFormProps {
  systemId: string;
  sectionType: string;
  initialContent: Record<string, string> | null;   // Scalar JSON fields
  childItems: ChildItem[];                          // Child entity rows
  governanceStatus: string;
  reviewerComments?: string;
  isReadOnly: boolean;                              // Derived from status + role
  onSave: (content: Record<string, string>, children: ChildItem[]) => Promise<void>;
  onSubmit: () => Promise<void>;
  isSubmitting: boolean;
  error: string | null;
}

interface ChildItem {
  id?: string;
  [key: string]: unknown;
}
```

### Behavior

- Renders controlled form inputs for scalar fields based on `sectionType`
- Renders an editable table for child entities (add/edit/remove rows)
- Follows the `BoundaryForm.tsx` pattern: useState per field, validation check, onSubmit/onCancel
- Uses existing Tailwind CSS classes matching project design system
- Read-only mode disables all inputs and hides action buttons

---

## New API Module: systemProfile.ts

**File**: `src/Ato.Copilot.Dashboard/src/api/systemProfile.ts` (NEW)

### Contract

```typescript
import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface ProfileOverview {
  systemId: string;
  systemName: string;
  missionOwner: { userId: string; displayName: string } | null;
  overallCompleteness: {
    completedCount: number;
    mandatorySections: number;              // Always 5 (mandatory sections denominator)
    allSections: number;                    // Always 6 (all sections including optional)
    approvedCount: number;
    approvedPercentage: number;             // approvedCount / mandatorySections × 100
  };
  sections: ProfileSectionSummary[];
}

export interface ProfileSectionSummary {
  sectionType: string;
  governanceStatus: string;
  completionPercentage: number;
  lastEditedBy: string | null;
  lastEditedAt: string | null;
  reviewerComments: string | null;
}

export interface ProfileSectionDetail {
  sectionId: string;
  sectionType: string;
  governanceStatus: string;
  draftContent: Record<string, string> | null;
  approvedContent: Record<string, string> | null;
  childItems: Record<string, unknown>[];
  reviewerComments: string | null;
  lastEditedBy: string | null;
  lastEditedAt: string | null;
  rowVersion: string;
}

export interface SaveProfileSectionRequest {
  content: Record<string, string>;
  childItems?: Record<string, unknown>[];
  rowVersion?: string;                      // For optimistic concurrency
}

export interface SubmitSectionsRequest {
  action?: 'submit' | 'withdraw';           // Default: 'submit'
  sectionTypes?: string[];                  // Omit for "submit all" / "withdraw all"
}

export interface ReviewSectionRequest {
  decision: 'approve' | 'request_revision';
  comments?: string;
}

export interface ProfileCompletenessResponse {
  systemId: string;
  totalSections: number;
  statusCounts: Record<string, number>;
  approvedPercentage: number;
  isProfileComplete: boolean;
  incompleteSections: { sectionType: string; status: string }[];
  missionOwnerAssigned: boolean;
  missionOwnerName: string | null;
  daysSinceRegistration: number;
}

export interface ProfileTodoResponse {
  hasProfileTasks: boolean;
  incompleteSections: ProfileTodoItem[];
  revisionSections: ProfileTodoItem[];
  flaggedControls: FlaggedControlItem[];
}

export interface ProfileTodoItem {
  sectionType: string;
  label: string;
  status: string;
  reviewerComments?: string;
  link: string;
}

export interface FlaggedControlItem {
  controlId: string;
  controlTitle: string;
  hasDraft: boolean;
  link: string;
}

// ─── API Functions ──────────────────────────────────────────────────────────

export async function getProfileOverview(systemId: string): Promise<ProfileOverview> {
  const { data } = await apiClient.get<ProfileOverview>(
    `/systems/${encodeURIComponent(systemId)}/profile`,
  );
  return data;
}

export async function getProfileSection(
  systemId: string,
  sectionType: string,
): Promise<ProfileSectionDetail> {
  const { data } = await apiClient.get<ProfileSectionDetail>(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}`,
  );
  return data;
}

export async function saveProfileSection(
  systemId: string,
  sectionType: string,
  request: SaveProfileSectionRequest,
): Promise<ProfileSectionDetail> {
  const { data } = await apiClient.put<ProfileSectionDetail>(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}`,
    request,
  );
  return data;
}

export async function submitSections(
  systemId: string,
  request: SubmitSectionsRequest,
): Promise<{ submittedSections: string[]; skippedSections: { sectionType: string; reason: string }[] }> {
  const { data } = await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/profile/submit`,
    request,
  );
  return data;
}

export async function reviewSection(
  systemId: string,
  sectionType: string,
  request: ReviewSectionRequest,
): Promise<{ sectionType: string; newStatus: string }> {
  const { data } = await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}/review`,
    request,
  );
  return data;
}

export async function batchApproveProfile(
  systemId: string,
): Promise<{ approvedSections: string[]; approvedCount: number }> {
  const { data } = await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/profile/batch-approve`,
  );
  return data;
}

export async function getProfileCompleteness(
  systemId: string,
): Promise<ProfileCompletenessResponse> {
  const { data } = await apiClient.get<ProfileCompletenessResponse>(
    `/systems/${encodeURIComponent(systemId)}/profile/completeness`,
  );
  return data;
}

export async function getProfileTodos(
  systemId: string,
): Promise<ProfileTodoResponse> {
  const { data } = await apiClient.get<ProfileTodoResponse>(
    `/systems/${encodeURIComponent(systemId)}/profile/todos`,
  );
  return data;
}
```

### Error Handling

Follows the existing `apiClient` interceptor pattern — errors are caught as `ErrorResponse` objects with `message`, `errorCode`, and `suggestion` fields.

---

## 8. Narratives Page — Business-Context Side Panel (FR-030, FR-031)

**File**: `src/Ato.Copilot.Dashboard/src/pages/Narratives.tsx` (MODIFY)

### Contract

When a control has a `BusinessContextDraft` from a Mission Owner, the expanded row in the narratives table shows a collapsible side panel with the Mission Owner's business-context draft.

```typescript
interface BusinessContextPanelProps {
  controlId: string;
  systemId: string;
  draft: BusinessContextDraftResponse | null;  // null = no draft exists
}

interface BusinessContextDraftResponse {
  draftId: string;
  controlId: string;
  content: string;
  governanceStatus: string;       // Draft, UnderReview, Approved, NeedsRevision
  authoredBy: string;
  authoredAt: string;
  reviewerComments?: string;
}
```

### Visual Layout

Within the expanded narrative row (existing `expanded.has(n.id)` section):

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ AC-1: Access Control Policy and Procedures                                 │
├───────────────────────────────────┬─────────────────────────────────────────┤
│ Technical Narrative (ISSO)        │ Business Context (Mission Owner)        │
│                                   │                                         │
│ [existing textarea for ISSO]      │ "The organization's access control      │
│                                   │  policy was established to ensure..."   │
│                                   │                                         │
│                                   │ Authored by: jane.smith@agency.gov      │
│                                   │ Status: Approved ● 2026-03-20           │
│                                   │                                         │
│                                   │ [Copy to Narrative]                     │
└───────────────────────────────────┴─────────────────────────────────────────┘
```

### Behavior

- Panel only renders when a `BusinessContextDraft` exists for the control (FR-030)
- Panel is collapsible — default expanded on first load, remembers state per session
- Shows draft content, author, date, and governance status badge
- "Copy to Narrative" button copies the business-context text into the ISSO's textarea for incorporation
- When the ISSO submits the combined narrative, the review metadata indicates MO contribution (FR-031) — shown as a small "Includes Mission Owner input" tag on the narrative list row
- Read-only for ISSOs — they cannot edit the Mission Owner's draft directly
- If no draft exists and the control is flagged for business context, shows a muted placeholder: "Awaiting business context from Mission Owner"

### Data Source

- Fetched per-control when the row is expanded: `GET /systems/{systemId}/business-context/{controlId}`
- Flagged control list fetched once on page load: `GET /systems/{systemId}/business-context/flagged-controls`

### Narrative List Row Enhancement

When a narrative has incorporated Mission Owner content (FR-031), the `NarrativeListItem` type is extended:

```typescript
export interface NarrativeListItem {
  // ... existing fields ...
  hasMissionOwnerInput: boolean;  // NEW — true when MO draft was incorporated
}
```

Displayed as a small tag/icon next to the approval status badge:

```
│ AC-1 │ Implemented │ Approved │ 👤 MO Input │
```

---

## New API Module: businessContext.ts

**File**: `src/Ato.Copilot.Dashboard/src/api/businessContext.ts` (NEW)

### Contract

```typescript
import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface BusinessContextDraftResponse {
  draftId: string;
  controlId: string;
  content: string;
  governanceStatus: string;
  authoredBy: string;
  authoredAt: string;
  submittedBy?: string;
  submittedAt?: string;
  reviewedBy?: string;
  reviewedAt?: string;
  reviewerComments?: string;
}

export interface FlaggedControlResponse {
  controlId: string;
  controlTitle: string;
  isFlagged: boolean;
  isDefaultFlag: boolean;    // true = from static -1 list, false = ISSM override
  hasDraft: boolean;
  flaggedBy?: string;
}

export interface SaveBusinessContextRequest {
  content: string;
}

export interface FlagControlRequest {
  controlId: string;
  isFlagged: boolean;
}

// ─── API Functions ──────────────────────────────────────────────────────────

export async function getBusinessContextDraft(
  systemId: string,
  controlId: string,
): Promise<BusinessContextDraftResponse | null> {
  try {
    const { data } = await apiClient.get<BusinessContextDraftResponse>(
      `/systems/${encodeURIComponent(systemId)}/business-context/${encodeURIComponent(controlId)}`,
    );
    return data;
  } catch {
    return null;  // 404 = no draft exists
  }
}

export async function saveBusinessContextDraft(
  systemId: string,
  controlId: string,
  request: SaveBusinessContextRequest,
): Promise<BusinessContextDraftResponse> {
  const { data } = await apiClient.put<BusinessContextDraftResponse>(
    `/systems/${encodeURIComponent(systemId)}/business-context/${encodeURIComponent(controlId)}`,
    request,
  );
  return data;
}

export async function getFlaggedControls(
  systemId: string,
): Promise<FlaggedControlResponse[]> {
  const { data } = await apiClient.get<FlaggedControlResponse[]>(
    `/systems/${encodeURIComponent(systemId)}/business-context/flagged-controls`,
  );
  return data;
}

export async function flagControl(
  systemId: string,
  request: FlagControlRequest,
): Promise<void> {
  await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/business-context/flags`,
    request,
  );
}
```

---

## Type Extensions: types/dashboard.ts

**File**: `src/Ato.Copilot.Dashboard/src/types/dashboard.ts` (MODIFY)

### Extensions

```typescript
// Add to SystemDetailResponse
export interface SystemDetailResponse {
  // ... existing fields ...
  profileSections?: ProfileSectionSummary[];        // NEW — profile section statuses for sidebar badges
  missionOwnerAssigned?: boolean;                   // NEW — whether MO role exists
  missionOwnerName?: string | null;                 // NEW — display name of assigned MO
  daysSinceRegistration?: number;                   // NEW — for 30-day advisory check
}

export interface ProfileSectionSummary {
  sectionType: string;
  governanceStatus: string;
  completionPercentage: number;
}

// Extend NarrativeListItem (if defined here)
export interface NarrativeListItem {
  // ... existing fields ...
  hasMissionOwnerInput: boolean;                    // NEW — FR-031
}
```

---

## Existing Component Modifications Summary

| Component | File | Change |
|-----------|------|--------|
| `SystemLayout` | `components/layout/SystemLayout.tsx` | Add 6 nav items to `navGroups[0]` with governance badges; extend System Details tab with profile summary card; add badge count to tab header |
| `TodoPanel` | `components/cards/TodoPanel.tsx` | Fetch profile todos; render YOUR PROFILE TASKS section above existing items when user has MissionOwner role |
| `SystemDetail` | `pages/SystemDetail.tsx` | Add ProfileReadinessCard to metrics row; add ProfileIncompleteBanner between PhaseReadiness and metrics; add MissingMissionOwnerBanner at top |
| `Narratives` | `pages/Narratives.tsx` | Add business-context side panel in expanded row; show "MO Input" tag on narratives with incorporated MO content (FR-030, FR-031) |
| `App` | `App.tsx` | Add `<Route path="profile/:sectionType" element={<SystemProfile />} />` under `/systems/:id`; mount RoleSwitcher in top nav |
| `useSettings` | `hooks/useSettings.ts` | Add `'MissionOwner'` to `DashboardSettings.role` union type (FR-043) |
| `apiClient` | `api/client.ts` | Add Axios request interceptor for `X-Simulated-Role` header from `settings.role` (FR-048) |
| `SystemDetailResponse` | `types/dashboard.ts` | Extend with `profileSections`, `missionOwnerAssigned`, `missionOwnerName`, `daysSinceRegistration` |
| `NarrativeListItem` | `types/dashboard.ts` | Extend with `hasMissionOwnerInput` (FR-031) |

## New Components Summary

| Component | File | Purpose |
|-----------|------|---------|
| `SystemProfile` | `pages/SystemProfile.tsx` | Profile section page with tabbed forms per section type |
| `ProfileSectionForm` | `components/forms/ProfileSectionForm.tsx` | Reusable form component for profile section editing |
| `ProfileReadinessCard` | `components/cards/ProfileReadinessCard.tsx` | MetricCard wrapper showing approved/total count |
| `systemProfile` | `api/systemProfile.ts` | API client module for profile endpoints |
| `businessContext` | `api/businessContext.ts` | API client module for business-context draft endpoints |
| `RoleSwitcher` | `components/layout/RoleSwitcher.tsx` | Role-switcher dropdown widget for top nav bar (FR-042) |

---

## 9. Role Switcher Widget (FR-042, FR-043, FR-044, FR-047)

**File**: `src/Ato.Copilot.Dashboard/src/components/layout/RoleSwitcher.tsx` (NEW)  
**Also**: `src/Ato.Copilot.Dashboard/src/hooks/useSettings.ts` (MODIFY)  
**Also**: `src/Ato.Copilot.Dashboard/src/api/client.ts` (MODIFY)  
**Also**: `src/Ato.Copilot.Dashboard/src/App.tsx` (MODIFY — mount in top nav)

### Settings Type Extension

In `hooks/useSettings.ts`, extend the `role` field:

```typescript
export interface DashboardSettings {
  // Profile & Identity
  displayName: string;
  role: 'AO' | 'ISSM' | 'ISSO' | 'SCA' | 'Engineer' | 'MissionOwner' | '';  // MODIFIED — added 'MissionOwner'
  organization: string;
  // ... rest unchanged
}
```

### Component Contract

```typescript
import { useSettings } from '../../hooks/useSettings';

interface RoleSwitcherProps {
  // No props — reads/writes via useSettings() context
}

const ROLE_OPTIONS = [
  { value: 'ISSM',          label: 'ISSM',           description: 'Security Lead' },
  { value: 'ISSO',          label: 'ISSO',           description: 'Security Analyst' },
  { value: 'MissionOwner',  label: 'Mission Owner',  description: 'Business Owner' },
  { value: 'Engineer',      label: 'Engineer',       description: 'Platform Engineer' },
  { value: 'SCA',           label: 'SCA',            description: 'Security Assessor' },
  { value: 'AO',            label: 'AO',             description: 'Authorizing Official' },
] as const;
```

### Visual Layout

```
┌───────────────────────────────────────────────────────────────────────┐
│ [Logo]  Eagle Eye Compliance    ... nav ...    ┌─DEV──────────────┐  │
│                                                │ 👤 Mission Owner ▾│  │
│                                                └──────────────────┘  │
└───────────────────────────────────────────────────────────────────────┘
                                                 ┌──────────────────┐
                                                 │ ● ISSM            │
                                                 │   Security Lead   │
                                                 │ ● ISSO            │
                                                 │   Security Analyst│
                                                 │ ● Mission Owner ✓ │
                                                 │   Business Owner  │
                                                 │ ● Engineer        │
                                                 │   Platform Eng.   │
                                                 │ ● SCA             │
                                                 │   Security Assess.│
                                                 │ ● AO              │
                                                 │   Authorizing Off.│
                                                 └──────────────────┘
```

### Behavior

- Renders as a compact dropdown button in the top navigation bar, right-aligned
- Shows current role icon + label in the button. When no role selected, shows "Select Role" in muted text
- Dropdown shows all 6 roles with label + description sub-text
- Active role has a checkmark indicator
- "DEV" badge (dashed amber border, `text-xs font-mono`) is always visible to signal this is a testing aid (FR-044)
- On selection: calls `updateSettings({ role: selectedRole })` — triggers immediate re-render of all consuming components (FR-047)
- Role persists via localStorage (existing `useSettings` behavior) — survives page refresh and browser restart
- No page reload on role change — React context propagation handles re-renders

### API Header Extension

In `api/client.ts`, add a request interceptor:

```typescript
// Add to existing apiClient interceptors
apiClient.interceptors.request.use((config) => {
  const role = localStorage.getItem('ato-dashboard-settings');
  if (role) {
    try {
      const settings = JSON.parse(role);
      if (settings.role) {
        config.headers['X-Simulated-Role'] = settings.role;
      }
    } catch { /* ignore parse errors */ }
  }
  return config;
});
```

**Note**: This reads directly from localStorage (not React state) because Axios interceptors run outside the React tree. The header is a development convenience — it is NOT an authorization mechanism (FR-048).

### Mounting

In `App.tsx`, mount the `RoleSwitcher` in the top-level layout, typically in the header/nav area alongside the existing settings and chat toggle:

```typescript
import RoleSwitcher from './components/layout/RoleSwitcher';

// Inside the top nav / header area:
<RoleSwitcher />
```

---

## 10. Role-Aware View Behavior (FR-045, FR-046)

This is not a single component but a **cross-cutting pattern** applied to all role-dependent components. All components that show/hide content or toggle edit/read-only based on the user's role MUST read from `useSettings().settings.role`.

### Role-to-View Mapping

| Component | MissionOwner | ISSM | ISSO | Engineer | SCA | AO | No role |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Profile section forms — edit mode | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Profile section forms — read-only | — | — | ✅ | ✅ | ✅ | ✅ | ✅ |
| Submit for Review button | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Withdraw from Review button | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Approve/Reject buttons | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Batch-approve action | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| YOUR PROFILE TASKS (To Do) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| MO advisory banner (30-day) | ❌ | ✅ (with action) | ✅ (info) | ✅ (info) | ✅ (info) | ✅ (info) | ✅ (info) |
| Assign Mission Owner action | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Business-context narrative input | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Business-context side panel (ISSO) | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Copy to Narrative button | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Profile Readiness card | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Profile Incomplete banner | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Sidebar profile nav badges | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

### Implementation Pattern

All role-dependent components follow this pattern:

```typescript
import { useSettings } from '../../hooks/useSettings';

function MyComponent() {
  const { settings } = useSettings();
  const role = settings.role;  // 'MissionOwner' | 'ISSM' | 'ISSO' | ... | ''
  
  const isMissionOwner = role === 'MissionOwner';
  const isIssm = role === 'ISSM';
  const isIsso = role === 'ISSO';
  const canEditProfile = isMissionOwner || isIssm;  // per FR-016
  const canReview = isIssm;                          // per FR-023
  const canSubmit = isMissionOwner;                  // per FR-021
  
  return (
    <>
      {canEditProfile && <EditableForm ... />}
      {!canEditProfile && <ReadOnlyView ... />}
      {canSubmit && <SubmitButton />}
      {canReview && <ReviewActions />}
    </>
  );
}
```

### No-Role Prompt

When `settings.role === ''`, display a subtle banner at the top of the system overview:

```
┌─────────────────────────────────────────────────────────────────────┐
│ 💡 Select a role in the top navigation to see your personalized    │
│    dashboard view. Your role determines which actions and tasks    │
│    are visible.                              [Select Role ▸]       │
└─────────────────────────────────────────────────────────────────────┘
```

Clicking "Select Role" opens the RoleSwitcher dropdown.

### Future Migration Path

When CAC/Entra ID authentication is implemented:
1. Remove `RoleSwitcher.tsx` component
2. Replace `settings.role` reads with authenticated user's `RmfRoleAssignment` for the current system
3. Remove `X-Simulated-Role` header interceptor from `api/client.ts`
4. All role-aware view logic in components remains unchanged — it reads from whatever role source is active
