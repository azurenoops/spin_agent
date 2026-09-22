# Research: CKL & XCCDF File Formats for SCAP/STIG Import

**Feature**: 017-scap-stig-import | **Date**: 2026-03-01

## R1: DISA STIG Viewer CKL Format

### What CKL Is

CKL (Checklist) is the XML export format produced by DISA STIG Viewer — the official DoD tool for manually evaluating STIG compliance. Every DoD team performing an ATO uses STIG Viewer to review systems against applicable STIGs and record compliance status per-rule.

**Key facts**:
- CKL is proprietary to DISA STIG Viewer (not a NIST standard)
- One CKL file per STIG benchmark per target system (e.g., `Windows_Server_2022_STIG_V1R1_web-server-01.ckl`)
- Typical ATO package contains 5–20 CKL files
- CKL files range from 50KB (small STIG with ~50 rules) to 5MB (large STIG with ~500+ rules)
- eMASS accepts CKL uploads as assessment evidence
- STIG Viewer versions 2.14–2.17 all produce the same XML structure

### CKL XML Structure Analysis

**Root element**: `<CHECKLIST>` (no namespace)

**Two main sections**:
1. `<ASSET>` — Target system identification (hostname, IP, MAC, FQDN)
2. `<STIGS>` → `<iSTIG>` — contains `<STIG_INFO>` (benchmark metadata) and `<VULN>` entries (per-rule results)

**STIG_INFO encoding**: Uses a flat `<SI_DATA>` list with `<SID_NAME>` / `<SID_DATA>` pairs rather than structured attributes. Must iterate all SI_DATA elements to find `stigid`, `version`, `title`, `releaseinfo`.

**VULN encoding**: Similar flat structure — `<STIG_DATA>` list with `<VULN_ATTRIBUTE>` / `<ATTRIBUTE_DATA>` pairs. Critical attributes:
- `Vuln_Num` — the V-XXXXX vulnerability number (primary key for matching)
- `Rule_ID` — the SV-XXXXX rule ID with revision (secondary key)
- `Rule_Ver` — the WN22-XX-XXXXXX version string (tertiary key)
- `Severity` — `high`, `medium`, `low`
- `CCI_REF` — appears multiple times per VULN (one per CCI)
- `STIGRef` — benchmark name + version string

**STATUS values** (direct children of `<VULN>`, not inside STIG_DATA):
- `Open` — finding exists, not remediated
- `NotAFinding` — checked, compliant
- `Not_Applicable` — rule doesn't apply
- `Not_Reviewed` — not yet evaluated

### Parsing Strategy

**Decision**: Use `System.Xml.Linq.XDocument` for parsing.

**Rationale**: CKL files are small enough to load fully into memory (max ~5MB). XDocument provides LINQ queries that are more readable than XmlReader for the flat `SI_DATA`/`STIG_DATA` attribute lists. No streaming needed.

**Alternatives considered**:
- `XmlReader` (streaming): Overkill for files < 5MB. More complex code for marginal performance gain.
- `XmlSerializer` (deserialization): CKL's flat attribute structure doesn't map naturally to C# classes. Would require excessive `[XmlElement]` annotations.
- Third-party library (e.g., STIG parsing NuGet): No mature packages exist for CKL parsing in .NET.

### Edge Cases

1. **Multiple iSTIG sections**: Some CKL files contain multiple `<iSTIG>` elements (one per STIG in a multi-STIG checklist). Parser must handle iteration over all iSTIGs.
2. **Missing CCI_REF**: Some older STIGs have VULNs without CCI references. These cannot be mapped to NIST controls — logged as warnings.
3. **Severity Override**: When `<SEVERITY_OVERRIDE>` is non-empty, it supersedes the STIG_DATA Severity. Must check for override first.
4. **HTML in FINDING_DETAILS**: Some assessors paste HTML or rich text into finding details. Store as-is (the field is a string).
5. **Encoding**: CKL files are UTF-8, but some tools produce UTF-16 BOM. XDocument handles both.

---

## R2: SCAP/XCCDF Results Format

### What XCCDF Is

XCCDF (Extensible Configuration Checklist Description Format) is an NIST standard (SP 800-126 Rev 3) for expressing security checklists and benchmark results. DISA's SCAP Compliance Checker (SCC) produces XCCDF results files after automated scanning.

**Key facts**:
- XCCDF is a NIST standard — defined in SP 800-126 Rev 3 (SCAP 1.3)
- Automated results (machine-verified) vs. CKL (human-verified)
- Produced by DISA SCC, OpenSCAP, and other SCAP-compliant scanners
- XCCDF results reference OVAL definitions for the actual check logic (we don't parse OVAL)
- Typical file size: 50KB–500KB
- Contains a `<score>` element with overall compliance percentage

### XCCDF XML Structure Analysis

**Root element**: `<TestResult>` in namespace `http://checklists.nist.gov/xccdf/1.2`

**Note on XCCDF versions**: XCCDF 1.1 uses namespace `http://checklists.nist.gov/xccdf/1.1`, XCCDF 1.2 uses `http://checklists.nist.gov/xccdf/1.2`. DISA SCC outputs 1.2 but older tools may use 1.1. Parser should handle both.

**Key elements**:
- `<benchmark>` — STIG benchmark reference (href attribute)
- `<target>` — scanned system hostname
- `<target-address>` — IP address
- `<target-facts>` — OS name, version, etc.
- `<rule-result>` — per-rule result with `idref`, `severity`, `weight` attributes and `<result>` child
- `<score>` — overall compliance score

**Rule ID format in XCCDF**: DISA uses a prefixed format:
```
xccdf_mil.disa.stig_rule_SV-254239r849090_rule
```
The important part is `SV-254239r849090_rule` — this matches `StigControl.RuleId`.

### XCCDF Result Values

| Value | Meaning | Frequency |
|-------|---------|-----------|
| `pass` | Compliant | Most common |
| `fail` | Non-compliant | Most common |
| `error` | Scanner error | Rare |
| `unknown` | Could not determine | Rare |
| `notapplicable` | Rule N/A | Common |
| `notchecked` | Rule not evaluated | Occasional |
| `notselected` | Rule deselected | Rare |
| `informational` | Info only | Rare |
| `fixed` | Was failing, auto-fixed | Rare (SCAP auto-remediation) |

### Parsing Strategy

**Decision**: Use `System.Xml.Linq.XDocument` with namespace-aware queries.

**Rationale**: Same as CKL — files are small, LINQ queries are readable. Namespace handling is required for XCCDF.

**Namespace handling approach**: Define both XCCDF 1.1 and 1.2 namespaces. Try 1.2 first (most common for DISA SCC), fall back to 1.1. If neither matches, attempt namespace-agnostic local-name matching as last resort.

```csharp
XNamespace xccdf12 = "http://checklists.nist.gov/xccdf/1.2";
XNamespace xccdf11 = "http://checklists.nist.gov/xccdf/1.1";
```

### Edge Cases

1. **Multiple TestResult elements**: XCCDF can contain multiple TestResult elements in a wrapper. We process the first (or all if in an ARF).
2. **Missing severity attribute**: Some rule-results omit severity. Fall back to the benchmark definition severity (which we may not have — log warning).
3. **XCCDF inside ARF (Asset Report Format)**: DISA SCC can produce ARF files that wrap XCCDF results. ARF uses namespace `http://scap.nist.gov/schema/asset-reporting-format/1.1`. Parser should detect ARF and extract the embedded TestResult.
4. **OVAL references**: `<check>` elements reference OVAL definitions. We capture the reference string for audit but don't parse OVAL XML.

---

## R3: Existing Security Posture Intelligence Navigator Integration Points

### StigControl Record (read-only, from curated JSON)

`StigControl` is loaded from `stig-controls.json` into memory via `IStigKnowledgeService`. Currently 880 entries covering common DoD technologies.

**Lookup methods available**:
- `GetStigControlAsync(stigId)` — by VulnId (e.g., "V-254239") ✅
- `SearchStigsAsync(query, severity)` — keyword search ✅
- `GetStigCrossReferenceAsync(stigId)` — cross-reference ✅
- `GetStigsByCciChainAsync(controlId)` — NIST→CCI→STIG ✅
- `GetStigControlByRuleIdAsync(ruleId)` — by RuleId ❌ **MISSING — need to add**

**Gap**: The XCCDF parser produces RuleIds (e.g., `SV-254239r849090_rule`), but `IStigKnowledgeService` only supports lookup by VulnId. Need to add a RuleId index.

### CCI→NIST Mapping (read-only, from curated JSON)

`cci-nist-mapping.json` contains ~7,575 CCI entries, each mapping to one or more NIST 800-53 control IDs. Loaded via `IStigKnowledgeService.GetCciMappingsAsync(controlId)`.

**Usage for import**: Given a CKL VULN with `CCI_REF` values:
1. Each CCI-XXXXXX → lookup in mapping → NIST control ID(s)
2. Collected NIST controls → validate against system baseline
3. Create/update ControlEffectiveness per unique NIST control

**Coverage**: Most DoD STIGs include CCI references. Older STIGs (pre-2015) may have VULNs without CCI refs — these can only be mapped via the curated `StigControl.NistControls` field.

### ComplianceFinding Integration

`ComplianceFinding` already has:
- `StigFinding` (bool) — set `true` for CKL/XCCDF imports
- `StigId` (string?) — set to VulnId
- `CatSeverity` (CatSeverity? enum) — mapped from CKL/XCCDF severity
- `Source` (string) — set to `"CKL Import"` or `"SCAP Import"`
- `ScanSource` (ScanSourceType enum) — use `Combined`

**New field needed**: `ImportRecordId` (nullable FK to `ScanImportRecord`) — tracks which import created the finding.

### ControlEffectiveness Integration

`IAssessmentArtifactService.AssessControlAsync` creates/upserts `ControlEffectiveness` records:
- `ControlId` — NIST control (resolved via CCI chain)
- `Determination` — `Satisfied` / `OtherThanSatisfied`
- `AssessmentMethod` — `"Test"` (scanner-verified)
- `EvidenceIds` — link to created `ComplianceEvidence`

**Aggregation rule**: If multiple STIG rules map to the same NIST control, the effectiveness is determined by the **worst result** — any `Open` finding → `OtherThanSatisfied`.

### ComplianceEvidence Integration

`ComplianceEvidence` supports:
- `EvidenceType` — string field, new values `"StigChecklist"` / `"ScapResult"`
- `CollectionMethod` — `"Manual"` for CKL (human-evaluated), `"Automated"` for XCCDF (machine-scanned)
- `ContentHash` — SHA-256 of raw file content for tamper detection
- `EvidenceCategory` — `Configuration`
- `CollectorIdentity` — user who performed the import

---

## R4: File Transfer via MCP Protocol

### The Challenge

MCP tools communicate via JSON-RPC 2.0. The protocol supports arbitrary JSON parameters but has no built-in concept of binary file upload (no multipart/form-data).

### Approach: Base64 Encoding

Encode file content as a base64 string in the tool parameter. This is the simplest approach and works within the existing MCP infrastructure.

**Size analysis**:
| File Type | Typical Size | Base64 Size | Within Limits? |
|-----------|-------------|-------------|---------------|
| Small CKL (~50 rules) | 50KB | 67KB | ✅ |
| Medium CKL (~200 rules) | 200KB | 267KB | ✅ |
| Large CKL (~500 rules) | 500KB | 667KB | ✅ |
| Very large CKL (~1000 rules) | 1.5MB | 2MB | ✅ |
| Extreme CKL (multi-STIG) | 3MB | 4MB | ✅ (under 5MB limit) |
| XCCDF results (~500 rules) | 100KB | 133KB | ✅ |

**Decision**: 5MB limit after base64 decoding. This covers >99% of real-world CKL/XCCDF files.

**Alternative considered**: Add a file upload HTTP endpoint to the MCP server. Rejected for this feature — adds transport complexity. The existing chat attachment endpoint (`POST /api/messages/{messageId}/attachments`, 10MB limit) is chat-only. A general compliance file upload endpoint could be added in a future feature.

---

## R5: Conflict Resolution Design

### The Problem

Users will re-import CKL files as they progress through assessment cycles. The same STIG rules will appear in multiple imports. Need a strategy for handling duplicates.

### Pattern: Follow EmassImportOptions

The eMASS import already implements conflict resolution with three strategies. We reuse this proven pattern.

| Strategy | Behavior | When to Use |
|----------|----------|-------------|
| **Skip** | Keep existing finding unchanged | Safe default; preserving manual annotations |
| **Overwrite** | Replace finding with imported data entirely | Fresh scan replacing stale data |
| **Merge** | Keep more-recent status; append new details | Incremental updates from ongoing assessment |

**Merge rules for SCAP/STIG import**:
- **Status**: Take the imported status (scan is more current than DB state)
- **Severity**: Take the higher severity (conservative approach)
- **Finding details**: If imported details differ from existing, append imported text with timestamp separator
- **Comments**: Append imported comments with timestamp
- **CatSeverity**: If severity override present in import, use it; otherwise keep existing
- **Timestamps**: Update `DiscoveredAt` to import timestamp; preserve `RemediatedAt` if finding was already remediated

### Conflict Detection Key

A finding is considered a "conflict" when:
- Same `StigId` (VulnId)
- Same `AssessmentId` (within same assessment context)
- Existing `StigFinding = true`

This allows multiple imports of the same STIG benchmark within one assessment cycle, with each import updating or supplementing the previous data.
