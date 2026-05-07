# Onboarding Administrator Persona Test Cases

**Feature**: 047 | **Persona**: Onboarding Administrator
**Role**: `Tenant.OnboardingAdministrator` | **Interface**: ATO Copilot Dashboard (`/onboarding`)
**Test Cases**: ONB-01 through ONB-10 (10 total)

This document drives the manual end-to-end walkthrough of the tenant
onboarding wizard introduced in Feature 047. Each test exercises a
critical flow surfaced by the wizard, the cascade view, or the imported
documents admin view.

---

## Pre-Execution Setup

1. **Bootstrap the tenant**: An empty `AtoCopilotContext` with **zero** existing
   `RoleAssignment` rows for the tenant. The first authenticated caller will be
   auto-granted the `Tenant.OnboardingAdministrator` role per FR-007.
2. **Sign in** to the dashboard as that caller (Entra ID / dev cookie).
3. Confirm the placeholder `/onboarding` route is reachable and the
   `WizardStepNavigator` lists all seven steps as `Pending`.
4. Have these fixtures available (built by
   `tests/Ato.Copilot.Tests.Integration/TestData/onboarding/build_fixtures.py`):
   - `sample-emass-5-systems.zip`
   - `sample-ssp.pdf`
   - `sample-ssp-encrypted.pdf`
   - `template-ssp.docx`, `template-sar.docx`, `template-sap.docx`
   - `template-crm.xlsx`, `template-hwsw.xlsx`
   - `policy-acme-cybersecurity.pdf`

---

## ONB-01 · Bootstrap Admin Auto-Grant

**Type**: Positive · **Phase**: Pre-wizard

**Steps**

1. Visit `/onboarding` as the first authenticated caller for the tenant.
2. Inspect the auth response in the browser dev-tools network tab.

**Expected**

- HTTP `200` from `GET /api/onboarding/state`.
- `roleAssignments` includes `Tenant.OnboardingAdministrator` for the caller.
- `WizardAuditLog` contains a `BootstrapAdminGranted` row with this user as actor.

**Failure indicators**

- Repeating the same call from a *second* anonymous browser must return
  `WIZARD_BOOTSTRAP_RACE` (FR-008) — verifies the race-protection branch.

---

## ONB-02 · Step 1 + Step 2 Happy Path (SC-001)

**Type**: Positive · **Phase**: 1 + 2

**Steps**

1. On Step 1 enter:
   - Tenant display name: `Acme Corp`
   - Mission summary: `Defensive cyber operations for joint task forces`
   - Compliance framework: `NIST 800-53 Rev 5`
   - Cloud environment: `Azure Government`
2. Save and advance to Step 2.
3. Enter ISSO (`isso@acme.test`), ISSM (`issm@acme.test`), and AO (`ao@acme.test`).

**Expected**

- Step 1 + Step 2 complete in **< 90 seconds** wall-clock (record the value
  in [Performance Spot-Check](#performance-spot-check)).
- `OrganizationContext` row written for the tenant.
- `WizardStepCompletion` rows for `OrganizationContext` and `RoleAssignments`.
- Audit `OrganizationContextUpdated`, `RoleAssignmentChanged` entries.

---

## ONB-03 · eMASS Import — Happy Path (SC-002)

**Type**: Positive · **Phase**: 3

**Steps**

1. Upload `sample-emass-5-systems.zip` on Step 3.
2. Wait for the `EmassParse` job to reach `Succeeded`.
3. Click **Commit** when the preview renders.

**Expected**

- HTTP `202` on upload with `jobId`.
- Importer commits **5 systems** + 25 control-implementation rows in
  **< 5 minutes** (SC-002).
- `EmassImportSession.Status` ends at `Imported`.
- Provenance: every imported `ControlImplementation.Source = "EmassImportSession:{id}"`.

**Failure variants**

- Re-upload an oversized (>200 MiB) zip → expect `WIZARD_EMASS_TOO_LARGE`.
- Upload a malformed zip (bytes truncated) → expect `WIZARD_EMASS_INVALID_FORMAT`.

---

## ONB-04 · eMASS Partial Failure → Skip-and-Resume

**Type**: Negative · **Phase**: 3

**Steps**

1. Re-upload the same `sample-emass-5-systems.zip` after manually editing one
   `controls.json` row to use an invalid `controlId` (e.g. `"AC-?"`).
2. Wait for `EmassCommit` to reach `Failed`.
3. From the failure UI choose **Skip & continue**.

**Expected**

- Wizard navigation advances to Step 4 (Skip-and-resume per FR-052).
- A POST to `/api/onboarding/state/skip` with `step=EmassImport` is recorded.
- Audit `WizardStepSkipped` with `reason="commit-failed"`.
- The `WizardJobStatus` row shows `errorCode=WIZARD_JOB_FAILED`.

---

## ONB-05 · SSP-PDF Rejection Categories

**Type**: Negative · **Phase**: 4

**Steps**

1. Upload `sample-ssp-encrypted.pdf` on Step 4.

**Expected**

- HTTP `400` with `errorCode=WIZARD_SSP_PDF_PASSWORD_PROTECTED`.
- `SspPdfImportSession.Status = Rejected` with
  `RejectReason = PasswordProtected`.

**Repeat** with:

- A purely image-based PDF → `WIZARD_SSP_PDF_NO_TEXT_LAYER`
  (`RejectReason = ImageOnly`).
- A truncated PDF → `WIZARD_SSP_PDF_UNREADABLE`.
- A PDF that doesn't list any recognized framework → `WIZARD_SSP_PDF_UNKNOWN_FRAMEWORK`.

---

## ONB-06 · ARM Consent Declined → Skip Step 5

**Type**: Negative · **Phase**: 5

**Steps**

1. On Step 5 click **Authorize Azure subscriptions**.
2. In the consent prompt deny consent (or sign in with an account that
   lacks the `Microsoft.Resources/subscriptions/read` permission).

**Expected**

- HTTP `400` from `GET /api/onboarding/azure/subscriptions` with
  `errorCode=WIZARD_ARM_CONSENT_REQUIRED` and a
  `suggestion` field that links the admin to a runbook.
- The Skip button is enabled. Skipping records
  `WizardStepCompletion(Status=Skipped, Reason="arm-consent-declined")`.

**Variant**

- After granting consent, retry against an account with **no visible**
  subscriptions to verify `WIZARD_ARM_NO_SUBSCRIPTIONS_VISIBLE`.

---

## ONB-07 · Custom Template Upload + Validation Feedback (SC-004)

**Type**: Positive + Negative · **Phase**: 6

**Steps**

1. Upload `template-ssp.docx` as the `Ssp` template.
2. Upload `template-crm.xlsx` as the `Crm` template.
3. Upload a deliberately malformed file (e.g. rename `template-ssp.docx` to
   `template-ssp.txt`).

**Expected**

- Successful uploads return HTTP `200` and surface a
  `ValidationStatus = Compliant` row in the table within **< 10 s**.
- The renamed `.txt` upload returns
  `errorCode=WIZARD_TEMPLATE_WRONG_FORMAT`.
- An oversized file (>50 MiB) returns
  `errorCode=WIZARD_TEMPLATE_TOO_LARGE`.
- Replacing the SSP template re-emits an `ExportRerender` job for every
  dependent `SspExport` (cascade contract).

---

## ONB-08 · Narrative Seeds Upload + Citation Guard

**Type**: Positive + Negative · **Phase**: 7

**Steps**

1. Upload `policy-acme-cybersecurity.pdf` with label `Acme Cybersecurity Policy`
   and tags `["policy", "ac"]`.
2. Wait until `IndexingStatus = Indexed`.
3. Attempt to delete it without `?confirmCitations=true`.

**Expected**

- Step 2: `NarrativeSeedDocument` row exists with `IndexingStatus=Indexed` and
  a populated `IndexJobId`.
- Step 3: HTTP `409` with
  `errorCode=WIZARD_NARRATIVE_SEED_HAS_CITATIONS`.
- After repeating with `?confirmCitations=true` the row is soft-deleted
  (`Status=Deleted`, `DeletedAt!=null`) and an audit trail entry is written.

---

## ONB-09 · Re-run Wizard After Template Replace

**Type**: Cascade · **Phase**: Admin

**Steps**

1. Visit `/admin/imported-documents`.
2. Filter to `Templates`.
3. Click into the row for the SSP template uploaded in ONB-07 → review the
   dependents drawer (should list any `SspExport` rows).
4. Click **Re-run** for one dependent.

**Expected**

- HTTP `202` with a fresh `jobId`.
- `WizardJobStatus` row created with `JobType=ExportRerender`,
  `Status=Pending` → `Succeeded`.
- After completion the dependent's `IsStale` flag clears, and
  `LastReRunJobId` points at the job id.

---

## ONB-10 · Admin View Performance (SC-014)

**Type**: Performance · **Phase**: Admin

**Steps**

1. Seed at least 200 templates (the inventory test in
   `ImportedDocumentsManagementViewTests` already does this — re-use that
   harness or run the seed against the dev DB).
2. Hit `GET /api/onboarding/imports?pageSize=200` from the dashboard.

**Expected**

- Wall-clock latency **< 2 seconds** for the full 200-row response.
- Each row carries a `dependentsCount`/`staleDependentsCount` pair.
- Record the timing in [Performance Spot-Check](#performance-spot-check).

---

## Performance Spot-Check

| ID | Acceptance criterion | Target | Measured | Notes |
|----|----------------------|--------|----------|-------|
| SC-001 | Step 1 + Step 2 wall-clock | < 90 s | _record during ONB-02_ | |
| SC-002 | eMASS 1k-control commit | < 5 min | _record during ONB-03_ (5 systems × 5 controls used here as a smoke proxy) | |
| SC-003 | Azure subscription enumeration | < 30 s | _record during ONB-06 (after consent)_ | |
| SC-004 | Template upload + validation feedback | < 10 s | _record during ONB-07_ | |
| SC-014 | `/admin/imported-documents` 200-row load | < 2 s | _record during ONB-10_ | |

> Capture the measured values directly in this table when running the
> walkthrough so they can be cited in the next ATO authorization package.

---

## Reference

- Spec: [`specs/047-onboarding-wizard/spec.md`](../../specs/047-onboarding-wizard/spec.md)
- Quickstart: [`specs/047-onboarding-wizard/quickstart.md`](../../specs/047-onboarding-wizard/quickstart.md)
- Contracts: [`specs/047-onboarding-wizard/contracts/`](../../specs/047-onboarding-wizard/contracts/)
- Error code catalog:
  [`src/Ato.Copilot.Core/Onboarding/WizardErrorCodes.cs`](../../src/Ato.Copilot.Core/Onboarding/WizardErrorCodes.cs)
