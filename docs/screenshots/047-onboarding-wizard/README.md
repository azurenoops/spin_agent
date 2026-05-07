# Onboarding Wizard — Screenshot Capture Plan

This directory holds screenshots referenced from
[specs/047-onboarding-wizard/quickstart.md](../../specs/047-onboarding-wizard/quickstart.md).

Because the wizard renders in the React dashboard against a live backend,
screenshots are captured manually as part of the **ONB-01 → ONB-10**
walkthrough in
[docs/persona-test-cases/047-onboarding-wizard.md](../persona-test-cases/047-onboarding-wizard.md).

## Capture procedure

1. Bring up the stack:

    ```bash
    docker compose -f docker-compose.mcp.yml up -d
    cd src/Ato.Copilot.Dashboard
    npm install
    npm run dev
    ```

2. Sign in as the bootstrap admin (first authenticated caller). Visit
    `/onboarding` and follow the seven steps end-to-end using the fixtures in
    `tests/Ato.Copilot.Tests.Integration/TestData/onboarding/` (regenerate
    them with `python3 build_fixtures.py` if missing).

3. Capture each screen below at **1440 × 900** with the browser dev-tools
    closed. Save as PNG at the listed filename.

| Filename | What to capture |
|----------|-----------------|
| `01-bootstrap-admin-grant.png` | The toast confirming `Tenant.OnboardingAdministrator` was auto-granted (ONB-01) |
| `02-step1-organization-context.png` | Filled Step 1 form (ONB-02) |
| `03-step2-role-assignments.png` | Step 2 with ISSO/ISSM/AO entered (ONB-02) |
| `04-step3-emass-preview.png` | eMASS commit preview after `EmassParse` succeeds (ONB-03) |
| `05-step3-emass-failure.png` | Skip & continue UI after partial failure (ONB-04) |
| `06-step4-ssp-pdf-rejection.png` | Friendly rejection message for the encrypted SSP PDF (ONB-05) |
| `07-step5-arm-consent.png` | The `WIZARD_ARM_CONSENT_REQUIRED` runbook link (ONB-06) |
| `08-step6-templates-upload.png` | Templates table with a `Compliant` validation row (ONB-07) |
| `09-step7-narrative-seeds.png` | Narrative seeds list with `Indexed` status (ONB-08) |
| `10-admin-imported-documents.png` | `/admin/imported-documents` view paginated to 200 rows (ONB-10) |
| `11-cascade-rerun.png` | Cascade re-run modal showing dependents + a queued job (ONB-09) |

4. Commit the PNGs into this directory. The quickstart references them by
   relative path:

    ```markdown
    ![Step 1](../docs/screenshots/047-onboarding-wizard/02-step1-organization-context.png)
    ```

> ⚠️  Do **not** commit screenshots that include real tenant data, real
> Entra IDs, or any production tokens. Use the synthetic fixtures only.
