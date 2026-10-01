# Quickstart — 074: Policy + Technical Narrative Split & Evidence Classification

This guide covers local development verification, migration verification, and end-to-end
testing for Feature 052 (#64).

---

## Requirement coverage local acceptance

The requirement-coverage continuation is under validation. Automated browser
fixtures are synthetic and do not establish acceptance by a real user or eMASS.
Do not run the historical migration-generation commands below for this
continuation: it uses additive, rerunnable startup schema additions.

### Isolated local startup

Use this branch, an isolated development database, your existing supported local
authentication configuration, and real server-side organization/system assignments.
Do not replace or restart shared local services. The following entry points and
configuration keys were checked in code; a real authenticated manual session
has not yet been exercised for this feature.

```bash
# Terminal 1, from repository root. Choose an unused port and an isolated DB path.
ATO_Database__Provider=SQLite \
ATO_ConnectionStrings__DefaultConnection="Data Source=/absolute/path/to/isolated-coverage.db" \
ATO_Server__Urls=http://127.0.0.1:3019 \
dotnet run --project src/Ato.Copilot.Mcp -- --http

# Terminal 2, from repository root. Keep the same API target.
cd src/Ato.Copilot.Dashboard
VITE_API_PROXY_TARGET=http://127.0.0.1:3019 \
npm run dev -- --host 127.0.0.1 --port 5189 --strictPort
```

Open `http://127.0.0.1:5189` and use the supported local login flow. Update the
local identity application's redirect URI if your configuration requires it;
do not disable authentication. Create or use a synthetic system and three
separate accounts:

- A system reader without narrative mutation permission.
- An assigned narrative author.
- A different person with both system-management and narrative-review permission
  for catalog reconciliation and selection acceptance. Narrative review alone
  does not authorize a baseline change.

Use the existing framework import workflow to load the desired registered source.
SPIN associates it automatically using the baseline's recorded framework.
Control Narratives must not ask the user to choose a catalog or supply a
reconciliation rationale. Unresolved identifiers remain an administrator-facing
data diagnostic; no alternate source is guessed.

For an existing catalog showing an administrator-refresh warning, first sign in
as a platform administrator and open **provider workspace -> Control Catalog ->
Reference catalog sources**. Use **Load source** or **Load missing sources**,
not the broader definition replacement action. Source-only loading preserves
existing definitions and all system records. Confirm the returned source
version/URI and any partial failures, then return to Control Narratives. The
association is automatic. Organization administrator status alone does not authorize
global catalog mutation.

### AC-11 and AC-11(1)

1. Open **Control Narratives**, search for AC-11 and record the current filter,
   page and workspace URL. As reader, verify source-labeled requirements under
   Statements and the unchanged Evidence/History tabs and Policy/Technical switch.
2. In the pinned NIST catalog, verify that `ac-11_smt.a` and `ac-11_smt.b`
   belong to AC-11, while `ac-11.1` / `ac-11.1_smt` identify the separate
   enhancement. Unknown parameter values must remain visible; the mock's
   descriptions are not source text.
3. Navigate parent -> selected enhancement -> parent, including an enhancement
   with no narrative. Confirm filters/page/workspace survive and read navigation
   creates no implementation record. Existing off-baseline narratives must be
   labeled separately from selected baseline controls.
4. The reader cannot save mappings, bind a catalog, propose or accept selection.
   Verify direct API mutations are also denied, not merely hidden buttons.
5. As author, enter separate requirement responses under the relevant
   Policy/Technical switch. Use **Find evidence** to select synthetic artifacts
   explicitly, and record source-supported parameter values. Save and reload.
   Drafts must not display as reviewed coverage or change control effectiveness.
6. Leave a response unsaved. Navigate to another control or close with Escape:
   discard confirmation must protect the draft. Switching detail tabs must not
   discard the response.
7. Use a synthetic baseline in which AC-11 is selected but AC-11(1) is not.
   **Propose enhancement AC-11(1)** requires rationale and a separate draft.
   Submission must not modify active selection or approved narrative content.
   Repeat submission to verify duplicate protection.
8. As the separate reviewer, inspect rationale/draft and accept selection.
   Verify membership and separate Draft implementation appear atomically, without
   copying the parent's responsibility. If a narrative already existed, verify
   its approved version and untouched half remain retained. Alternatively,
   request revision with a note; no baseline change should occur.
9. Complete requirement responses and evidence, then have a different narrative
   reviewer review coverage. Missing evidence, parameters or stale versions must
   block that review. General statement approval and control effectiveness remain
   separate records.
10. Use two browser sessions for stale-version saves and concurrent review.
    Failed writes must remain explicit, without silent overwrites. Transaction
    fault injection belongs in the isolated relational tests, not a live system.
11. Generate SSP working preview, DOCX/PDF, OSCAL and eMASS preparation output.
    Inspect parent responses and the separate enhancement record. OSCAL must use
    actual pinned-catalog identifiers, not `.policy` or `.technical` statement
    IDs. Inspect actual bytes, not just a success notification.
12. Change a supporting artifact or draft after review. Current coverage/readiness
    must show the resulting gap; historical reviewed content and retained
    archives must remain preserved. Required gaps block a new
    submission-preparation readiness claim, not visibility of a labeled preview.

### Repeatable isolated checks

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
cd src/Ato.Copilot.Dashboard
npx --no-install tsc --noEmit -p tsconfig.json
npm test -- src/__tests__/pages/RequirementCoveragePanel.test.tsx \
  src/__tests__/pages/ControlNarrativeWorkspace.test.tsx \
  src/__tests__/api/requirementCoverage.test.ts \
  src/__tests__/api/controlNarrativeWorkspace.test.ts
npx --no-install playwright test --config playwright.requirement-coverage.config.ts
```

The browser configuration starts an isolated server on port 4179, mocks API
responses and points any missed proxy request at a non-serving port. It does not
use the live/CRUD global setup or mutate a real system.

### Rollback and acceptance

Keep a backup of the isolated database before startup schema tests. No schema
columns or historical catalog bindings, proposal records or reviewed snapshots
should be deleted for a rollback. Revert only feature code through a reviewed
change; verify older-writer compatibility first. Reversing accepted baseline
selection requires authorized reviewed work, not removal of audit history.

Record the real-user manual result before declaring the feature complete.
Generated documents are package preparation, not evidence of eMASS receipt,
acceptance, authorization or operational control effectiveness.

---

## Prerequisites

```bash
# From repo root
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln

# Dashboard (Vite dev server)
cd src/Ato.Copilot.Dashboard
npm ci
npm run dev
```

---

## 1. Apply the Migration (Dev — SQLite)

```bash
# From repo root
dotnet ef migrations add Feature052_PolicyTechnicalNarrativeSplit \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

# Review the generated migration file — confirm 6 AddColumn calls
# and the two Sql() back-fill statements

dotnet ef database update \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

# Verify new columns exist
sqlite3 data/ato-copilot.db ".schema ControlImplementations" | grep -E 'PolicyNarrative|TechnicalNarrative|MigratedFromLegacy'
sqlite3 data/ato-copilot.db ".schema EvidenceArtifacts" | grep -E 'NarrativeType|AutoTagRationale|ManuallyTaggedBy'
```

**Expected output (ControlImplementations):**
```
"PolicyNarrative" TEXT,
"TechnicalNarrative" TEXT,
"MigratedFromLegacy" INTEGER NOT NULL DEFAULT 0,
```

---

## 2. Verify Back-Fill

```bash
# Verify TechnicalNarrative was populated from legacy Narrative
sqlite3 data/ato-copilot.db \
  "SELECT Id, substr(Narrative,1,40), substr(TechnicalNarrative,1,40), MigratedFromLegacy \
   FROM ControlImplementations \
   WHERE Narrative IS NOT NULL LIMIT 5;"
```

Expected: `TechnicalNarrative` matches the first 40 chars of `Narrative` for each row,
and `MigratedFromLegacy = 1`.

```bash
# Verify existing EvidenceArtifacts got Combined (2), not Unclassified (3)
sqlite3 data/ato-copilot.db \
  "SELECT NarrativeType, COUNT(*) FROM EvidenceArtifacts GROUP BY NarrativeType;"
```

Expected: all rows show `NarrativeType = 2` (Combined).

---

## 3. Dual Narrative GET Endpoint

```bash
# Authenticate and get a JWT first (use your local dev token)
TOKEN="<your-dev-JWT>"
SYSTEM_ID="<a-registered-system-id-from-your-dev-db>"

curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/systems/$SYSTEM_ID/controls/AC-2/narrative" | jq .
```

**Expected response shape:**
```json
{
  "status": "success",
  "data": {
    "systemId": "...",
    "controlId": "AC-2",
    "policyNarrative": null,
    "technicalNarrative": "... (back-filled from legacy Narrative) ...",
    "legacyNarrative": "... (original Narrative, unchanged) ...",
    "migratedFromLegacy": true,
    "policyEvidence": [],
    "technicalEvidence": [],
    "unclassifiedEvidence": [],
    "isPolicyStale": false,
    "isTechnicalStale": false,
    "policyStaleReason": null,
    "technicalStaleReason": null
  },
  "metadata": { "executionTimeMs": 12, "timestamp": "..." }
}
```

---

## 4. Dual Narrative PATCH — Technical Half Only

```bash
curl -s -X PATCH \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"technicalNarrative": "Azure AD Conditional Access enforces MFA for all AC-2 user accounts."}' \
  "http://localhost:5000/api/systems/$SYSTEM_ID/controls/AC-2/narrative" | jq .data.technicalNarrative
```

Expected: `"Azure AD Conditional Access enforces MFA for all AC-2 user accounts."`.
`policyNarrative` must remain `null` (unchanged).

---

## 5. Role Gate — PlatformEngineer → 403 on Policy Write

```bash
PE_TOKEN="<a-JWT-for-a-PlatformEngineer-role>"

curl -s -X PATCH \
  -H "Authorization: Bearer $PE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"policyNarrative": "Attempting to write policy as Engineer."}' \
  "http://localhost:5000/api/systems/$SYSTEM_ID/controls/AC-2/narrative" | jq .status

# Expected: "error" with errorCode "FORBIDDEN"
```

---

## 6. Evidence Classify Endpoint

```bash
ARTIFACT_ID="<an-evidence-artifact-id>"

curl -s -X PATCH \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"narrativeType": 0, "rationale": "Manual: this is the AC-2 Policy PDF"}' \
  "http://localhost:5000/api/evidence/$ARTIFACT_ID/classify" | jq .
```

Expected: artifact's `narrativeType` updated to `0` (Policy), `autoTagRationale` cleared,
`manuallyTaggedBy` set to caller OID.

---

## 7. MCP Tool — `narrative_set_policy`

Via MCP test harness or `curl` to the MCP endpoint:

```json
{
  "tool": "narrative_set_policy",
  "arguments": {
    "system_id": "<system-id>",
    "control_id": "AC-2",
    "policy_narrative": "The Account Management Policy (AMP-001) governs account lifecycle for all system users. Reviews occur annually per FISMA requirements. The ISSM is the policy owner."
  }
}
```

Expected response:
```json
{
  "status": "success",
  "data": {
    "controlId": "AC-2",
    "policyNarrative": "The Account Management Policy..."
  }
}
```

---

## 8. Known Pitfalls

| Pitfall | Mitigation |
|---------|-----------|
| Running `dotnet ef migrations add` without reviewing the generated file first — the back-fill SQL must be manually added (T005) | Always open the migration file after generation and add the `Sql()` calls before applying |
| `NarrativeType = 3` (Unclassified) appears on existing evidence after migration | The back-fill `UPDATE EvidenceArtifacts SET NarrativeType = 2` in `Up()` corrects this; verify with the `GROUP BY` query in Step 2 above |
| PlatformEngineer can still write `technicalNarrative` when `policyNarrative` is also included in the body | The role gate checks whether `policyNarrative` is non-null; strip it from the body to test Technical-only write |
| OSCAL export inserts `_smt.policy` but old consumers expect `_smt.a` | See research.md R6 — `_smt.policy/_smt.technical` is the chosen convention; consumers need updating if they hard-code `_smt.a` |
| Bulk classifier job re-runs override manual tags | The job skips rows where `ManuallyTaggedBy IS NOT NULL` — verify this guard is in place before running |
