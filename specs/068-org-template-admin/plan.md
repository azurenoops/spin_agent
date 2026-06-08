# Implementation Plan — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **PR:** #325 | **Owner:** Mr. Terrific (implementation)  
**Spec author:** Oracle

---

## Overview

Frontend-only spec (no backend changes). All template and seed API methods are implemented in `onboardingApi.ts`. `TemplatesAdminPage.tsx` is already written (untracked file). The work is:
1. Register the route (`App.tsx` — one line)
2. Verify and harden the templates CRUD wiring
3. Add the Narrative Seeds tab
4. Implement seed polling for Pending state
5. Gate on Task #314 for AI pipeline confirmation

**Estimated scope:** ~200 LOC new, ~50 LOC verification/hardening

---

## Phase 1 — Route Registration (Day 1, ~30 min)

**Goal:** Templates admin page accessible at `/admin/templates`.

```bash
# In App.tsx, add after line 120 (near /admin/imported-documents):
<Route path="/admin/templates" element={<RequireAuth><TemplatesAdminPage /></RequireAuth>} />
```

Add to admin nav sidebar: "Templates" link pointing to `/admin/templates`.

**Verification:** `npm run dev` → navigate to `/admin/templates` → page loads with template slots.

---

## Phase 2 — Templates Tab Verification (Day 1, ~2h)

**Goal:** All 6 template actions are correctly wired and tested.

**Checklist:**
1. Open `TemplatesAdminPage.tsx` — confirm `TEMPLATE_SLOTS` renders 5 slots (Ssp, Sar, Sap, Crm, HwSwInventory)
2. Upload flow: `UploadTemplateModal` submits via `onboarding.uploadTemplate({ templateType, label, version, file, isDefault })`; warnings array surfaced below form on `FlaggedNonCompliant`
3. Replace flow: `onboarding.replaceTemplateFile(id, file, version?)` called; `dependentsFlagged` surfaced as warning
4. Set Default: `onboarding.markTemplateDefault(id)` called; previous Default badge cleared immediately; new Default badge shown
5. Clear Default: `onboarding.clearTemplateDefault(id)` called; all Default badges removed
6. Delete: confirmation modal with extra warning if `isDefault = true` ("This is the active default template")
7. Download: blob proxy pattern (see research.md R5) — do NOT construct storage URLs

**Verification:**
- Upload a .docx → slot shows with label/version/size
- Set as default → "Default" badge appears
- Delete default template → extra warning modal shown

---

## Phase 3 — Narrative Seeds Tab (Day 1–2, ~2h)

**Goal:** Seeds tab with full CRUD and status badges.

**Steps:**
1. Add tab state to `TemplatesAdminPage`: `activeTab: 'templates' | 'seeds'`
2. Tab bar at top of page with two tabs; conditionally render template slots or seeds panel
3. Seeds panel:
   - Load on tab activation via `onboarding.listNarrativeSeeds()`
   - Render table: label, `tryFormatTags(tags)`, indexing status badge, `fmtDate(createdAt)`, `fmtKb(fileSizeBytes)` (if available in DTO), delete button
4. "Add Seed" button → modal: file picker (any format) + label (required) + tags (comma-separated textarea, optional)
   - On submit: `onboarding.uploadNarrativeSeed(file, label, tags.split(',').map(t=>t.trim()).filter(Boolean))`
   - New seed appended to list with `Pending` status
5. Delete seed: if `IndexingStatus === 'Indexed'` → confirmation with citation warning; call with `confirmCitations=true`; otherwise simple confirm; call with default (`false`)

**Status badge colors:**
- `Pending` — amber/yellow ring + spinner
- `Indexed` — green ring + checkmark
- `Failed` — red ring + exclamation

---

## Phase 4 — Seed Polling (Day 2, ~1h)

**Goal:** Pending seeds auto-update to Indexed/Failed without page refresh.

**Steps:**
1. On seeds list load: check if any seed has `IndexingStatus === 'Pending'`
2. If yes, start `setInterval(() => onboarding.listNarrativeSeeds(), 5000)`
3. On each poll: update seeds state
4. Stop polling when no seeds remain in `Pending` (clear interval)
5. Use `useEffect` cleanup to clear interval on unmount

```typescript
useEffect(() => {
  const hasPending = seeds.some(s => s.indexingStatus === 'Pending');
  if (!hasPending) return;
  const id = setInterval(async () => {
    const fresh = await onboarding.listNarrativeSeeds();
    setSeeds(fresh);
    if (!fresh.some(s => s.indexingStatus === 'Pending')) clearInterval(id);
  }, 5_000);
  return () => clearInterval(id);
}, [seeds]);
```

---

## Phase 5 — Task #314 Gate

**No code work until #314 completes.** When #314 finishes:
1. Review `docs/narrative-seeds.md` created by #314
2. Update `spec.md` US3 acceptance criteria with confirmed pipeline description
3. If #314 implemented an injection hook: update seed status badge label (e.g., "Active in AI" instead of "Indexed")
4. Run integration test from #314: upload seed with unique phrase → generate narrative → confirm phrase appears

---

## Phase 6 — Testing (Day 2–3, ~1.5h)

**Tests to write:**
- Templates tab: 5 CRUD paths + role guard + FlaggedNonCompliant badge
- Seeds tab: upload, delete with/without citation warning, polling behavior (mock timer)
- Full build + lint pass

---

## Checkpoints

| Checkpoint | Condition |
|---|---|
| Phase 1 complete | Route accessible; nav link visible |
| Phase 2 complete | All 6 template actions functional |
| Phase 3 complete | Seeds tab with CRUD |
| Phase 4 complete | Polling transitions Pending → Indexed |
| #314 gate | Pipeline confirmed or implemented |
| Phase 5 complete | US3 acceptance criteria updated |
| Phase 6 complete | All tests pass; no TypeScript errors |
| Spec-done | PR #325 DoD checked; #314 closed |
