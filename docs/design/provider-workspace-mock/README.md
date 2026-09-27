# Provider workspace and Systems handoff — design prototype

This companion to `../system-overview-mock/pages.html` implements the proposed direction in `../csp-product-validation-2026-09-26.md` as a connected, local HTML prototype. It does not change production routes or services.

Open `index.html` for the interactive workspace. `gallery.html` indexes every designed screen and its screenshot. Provider navigation is Overview, Offerings, Mission systems, Changes, and Administration. Offering tabs keep authorization sources, services, reusable implementations, and evidence together. Mission screens retain the eight navigation groups in the Systems proposal.

All records, organizations, permissions, counts, cloud scopes, control implementations, authorization references, dates, and evidence are synthetic. Names such as Flank Speed, cArmy, and Cloud One identify illustrative provider scenarios, not verified records from those programs. The interface does not connect to Azure, Entra, eMASS, or the production application. No real documents are uploaded. Simulation changes are held in browser memory and reset on reload.

Primary flows:

1. Overview → offering → sources → add sources → analysis → source review → capability → review release.
2. Mission systems → assign service → customer relationship → Systems hosting → apply capability → responsibilities → evidence → document preview → package readiness.
3. Provider change → impact review → Mission Owner impact review → document preview. Monitoring rules show proposed executable conditions and collection health separately.

Use the design toolbar to navigate every screen or inspect empty, unavailable, and restricted-access states. Dialogs show explicit simulated outcomes. Provider publication and Mission Owner adoption remain separate, and eMASS export is never represented as actual receipt or authorization.

Implementation direction: reuse the existing offering, source processing, canonical capability release, hosting association, finding/evidence, impact, and Systems services. These screens are design targets, not evidence of API availability. Use the research report's findings to plan backend work and migration; do not introduce a second set of canonical records.

## Coverage and Systems alignment

36 screens: 28 provider screens and 8 Systems companion screens, plus 3 alternate-state screenshots and 4 mobile examples. Each page is a designed snapshot of a workflow stage; this prototype is not a complete stateful application. The publication/association/adoption sequence retains its selected state in memory, while many forms and detail dialogs illustrate the proposed interaction without persisting data.

- **Provider operations:** overview, offering list/create/overview, source list/import/analysis/review, authorization details, scope view/edit, capabilities list/detail/release, evidence list/detail, finding remediation, mission list/allocation/relationship, changes/impact, monitoring/rule configuration, administration/Entra lookup, history, onboarding/setup completion.
- **System definition:** Environment & hosting shows the actual allocated service relationship. This replaces the need to mix hosting, cloud assessment setup, and all environment fields in one long screen.
- **Controls & evidence:** Applied capabilities, Responsibilities, and Evidence receive the published implementation, explicit customer tasks, and permitted supporting artifacts.
- **ATO package & eMASS:** Document previews and package readiness expose the provider records' concrete contribution and remaining mission gaps. Receipt/import acceptance remains separate from export generation.
- **Continuous monitoring:** Coverage & health and Impact reviews connect service/cloud changes to mission scope, controls, documents, and accountable follow-up.
- **Other Systems pages:** the existing proposed Overview, Assessment & risk, Team, and History pages remain linked rather than recreated.

Provider onboarding accepts sources without forcing the user to inspect every extracted capability/component during setup. It hands that work to the portal's review queue and leaves publication explicit.

## Verification

`verify.cjs` rendered all 36 screens at 1440px, exercised their primary actions, checked document-level overflow at 390px, captured the gallery, and tested publication confirmation, association/adoption prerequisites, retained open customer duties, source review, rule evaluation, offering filtering, and unavailable-state recovery. The run completed without browser script errors. `verification.json` records the results; `screens/index.json` is the complete screen inventory.

Browser checks verify this standalone prototype only. They do not test production authorization, storage, cloud connectivity, extraction quality, or eMASS compatibility. For manual design review, open the gallery, follow the source → release → mission handoff, and use the preview-state selector to inspect empty, unavailable, and restricted variants.
