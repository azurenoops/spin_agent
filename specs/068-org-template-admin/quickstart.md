# Quickstart — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **Owner:** Mr. Terrific (dev setup)

---

## Prerequisites

- Local dev bootstrapped: `scripts/bootstrap.sh` or `scripts/bootstrap.ps1`
- Node 20+ installed (`node --version`)
- Dashboard dependencies installed: `cd src/Ato.Copilot.Dashboard && npm ci`

---

## Frontend-Only Development (MSW Mocks)

```bash
cd src/Ato.Copilot.Dashboard
npm run dev     # Vite dev server on http://localhost:5173
```

Add MSW handlers for the template and seed endpoints in `src/mocks/handlers/`:

```typescript
// Template endpoints
rest.get('/api/onboarding/templates/', (req, res, ctx) => res(ctx.json({ ok: true, data: [] }))),
rest.post('/api/onboarding/templates/upload', (req, res, ctx) => res(ctx.json({ ok: true, data: { template: {...}, warnings: [] } }))),
rest.delete('/api/onboarding/templates/:id', (req, res, ctx) => res(ctx.status(204))),
rest.post('/api/onboarding/templates/:id/default', (req, res, ctx) => res(ctx.json({ ok: true, data: {...} }))),
rest.delete('/api/onboarding/templates/:id/default/clear', (req, res, ctx) => res(ctx.status(204))),
rest.get('/api/onboarding/templates/:id/download', (req, res, ctx) => res(ctx.body(new Blob(['test content'])))),

// Seed endpoints
rest.get('/api/onboarding/narrative-seeds/', (req, res, ctx) => res(ctx.json({ ok: true, data: [] }))),
rest.post('/api/onboarding/narrative-seeds/', (req, res, ctx) => res(ctx.status(202), ctx.json({ ok: true, data: { document: { id: '...', indexingStatus: 'Pending', ... }, indexJobId: null } }))),
rest.delete('/api/onboarding/narrative-seeds/:id', (req, res, ctx) => res(ctx.status(204))),
```

---

## Verifying Route Registration

After adding route to `App.tsx`:
1. Navigate to `http://localhost:5173/admin/templates`
2. Expected: TemplatesAdminPage renders with "Document Templates" tab and 5 template type slots

If you see a blank page:
- Check the import is at top of `App.tsx`
- Check route path is exactly `/admin/templates` (no trailing slash)
- Check route is inside the authenticated route group (`<RequireAuth>`)

---

## Testing Template Slots

1. Mock `GET /api/onboarding/templates/` to return one template of type `Ssp`
2. Expected: SSP slot shows the template with label, version, file size; other 4 slots show empty "Upload" CTA
3. Click "Set as Default" → mock `POST .../default` → expected: Default badge appears on SSP slot
4. Click "Delete" on default template → expected: extra warning "This is the active default template"

---

## Testing Seed Polling

To test polling behavior:
1. Mock seed list to return a seed with `indexingStatus: 'Pending'`
2. After 5 seconds, change the mock to return `indexingStatus: 'Indexed'`
3. Expected: badge updates from amber/spinner to green/checkmark without page refresh

In unit tests, use `vi.useFakeTimers()` + `vi.advanceTimersByTime(5000)` to trigger the polling interval.

---

## Task #314 Gate Verification

After Task #314 completes and `docs/narrative-seeds.md` is created:

1. Read `docs/narrative-seeds.md` to understand the confirmed pipeline
2. If seeds are now injected into prompts:
   ```bash
   # Integration test from #314:
   # 1. Upload a seed with a unique phrase (e.g. "ORACLE-TEST-PHRASE-XYZ")
   # 2. Generate a narrative for a relevant control
   # 3. Confirm the phrase or derived content appears in the output
   ```
3. Update `specs/068-org-template-admin/spec.md` US3 acceptance criteria with confirmed text

---

## Running Tests

```bash
cd src/Ato.Copilot.Dashboard
npm test                       # Vitest
npm run test -- --watch        # Watch mode
npm run build                  # TypeScript + Vite build check
```

---

## Common Pitfalls

- **Download exposes storage URL:** The `StorageBlobKey` field starts with `wizard/templates/...` — this is an internal storage path. Never construct a download URL from it. Always call `GET /api/onboarding/templates/{id}/download` and use `URL.createObjectURL(blob)`.
- **Set Default race condition:** Don't update the default badge optimistically before the API resolves — wait for the `markTemplateDefault` promise and then reload the list or update state atomically.
- **Polling not stopping:** If seeds keep polling after all are `Indexed`, check that the `useEffect` dependency includes `seeds` and the interval is cleared when no `Pending` seeds remain.
- **Upload endpoint path:** The template upload is `POST /api/onboarding/templates/upload` (not `POST /api/onboarding/templates/`). The `onboarding.uploadTemplate()` method in `onboardingApi.ts` already calls the correct path — don't bypass the client.
- **Claiming seeds improve AI output before #314:** Do NOT update product copy or status labels to say "Active in AI" or "Influencing AI" until Task #314 confirms this is true. The handler is a documented stub.
