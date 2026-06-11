# Quickstart — 074: Policy + Technical Narrative Split & Evidence Classification

This guide covers local development verification, migration verification, and end-to-end
testing for Feature 052 (#64).

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
