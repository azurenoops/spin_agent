# Research: Prisma Cloud CSV & API JSON Formats for CSPM Import

**Feature**: 019-prisma-cloud-import | **Date**: 2026-03-05

## R1: Prisma Cloud CSV Export Format

### What Prisma Cloud CSV Export Is

Prisma Cloud (Palo Alto Networks) provides a compliance CSV export via **Alerts > Download** in the Prisma Cloud console. This is the universally available export format — all Prisma Cloud tiers (Compute, Enterprise, Premier) support it, and it requires no API credentials.

**Key facts**:
- CSV export is available from the Alerts page with optional filters (policy, severity, cloud type, account)
- One CSV row per compliance mapping (an alert with N NIST controls produces N rows)
- Typical file size: 100KB–5MB depending on environment size and filter scope
- Enterprise Prisma exports (500+ policies × 1000+ resources) can reach 10–20MB
- Export uses RFC 4180 CSV format: comma-delimited, double-quote text qualifiers
- Encoding: UTF-8

### CSV Column Analysis

**Decision**: Parse by column name from header row, not by position.

**Rationale**: Prisma Cloud has updated its export format across versions. Column order is not guaranteed. Column presence is near-guaranteed (they've never removed a column, only added). Parsing by header name is defensive and forward-compatible.

**Alternatives considered**:
- Positional parsing (faster, simpler): Rejected — brittle against column order changes.
- Third-party CSV library (`CsvHelper` NuGet): Available but adds a dependency. The quote-aware splitting logic is straightforward enough to implement inline, and the existing codebase uses no CSV libraries. Rejected to maintain zero external dependencies for parsers.

**Core columns (always present)**:

| Column | Maps To | Notes |
|--------|---------|-------|
| `Alert ID` | `PrismaAlertId` | Unique per alert. Same ID appears on multiple rows when one alert maps to multiple compliance standards. **Primary grouping key.** |
| `Status` | `FindingStatus` | `open`, `resolved`, `dismissed`, `snoozed` |
| `Policy Name` | `ComplianceFinding.Title` | Human-readable policy name. May contain commas (always quoted). |
| `Policy Type` | Metadata | `config` (CSPM), `network` (network policy), `audit_event` (audit), `anomaly` |
| `Severity` | `CatSeverity` mapping | `critical`, `high`, `medium`, `low`, `informational` |
| `Cloud Type` | Filter/routing | `azure`, `aws`, `gcp`, `alibaba_cloud`, `oci` |
| `Account Name` | Display name | Cloud account display name (e.g., "FS-Azure-Prod") |
| `Account ID` | Subscription resolver input | Azure subscription GUID for Azure. AWS account number for AWS. |
| `Region` | Resource context | Cloud region (e.g., `eastus`, `usgovvirginia`, `us-east-1`) |
| `Resource Name` | Resource context | Cloud resource display name |
| `Resource ID` | `ComplianceFinding.ResourceId` | Full ARM resource ID for Azure (e.g., `/subscriptions/…/providers/…`) |
| `Resource Type` | Resource context | ARM type for Azure (e.g., `Microsoft.Storage/storageAccounts`) |
| `Alert Time` | `ComplianceFinding.DiscoveredAt` | ISO 8601 UTC timestamp |
| `Resolution Reason` | Resolution metadata | Empty for open, "Resolved" or "Dismissed" for closed |
| `Resolution Time` | Resolution metadata | ISO 8601 UTC timestamp when resolved/dismissed |
| `Compliance Standard` | NIST filter | Must contain "NIST 800-53" to extract NIST mapping |
| `Compliance Requirement` | NIST control ID | The control family/ID (e.g., `SC-28`, `AC-2`, `IA-5(1)`) |
| `Compliance Section` | Full control reference | Control ID with title (e.g., `SC-28 Protection of Information at Rest`) |

### Multi-Row Grouping

**Decision**: Group CSV rows by `Alert ID` to consolidate multi-compliance-mapping alerts into a single `ParsedPrismaAlert`.

**Rationale**: A single Prisma alert (one resource with one policy violation) commonly maps to multiple compliance frameworks and controls. The CSV contains one row per compliance mapping. For example, "Azure Storage encryption not enabled" on resource `storagesecure01` produces:

| Alert ID | Compliance Standard | Compliance Requirement |
|----------|---------------------|----------------------|
| P-12345  | NIST 800-53 Rev 5   | SC-28               |
| P-12345  | NIST 800-53 Rev 5   | SC-12               |
| P-12345  | CIS v2.0.0 (Azure)  | 3.2                  |
| P-12345  | SOC 2               | CC6.1                |

Our parser groups these into one `ParsedPrismaAlert` with `NistControlIds = ["SC-28", "SC-12"]`. CIS and SOC 2 rows are filtered out.

### Parsing Strategy

**Decision**: Custom quote-aware CSV parser using `string.Split` with state machine logic.

**Rationale**: CKL/XCCDF used `System.Xml.Linq.XDocument` because XML is complex. CSV is simpler but requires quote-awareness because:
1. Policy names contain commas: `"Azure Storage account should use customer-managed key for encryption, version 2.0"`
2. Compliance sections contain commas: `"SC-28 Protection of Information at Rest, Including Cryptographic Protection"`
3. Finding details may contain newlines within quotes

The state machine tracks quoted/unquoted state and handles escaped quotes (`""` within quoted fields per RFC 4180).

**Alternatives considered**:
- `CsvHelper` NuGet: Mature library, would simplify parsing. Rejected to maintain zero external dependencies for parsers (consistent with Feature 017 which used no external XML libraries).
- Simple `string.Split(',')`: Breaks on fields containing commas. Not viable.
- Regex-based parsing: Brittle with edge cases (nested quotes, newlines). Not recommended.

### Edge Cases

1. **Empty compliance columns**: Some alerts have no compliance mapping (custom policies). Parser creates `ParsedPrismaAlert` with empty `NistControlIds` — downstream marks as "unmapped".
2. **Multi-line quoted fields**: Policy descriptions may contain newlines within quotes. State machine must handle `\n` within quoted state.
3. **UTF-8 BOM**: Some Prisma exports include a UTF-8 BOM (`0xEF 0xBB 0xBF`). Parser strips BOM if present.
4. **Empty rows**: Skip blank lines between data rows.
5. **Trailing commas**: Some exports have trailing commas on each row. Parser trims trailing empty fields.
6. **Account ID format**: Azure subscription IDs are GUIDs (`a1b2c3d4-5678-90ab-cdef-1234567890ab`). AWS account IDs are 12-digit numbers. Parser does not validate format — passes raw value to `ISystemSubscriptionResolver` which returns `null` for non-Azure IDs.

---

## R2: Prisma Cloud API JSON Format

### What the API JSON Format Is

Prisma Cloud's RQL (Resource Query Language) alert API returns detailed alert objects as JSON. This is available to teams with Prisma Cloud Enterprise tier and API access. The JSON format is significantly richer than CSV, including remediation scripts, alert history, and policy metadata.

**Key facts**:
- API endpoint: `GET /v2/alert` with RQL filter parameters
- Response: JSON array of alert objects (paginated, typically 100–1000 per page)
- Typical response size: 500KB–10MB depending on alert count and metadata
- Contains fields not available in CSV: remediation scripts, alert history, policy labels, remediable flag
- `complianceMetadata` array directly embeds framework-specific control mappings

### JSON Structure Analysis

**Decision**: Use `System.Text.Json` for deserialization with `JsonPropertyName` attributes.

**Rationale**: The JSON structure is deeply nested but well-typed. `System.Text.Json` is built into .NET 9.0, zero dependencies. The structure maps naturally to C# record types.

**Alternatives considered**:
- `Newtonsoft.Json`: More features (e.g., `JObject` for dynamic queries), but adds a dependency. `System.Text.Json` covers all needs here.
- Manual `JsonDocument` traversal: More code, less type safety. Full deserialization is cleaner for the strongly-typed alert structure.

### Key JSON Fields (Beyond CSV)

| Field | Path | Purpose | CSV Equivalent |
|-------|------|---------|----------------|
| `policy.description` | `$.policy.description` | Full policy description | Not in CSV |
| `policy.recommendation` | `$.policy.recommendation` | Step-by-step remediation guidance | Not in CSV |
| `policy.remediation.cliScriptTemplate` | `$.policy.remediation.cliScriptTemplate` | Azure CLI / PowerShell script | Not in CSV |
| `policy.labels` | `$.policy.labels` | Policy classification tags | Not in CSV |
| `policy.remediable` | `$.policy.remediable` | Whether auto-remediation is possible | Not in CSV |
| `policy.policyType` | `$.policy.policyType` | Policy classification | `Policy Type` column |
| `policy.complianceMetadata[]` | `$.policy.complianceMetadata` | Framework-specific control mappings | `Compliance Standard/Requirement/Section` columns |
| `resource.resourceGroupName` | `$.resource.resourceGroupName` | Azure resource group | Not in CSV |
| `resource.rrn` | `$.resource.rrn` | Prisma Resource Reference Number | Not in CSV |
| `history[]` | `$.history` | Alert state change history | Not in CSV |

### complianceMetadata Extraction

**Decision**: Filter `complianceMetadata` entries where `standardName` contains "NIST 800-53". Extract `requirementId` as the NIST control ID.

**Rationale**: Prisma embeds NIST control IDs directly — no CCI intermediate mapping needed (unlike STIG imports). This is simpler and more accurate than the CCI→NIST chain used in Feature 017.

Example:
```json
"complianceMetadata": [
  {
    "standardName": "NIST 800-53 Rev 5",
    "requirementId": "SC-28",
    "requirementName": "Protection of Information at Rest"
  },
  {
    "standardName": "CIS v2.0.0 (Azure)",
    "requirementId": "3.2",
    "requirementName": "Ensure that Storage Account Access Keys are Periodically Regenerated"
  }
]
```

Parser filters for `standardName.Contains("NIST 800-53")` → extracts `requirementId` = `SC-28`.

### Edge Cases

1. **Empty `complianceMetadata`**: Some policies (custom, anomaly-based) have no compliance mappings. Parser creates alert with empty `NistControlIds`.
2. **Missing `remediation` object**: Not all policies have CLI scripts. `remediation` may be null or have empty `cliScriptTemplate`.
3. **Missing `history` array**: API may omit `history` for very new alerts. Parser defaults to empty list.
4. **Paginated responses**: Users may export a single page or concatenate multiple pages. Parser accepts both single object and array of objects.
5. **Unknown properties**: `System.Text.Json` ignores unknown properties by default — future Prisma API fields won't break deserialization.

---

## R3: Severity Mapping

### Decision: Prisma → CAT Severity

| Prisma Severity | CAT Severity | Finding Severity | Rationale |
|-----------------|-------------|------------------|-----------|
| `critical` | `CatI` | `Critical` | DoD critical — immediate mission impact. Prisma treats critical as more severe than high. |
| `high` | `CatI` | `High` | DoD CAT I — direct exploitation risk. Standard DoD convention: Critical and High both map to CAT I. |
| `medium` | `CatII` | `Medium` | DoD CAT II — security posture degradation. |
| `low` | `CatIII` | `Low` | DoD CAT III — minimal direct impact. |
| `informational` | *(none)* | `Informational` | No CAT equivalent. Finding created for audit trail but no ControlEffectiveness impact. |

**Rationale**: DISA CAT severity has three levels (I, II, III). Prisma has five. The mapping collapses Critical and High into CAT I, which matches DoD convention and the existing STIG import mapper (Feature 017 maps STIG `high` → `CatI` and STIG `Severity_Override = critical` → `CatI`).

**Alternatives considered**:
- Map `critical` to a new `CatCritical` severity: Rejected — CAT I is the highest DoD severity level. Introducing a non-standard level would break downstream tools that enumerate `CatSeverity`.
- Map `informational` to `CatIII`: Rejected — informational findings have no security impact and should not affect control effectiveness.

---

## R4: Subscription Auto-Resolution

### Decision: Use existing `ISystemSubscriptionResolver`

**Rationale**: Feature 015 Phase 17 implemented `SystemSubscriptionResolver` which maps Azure subscription GUIDs to `RegisteredSystem.Id` values by querying `AzureSubscriptionProfile.SubscriptionIds`. It has a 5-minute cache TTL to avoid N+1 database queries.

For Prisma CSV import:
1. Parse all unique `Account ID` values from the CSV
2. For each unique Account ID, call `ISystemSubscriptionResolver.ResolveAsync(accountId)`
3. Group alerts by resolved system ID
4. One `ScanImportRecord` per resolved system

### Multi-Subscription Handling

**Decision**: Auto-split by subscription. One import produces multiple `ScanImportRecord` entries.

**Rationale**: 
- Prisma console allows exporting alerts across multiple cloud accounts in a single CSV
- Security Posture Intelligence Navigator systems are scoped to individual subscriptions/boundaries
- Splitting ensures each system gets its own import record with accurate finding counts

**Edge cases**:
- All subscriptions resolve → success with N import records
- Some subscriptions resolve, some don't → partial success with warnings listing unresolved subscriptions and `compliance_register_system` guidance
- No subscriptions resolve + no `system_id` → error with actionable message
- `system_id` explicitly provided → all alerts go to that system regardless of Account ID

### Non-Azure Cloud Types

**Decision**: When auto-resolving, skip non-Azure alerts with warning. When `system_id` explicit, import all cloud types.

**Rationale**: `ISystemSubscriptionResolver` only handles Azure subscription GUIDs. AWS account IDs (12-digit numbers) and GCP project IDs (string identifiers) cannot be resolved to registered systems through the existing resolver. Rather than reject the entire import, we skip non-Azure alerts and provide an actionable warning.

---

## R5: Effectiveness Aggregation Strategy

### Decision: Aggregate-based evaluation (same as Feature 017)

After creating/updating Prisma findings, for each affected NIST control:

1. Query ALL `ComplianceFinding` records for that control across ALL sources (STIG, Prisma, manual)
2. If ANY finding has `Status == Open` → `ControlEffectiveness.Determination = OtherThanSatisfied`
3. Only when ALL findings have `Status ∈ {Remediated, Accepted}` → `ControlEffectiveness.Determination = Satisfied`

**Rationale**: A resolved Prisma alert should NOT independently assert `Satisfied` — it would mask an open STIG finding for the same control. The aggregate approach ensures consistency across source types.

**Alternatives considered**:
- Source-independent effectiveness: Each source maintains its own effectiveness. Rejected — a control with an open STIG finding and a resolved Prisma finding would show as partially satisfied, which is confusing and incorrect per RMF.
- Latest-source-wins: Most recent import overrides effectiveness. Rejected — would lose effectiveness from older STIG imports.

---

## R6: CSV Parsing — Best Practices for .NET

### RFC 4180 Compliance

The Prisma Cloud CSV export follows RFC 4180 conventions:
- Fields containing commas, double quotes, or newlines are enclosed in double quotes
- A double quote within a quoted field is escaped as `""`
- Line endings are CRLF (`\r\n`), though LF-only (`\n`) should also be accepted
- First row is a header row

### Recommended Implementation

```csharp
// State machine states for quote-aware CSV parsing
enum CsvParseState { OutsideField, InsideQuotedField, QuoteInQuotedField }
```

The parser processes the file byte-by-byte (after UTF-8 decoding to string), tracking the current state. This handles:
- Commas inside quoted fields (don't split)
- Double-quote escaping (`""` → `"`)
- Newlines inside quoted fields (don't split on row boundary)
- Trailing delimiter handling

### Memory Considerations

- Prisma CSV files up to 25MB after base64 decode (≈33MB base64 encoded)
- Full string loading is acceptable — .NET string pooling and GC handle this efficiently
- No streaming parser needed for files < 25MB
- Peak memory: ~3× file size (raw bytes + decoded string + parsed DTOs)
- For a 25MB file: ~75MB peak, well within 512MB budget

---

## R7: Import Pipeline Reuse from Feature 017

### What Can Be Reused

| Component | Reuse Type | Notes |
|-----------|-----------|-------|
| `ScanImportRecord` entity | Extend | Add `PrismaCsv`/`PrismaApi` to `ScanImportType` enum |
| `ScanImportFinding` entity | Extend | Add Prisma-specific nullable fields |
| `ImportResult` DTO | Reuse as-is | Same return shape for all import methods |
| `ImportConflictResolution` enum | Reuse as-is | Skip/Overwrite/Merge works for Prisma too |
| `ImportFindingAction` enum | Reuse as-is | Created/Updated/Skipped/Unmatched all apply |
| `ScanImportStatus` enum | Reuse as-is | Completed/CompletedWithWarnings/Failed |
| Conflict resolution logic | Reuse pattern | Match on `PrismaAlertId` instead of `StigId` |
| Duplicate file detection | Reuse as-is | SHA-256 hash + SystemId check |
| Assessment context resolution | Reuse as-is | Same `GetOrCreateAssessmentAsync` logic |
| Evidence creation | Reuse pattern | Different `EvidenceType` ("CloudScanResult") |
| Effectiveness upsert | Reuse logic | Same aggregate query pattern |
| Structured logging pattern | Reuse pattern | Same Stopwatch + duration_ms approach |

### What Is New

| Component | Why New |
|-----------|---------|
| `PrismaCsvParser` | CSV format is fundamentally different from XML |
| `PrismaApiJsonParser` | JSON format with nested `complianceMetadata` |
| Subscription auto-resolution | CKL/XCCDF had explicit `system_id`; Prisma auto-resolves from Account ID |
| Multi-subscription splitting | Feature 017 was always single-system; Prisma CSV may span multiple |
| Non-Azure cloud type handling | Feature 017 had no cloud type concept |
| Trend analysis service | New analysis not in Feature 017 |
| Policy catalog query | New query not in Feature 017 |
