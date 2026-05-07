# Quickstart: Tenant Onboarding Wizard (Feature 047)

**Date**: 2026-05-07 · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md)

This quickstart shows how to bring a fresh ATO Copilot tenant up using the new onboarding
wizard, end-to-end, on a developer workstation. It is also the manual-test script for the
**P1 user stories** (US1, US2) and reaches into the optional steps (US3–US7) for partial
coverage.

## Prerequisites

- macOS / Linux / Windows with the ATO Copilot dev container OR a workstation that has run
  `scripts/bootstrap.sh` (or `scripts/bootstrap.ps1`) successfully.
- .NET 9.0 SDK (pinned via [global.json](../../global.json)).
- Node.js ≥ 20 LTS.
- Docker (only required if you want to run against SQL Server instead of SQLite).
- An Entra ID app registration that the dashboard already uses for sign-in. **No additional
  service principal is required for the wizard** (research §R8).
- A user account that can be the **first authenticated user** of the fresh tenant (i.e., the
  bootstrap admin — research §R10).

## 1. Reset to a fresh-tenant state

> Skip this section if you are running against an empty database for the first time.

```bash
# From repo root.
# 1. Drop and recreate the local SQLite dev database.
rm -f data/ato-copilot.dev.db

# 2. (Optional) If you use SQL Server in Docker, recreate the container instead:
docker compose -f docker-compose.mcp.yml down -v sqlserver
docker compose -f docker-compose.mcp.yml up -d sqlserver
```

The MCP host runs `EnsureCreatedAsync()` on startup in development, applying the new
migration `Migration_2026_05_07_Onboarding` automatically.

## 2. Build and run

```bash
# Build everything.
dotnet build Ato.Copilot.sln

# Run the MCP host (HTTP + SignalR).
dotnet run --project src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj

# In a second terminal: run the dashboard SPA in dev mode.
cd src/Ato.Copilot.Dashboard
npm ci
npm run dev
```

Open the dashboard URL printed by Vite (typically `http://localhost:5173`).

## 3. Sign in as the bootstrap admin

1. Click **Sign in** and authenticate with the Entra ID account that should bootstrap the
   tenant.
2. After sign-in, the dashboard root automatically redirects to `/onboarding` because
   `TenantOnboardingState.Status = NotStarted` for this fresh tenant.
3. The wizard auto-grants the in-app `Administrator` RMF role to this first authenticated user
   under a tenant-level lock and writes a `WizardAuditEntry` of `WizardBootstrapAdminGranted`
   (FR-001, research §R10). You can confirm via:

   ```bash
   curl -H "Authorization: Bearer $TOKEN" http://localhost:5279/api/onboarding/state | jq
   # → status: "InProgress", lastStep: "OrganizationContext"
   ```

## 4. Walk Step 1 — Organization & branch context (US1, P1)

In the wizard UI:

1. Enter an organization name (e.g., `Acme Civil Agency`).
2. Pick a branch (e.g., `Civil Agency`). If you pick `Industry Partner / Other`, the wizard
   requires the free-text qualifier.
3. (Optional) sub-organization, classification posture, repository URL, POC email.
4. Click **Next**.

**Verify**:

- The onboarding state advances to step `Roles`.
- A new `OrganizationContext` row exists for the tenant.
- A future-generated SSP cover page renders with the captured organization name (try a
  per-system intake afterwards to confirm).

## 5. Walk Step 2 — RMF role assignments (US2, P1)

1. The ISSM slot is pre-filled with the current user.
2. For ISSO, click **Search directory** and pick a colleague from Entra ID. The created
   `Person` row has `IsLinkedToDirectory = true`.
3. For Assessor, type a name + email that does **not** match anyone in Entra and click
   **Add**. The created `Person` row has `IsLinkedToDirectory = false` (research §R1).
4. (Optional) Later, promote the local Person to directory-linked:

   ```bash
   curl -X POST -H "Authorization: Bearer $TOKEN" \
        -H "Content-Type: application/json" \
        -d '{"entraObjectId":"00000000-0000-0000-0000-000000000000"}' \
        http://localhost:5279/api/onboarding/persons/$PERSON_ID/promote
   ```

5. Click **Next** to advance to Step 3.

**Verify** (US2 independent test from spec):

- Create a new system in the portfolio after onboarding.
- The newly created system inherits the organization-level role assignments without re-entry.

## 6. (Optional) Step 3 — Bulk import from eMASS (US3, P2)

1. Click **Import from eMASS** and upload the sample export at
   `tests/Ato.Copilot.Tests.Integration/TestData/onboarding/sample-emass-5-systems.zip`.
2. The dashboard shows a `BackgroundJobProgress` component subscribed to
   `wizard-{tenantId}-job-{jobId}` (research §R2). Status transitions:
   `Queued → InProgress (...%) → Succeeded`.
3. Review the parsed preview (5 systems, control + POA&M counts).
4. Pick a per-system decision (Merge / Skip / Overwrite where conflicts exist).
5. Click **Commit Import**. A second background job runs (`EmassCommit`).
6. Download the import log when finished.

**Verify**:

- All 5 systems appear in the portfolio.
- An `EmassImportSession` row exists with `Status = Imported`.
- `WizardArtifactDependency` rows link the session to each created system / control / POA&M.

## 7. (Optional) Step 4 — SSP PDF ingestion (US4, P2)

1. Upload `tests/Ato.Copilot.Tests.Integration/TestData/onboarding/sample-ssp.pdf` (digital
   PDF) and `sample-ssp-encrypted.pdf` (password-protected) together as a batch.
2. The wizard shows per-PDF progress; the encrypted PDF is rejected with
   `WIZARD_SSP_PDF_PASSWORD_PROTECTED`.
3. The digital PDF extraction completes; review the extracted fields with confidence bands.
4. Correct any low-confidence fields, then click **Import**.

**Verify**:

- The system created from the digital PDF has audit metadata
  `Source: SSP PDF (sample-ssp.pdf)` (FR-043).
- The encrypted PDF leaves no partial system in the portfolio (SC-008).

## 8. Step 5 — Azure subscriptions (US5, P2)

1. Click **Connect Azure** in Step 5. The dashboard requests the ARM scope incrementally (FR-070a).
2. Approve the Entra ID consent prompt for `https://management.azure.com/user_impersonation`.
3. The wizard enumerates subscriptions via the user's delegated ARM token. No tenant-wide
   service principal is created.
4. Select one or more subscriptions and click **Next**.

**Verify**:

- `AzureSubscriptionRegistration` rows exist for each selection.
- Triggering an Azure Policy evidence pull from any later feature scopes to the selected
  subscriptions automatically (SC-010).
- If you decline the consent prompt, the wizard surfaces `WIZARD_ARM_CONSENT_REQUIRED` and
  offers Skip — onboarding is **not** blocked (FR-073, FR-075).

## 9. Step 6 — Custom document templates (US6, P2)

For each of the five types, upload a sample template:

| Slot | Sample file (under `tests/.../TestData/onboarding/`) |
|------|------------------------------------------------------|
| SSP  | `template-ssp.docx` |
| SAR  | `template-sar.docx` |
| SAP  | `template-sap.docx` |
| CRM  | `template-crm.xlsx` |
| H/W/S/W list | `template-hwsw.xlsx` |

For each upload:

1. Provide a label and version.
2. Click **Mark as Default**.
3. Confirm the validator finishes inline (these samples are < 5 MB) and shows
   `validationStatus: Compliant` or `FlaggedNonCompliant` with a warning list.

**Verify**:

- `OrganizationDocumentTemplate` rows exist with `IsDefault = true` exactly once per type
  (data model §9 filtered unique index).
- A subsequent SSP DOCX export uses the uploaded template (SC-011).

## 10. Step 7 — Narrative seed documents (US7, P3)

1. Upload `tests/.../TestData/onboarding/policy-acme-cybersecurity.pdf`.
2. Provide a label (`Acme Cybersecurity Policy 2026`) and tags (`policy`, `cyber`).
3. The dashboard shows the indexing job progressing.

**Verify**:

- The document is stored in the **Feature 038 evidence repository** (`NarrativeSeedDocument`
  rows hold metadata only; bytes live behind `IEvidenceArtifactService`).
- After indexing, authoring a NIST control narrative offers a suggestion drawn from this
  document with a citation to its filename (SC-009).

## 11. Use the Imported Documents management view (FR-092..FR-097)

1. Open the **Imported Documents** view from the admin menu.
2. Filter by kind = `Template`.
3. Click **Replace** on the SSP template, upload a new version. Watch the cascade:
   - Existing `SspExport` rows from the prior version are flagged
     `IsStale = true, StaleReason = "rendered with prior template version v1.0"`.
   - The summary returned by the replace endpoint lists how many dependents were flagged.
4. Click **Re-run** on one of the flagged exports. The job runs through SignalR + polling
   fallback as expected.
5. Try to delete the SSP template that is currently `IsDefault = true`. The API responds
   `409 WIZARD_TEMPLATE_DEFAULT_PROTECTED` with a `suggestion` to mark another default first
   or accept fallback to the built-in default (FR-096).

**Verify SC-013**: For each of the four artifact types (template, eMASS export, SSP PDF,
narrative seed), replacing the source produces dependent rows with `IsStale=true` within the
same UI session.

**Verify SC-014**: Locating and replacing any artifact from the management view takes under
2 minutes end-to-end.

## 12. Re-run the wizard later

1. As an Administrator, open `/onboarding` from the admin menu after onboarding is `Completed`.
2. Use the deep links in the step navigator to jump directly to (e.g.) Step 6 without revisiting
   Steps 1–5.
3. Sign in as a non-admin user and try the same. The dashboard renders a **read-only summary**
   only (FR-002). Hitting any wizard endpoint returns `403 WIZARD_AUTH_FORBIDDEN`, with a
   `WizardAuditEntry` row of action `WizardAccessDenied`.

## Automated test coverage to validate

After running through this quickstart, these tests should pass:

```bash
dotnet test --filter "FullyQualifiedName~Ato.Copilot.Tests.Unit.Onboarding"
dotnet test --filter "FullyQualifiedName~Ato.Copilot.Tests.Integration.Onboarding"
```

The integration tests cover at minimum (Constitution III):

- Bootstrap-grant race (two simultaneous first users → exactly one wins, the other gets
  `WIZARD_BOOTSTRAP_RACE`).
- "Last administrator" invariant (cannot remove the only admin without designating a
  replacement).
- ARM consent declined → wizard surfaces `WIZARD_ARM_CONSENT_REQUIRED` and allows Skip.
- eMASS import partial failure (4/5 succeed, 1 fails, log lists the failure — SC-007).
- SSP PDF rejection categories all map to specific error codes (SC-008).
- Default-template "exactly one per type" invariant under concurrent toggles.
- Cascade flagging of dependents on replace, with all four source artifact types (SC-013).
- SignalR disconnect mid-job → polling fallback recovers status without marking the job failed
  (FR-066).

## Cleanup

```bash
# Stop dashboard (Ctrl-C) and MCP host (Ctrl-C).
# Optionally drop the dev DB to start over:
rm -f data/ato-copilot.dev.db
```

## Screenshots

The end-to-end walkthrough has a [screenshot capture
plan](../../docs/screenshots/047-onboarding-wizard/README.md). Captured PNGs
land alongside that README and are referenced from the persona test cases at
[`docs/persona-test-cases/047-onboarding-wizard.md`](../../docs/persona-test-cases/047-onboarding-wizard.md).
