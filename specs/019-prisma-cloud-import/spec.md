# Feature 019: Prisma Cloud Scan Import

**Created**: 2026-03-05  
**Status**: Strategic Plan  
**Purpose**: Enable Security Posture Intelligence Navigator to ingest Prisma Cloud compliance scan results (CSPM alerts and policy findings), map them to NIST 800-53 controls, create compliance findings, and feed them into the existing assessment pipeline — closing the gap between cloud security posture scanning and RMF artifact generation.

---

## Clarifications

### Session 2026-03-05

- Q: What Prisma Cloud output formats should we support? → A: Prisma Cloud CSV export (available from all tiers) as the primary format, and Prisma Cloud API JSON (RQL alert response) as the secondary format. CSV is universally available and requires no API credentials; JSON is richer and supports automation.
- Q: How does Prisma Cloud data map to existing Security Posture Intelligence Navigator entities? → A: Prisma policies have compliance standards metadata that includes NIST 800-53 control IDs directly. Each alert/finding maps to a `ComplianceFinding` with cloud resource context. No CCI intermediate mapping needed — Prisma embeds the NIST control reference directly, unlike CKL/XCCDF which requires CCI→NIST resolution.
- Q: Should we support Prisma Cloud's CSPM, CWP, and CWPP modules? → A: Start with CSPM (Cloud Security Posture Management) compliance alerts, which is the module relevant to ATO/RMF. CSPM produces policy-level compliance findings mapped to frameworks. CWP (workload protection) and CNAPP (application security) are separate modules with different data structures — they're future enhancements.
- Q: Should Prisma findings overwrite or coexist with existing STIG/SCAP findings for the same NIST control? → A: Coexist. Prisma findings have `ScanSource = Cloud` (distinct from CKL's `Combined` or XCCDF's `Automated`). Multiple evidence sources per control is the correct posture — the effectiveness determination aggregates all findings regardless of source. A control is `OtherThanSatisfied` if ANY source reports an open finding.
- Q: How does Prisma severity map to CAT severity? → A: Prisma uses Critical/High/Medium/Low/Informational. Map: Critical→CatI, High→CatI, Medium→CatII, Low→CatIII, Informational→Informational (no CAT equivalent). This matches DoD CAT severity conventions where Critical/High both represent CAT I.
- Q: Should we support real-time webhook integration with Prisma? → A: Not in this feature. Prisma provides webhooks for alert notifications, but that requires a publicly-accessible endpoint and Prisma admin configuration. Start with file/API import (user-initiated), add webhook integration as a future enhancement.
- Q: What about Prisma Cloud's compliance report PDF export? → A: PDF reports are human-readable but not machine-parseable. We support CSV export (structured, exportable from any Prisma deployment) and API JSON (for teams with API access). PDF import is out of scope.
- Q: How do we handle Prisma policies that map to frameworks other than NIST 800-53? → A: Prisma policies can map to CIS Benchmarks, SOC 2, HIPAA, PCI-DSS, and NIST 800-53 simultaneously. We extract ONLY the NIST 800-53 mappings. If a policy has no NIST 800-53 mapping, it's imported as an "unmapped" finding — `ComplianceFinding` is created for audit trail with a warning, but no `ControlEffectiveness` is generated.
- Q: Should we auto-detect the Azure subscription and correlate to a RegisteredSystem? → A: Yes, when possible. Prisma includes subscription/account metadata. Use the existing `ISystemSubscriptionResolver` (Feature 015 Phase 17) to resolve Azure subscriptions to registered systems. If resolution fails, require explicit `system_id`.
- Q: When one Prisma alert maps to multiple NIST controls, should we create one ComplianceFinding per alert or one per control? → A: One `ComplianceFinding` per alert, with multiple `ControlEffectiveness` records (one per mapped NIST control). This avoids data duplication and keeps finding counts aligned with actual Prisma alert counts. The alert is the real-world unit of work; NIST control linkage is an effectiveness-layer concern.
- Q: How should multi-subscription CSVs be handled when a single export contains alerts from multiple Azure subscriptions? → A: Auto-split by subscription. Each unique `Account ID` in the CSV is resolved via `ISystemSubscriptionResolver` independently. One import produces multiple `ScanImportRecord` entries (one per resolved system). For subscriptions that cannot be resolved to a `RegisteredSystem`, skip those alerts and return an actionable error prompting the user to register the system first (e.g., "Subscription {id} ({name}) has {N} alerts but is not registered. Use `compliance_register_system` to register it, then re-import."). Do not silently discard unresolved alerts.
- Q: Should `resolved` Prisma alerts actively assert `Satisfied` on their mapped controls, or just close the finding? → A: Resolved alerts close the finding (`FindingStatus.Remediated`) but do NOT independently upsert `Satisfied`. Effectiveness is determined by the aggregate: after import, for each affected NIST control, query ALL `ComplianceFinding` records across all sources (STIG, Prisma, manual). If no open findings remain, the control is `Satisfied`. If any source still has an open finding, the control stays `OtherThanSatisfied`. This prevents a resolved Prisma alert from masking an open STIG finding for the same control.
- Q: What file size limit should Prisma imports enforce? → A: 25MB after base64 decoding. Large enough for enterprise Prisma exports (500+ policies × 1000+ resources), while staying within reasonable memory bounds. Files exceeding 25MB are likely unfiltered full-tenant dumps that should be scoped by subscription or policy type before export.
- Q: How should non-Azure cloud types (AWS, GCP) in a Prisma CSV be handled? → A: When `system_id` is explicitly provided, import alerts from all cloud types (Azure, AWS, GCP) into that system — the user is asserting those resources belong to that system boundary. When auto-resolving (no `system_id`), only process Azure alerts since `ISystemSubscriptionResolver` only supports Azure subscription IDs. Non-Azure alerts without an explicit `system_id` are skipped with a warning: "{N} alerts from {cloud_types} skipped — provide explicit `system_id` to import non-Azure alerts."
- Q: Should the spec include documentation deliverables about Prisma scans — who runs them, when, and at what RMF stage? → A: Yes. Add Part 13 (Documentation Deliverables) covering: who runs Prisma scans (ISSO primary, cloud security engineer, ISSM directs), when (pre-assessment at Step 4, post-remediation, periodic ConMon, pre-reauthorization, on-demand), at what RMF stage (Step 4 Assess primary, Step 6 Monitor ongoing). Update persona guides (ISSO, SCA, Engineer, ISSM), tool catalog, tool inventory, and RMF phase pages (Assess, Monitor). Added Phase 5 capabilities (5.1–5.6) and tasks (T049–T056).

---

## Part 1: The Problem

### Why This Matters

Prisma Cloud (Palo Alto Networks) is one of the most widely deployed Cloud-Native Application Protection Platforms (CNAPP) in DoD and federal environments, particularly in Azure Government (GovCloud) and FS Azure (Azure for Operators/Mission Owner) tenancies. The platform continuously scans cloud infrastructure against compliance frameworks, including NIST 800-53, CIS Benchmarks, and DISA STIGs.

**The problem is not scanning — it's what happens after the scan.**

Prisma tells the AO there are 47 findings. Then someone has to manually:
1. Export findings from the Prisma console
2. Cross-reference each finding to NIST 800-53 controls
3. Determine which controls are affected in the system's baseline
4. Create POA&M entries for open findings
5. Write control narratives reflecting the current posture
6. Build SSP/SAR/SAP sections from the scan data
7. Bundle the authorization package
8. Track remediation milestones

**This is the 18-month bottleneck.** NAVAIR and other Navy commands report that teams spend more time chasing artifacts than running scans. The AO constantly requests updated artifacts, and each request triggers a manual data extraction → documentation cycle.

### The Current Gap

| What Teams Do Today | What Security Posture Intelligence Navigator Can Do Today |
|---------------------|-------------------------------|
| Run Prisma Cloud compliance scans | Nothing — no Prisma parser exists |
| Export findings as CSV or pull via API | Nothing — no cloud CSPM import pipeline |
| Manually map Prisma policies to NIST controls | NIST control baseline and mapping exists, but no Prisma ingest |
| Re-enter Prisma findings into eMASS/POA&M spreadsheets | POA&M tools exist but require manual finding creation |
| Generate compliance reports from scan data | SSP/SAR/SAP generation exists but only from manually-entered or STIG-imported data |
| Track remediation progress across scan cycles | Trend tracking exists for STIG imports but not cloud CSPM |

### The Opportunity

Security Posture Intelligence Navigator already has:
- **Compliance Finding pipeline** — `ComplianceFinding` with severity, status, remediation tracking
- **Control Effectiveness** — `ControlEffectiveness` for per-control assessment determinations
- **Evidence chain** — `ComplianceEvidence` with SHA-256 hashing and collection method tracking
- **Scan Import framework** — `ScanImportRecord` / `ScanImportFinding` entities with conflict resolution, dry-run, and duplicate detection (Feature 017)
- **NIST control baseline** — `ControlBaseline` with tailoring and inheritance
- **Subscription resolution** — `ISystemSubscriptionResolver` (Feature 015) resolves Azure subscriptions to registered systems
- **Downstream artifact generation** — SSP, SAR, SAP, POA&M, RAR, authorization package bundling — all tools that consume `ComplianceFinding` and `ControlEffectiveness` data

The missing piece is **Prisma data parsing** — getting Prisma Cloud's CSV/JSON output into the existing entity pipeline. Once Prisma findings flow into `ComplianceFinding` records, every downstream tool (SSP generation, SAR generation, POA&M creation, authorization package bundling) automatically includes them.

**This is the net-add value**: Prisma is the input, Security Posture Intelligence Navigator is the output. Prisma tells you what's broken; Security Posture Intelligence Navigator turns that into the package the AO signs.

---

## Part 2: The Product

### What We're Building

**Prisma Cloud Scan Import** allows users to upload Prisma Cloud compliance export files (CSV or API JSON) through MCP tools, parse the findings, map policies to NIST 800-53 controls, and create `ComplianceFinding` + `ControlEffectiveness` + `ComplianceEvidence` records — feeding Prisma scan data into the full RMF artifact generation pipeline.

### What It Is

- A **file import pipeline** that parses Prisma Cloud CSV exports and API JSON response payloads
- A **policy-to-control mapper** that resolves Prisma policy compliance metadata to NIST 800-53 controls
- A **cloud resource finding creator** that creates `ComplianceFinding` records with Azure resource context (subscription, resource group, resource type)
- An **assessment integration** that feeds cloud scan results into the existing ControlEffectiveness pipeline
- A **trend analysis** tool that compares findings across scan cycles to show remediation progress

### What It Is NOT

- Not a Prisma Cloud scanner — we import results, we don't run Prisma scans
- Not a Prisma Cloud console replacement — we import/analyze findings, we don't manage Prisma policies
- Not a Prisma Cloud API proxy — we parse export files and API responses, we don't maintain Prisma API sessions
- Not a real-time webhook receiver — file/API import is user-initiated (webhook integration is a future enhancement)
- Not a CWP/CWPP/code security importer — this feature covers CSPM compliance alerts only

### Interfaces

| Surface | User | Purpose |
|---------|------|---------|
| **MCP Tools** | ISSO, SCA | `compliance_import_prisma`, `compliance_import_prisma_api`, `compliance_list_prisma_policies`, `compliance_prisma_trend` |
| **VS Code (@ato)** | Engineer, ISSO | `@ato Import my Prisma Cloud scan results` → triggers file picker → parses and imports |
| **Teams Bot** | ISSM, SCA | Upload Prisma CSV attachment → bot detects file type → imports and shows summary card |

---

## Part 3: Prisma Cloud Data Formats

### 3.1 Prisma Cloud CSV Export

Prisma Cloud's compliance CSV export is available from **Alerts > Download** in the Prisma Cloud console. This is the most accessible format since it requires no API credentials and works with all Prisma Cloud tiers.

**Structure (representative columns):**

```csv
Alert ID,Status,Policy Name,Policy Type,Severity,Cloud Type,Account Name,Account ID,Region,Resource Name,Resource ID,Resource Type,Alert Time,Resolution Reason,Resolution Time,Compliance Standard,Compliance Requirement,Compliance Section
P-12345,open,Azure Storage account should use customer-managed key for encryption,config,high,azure,FS-Azure-Prod,a1b2c3d4-5678-90ab-cdef-1234567890ab,eastus,storagesecure01,/subscriptions/a1b2c3d4-.../resourceGroups/rg-prod/providers/Microsoft.Storage/storageAccounts/storagesecure01,Microsoft.Storage/storageAccounts,2026-03-01T10:30:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Protection of Information at Rest
P-12346,open,Azure SQL Database should have Azure Active Directory admin configured,config,medium,azure,FS-Azure-Prod,a1b2c3d4-5678-90ab-cdef-1234567890ab,eastus,sqldb-mission01,/subscriptions/a1b2c3d4-.../resourceGroups/rg-prod/providers/Microsoft.Sql/servers/sqldb-mission01,Microsoft.Sql/servers,2026-03-01T10:30:15Z,,,NIST 800-53 Rev 5,AC-2,AC-2 Account Management
P-12347,resolved,Azure Key Vault should have soft delete enabled,config,medium,azure,FS-Azure-Prod,a1b2c3d4-5678-90ab-cdef-1234567890ab,eastus,kv-mission-secrets,/subscriptions/a1b2c3d4-.../resourceGroups/rg-prod/providers/Microsoft.KeyVault/vaults/kv-mission-secrets,Microsoft.KeyVault/vaults,2026-02-15T08:00:00Z,Resolved,2026-02-28T14:30:00Z,NIST 800-53 Rev 5,SC-12,SC-12 Cryptographic Key Establishment and Management
```

**Key CSV Fields:**

| Column | Maps To |
|--------|---------|
| `Alert ID` | `PrismaAlertId` on `ScanImportFinding` |
| `Status` | `open` → `FindingStatus.Open`, `resolved` → `FindingStatus.Remediated`, `dismissed` → `FindingStatus.Accepted` |
| `Policy Name` | `ComplianceFinding.Title` |
| `Policy Type` | `config` (CSPM), `network` (network policy), `audit_event` (audit) |
| `Severity` | `critical`/`high` → CatI, `medium` → CatII, `low` → CatIII, `informational` → Informational |
| `Cloud Type` | `azure`, `aws`, `gcp` — filter to cloud type if needed |
| `Account Name` | Cloud account display name |
| `Account ID` | Azure subscription ID → `ISystemSubscriptionResolver` |
| `Region` | Azure region (e.g., `eastus`, `usgovvirginia`) |
| `Resource Name` | Cloud resource display name |
| `Resource ID` | Full Azure resource ID → `ComplianceFinding.ResourceId` |
| `Resource Type` | ARM resource type (e.g., `Microsoft.Storage/storageAccounts`) |
| `Alert Time` | Finding timestamp |
| `Resolution Reason` | Dismissal or remediation reason |
| `Resolution Time` | When finding was resolved |
| `Compliance Standard` | Must contain "NIST 800-53" to extract NIST mapping |
| `Compliance Requirement` | NIST control family/ID (e.g., `SC-28`, `AC-2`) |
| `Compliance Section` | Full control reference with title |

**Prisma CSV Status Values:**

| Prisma Status | Meaning | Maps To |
|---------------|---------|---------|
| `open` | Active finding, not remediated | `FindingStatus.Open` — contributes `OtherThanSatisfied` to aggregate effectiveness |
| `resolved` | Finding remediated (confirmed by re-scan) | `FindingStatus.Remediated` — closes finding; effectiveness re-evaluated by aggregate (does NOT independently assert `Satisfied`) |
| `dismissed` | Finding dismissed with reason (risk accepted, false positive, etc.) | `FindingStatus.Accepted` — effectiveness re-evaluated by aggregate |
| `snoozed` | Temporarily suppressed | `FindingStatus.Open` with note: CSV → "Snoozed (snooze expiry unavailable from CSV)"; API JSON → "Snoozed until {date}" if expiry available in `history[]` — contributes `OtherThanSatisfied` to aggregate |

**CSV Delimiter Notes:**
- Prisma Cloud exports use standard comma-delimited CSV with double-quote text qualifiers.
- Policy names and compliance sections may contain commas — always quoted.
- Multi-value compliance mappings (one policy → multiple NIST controls) appear as separate rows with the same `Alert ID`.

### 3.2 Prisma Cloud API JSON (RQL Alert Response)

For teams with Prisma Cloud API access (Enterprise tier), the RQL (Resource Query Language) alert endpoint returns richer data:

```json
{
  "id": "P-12345",
  "status": "open",
  "firstSeen": 1709289000000,
  "lastSeen": 1709375400000,
  "alertTime": 1709289000000,
  "policy": {
    "policyId": "a1b2c3d4-uuid",
    "name": "Azure Storage account should use customer-managed key for encryption",
    "policyType": "config",
    "severity": "high",
    "description": "This policy identifies Azure Storage accounts that are not using CMK...",
    "recommendation": "1. Navigate to Azure Portal\n2. Go to Storage Accounts\n3. ...",
    "complianceMetadata": [
      {
        "standardName": "NIST 800-53 Rev 5",
        "requirementId": "SC-28",
        "requirementName": "Protection of Information at Rest",
        "sectionId": "SC-28",
        "sectionDescription": "Protect the confidentiality and integrity of the following information at rest..."
      },
      {
        "standardName": "CIS v2.0.0 (Azure)",
        "requirementId": "3.2",
        "requirementName": "Ensure that Storage Account Access Keys are Periodically Regenerated"
      }
    ],
    "labels": ["CSPM", "Azure", "Storage"],
    "remediable": true,
    "remediation": {
      "cliScriptTemplate": "az storage account update --name ${resourceName} --resource-group ${resourceGroup} --encryption-key-source Microsoft.Keyvault ...",
      "description": "Enable customer-managed key encryption for the storage account"
    }
  },
  "resource": {
    "id": "/subscriptions/a1b2c3d4-.../resourceGroups/rg-prod/providers/Microsoft.Storage/storageAccounts/storagesecure01",
    "name": "storagesecure01",
    "resourceType": "Microsoft.Storage/storageAccounts",
    "region": "eastus",
    "cloudType": "azure",
    "accountId": "a1b2c3d4-5678-90ab-cdef-1234567890ab",
    "accountName": "FS-Azure-Prod",
    "resourceGroupName": "rg-prod",
    "rrn": "rrn:prisma:cloud:azure:a1b2c3d4:storageAccounts:storagesecure01"
  },
  "reason": "NEW_ALERT",
  "history": [
    {
      "modifiedBy": "System",
      "modifiedOn": 1709289000000,
      "reason": "NEW_ALERT",
      "status": "open"
    }
  ]
}
```

**Key API JSON Fields (beyond CSV):**

| Field | Value |
|-------|-------|
| `policy.description` | Full policy description → `ComplianceFinding.Description` |
| `policy.recommendation` | Remediation guidance → `ComplianceFinding.RemediationGuidance` |
| `policy.complianceMetadata[]` | Direct NIST 800-53 control mappings (no CCI translation needed) |
| `policy.remediation.cliScriptTemplate` | CLI remediation script → evidence/remediation context |
| `resource.resourceGroupName` | Azure resource group → additional resource context |
| `resource.rrn` | Prisma Resource Reference Number for cross-system correlation |
| `history[]` | Alert state change history → audit trail |

### 3.3 Prisma Policy → NIST Control Mapping

Unlike STIG imports (which require CCI→NIST indirect mapping), Prisma Cloud embeds NIST 800-53 control IDs directly in `complianceMetadata`:

```
Prisma Policy ──────▶ complianceMetadata[].standardName = "NIST 800-53 Rev 5"
                           │
                           ├── requirementId = "SC-28"    ──▶ ControlBaseline lookup
                           ├── requirementId = "AC-2"     ──▶ ControlBaseline lookup
                           └── requirementId = "IA-5(1)"  ──▶ ControlBaseline lookup
```

**Mapping Rules:**
1. Extract all `complianceMetadata` entries where `standardName` contains "NIST 800-53"
2. Parse `requirementId` as the NIST control ID (e.g., `SC-28`, `AC-2`, `IA-5(1)`)
3. Validate each control exists in the system's `ControlBaseline.ControlIds`
4. Controls in baseline → create `ComplianceFinding` + `ControlEffectiveness`
5. Controls outside baseline → create `ComplianceFinding` only (audit trail), skip `ControlEffectiveness`, add warning

**One policy → Many controls**: A single Prisma policy commonly maps to multiple NIST controls. For example, "Azure Storage encryption" maps to SC-28 (data at rest) AND SC-12 (key management). This produces **one `ComplianceFinding`** (the alert) with **multiple `ControlEffectiveness` upserts** (one per NIST control). The alert is the unit of finding; NIST control linkage is at the effectiveness layer.

**One alert → Many policies (rare)**: API JSON may return alerts where a single resource triggers multiple policies. Each policy is processed independently.

---

## Part 4: Personas & Needs

### ISSO (Information System Security Officer)

| Need | Description | Status |
|------|-------------|--------|
| Import Prisma CSV export | Upload Prisma compliance CSV from the console | **MISSING** |
| Auto-resolve subscription to system | Map Azure subscription → registered system automatically | **HAVE** (`ISystemSubscriptionResolver`) |
| See import summary with pass/fail/dismissed counts | After import, see a dashboard of results by NIST control | **MISSING** |
| View import history | Track which scans were imported, when, by whom | **HAVE** (`compliance_list_imports`) |
| Resolve conflicts when re-importing | Choose skip/overwrite/merge for existing findings | **HAVE** (Feature 017 pattern) |
| Map Prisma policies to NIST controls automatically | Direct mapping from compliance metadata | **MISSING** |

### SCA (Security Control Assessor)

| Need | Description | Status |
|------|-------------|--------|
| Use Prisma data for effectiveness determinations | Cloud scan results auto-populate `ControlEffectiveness` | **MISSING** |
| Compare Prisma results across scan cycles | Trend analysis — remediation progress between imports | **MISSING** |
| Verify evidence chain from cloud scan import | Imported Prisma data becomes tamper-evident evidence | **MISSING** |
| See combined STIG + Prisma findings per control | Single control view showing all evidence sources | **HAVE** (once imported, existing tools aggregate) |

### ISSM (Information System Security Manager)

| Need | Description | Status |
|------|-------------|--------|
| Track cloud posture across systems | See Prisma findings aggregated by NIST control family | **MISSING** |
| Generate artifacts from cloud scan data | Prisma findings flow into SSP/SAR/POA&M generation | **HAVE** (once imported, downstream tools work) |
| Demonstrate continuous monitoring | Show scan-to-scan trend of remediated findings | **MISSING** |

### Engineer

| Need | Description | Status |
|------|-------------|--------|
| Upload Prisma CSV from VS Code | `@ato Import Prisma scan results` with file picker | **MISSING** |
| See remediation guidance from Prisma | Prisma policy recommendations shown on findings | **MISSING** |
| Track which resources have open findings | Filter findings by resource type and region | **MISSING** |

---

## Part 5: Capabilities

### Phase 1: Prisma CSV Import (Core)

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 1.1 | **Prisma CSV Parser** | — | Parse Prisma Cloud compliance CSV exports. Extract Alert ID, Status, Policy Name, Severity, Resource metadata, and Compliance Standard/Requirement/Section fields. Handle multi-row alerts (one alert → multiple compliance mappings) by grouping on Alert ID. Handle quoted fields with embedded commas. |
| 1.2 | **NIST Control Extraction** | — | From CSV `Compliance Standard`/`Compliance Requirement` columns, extract NIST 800-53 control IDs. Filter rows where `Compliance Standard` contains "NIST 800-53". Parse control IDs supporting both base controls (`AC-2`) and enhancements (`IA-5(1)`). |
| 1.3 | **Subscription-to-System Resolution** | — | For each unique `Account ID` in the import, use `ISystemSubscriptionResolver` to resolve to a `RegisteredSystemId`. Multi-subscription CSVs are auto-split: one `ScanImportRecord` per resolved system. If explicit `system_id` is provided, all alerts are assigned to that system regardless of cloud type (overrides auto-resolution). When auto-resolving (no `system_id`): only Azure alerts are processed; non-Azure alerts (AWS, GCP) are skipped with a warning: "{N} alerts from {cloud_types} skipped — provide explicit `system_id` to import non-Azure alerts." For Azure subscriptions that cannot be resolved, skip those alerts and include an actionable prompt: "Subscription {id} ({name}) has {N} alerts but is not registered. Use `compliance_register_system` to register it, then re-import." |
| 1.4 | **ComplianceFinding Creation** | ISSO, SCA | Create **one** `ComplianceFinding` per Prisma alert (not per NIST control). Set `StigFinding = false`, `ScanSource = ScanSourceType.Cloud`, `Source = "Prisma Cloud"`, `ResourceId` from the Azure resource ID. Include Policy Name as `Title`, policy description as `Description`. When one alert maps to multiple NIST controls, the single finding links to multiple `ControlEffectiveness` records via the effectiveness upsert step. |
| 1.5 | **Severity Mapping** | — | Map Prisma severity to CAT: `critical`/`high` → `CatI`, `medium` → `CatII`, `low` → `CatIII`, `informational` → Informational finding (no CAT, no effectiveness impact). |
| 1.6 | **ControlEffectiveness Upsert** | SCA | For each unique NIST control resolved from findings **that exists in the system's control baseline**, re-evaluate effectiveness using the aggregate approach. After creating/updating findings, query ALL `ComplianceFinding` records for the control across all sources (STIG, Prisma, manual). If any finding is `Open` → `OtherThanSatisfied`. Only when ALL findings are `Remediated`/`Accepted` → `Satisfied`. Resolved Prisma alerts close the finding but do not independently assert `Satisfied` — effectiveness is always determined by the aggregate. Set `AssessmentMethod = "Test"` (automated cloud scan). Controls outside the baseline get `ComplianceFinding` records only. |
| 1.7 | **Evidence Auto-Creation** | ISSO | Create `ComplianceEvidence` records. `EvidenceType = "CloudScanResult"`, `EvidenceCategory = Configuration`, `CollectionMethod = "Automated"`. SHA-256 hash of import file content for integrity chain. |
| 1.8 | **Import Conflict Resolution** | ISSO | Reuse the Feature 017 conflict resolution pattern (`Skip`/`Overwrite`/`Merge`). Matching key: `PrismaAlertId` + `RegisteredSystemId` (not `StigId` like CKL/XCCDF). |
| 1.9 | **Dry-Run Mode** | ISSO, SCA | Preview import results without persisting changes. Show: findings to create, controls affected, policies with no NIST mapping, subscription resolution result. |
| 1.10 | **Duplicate File Detection** | ISSO | Check `ScanImportRecord` for same `FileHash` + `RegisteredSystemId`. Warn but allow on duplicate (same pattern as Feature 017). |
| 1.11 | **Import MCP Tool** | ISSO | `compliance_import_prisma` tool accepting base64-encoded CSV content, optional `system_id` (auto-resolved from subscription if omitted), `conflict_resolution`, `dry_run`. Returns import result with counts and unmapped policies. |

### Phase 2: Prisma API JSON Import

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 2.1 | **Prisma API JSON Parser** | — | Parse Prisma Cloud RQL alert API JSON response. Extract alerts with full policy metadata, resource details, compliance mappings, and remediation guidance. Handle paginated API responses (array of alert objects). |
| 2.2 | **Enhanced Remediation Context** | Engineer | Extract `policy.recommendation` and `policy.remediation.cliScriptTemplate` from API JSON. Persist as `ComplianceFinding.RemediationGuidance` and create evidence with CLI script content. |
| 2.3 | **Alert History Capture** | SCA | Extract `history[]` from API JSON and store as finding metadata for audit trail — when the alert was opened, state changes, who dismissed/resolved. |
| 2.4 | **Policy Metadata Enrichment** | — | Extract `policy.labels`, `policy.policyType`, and `policy.remediable` flag. Persist as structured metadata on the import finding for filtering and reporting. |
| 2.5 | **Import MCP Tool** | ISSO | `compliance_import_prisma_api` tool accepting base64-encoded JSON content (array of alert objects), optional `system_id`, `conflict_resolution`, `dry_run`. Same downstream pipeline as CSV import. |

### Phase 3: Policy Catalog & Trend Analysis

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 3.1 | **Prisma Policy Registry** | — | Build an in-memory registry of Prisma policy → NIST control mappings from imported data. Cache policy metadata (name, severity, NIST controls, resource types) for fast lookup and reporting. |
| 3.2 | **Policy Listing Tool** | ISSO, SCA | `compliance_list_prisma_policies` — list unique Prisma policies seen across imports for a system with NIST control mappings, open/resolved counts, and affected resource counts. |
| 3.3 | **Trend Analysis Tool** | SCA, ISSM | `compliance_prisma_trend` — compare findings between two or more imports to show remediation progress: new findings, resolved findings, persistent findings, and compliance score trend. |
| 3.4 | **Resource-Centric View** | Engineer | Group findings by Azure resource type and region. Show which resource types have the most open findings and which NIST controls they affect. Exposed via the trend tool's `group_by` parameter. |

### Phase 4: Integration & Observability

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 4.1 | **ScanImportType Extension** | — | Add `PrismaCsv` and `PrismaApi` values to `ScanImportType` enum. Extends the existing import tracking framework to cover Prisma imports alongside CKL/XCCDF. |
| 4.2 | **Import List/Summary Compatibility** | — | Existing `compliance_list_imports` and `compliance_get_import_summary` tools work for Prisma imports without modification (they query `ScanImportRecord` generically). |
| 4.3 | **Downstream Artifact Integration Verification** | SCA, ISSM | Verify that Prisma-sourced `ComplianceFinding` records flow correctly into: `compliance_generate_sar` (appears in SAR findings), `compliance_create_poam` (open findings create POA&M items), `compliance_generate_ssp` (findings inform control narrative posture), `compliance_generate_sap` (Prisma scan plan section). |
| 4.4 | **Structured Logging** | — | Log Prisma import with system ID, import type, alert count, NIST controls affected, subscription resolved, duration. Follow Feature 017/018 logging patterns with Stopwatch and duration_ms. |
| 4.5 | **ConMon Integration** | ISSM | Prisma imports contribute to continuous monitoring. `GenerateReportAsync()` includes Prisma scan data when calculating drift and compliance posture. |

### Phase 5: Documentation Updates

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 5.1 | **ISSO Prisma Import Guide** | ISSO | Add step-by-step workflow to ISSO documentation: exporting CSV from Prisma Cloud console → running `compliance_import_prisma` → interpreting import summary → handling unmapped policies → re-importing after remediation. Include screenshots/examples of the CSV export process. |
| 5.2 | **SCA Prisma Assessment Guide** | SCA | Add "Assess Controls Using Prisma Cloud Data" to SCA guide: how Prisma findings auto-populate `ControlEffectiveness`, using `compliance_prisma_trend` for remediation validation, reviewing combined STIG + Prisma evidence per control. |
| 5.3 | **Engineer Remediation Guide** | Engineer | Add "Prisma Remediation Workflow" to Engineer guide: viewing Prisma-sourced findings, using CLI remediation scripts from API JSON imports, resource-centric filtering by `group_by` parameter. |
| 5.4 | **ISSM Cloud Posture Oversight** | ISSM | Add "Cloud Posture Oversight" to ISSM guide: directing ISSOs to import scans, reviewing trend data across systems, Prisma findings in ConMon reports. |
| 5.5 | **Tool Catalog & Inventory Updates** | — | Add 4 new MCP tools to Agent Tool Catalog and Tool Inventory: `compliance_import_prisma`, `compliance_import_prisma_api`, `compliance_list_prisma_policies`, `compliance_prisma_trend` with parameters, RBAC roles, and examples. |
| 5.6 | **RMF Phase Page Updates** | — | Update Assess phase page with Prisma Cloud scan import as an assessment input. Update Monitor phase page with Prisma periodic re-import as a ConMon data source and trend analysis for drift detection. |

---

## Part 6: Mapping Pipeline Architecture

### Import Flow (Prisma CSV)

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────────┐
│  Prisma CSV      │────▶│  CSV Parser      │────▶│  NIST Control        │
│  (Compliance     │     │  (extract alerts, │     │  Extractor           │
│   Export)        │     │   group by Alert  │     │  (Compliance         │
│                  │     │   ID)             │     │   Standard →         │
└──────────────────┘     └──────────────────┘     │   NIST control IDs)  │
                                                   └──────────┬───────────┘
                                                              │
                                                              ▼
                                                   ┌──────────────────────┐
                                                   │  Subscription        │
                                                   │  Resolver            │
                                                   │  (Account ID →       │
                                                   │   RegisteredSystem)  │
                                                   └──────────┬───────────┘
                                                              │
                                                              ▼
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────────┐
│  Import Record   │◀────│  Evidence Creator │◀────│  Finding Creator     │
│  (ScanImport     │     │  (SHA-256 hash,  │     │  (ComplianceFinding  │
│   Record with    │     │   CloudScanResult │     │   with ScanSource =  │
│   PrismaCsv type)│     │   evidence type)  │     │   Cloud, ResourceId) │
└──────────────────┘     └──────────────────┘     └──────────────────────┘
                                                              │
                                                              ▼
                                                   ┌──────────────────────┐
                                                   │  Effectiveness       │
                                                   │  Upsert              │
                                                   │  (per NIST control,  │
                                                   │   aggregate all      │
                                                   │   finding sources)   │
                                                   └──────────────────────┘
```

### Import Flow (Prisma API JSON)

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────────┐
│  Prisma API      │────▶│  JSON Parser     │────▶│  Policy Compliance   │
│  JSON Response   │     │  (deserialize    │     │  Metadata Extractor  │
│  (array of       │     │   alert objects,  │     │  (complianceMetadata │
│   alerts)        │     │   extract policy  │     │   → NIST controls,  │
│                  │     │   + resource)     │     │   filter "800-53")   │
└──────────────────┘     └──────────────────┘     └──────────┬───────────┘
                                                              │
                                                    ── same pipeline as CSV ──▶
```

### Resolution Logic

1. **Parse** CSV or JSON → list of `ParsedPrismaAlert` DTOs
2. **Group** by Alert ID (CSV may have multiple rows per alert for multi-control policies)
3. **Resolve subscriptions** — For each unique `Account ID`, use `ISystemSubscriptionResolver`. Multi-subscription CSVs produce multiple `ScanImportRecord` entries (one per resolved system). If explicit `system_id` provided, all alerts go to that system. For unresolved subscriptions (no matching `RegisteredSystem`), skip those alerts and include an actionable prompt in the response: "Subscription {id} ({name}) has {N} alerts but is not registered. Use `compliance_register_system` to register it, then re-import."
4. **Resolve NIST controls** — Extract from CSV `Compliance Requirement` column or JSON `complianceMetadata[].requirementId`
   - Validate each NIST control exists in the system's `ControlBaseline.ControlIds`
   - For controls **in baseline**: create `ComplianceFinding` + `ControlEffectiveness`
   - For controls **outside baseline**: create `ComplianceFinding` only, add warning
   - For policies with **no NIST 800-53 mapping**: create `ComplianceFinding` as unmapped, add warning
5. **Check conflicts** — For each alert, check if a `ComplianceFinding` already exists with the same `PrismaAlertId` + `RegisteredSystemId`
6. **Apply** — Based on conflict resolution strategy, create/update/skip
7. **Re-evaluate effectiveness** — For each NIST control affected, query ALL current `ComplianceFinding` records (including STIG, manual, and Prisma sources). Any open finding → `OtherThanSatisfied`. Only when ALL findings are `Remediated`/`Accepted` → `Satisfied`. Resolved Prisma alerts close the finding but do not independently assert `Satisfied` — a resolved Prisma alert cannot mask an open STIG finding for the same control.
8. **Record** — Create `ScanImportRecord` with `ImportType = PrismaCsv` or `PrismaApi`

### Severity Mapping

| Prisma Severity | `CatSeverity` | `FindingSeverity` | Rationale |
|-----------------|---------------|-------------------|-----------|
| `critical` | `CatI` | `Critical` | DoD critical — immediate mission impact |
| `high` | `CatI` | `High` | DoD CAT I — direct exploitation risk |
| `medium` | `CatII` | `Medium` | DoD CAT II — security posture degradation |
| `low` | `CatIII` | `Low` | DoD CAT III — minimal direct impact |
| `informational` | — | `Informational` | No CAT equivalent; no effectiveness impact |

---

## Part 7: Integration Points

### 7.1 Existing Services Used

| Service | How It's Used |
|---------|--------------|
| `ISystemSubscriptionResolver` | Resolve Azure subscription → RegisteredSystem (Feature 015 Phase 17) |
| `IAssessmentArtifactService` | `AssessControlAsync` — upsert `ControlEffectiveness` from parsed Prisma results |
| `IBaselineService` | `GetBaselineAsync` — validate NIST controls are in system's baseline |
| `IRmfLifecycleService` | `GetSystemAsync` — validate system exists |
| `IComplianceAssessmentService` | Create/get assessment context for findings |
| `IScanImportService` | Extend with Prisma import methods; reuse `ListImportsAsync` and `GetImportSummaryAsync` |

### 7.2 New Components

| Component | Purpose |
|-----------|---------|
| `IPrismaParser` / `PrismaCsvParser` | Parse Prisma CSV export format |
| `IPrismaParser` / `PrismaApiJsonParser` | Parse Prisma API JSON response |
| `PrismaImportTools.cs` | MCP tool wrappers for Prisma import/query tools |

### 7.3 Service Extension

The existing `IScanImportService` is extended with two new methods:

```csharp
Task<ImportResult> ImportPrismaCsvAsync(
    string? systemId, byte[] fileContent, string fileName,
    ImportConflictResolution resolution, bool dryRun, string importedBy,
    CancellationToken ct = default);

Task<ImportResult> ImportPrismaApiAsync(
    string? systemId, byte[] fileContent, string fileName,
    ImportConflictResolution resolution, bool dryRun, string importedBy,
    CancellationToken ct = default);
```

### 7.4 File Transfer

Same pattern as Feature 017: base64-encoded file content in MCP tool parameters.

- Prisma CSV exports: typically 100KB–5MB depending on environment size
- Prisma API JSON: typically 500KB–10MB for large environments
- File size limit: 25MB after base64 decoding (enterprise Prisma exports with 500+ policies × 1000+ resources)

### 7.5 Assessment Context

Same pattern as Feature 017:
1. If `assessment_id` is provided, use it
2. If no assessment exists, create one automatically with `source = "Prisma Cloud Import"`
3. If an active assessment exists, use it

### 7.6 Downstream Artifact Impact

Once Prisma findings are imported as `ComplianceFinding` records, these existing tools automatically include them:

| Tool | How Prisma Data Appears |
|------|------------------------|
| `compliance_generate_sar` | Prisma findings appear in SAR "Assessment Findings" section with `Source = "Prisma Cloud"` |
| `compliance_generate_ssp` | Control narratives reflect Prisma-detected posture |
| `compliance_create_poam` | Open Prisma findings eligible for POA&M item creation |
| `compliance_generate_sap` | Prisma Cloud appears in "Technical Testing Plan" section as a scan source |
| `compliance_bundle_authorization_package` | Prisma evidence included in authorization package |
| `compliance_generate_conmon_report` | Prisma scan data contributes to continuous monitoring metrics |
| `compliance_multi_system_dashboard` | Prisma findings reflected in portfolio compliance scores |

---

## Part 8: What This Changes

### Entity Model Changes

| Entity | Change | Details |
|--------|--------|---------|
| `ScanImportType` | **EXTENDED** | Add `PrismaCsv` and `PrismaApi` enum values |
| `ScanImportFinding` | **ENRICHED** | Add nullable `PrismaAlertId`, `PrismaPolicyId`, `PrismaPolicyName`, `CloudResourceId`, `CloudResourceType`, `CloudRegion`, `CloudAccountId` fields |
| `ComplianceFinding` | **ENRICHED** | New `ScanSource` value `Cloud` for Prisma imports. Existing `ResourceId` field stores Azure resource ID. |
| `ComplianceEvidence` | **ENRICHED** | New `EvidenceType` value: `"CloudScanResult"` |

### Service Layer Changes

| Component | Change |
|-----------|--------|
| `IScanImportService` | **EXTENDED** — Add `ImportPrismaCsvAsync` and `ImportPrismaApiAsync` methods |
| `ScanImportService` | **EXTENDED** — Implement Prisma import pipeline with CSV/JSON parsing |
| `PrismaCsvParser` | **NEW** — CSV parsing with comma handling, Alert ID grouping, NIST extraction |
| `PrismaApiJsonParser` | **NEW** — JSON deserialization with complianceMetadata extraction |
| `AtoCopilotContext` | **NO CHANGE** — Reuses existing `ScanImportRecords` and `ScanImportFindings` DbSets |

### Tool Layer Changes

| Tool | Parent File | Description |
|------|-------------|-------------|
| `ImportPrismaTool` | `PrismaImportTools.cs` | Import Prisma CSV export file |
| `ImportPrismaApiTool` | `PrismaImportTools.cs` | Import Prisma API JSON response |
| `ListPrismaPoliciesTool` | `PrismaImportTools.cs` | List unique Prisma policies and NIST mappings |
| `PrismaTrendTool` | `PrismaImportTools.cs` | Scan-to-scan trend analysis |

### MCP Registration

4 new tools registered in `ComplianceMcpTools.cs`.

---

## Part 9: What We're NOT Building

| Out of Scope | Reason |
|-------------|--------|
| **Prisma Cloud scanner** | We import results, we don't run Prisma scans |
| **Prisma Cloud API client** | We parse export files and API JSON payloads; we don't manage Prisma API authentication or session tokens |
| **Real-time webhook receiver** | Requires publicly-accessible endpoint + Prisma admin config — future enhancement |
| **CWP/CWPP import** | Workload protection module has different data structures — separate feature |
| **CNAPP code security import** | Application security scan results use different schemas — separate feature |
| **Prisma Cloud PDF report parsing** | PDF is human-readable, not machine-parseable |
| **Prisma policy management** | We read policies from imports; we don't create/edit policies in Prisma |
| **Multi-cloud auto-detection** | Auto-resolution uses Azure subscription IDs via existing `ISystemSubscriptionResolver`; AWS account / GCP project auto-resolution is a future enhancement. Non-Azure alerts can still be imported when explicit `system_id` is provided. |
| **Prisma Console SSO/UI integration** | We're an import pipeline, not a Prisma UI extension |
| **Alert remediation via Prisma API** | We import and track remediation status; we don't push remediation actions back to Prisma |
| **ACAS/Nessus/Tenable import** | Different scanner, different format — separate feature |

---

## Part 10: Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| CSV import parses all alert columns correctly | 100% of Prisma Cloud Console-exported CSVs parse without error (tested with 5+ policy types) |
| API JSON import parses all alert objects correctly | 100% of Prisma RQL alert API responses parse without error |
| NIST control extraction resolves correctly | ≥95% of Prisma policies with NIST 800-53 compliance metadata map to valid NIST controls |
| Subscription auto-resolution works for Azure | Azure subscription IDs resolve to RegisteredSystem via `ISystemSubscriptionResolver` |
| Multi-subscription CSVs split correctly | CSV with alerts from 3 subscriptions produces 3 `ScanImportRecord` entries (one per resolved system) |
| Unresolved subscriptions prompt registration | Unresolved subscription returns actionable message with `compliance_register_system` guidance |
| Non-Azure alerts handled correctly | Auto-resolve mode skips non-Azure with warning; explicit `system_id` imports all cloud types |
| Findings created with correct severity and status | All Prisma severity/status values correctly mapped to ComplianceFinding status |
| ControlEffectiveness records created per NIST control | One effectiveness record per unique NIST control from resolved findings |
| Evidence chain integrity | Import file SHA-256 hash verifiable post-import |
| Conflict resolution works correctly | Skip/Overwrite/Merge strategies produce expected results for Prisma Alert ID matching |
| Dry-run shows accurate preview | Dry-run output matches actual import results |
| Existing import tools work for Prisma | `compliance_list_imports` and `compliance_get_import_summary` correctly list/detail Prisma imports |
| Downstream artifacts include Prisma data | SAR, SSP, POA&M, SAP, ConMon reports include Prisma-sourced findings when present |
| Trend analysis shows remediation progress | Comparing two imports shows new/resolved/persistent finding counts |
| Performance: CSV import < 15s for 500 alerts | Single Prisma CSV with ~500 alerts processed in < 15 seconds |
| Performance: API JSON import < 10s for 500 alerts | Prisma API JSON with ~500 alert objects processed in < 10 seconds |
| Multi-row alerts grouped correctly | CSV rows with same Alert ID but different compliance mappings produce one Finding with multiple NIST control links |

---

## Part 11: Build Sequence & Rationale

### Why This Order?

1. **Phase 1 (CSV Import) first** because CSV export is universally available from all Prisma Cloud tiers and requires no API credentials. It's the fastest path to getting Prisma data into Security Posture Intelligence Navigator for any customer, including the FS Azure MO.

2. **Phase 2 (API JSON) second** because it extends the same pipeline with a richer parser. The NIST control extraction, finding creation, and effectiveness upsert logic from Phase 1 is reused. API JSON adds remediation guidance and alert history.

3. **Phase 3 (Policy Catalog & Trend) third** because trend analysis is high-value for continuous monitoring but requires at least two imports to be meaningful. The policy catalog organizes Prisma knowledge for the ISSO/SCA.

4. **Phase 4 (Integration) last** because downstream artifact integration is largely automatic (Prisma findings → ComplianceFinding → existing tools) but needs verification testing and ConMon enrichment.

5. **Phase 5 (Documentation) after all code** because documentation requires the tools and workflows to be implemented before they can be accurately documented. Persona guides reference specific tool names, parameters, and behaviors that must be finalized.

### Timeline Estimate

| Phase | Scope | Estimate |
|-------|-------|----------|
| Phase 1 | CSV parser, NIST extraction, subscription resolution, finding/evidence creation, conflict resolution, MCP tool, tests | 3–4 days |
| Phase 2 | API JSON parser, remediation context, alert history, MCP tool, tests | 2–3 days |
| Phase 3 | Policy registry, list policies tool, trend analysis tool, resource grouping, tests | 2–3 days |
| Phase 4 | ScanImportType extension, downstream verification tests, ConMon integration, structured logging, tests | 1–2 days |
| Phase 5 | ISSO/SCA/Engineer/ISSM guide updates, tool catalog, tool inventory, RMF phase pages | 1–2 days |
| **Total** | **4 tools, 2 parsers, entity extensions, service extension, 8 doc updates** | **9–14 days** |

---

## Part 12: Prisma Cloud × Security Posture Intelligence Navigator Value Proposition

### The Pipeline

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────────────────┐
│  Prisma Cloud   │     │  Security Posture Intelligence Navigator    │     │  Authorization Package      │
│  (CSPM Scanner) │────▶│  (Import +      │────▶│  (AO Signs)                 │
│                 │     │   Artifact Gen) │     │                             │
│  • Scans Azure  │     │  • Import CSV   │     │  • SSP with control posture │
│  • 47 findings  │     │  • Map to NIST  │     │  • SAR with cloud findings  │
│  • Policy alerts│     │  • Create POA&M │     │  • SAP with scan plan       │
│  • CSV export   │     │  • Generate SAR │     │  • POA&M with milestones    │
│                 │     │  • Bundle auth  │     │  • RAR with risk assessment │
│                 │     │    package      │     │  • Evidence package         │
└─────────────────┘     └─────────────────┘     └─────────────────────────────┘

         SCAN                AUTOMATE                DELIVER
    "What's broken"     "Turn it into docs"     "What the AO signs"
```

### Key Differentiator

> **Prisma Cloud tells you what's broken. Security Posture Intelligence Navigator turns that into the package the AO signs.**
>
> The 18-month ATO bottleneck at NAVAIR is not a scanning gap — it's a documentation and artifact generation gap. Teams spend more time chasing artifacts than fixing findings. Security Posture Intelligence Navigator eliminates the manual data extraction → documentation cycle by importing Prisma scan results directly into the RMF artifact pipeline.
>
> For the FS Azure Mission Owner: Prisma scans the Azure environment → Security Posture Intelligence Navigator ingests findings → auto-generates the SSP, SAP, SAR, POA&M → produces the authorization package → tracks ATO expiration and continuous monitoring. **End-to-end, from scan to signed ATO.**

---

## Part 13: Documentation Deliverables

### Prisma Scans — Who, When, and At What RMF Stage

#### Who Runs Prisma Scans

| Role | Responsibility | Security Posture Intelligence Navigator Persona |
|------|---------------|---------------------|
| **ISSO** (Information System Security Officer) | Primary operator. Exports Prisma CSV/JSON and imports into Security Posture Intelligence Navigator. Configures Prisma compliance policies for the system boundary. Monitors alerts in the Prisma console. | `Analyst` role — runs `compliance_import_prisma` |
| **Cloud Security Engineer** | Configures Prisma Cloud CSPM policies, manages cloud accounts, tunes alert rules. May export scan data for the ISSO. | `Analyst` role — runs `compliance_import_prisma` |
| **ISSM** (Information System Security Manager) | Directs the ISSO to run/import scans. Reviews aggregated findings across systems. Does not typically run the import directly. | `SecurityLead` role — reviews via `compliance_prisma_trend`, `compliance_list_prisma_policies` |
| **SCA** (Security Control Assessor) | Consumes imported Prisma data for effectiveness determinations. Does not run scans or imports. Reviews trend data to validate remediation claims. | `Assessor` role — read-only access to findings and trend data |

> **Important**: Prisma Cloud itself runs **continuously** — CSPM scans execute automatically on a schedule (typically every 1–4 hours). The human action is **exporting and importing** the results into Security Posture Intelligence Navigator, not triggering the scan itself.

#### When Prisma Scans Are Imported

| Timing | Trigger | Purpose |
|--------|---------|---------|
| **Pre-Assessment** | Before SCA begins RMF Step 4 (Assess) | Establish baseline cloud posture. All Prisma findings become `ComplianceFinding` records before the SCA records effectiveness determinations. |
| **Post-Remediation** | After engineers address open findings | Re-import to show resolved alerts → closed findings → updated control effectiveness. Used to demonstrate remediation progress before AO authorization decision. |
| **Periodic ConMon** | Monthly or quarterly (per ConMon schedule) | Import latest Prisma scan data as part of continuous monitoring. Trend analysis compares this import to previous imports to detect drift. |
| **Pre-Reauthorization** | Before ATO renewal (typically annually) | Fresh scan import to demonstrate current posture for reauthorization assessment. |
| **On-Demand** | ISSM directs ISSO to import after posture change | After cloud infrastructure changes (new subscriptions, resource deployments, policy updates), import to capture new findings. |

#### At What RMF Stage

```
RMF Step 1: Categorize  ──  (Prisma not relevant yet)
RMF Step 2: Select      ──  (Prisma not relevant yet — baseline not selected)
RMF Step 3: Implement   ──  Engineer configures Prisma policies for the boundary
                              └─ Optional: early import to identify gaps before assessment
RMF Step 4: Assess      ──  ★ PRIMARY STAGE ★
                              ├─ ISSO exports Prisma CSV and imports into Security Posture Intelligence Navigator
                              ├─ Findings auto-populate ComplianceFinding + ControlEffectiveness
                              ├─ SCA uses imported data for effectiveness determinations
                              └─ SAR generation includes Prisma-sourced findings
RMF Step 5: Authorize   ──  AO reviews authorization package containing Prisma findings
                              └─ Prisma evidence in SSP, SAR, POA&M, authorization bundle
RMF Step 6: Monitor     ──  ★ ONGOING STAGE ★
                              ├─ Periodic re-import (monthly/quarterly per ConMon plan)
                              ├─ Trend analysis: compliance_prisma_trend
                              ├─ ConMon report includes Prisma scan data
                              └─ Drift detection: new findings since last import
```

### Documentation Updates Required

The following existing documentation pages must be updated to cover Prisma Cloud scan import workflows:

| Document | Update Required |
|----------|----------------|
| **ISSO Guide** (`docs/guides/issm-guide.md` — ISSO workflow section) | Add "Import Prisma Cloud Scan Results" workflow: step-by-step for CSV export from Prisma console → `compliance_import_prisma` → review import summary → resolve unmapped policies → track remediation |
| **SCA Guide** (`docs/guides/sca-guide.md`) | Add "Assess Controls Using Prisma Cloud Data" section: how imported Prisma findings appear in `ControlEffectiveness`, using `compliance_prisma_trend` for remediation validation, combined STIG + Prisma evidence review |
| **Engineer Guide** (`docs/guides/engineer-guide.md`) | Add "Prisma Remediation Workflow" section: viewing Prisma-sourced findings with remediation guidance, CLI scripts from API JSON imports, resource-centric view by `group_by` |
| **ISSM Guide** (`docs/guides/issm-guide.md`) | Add "Cloud Posture Oversight" section: directing ISSOs to import Prisma scans, reviewing trend data across systems, Prisma findings in ConMon reports |
| **Agent Tool Catalog** (`docs/architecture/agent-tool-catalog.md`) | Add entries for 4 new MCP tools: `compliance_import_prisma`, `compliance_import_prisma_api`, `compliance_list_prisma_policies`, `compliance_prisma_trend` |
| **RMF Assess Phase** (`docs/rmf-phases/assess.md`) | Add Prisma Cloud scan import as an assessment input alongside STIG/SCAP imports |
| **RMF Monitor Phase** (`docs/rmf-phases/monitor.md`) | Add Prisma Cloud periodic re-import as a ConMon data source, trend analysis for drift detection |
| **Tool Inventory** (`docs/reference/tool-inventory.md`) | Add 4 new Prisma tools with parameters, RBAC roles, and example invocations |
