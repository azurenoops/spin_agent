# Spec 419 — OSCAL Import/Export UI/UX

**Wave:** 7  
**Status:** implementing  
**Owner:** Mr. Terrific  
**Epic:** #415 (closed)  
**Issue:** #419  

## Summary

Deliver two polished UI flows completing the OSCAL support epic:

**A. OSCAL SSP Import Wizard** — wire the existing `OscalImportWizard` component (already built, Feature 076 T011) into the `ImportedDocumentsView` admin page. Users click 'Import OSCAL SSP' to open the 4-step wizard (Upload → Validate → Preview → Commit).

**B. OSCAL Export Dialog Upgrades** — refactor `ExportSspDialog` to surface OSCAL SSP as a first-class download card (schema version badge, inline validation status), remove OSCAL from the generic format picker, and add 'OSCAL 1.1.2' version labels to supplemental artifacts (POA&M, SAP, AR).

## Backend Contract

All backend endpoints are deployed (Feature 076):

```
POST /api/systems/{systemId}/oscal/import/ssp?mode=preview
POST /api/systems/{systemId}/oscal/import/ssp?mode=full
GET  /api/systems/{systemId}/oscal/import/runs
GET  /api/v1/systems/{systemId}/packages/oscal-ssp
```

## Files Changed

| File | Change |
|------|--------|
| `features/oscal/OscalImportWizard.tsx` | Export `ValidationBadge` as named export |
| `features/oscal/index.ts` | Re-export `ValidationBadge` |
| `features/admin/imported-documents/ImportedDocumentsView.tsx` | Add CTA, wizard state, KIND_LABELS entry, filter chip |
| `components/ExportSspDialog.tsx` | OSCAL SSP first-class card, schema version badges, remove 'json' from format picker |
| `src/__tests__/features/oscal/OscalImportWizard.test.tsx` | New unit tests |
| `src/__tests__/components/ExportSspDialog.oscal.test.tsx` | New unit tests |

## Acceptance Criteria

- [ ] ImportedDocumentsView has 'Import OSCAL SSP' CTA
- [ ] OscalImportWizard mounts on CTA click
- [ ] ExportSspDialog format picker no longer includes 'OSCAL JSON (.json)'
- [ ] ExportSspDialog has 'OSCAL Documents' section with SSP first-class card
- [ ] 'OSCAL 1.1.2' schema version badge visible on SSP card
- [ ] ValidationBadge exported from features/oscal/index.ts
- [ ] All Vitest tests pass
- [ ] tsc --noEmit clean
- [ ] CI: Dashboard Production Bundle Audit passes
