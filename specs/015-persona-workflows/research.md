# Research: RMF Foundation Data Structures

**Feature**: 015-persona-workflows | **Date**: 2026-02-27

## R1: CNSSI 1253 Overlay Structure

### What CNSSI 1253 Is

CNSSI 1253 (*Security Categorization and Control Selection for National Security Systems*) is the CNSS instruction that adapts NIST SP 800-53 for National Security Systems (NSS) and DoD systems. It replaces the simple FIPS 199 Low/Moderate/High categorization with a **three-dimensional categorization** (separate C/I/A levels) and adds DoD-specific overlays that modify the NIST baselines.

**Key distinction from NIST 800-53 baselines**: NIST 800-53 uses a **single high-water mark** baseline (Low/Moderate/High). CNSSI 1253 uses **independent C/I/A levels** — a system can be categorized as `(Moderate, Moderate, Low)` for `(Confidentiality, Integrity, Availability)`, producing a control selection that differs from any single NIST baseline.

### Relationship Between CNSSI 1253 and NIST 800-53 Baselines

```
NIST 800-53 Rev 5 Baselines (FIPS 200 / SP 800-53B):
  Low:      131 controls (base + enhancements)
  Moderate: 325 controls
  High:     421 controls

CNSSI 1253 Approach:
  1. Start with the NIST baseline corresponding to each C/I/A level independently
  2. Apply CNSSI 1253 overlay to add/modify/remove controls based on:
     - The system's C/I/A categorization triple
     - The DoD Impact Level (IL2, IL4, IL5, IL6)
     - System type and operational environment
  3. Result: A tailored control set that is a superset of the NIST baseline
```

CNSSI 1253 defines **three separate baselines** — one for Confidentiality, one for Integrity, one for Availability — at Low/Moderate/High each. The union of controls from all three dimensions forms the system's required control set. This always produces a result **at least as restrictive** as the NIST high-water mark baseline, and usually more restrictive.

### How Controls Are Added/Modified by Impact Level

**DoD Impact Levels (from DoDI 8510.01 / DISA Cloud SRG):**

| Impact Level | Data Types | NIST Baseline Floor | CNSSI 1253 Overlay Effect |
|---|---|---|---|
| **IL2** | Public, non-CUI unclassified | Low | Minimal additions — mostly parameter tightening (shorter password expiry, faster session timeout) |
| **IL4** | CUI (FOUO, Privacy, Export-controlled) | Moderate | Adds ~30-50 controls/enhancements beyond Moderate baseline. Adds FIPS 140-2 requirements to SC controls, US-person restrictions to PS controls, enhanced audit to AU controls |
| **IL5** | Higher-sensitivity CUI, mission-critical, NSS | High (or Moderate+overlay) | Adds ~50-80 additional controls/enhancements. Requires physical separation, dedicated infrastructure, enhanced cryptography (FIPS 140-2 Level 3), CAP connectivity |
| **IL6** | Classified (Secret) | High + classified overlay | Adds classified handling controls, mandatory two-person integrity for certain operations, Cross Domain Solution (CDS) controls, enhanced physical security. Approximately 30-40 additional controls beyond IL5 |

**Types of modifications per control:**

1. **Add control**: Control not in NIST baseline but required by overlay (e.g., SI-4(22) at IL5 — not in NIST High baseline but added for NSS)
2. **Add enhancement**: Base control exists but an additional enhancement is required (e.g., AC-2 exists in Moderate, but AC-2(13) added by IL5 overlay)
3. **Modify parameter**: Control exists but parameter values are tightened (e.g., AC-12 session timeout: NIST says "organization-defined", CNSSI 1253 IL5 specifies "15 minutes")
4. **Add supplemental guidance**: DoD-specific implementation guidance added to control narrative
5. **Remove control**: Rare — only when a control is explicitly not applicable to NSS (e.g., certain PT controls for systems that don't process PII)

### CNSSI 1253 Overlay Data Structure

**Decision**: Model CNSSI 1253 overlays as a JSON reference data file (`cnssi-1253-overlays.json`) loaded at startup, with typed C# records for deserialization. Not stored in EF Core — this is **reference data** (like the OSCAL catalog), not user-mutable data.

**Rationale**: Overlay data changes only when CNSSI 1253 is revised (infrequently — last major revision was 2014, updated 2022). It maps cleanly to a static JSON file that ships with the application, similar to how `impact-levels.json` and `stig-controls.json` are structured today. Storing in the database would add migration complexity for data that users don't modify.

**Alternatives considered**:
- **EF Core seed data**: Rejected — overlay data is dense (1000+ entries), mostly read-only, and would create large migration files. The OSCAL catalog (254K lines) already proved that JSON reference data files work well for this pattern.
- **Embedded OSCAL profile format**: NIST publishes baselines as OSCAL profiles that reference the catalog. CNSSI 1253 does **not** have an official OSCAL profile published by CNSS. We would have to author our own, which means we're the source of truth either way. A simpler flat JSON is easier to maintain and audit.
- **Database table with admin UI**: Rejected — overlay data should not be editable by users. An ISSM modifying the CNSSI 1253 overlay would produce non-compliant results. Tailoring (local add/remove with rationale) is a separate concept tracked in the `ControlTailoring` entity.

**Proposed JSON structure:**

```jsonc
{
  "version": "2022-03",
  "source": "CNSSI 1253 (March 2014, updated 2022) — Security Posture Intelligence Navigator representation",
  "description": "DoD/CNSS overlay mappings to NIST SP 800-53 Rev 5 controls",

  // Per-control overlay entries
  "overlays": [
    {
      "controlId": "ac-2",              // NIST 800-53 control ID (matches OSCAL catalog)
      "family": "AC",                    // Family code for filtering
      "isEnhancement": false,

      // Which C/I/A dimensions require this control and at what level
      // A control can be required by any combination of C/I/A dimensions
      "applicability": {
        "confidentiality": "Low",        // null = not applicable to this dimension
        "integrity": "Low",
        "availability": "Low"
      },

      // Impact Level overlay details — what changes at each IL
      "impactLevelOverrides": {
        "IL2": null,                     // null = no changes beyond NIST baseline
        "IL4": {
          "parameterOverrides": {
            "ac-02_odp.01": "annually",      // CNSSI 1253 sets "organization-defined" to specific value
            "ac-02_odp.10": "within 24 hours" // Tighter than NIST's "organization-defined"
          },
          "supplementalGuidance": "DoD requires review of privileged accounts quarterly rather than annually.",
          "additionalAssessmentProcedures": null
        },
        "IL5": {
          "parameterOverrides": {
            "ac-02_odp.01": "quarterly",
            "ac-02_odp.10": "within 8 hours"
          },
          "supplementalGuidance": "NSS systems must implement automated account management with continuous monitoring. Privileged accounts require two-party approval for creation.",
          "additionalAssessmentProcedures": "Verify automated account management is integrated with the organization's identity management system. Verify two-party approval for privileged account creation."
        },
        "IL6": {
          "parameterOverrides": {
            "ac-02_odp.01": "monthly",
            "ac-02_odp.10": "within 4 hours"
          },
          "supplementalGuidance": "Classified system accounts must be reviewed monthly. All account creation/modification requires documented approval from the ISSM and recorded in the system audit log.",
          "additionalAssessmentProcedures": "Verify monthly account reviews are documented. Verify ISSM approval records for all account changes."
        }
      },

      // Controls added by the overlay that are NOT in any NIST baseline
      "addedByOverlay": false
    },
    {
      "controlId": "ac-2(13)",
      "family": "AC",
      "isEnhancement": true,

      "applicability": {
        "confidentiality": "High",
        "integrity": null,
        "availability": null
      },

      // This enhancement is added by the overlay — not in NIST Moderate baseline
      "addedByOverlay": true,
      "addedAtImpactLevel": "IL5",       // First IL where this control appears

      "impactLevelOverrides": {
        "IL2": null,
        "IL4": null,
        "IL5": {
          "parameterOverrides": {
            "ac-02.13_odp.01": "30 days"
          },
          "supplementalGuidance": "Disable accounts of users who have not authenticated within 30 days. Exceptions require documented ISSM approval with expiration date.",
          "additionalAssessmentProcedures": null
        },
        "IL6": {
          "parameterOverrides": {
            "ac-02.13_odp.01": "15 days"
          },
          "supplementalGuidance": "Classified systems: 15-day inactivity threshold. No exceptions permitted without AO approval.",
          "additionalAssessmentProcedures": null
        }
      }
    }
  ]
}
```

**C# record models:**

```csharp
// Reference data record — deserialized from cnssi-1253-overlays.json
public record Cnssi1253OverlayData(
    string Version,
    string Source,
    string Description,
    List<Cnssi1253ControlOverlay> Overlays);

public record Cnssi1253ControlOverlay(
    string ControlId,
    string Family,
    bool IsEnhancement,
    CiaApplicability Applicability,
    Dictionary<string, ImpactLevelOverride?> ImpactLevelOverrides,
    bool AddedByOverlay = false,
    string? AddedAtImpactLevel = null);

public record CiaApplicability(
    string? Confidentiality,   // "Low", "Moderate", "High", or null
    string? Integrity,
    string? Availability);

public record ImpactLevelOverride(
    Dictionary<string, string>? ParameterOverrides,
    string? SupplementalGuidance,
    string? AdditionalAssessmentProcedures);
```

**Parameter IDs**: CNSSI 1253 parameter overrides reference OSCAL parameter IDs from the catalog (e.g., `ac-02_odp.01`). These IDs correspond to the `params[].id` field in the OSCAL catalog JSON. This allows the UI to display: "NIST says: *[organization-defined frequency]* → CNSSI 1253 IL5 says: *quarterly*".

### Estimated Overlay Entry Count

Based on CNSSI 1253 Attachment 2 (Baseline Control Sets):
- **~370 base controls** have overlay applicability entries (C/I/A dimension assignments)
- **~80 controls** have IL-specific parameter overrides
- **~60 controls/enhancements** are added by overlay (not in standard NIST baselines)
- **Total JSON entries**: ~450-500 overlay records

---

## R2: NIST 800-53 Rev 5 Baseline Counts

### Official Counts from NIST SP 800-53B (Control Baselines)

**Decision**: Use the NIST SP 800-53B (October 2020, updated December 2020) official baseline allocations. Counts include both base controls and control enhancements.

**Source**: NIST SP 800-53B, Table 1 (Control Baselines for Information Systems). Cross-referenced with the OSCAL catalog version 5.2.0 already embedded in the project at `src/Ato.Copilot.Agents/Compliance/Resources/NIST_SP-800-53_rev5_catalog.json`.

| Baseline | Base Controls | Enhancements | **Total** |
|---|---|---|---|
| **Low** | 82 | 49 | **131** |
| **Moderate** | 170 | 155 | **325** |
| **High** | 170 | 251 | **421** |

**Important clarifications:**
- These counts come from **SP 800-53B** (the baselines publication), not SP 800-53 (the catalog). The catalog contains **all 1,189 controls and enhancements** across 20 families. Baselines are subsets.
- The spec document already uses these numbers: `Low=131, Moderate=325, High=421` (Part 2, Step 2). These are confirmed correct against SP 800-53B.
- **Rev 5.1.1** added 1 control (IA-13) and 3 enhancements. **Rev 5.2.0** (July 2025) added further corrections. The baseline allocations for new controls are published in SP 800-53B updates.
- PM (Program Management) family controls are **not assigned to baselines** — they are organization-level, not system-level. 39 PM controls + enhancements exist in the catalog but appear in none of the Low/Moderate/High baselines.
- PT (PII Processing and Transparency) controls from Rev 5 are partially allocated to baselines (7 base controls + 5 enhancements in Moderate/High).
- SR (Supply Chain Risk Management) controls from Rev 5 are partially allocated (all in Moderate and High, none in Low).

### Full Catalog Breakdown by Family

| Family | Total Controls+Enhancements | In Low | In Moderate | In High |
|---|---|---|---|---|
| AC | 113 | 16 | 42 | 55 |
| AT | 9 | 4 | 5 | 5 |
| AU | 33 | 10 | 17 | 24 |
| CA | 27 | 8 | 12 | 14 |
| CM | 34 | 8 | 16 | 23 |
| CP | 32 | 6 | 14 | 23 |
| IA | 36 | 10 | 20 | 25 |
| IR | 20 | 7 | 12 | 17 |
| MA | 16 | 4 | 9 | 13 |
| MP | 15 | 4 | 9 | 11 |
| PE | 38 | 12 | 17 | 22 |
| PL | 10 | 4 | 5 | 5 |
| PM | 39 | 0 | 0 | 0 |
| PS | 14 | 8 | 9 | 9 |
| PT | 18 | 0 | 7 | 12 |
| RA | 16 | 4 | 8 | 11 |
| SA | 45 | 7 | 22 | 30 |
| SC | 96 | 12 | 38 | 57 |
| SI | 40 | 7 | 16 | 24 |
| SR | 14 | 0 | 12 | 14 |
| **Total** | **665 unique** | **131** | **325** | **421** |

> **Note**: The full catalog contains ~1,189 items (controls + enhancements) but many have no baseline allocation (PM family, withdrawn controls, privacy-only controls). The 665 figure above counts only the unique controls/enhancements that participate in at least one baseline or are candidates for overlay selection.

**Rationale for embedding counts**: The baseline counts are needed for the `ControlBaseline` entity to show "X of Y controls documented" progress. Rather than querying the OSCAL catalog at runtime to count baseline-allocated controls, we should precompute baseline membership during catalog parsing and cache it. The existing `NistControl.Baselines` property (`List<string>`) already tracks which baselines each control belongs to — this is the correct approach.

**Constants to add to `ComplianceFrameworks.cs`:**

```csharp
/// <summary>
/// NIST SP 800-53B official baseline control counts (base controls + enhancements).
/// </summary>
public static readonly IReadOnlyDictionary<string, int> BaselineControlCounts =
    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Low"] = 131,
        ["Moderate"] = 325,
        ["High"] = 421
    };

/// <summary>
/// Total controls + enhancements in the NIST SP 800-53 Rev 5 catalog
/// (excluding withdrawn controls).
/// </summary>
public const int TotalCatalogControls = 1189;
```

---

## R3: FIPS 199 Categorization Data Model

### What FIPS 199 Categorization Requires

FIPS 199 (*Standards for Security Categorization of Federal Information and Information Systems*) defines how to categorize a system based on the potential impact (Low/Moderate/High) of a loss of **Confidentiality**, **Integrity**, and **Availability**.

The process:
1. **Identify information types** processed/stored/transmitted by the system (using NIST SP 800-60 Vol 2 as the taxonomy)
2. **Assign C/I/A impact levels** to each information type (Low/Moderate/High per FIPS 199 definitions)
3. **Calculate the system-level impact** as the **high-water mark** across all information types, per dimension
4. **Determine the overall system categorization** as the highest of the three C/I/A values (this becomes the NIST baseline selector)

**FIPS 199 formal notation:**

```
SC(information system) = {
  (confidentiality, impact),
  (integrity, impact),
  (availability, impact)
}

Where impact ∈ { Low, Moderate, High }

Example: SC(ACME Portal) = { (confidentiality, Moderate), (integrity, Moderate), (availability, Low) }
Overall categorization: Moderate (high-water mark)
```

### NIST SP 800-60 Information Types

SP 800-60 Vol 2 provides a taxonomy of ~180 information types organized into categories. Each information type has **provisional** (recommended default) C/I/A impact levels that can be adjusted with justification.

**Examples:**

| SP 800-60 ID | Information Type | Prov. C | Prov. I | Prov. A |
|---|---|---|---|---|
| D.1.1 | National Defense — Strategic Planning | High | High | High |
| D.3.1 | Intelligence Operations — Intelligence Planning | High | High | Moderate |
| C.2.8.7 | Personnel Management — Payroll | Moderate | Moderate | Low |
| C.3.5.1 | IT Infrastructure Maintenance — IT Security | High | High | Moderate |
| D.14.1 | Defense Logistics — Supply Chain Mgmt | Moderate | Moderate | Moderate |

### DoD IL Mapping from FIPS 199

For DoD systems, the FIPS 199 categorization maps to a DoD Impact Level:

| FIPS 199 Overall | Data Sensitivity | DoD IL |
|---|---|---|
| Low | Public/non-sensitive unclassified | IL2 |
| Moderate | CUI (any type) | IL4 or IL5 (depends on mission criticality) |
| Moderate + NSS designation | Higher-sensitivity CUI | IL5 |
| High | Mission-critical / high-impact CUI | IL5 |
| High + Classified | Secret and above | IL6 |

> **Note**: IL3 was deprecated by DISA in 2017 (merged into IL2). There is no IL1 in practice. IL4 vs IL5 for Moderate systems depends on the **mission criticality** and whether the system is designated as a National Security System (NSS) under FISMA Section 3553.

### Data Model

**Decision**: Model FIPS 199 categorization as an EF Core entity (`SecurityCategorization`) with a child collection of information types (`InformationType`). The high-water mark is a computed property, not stored. The DoD IL mapping is also computed from the categorization + a user-supplied `isNationalSecuritySystem` flag.

**Rationale**: The high-water mark must always reflect the current information types — storing it as a column would create a stale-data risk. Computing it is O(n) where n = number of information types (typically 3-15 per system), which is trivially fast. The DoD IL mapping depends on both the categorization and NSS designation, so it too should be computed.

**Alternatives considered**:
- **Store high-water mark as a column**: Rejected — creates sync risk. If an information type's impact is updated without recalculating, the system could be under-categorized (security risk).
- **Single C/I/A triple without information types**: Rejected — auditors and SCAs need to see *why* the system is categorized at a given level. The information type inventory is a required artifact per NIST SP 800-60 and DoDI 8510.01.
- **Separate InfoType table with FK to categorization**: This is the chosen approach — normalized, supports multiple info types per system, supports audit trail of changes.

**EF Core entities:**

```csharp
/// <summary>
/// FIPS 199 security categorization for a registered system.
/// One-to-one with RegisteredSystem.
/// </summary>
public class SecurityCategorization
{
    /// <summary>Primary key (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to RegisteredSystem.</summary>
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Whether this system is designated as a National Security System (NSS).
    /// Affects DoD IL mapping: NSS Moderate → IL5 instead of IL4.</summary>
    public bool IsNationalSecuritySystem { get; set; }

    /// <summary>Justification text for the categorization determination.</summary>
    public string? Justification { get; set; }

    /// <summary>User who performed/approved the categorization.</summary>
    public string CategorizedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp of categorization.</summary>
    public DateTime CategorizedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of last modification.</summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>Information types processed by this system.</summary>
    public List<InformationType> InformationTypes { get; set; } = new();

    // ─── Computed Properties ───

    /// <summary>System-level Confidentiality impact (high-water mark across all info types).</summary>
    public ImpactValue ConfidentialityImpact =>
        InformationTypes.Count == 0 ? ImpactValue.Low
        : InformationTypes.Max(it => it.ConfidentialityImpact);

    /// <summary>System-level Integrity impact (high-water mark).</summary>
    public ImpactValue IntegrityImpact =>
        InformationTypes.Count == 0 ? ImpactValue.Low
        : InformationTypes.Max(it => it.IntegrityImpact);

    /// <summary>System-level Availability impact (high-water mark).</summary>
    public ImpactValue AvailabilityImpact =>
        InformationTypes.Count == 0 ? ImpactValue.Low
        : InformationTypes.Max(it => it.AvailabilityImpact);

    /// <summary>
    /// Overall system categorization (highest of C/I/A).
    /// This determines the NIST baseline: Low → Low, Moderate → Moderate, High → High.
    /// </summary>
    public ImpactValue OverallCategorization =>
        new[] { ConfidentialityImpact, IntegrityImpact, AvailabilityImpact }.Max();

    /// <summary>
    /// DoD Impact Level derived from categorization + NSS flag.
    /// IL2: Low overall, non-NSS
    /// IL4: Moderate overall, non-NSS
    /// IL5: High overall, OR Moderate+NSS
    /// IL6: Requires explicit classified designation (not auto-derived)
    /// </summary>
    public string DoDImpactLevel => OverallCategorization switch
    {
        ImpactValue.Low => "IL2",
        ImpactValue.Moderate => IsNationalSecuritySystem ? "IL5" : "IL4",
        ImpactValue.High => "IL5",
        _ => "IL2"
    };
    // Note: IL6 (classified) cannot be auto-derived — requires explicit AO designation
    // stored on RegisteredSystem.ClassifiedDesignation

    /// <summary>FIPS 199 formal notation string.</summary>
    public string FormalNotation =>
        $"SC = {{(confidentiality, {ConfidentialityImpact}), (integrity, {IntegrityImpact}), (availability, {AvailabilityImpact})}}";

    /// <summary>NIST baseline name derived from overall categorization.</summary>
    public string NistBaseline => OverallCategorization.ToString();
}

/// <summary>FIPS 199 impact level values.</summary>
public enum ImpactValue
{
    Low = 0,
    Moderate = 1,
    High = 2
}

/// <summary>
/// An information type processed/stored/transmitted by a system,
/// per NIST SP 800-60 Vol 2 taxonomy.
/// </summary>
public class InformationType
{
    /// <summary>Primary key (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to SecurityCategorization.</summary>
    public string SecurityCategorizationId { get; set; } = string.Empty;

    /// <summary>SP 800-60 identifier (e.g., "D.1.1", "C.2.8.7").</summary>
    public string Sp80060Id { get; set; } = string.Empty;

    /// <summary>Information type name (e.g., "National Defense — Strategic Planning").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Category/subcategory from SP 800-60 taxonomy.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Confidentiality impact for this information type.</summary>
    public ImpactValue ConfidentialityImpact { get; set; } = ImpactValue.Low;

    /// <summary>Integrity impact for this information type.</summary>
    public ImpactValue IntegrityImpact { get; set; } = ImpactValue.Low;

    /// <summary>Availability impact for this information type.</summary>
    public ImpactValue AvailabilityImpact { get; set; } = ImpactValue.Low;

    /// <summary>Whether the C/I/A values match SP 800-60 provisional recommendations.</summary>
    public bool UsesProvisionalImpactLevels { get; set; } = true;

    /// <summary>
    /// If any C/I/A level was adjusted from the SP 800-60 provisional value,
    /// this is the documented justification (required for audit).
    /// </summary>
    public string? AdjustmentJustification { get; set; }
}
```

### SP 800-60 Information Type Reference Data

**Decision**: Provide a JSON reference file (`sp800-60-information-types.json`) with the ~180 SP 800-60 Vol 2 information types and their provisional C/I/A levels. Users select from this list when adding information types; the provisional levels auto-populate but can be overridden with justification.

**Structure:**

```jsonc
{
  "version": "2.0",
  "source": "NIST SP 800-60 Vol 2, Rev 1 (August 2008)",
  "categories": [
    {
      "id": "C.2",
      "name": "Services Delivery Support Functions",
      "subcategories": [
        {
          "id": "C.2.8",
          "name": "Human Resources Management",
          "informationTypes": [
            {
              "id": "C.2.8.7",
              "name": "Payroll Management and Time & Attendance",
              "description": "...",
              "provisionalImpact": {
                "confidentiality": "Moderate",
                "integrity": "Moderate",
                "availability": "Low"
              },
              "specialFactors": "Payroll data may be elevated to High confidentiality if it includes banking information for a large population."
            }
          ]
        }
      ]
    },
    {
      "id": "D",
      "name": "Mission-Based Functions",
      "subcategories": [
        {
          "id": "D.1",
          "name": "National Defense",
          "informationTypes": [
            {
              "id": "D.1.1",
              "name": "Strategic Planning",
              "description": "...",
              "provisionalImpact": {
                "confidentiality": "High",
                "integrity": "High",
                "availability": "High"
              },
              "specialFactors": null
            }
          ]
        }
      ]
    }
  ]
}
```

---

## R4: DISA STIG to NIST 800-53 Control Mapping

### How STIG Rules Map to NIST Controls

The mapping chain is:

```
STIG Rule → CCI (Control Correlation Identifier) → NIST 800-53 Control

Example:
  SV-12345r1_rule → CCI-000130, CCI-000131 → AU-2, AU-3, AU-12
```

**CCI (Control Correlation Identifier)** is the DISA-maintained bridge between STIGs and NIST controls. Each CCI maps to exactly one NIST 800-53 control (including specific enhancement). Each STIG rule can reference multiple CCIs, which means a single STIG rule can map to multiple NIST controls.

The CCI list is maintained by DISA and published as an XML file (`U_CCI_List.xml`). As of 2025, it contains ~7,575 CCIs mapping to NIST 800-53 Rev 5.

### STIG Distribution Format

STIGs are published by DISA as **XCCDF** (Extensible Configuration Checklist Description Format) XML files, packaged in ZIP archives. Each STIG ZIP contains:

```
U_<Technology>_V<Version>R<Release>_STIG/
├── U_<Technology>_V<Version>R<Release>_Manual-xccdf.xml   ← The STIG rules
├── U_<Technology>_V<Version>R<Release>_STIG_Benchmarks/
│   └── ... (optional SCAP content)
└── readme.txt
```

**The XCCDF XML structure for a single rule:**

```xml
<Group id="V-12345">
  <title>SRG-OS-000004-GPOS-00004</title>
  <description>&lt;GroupDescription&gt;&lt;/GroupDescription&gt;</description>
  <Rule id="SV-12345r1_rule" severity="high" weight="10.0">
    <version>WN22-AU-000010</version>
    <title>Windows Server must have audit policy enabled for logon events</title>
    <description>
      &lt;VulnDiscussion&gt;Without generating audit records...&lt;/VulnDiscussion&gt;
      &lt;FalsePositives&gt;&lt;/FalsePositives&gt;
      &lt;FalseNegatives&gt;&lt;/FalseNegatives&gt;
      &lt;Documentable&gt;false&lt;/Documentable&gt;
      &lt;Mitigations&gt;&lt;/Mitigations&gt;
      &lt;SeverityOverrideGuidance&gt;&lt;/SeverityOverrideGuidance&gt;
      &lt;ThirdPartyTools&gt;&lt;/ThirdPartyTools&gt;
      &lt;MitigationControl&gt;&lt;/MitigationControl&gt;
      &lt;Responsibility&gt;System Administrator&lt;/Responsibility&gt;
      &lt;IAControls&gt;&lt;/IAControls&gt;
    </description>
    <reference>
      <dc:publisher>DISA</dc:publisher>
      <dc:source>STIG.DOD.MIL</dc:source>
    </reference>
    <ident system="http://cyber.mil/cci">CCI-000130</ident>
    <ident system="http://cyber.mil/cci">CCI-000131</ident>
    <ident system="http://cyber.mil/cci">CCI-000132</ident>
    <fixtext fixref="F-12345r1_fix">
      Configure the policy value for Computer Configuration >> Windows Settings >> ...
    </fixtext>
    <check system="C-12345r1_chk">
      <check-content>Open the local Group Policy Editor (gpedit.msc). Navigate to ...</check-content>
    </check>
  </Rule>
</Group>
```

**Key fields in a STIG rule:**

| Field | XML Path | Description | Maps to existing `StigControl` |
|---|---|---|---|
| Vuln ID | `Group/@id` | Vulnerability number (e.g., V-12345) | `VulnId` |
| Rule ID | `Rule/@id` | Rule identifier with revision (e.g., SV-12345r1_rule) | `RuleId` |
| Severity | `Rule/@severity` | `high` / `medium` / `low` (= CAT I/II/III) | `Severity` |
| STIG ID / Version | `Rule/version` | Technology-specific ID (e.g., WN22-AU-000010) | `StigId` |
| Title | `Rule/title` | Short rule title | `Title` |
| Discussion | `Rule/description` (parsed inner XML) | Full vulnerability discussion | `Description` |
| CCI References | `Rule/ident[@system="http://cyber.mil/cci"]` | CCI numbers for NIST mapping | `CciRefs` |
| Fix Text | `Rule/fixtext` | Remediation instructions | `FixText` |
| Check Text | `Rule/check/check-content` | Verification procedure | `CheckText` |
| Responsibility | Embedded in description | Who is responsible | (not yet modeled) |
| Documentable | Embedded in description | Whether a documented exception is acceptable | (not yet modeled) |
| Mitigations | Embedded in description | Possible mitigations | (not yet modeled) |

### Existing `StigControl` Model Assessment

The existing `StigControl` record in [StigModels.cs](src/Ato.Copilot.Core/Models/Compliance/StigModels.cs) is well-structured and covers the essential fields. Enhancements needed for expanded STIG support:

**Decision**: Extend `StigControl` with additional XCCDF fields, add a new `StigBenchmark` container record, and add a `CciMapping` reference data model. The existing 7-entry `stig-controls.json` becomes the seed; expanded STIG data follows the same format.

**Fields to add to `StigControl`:**

```csharp
// Additional fields for full XCCDF fidelity
public record StigControl(
    // ... existing fields ...
    string StigId,
    string VulnId,
    string RuleId,
    string Title,
    string Description,
    StigSeverity Severity,
    string Category,          // Technology category (e.g., "Windows Server 2022")
    string StigFamily,        // Mapped NIST family
    List<string> NistControls,
    List<string> CciRefs,
    string CheckText,
    string FixText,
    Dictionary<string, string> AzureImplementation,
    string ServiceType,
    // --- new fields ---
    string? StigVersion = null,         // XCCDF version (e.g., "WN22-AU-000010")
    string? BenchmarkId = null,         // Parent benchmark ID
    string? Responsibility = null,      // "System Administrator", "IA Officer", etc.
    bool Documentable = false,          // Whether exceptions can be documented
    string? MitigationGuidance = null,  // Possible mitigations
    decimal Weight = 10.0m,             // Rule weight (default 10)
    string? SeverityOverrideGuidance = null,
    DateTime? ReleaseDate = null        // STIG release date
);
```

**New container for a STIG benchmark:**

```csharp
/// <summary>
/// Represents a DISA STIG benchmark (technology-specific rule set).
/// </summary>
public record StigBenchmark(
    string BenchmarkId,          // e.g., "Windows_Server_2022_STIG"
    string Title,                // e.g., "Microsoft Windows Server 2022 STIG"
    string Version,              // e.g., "V2R1"
    DateTime ReleaseDate,
    string Publisher,            // "DISA"
    int RuleCount,
    int CatICount,
    int CatIICount,
    int CatIIICount,
    List<string> ApplicablePlatforms,  // e.g., ["Windows Server 2022"]
    List<StigControl> Rules);
```

### CCI Reference Data

**Decision**: Include a CCI-to-NIST mapping JSON reference file (`cci-nist-mapping.json`) with the essential CCI→Control mappings. This allows STIG rules to be resolved to NIST controls without embedding the full NIST control ID in every STIG entry.

```jsonc
{
  "version": "2024-06-11",
  "source": "DISA CCI List (U_CCI_List.xml)",
  "mappings": [
    {
      "cciId": "CCI-000130",
      "nistControlId": "au-3",
      "definition": "The information system generates audit records containing information that establishes what type of event occurred.",
      "status": "published"       // "published", "draft", "deprecated"
    },
    {
      "cciId": "CCI-000131",
      "nistControlId": "au-3",
      "definition": "The information system generates audit records containing information that establishes when the event occurred.",
      "status": "published"
    }
    // ... ~7,575 total entries
  ]
}
```

### STIG Data Sourcing Strategy

**Decision**: Start with a curated subset of ~200 rules covering the most common Azure-related technologies, then provide an import mechanism for full XCCDF STIGs.

**Rationale**: The complete DISA STIG library contains 300+ benchmarks with 30,000+ rules. Loading all of them is impractical for a JSON file. The curated subset covers the technologies most likely deployed on Azure (Windows Server 2022, SQL Server 2019, IIS 10, Azure-specific configuration). Additional STIGs are loaded via an import tool that parses XCCDF XML.

**Priority STIGs for initial inclusion:**

| STIG | Est. Rules | Relevance |
|---|---|---|
| Microsoft Windows Server 2022 | ~280 | Most common Azure VM OS |
| Microsoft SQL Server 2019 | ~120 | Most common Azure DB workload |
| Microsoft IIS 10 | ~80 | Web server on Azure VMs |
| Microsoft Windows 11 | ~240 | Client endpoints |
| Microsoft Azure Foundations | ~50 | CIS Azure benchmark (DISA-adapted) |
| Docker Enterprise | ~30 | Container workloads |
| Kubernetes | ~80 | AKS workloads |
| **Total initial** | **~880** | |

---

## R5: eMASS Export Formats

### What eMASS Is

eMASS (Enterprise Mission Assurance Support Service) is the DoD's official GRC (Governance, Risk, and Compliance) system for managing RMF packages. All DoD systems must be registered in eMASS, and authorization packages are submitted/reviewed through eMASS. It is operated by DISA.

### eMASS Data Exchange Formats

eMASS supports several import/export formats:

| Format | Direction | Purpose |
|---|---|---|
| **Excel (.xlsx)** | Import & Export | Control-level data, POA&M items, test results. Most commonly used format. eMASS exports are worksheets with specific column headers. |
| **CSV** | Import & Export | Alternative to Excel for programmatic use. Same column structure. |
| **XCCDF Results** | Import only | SCAP scan results (SCC tool output). Imported to populate compliance findings. |
| **STIG Checklist (.ckl / .cklb)** | Import only | STIG Viewer checklist files. CKL (legacy XML) and CKLB (new JSON-based). Populates per-STIG compliance status. |
| **eMASS REST API** | Both | API for programmatic access (requires PKI/CAC auth). JSON payloads. |
| **POAM Import Template (.xlsx)** | Import | Specific Excel template for bulk-importing POA&M items. |

### eMASS Control-Level Export Fields

**Decision**: Model the eMASS export as a flat record per control row, matching the eMASS Excel export column headers. This allows round-trip import/export without data loss.

The eMASS control-level export (from the "Controls" worksheet) contains these columns:

```csharp
/// <summary>
/// Represents a single row in an eMASS control-level export.
/// Column names match the eMASS Excel export format exactly.
/// </summary>
public record EmassControlExportRow(
    // ─── System Identification ───
    string SystemName,                      // "ACME Portal"
    string SystemAcronym,                   // "ACME"
    string DitprId,                         // DoD IT Portfolio Repository ID
    string EmassId,                         // eMASS system identifier (integer)

    // ─── Control Identification ───
    string ControlIdentifier,               // "AC-2" (NIST control ID, uppercase)
    string ControlName,                     // "Account Management"
    string ControlFamily,                   // "Access Control"

    // ─── Implementation ───
    string ImplementationStatus,            // "Implemented", "Partially Implemented",
                                            // "Planned", "Not Applicable", "Not Implemented"
    string? ImplementationNarrative,        // The SSP implementation description
    string? CommonControlProvider,          // Organization providing common/inherited control
    string ResponsibilityType,             // "Inherited", "Shared", "System-Specific"
                                            // (eMASS uses these terms vs. "Customer")

    // ─── Assessment ───
    string? ComplianceStatus,              // "Compliant", "Non-Compliant", "Not Assessed"
    string? AssessmentProcedure,           // "Test", "Interview", "Examine"
    string? AssessorName,                  // SCA who assessed
    DateTime? AssessmentDate,              // Date of assessment
    string? TestResult,                    // Assessment result narrative

    // ─── Applicable Baseline ───
    string SecurityControlBaseline,        // "Moderate", "High"
    bool IsOverlayControl,                 // Whether added by overlay (CNSSI 1253)
    string? OverlayName,                   // "CNSSI 1253 IL5"

    // ─── AP / SSP Fields ───
    string? ApNumber,                      // Security Plan (AP) number 
    string? SecurityPlanTitle,

    // ─── Metadata ───
    DateTime? LastModified,
    string? ModifiedBy
);
```

### eMASS POA&M Export Fields

The POA&M worksheet export has these columns:

```csharp
/// <summary>
/// Represents a single row in an eMASS POA&M export.
/// </summary>
public record EmassPoamExportRow(
    // ─── System ───
    string SystemName,
    string EmassId,

    // ─── POA&M Item ───
    string PoamId,                         // eMASS-assigned POA&M item number
    string Weakness,                       // Weakness/finding description
    string WeaknessSource,                 // "ACAS", "STIG", "Manual Assessment", "SCA Assessment"
    string PointOfContact,                 // POC name for remediation
    string? PocEmail,
    string SecurityControlNumber,          // Related NIST control (e.g., "AC-2")

    // ─── Severity ───
    string RawSeverity,                    // "I", "II", "III" (CAT level)
    string? RelevanceOfThreat,             // "High", "Moderate", "Low", "Very Low"
    string? LikelihoodOfExploitation,      // "High", "Moderate", "Low", "Very Low"
    string? ImpactDescription,
    string? ResidualRiskLevel,             // "High", "Moderate", "Low", "Very Low"

    // ─── Remediation ───
    string ScheduledCompletionDate,        // Target fix date (MM/DD/YYYY)
    string? PlannedMilestones,             // Milestone description text
    string? MilestoneChanges,              // Milestone change history
    string? ResourcesRequired,             // "Funding", "Personnel", "Technology"
    string? CostEstimate,                  // Dollar amount

    // ─── Status ───
    string Status,                         // "Ongoing", "Completed", "Delayed", "Risk Accepted"
    DateTime? CompletionDate,              // Actual completion date
    string? Comments,
    bool IsActive,                         // Whether the POA&M item is still open

    // ─── Metadata ───
    DateTime? CreatedDate,
    DateTime? LastUpdatedDate,
    string? LastUpdatedBy
);
```

### eMASS REST API

eMASS provides a REST API (documented at https://emass.apps.disa.mil/api/) that uses PKI (CAC/PIV) certificate-based authentication. Key endpoints:

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/systems` | GET | List registered systems |
| `/api/systems/{id}/controls` | GET | Get control compliance data |
| `/api/systems/{id}/poams` | GET/POST | Get/create POA&M items |
| `/api/systems/{id}/test-results` | GET/POST | Get/submit test results |
| `/api/systems/{id}/artifacts` | GET/POST | Get/upload evidence artifacts |
| `/api/systems/{id}/milestones` | GET/POST | Get/create milestones |

**Decision**: For Phase 5 (eMASS Data Exchange), implement **Excel export first** (most familiar to users), then CSV export, then API integration. Excel is the universally understood format — every ISSM knows how to import an .xlsx into eMASS.

**Rationale**: The eMASS REST API requires CAC authentication and is only available from DoD networks (NIPR/SIPR). Many organizations prefer Excel uploads because they allow review before import. Excel export has zero infrastructure dependencies.

**Alternatives considered**:
- **API-first**: Rejected — requires CAC middleware, DoD network access, and DISA API key approval. Too many dependencies for initial release.
- **OSCAL export (SSP/POA&M in OSCAL format)**: Interesting future option — eMASS has stated intent to support OSCAL import. However, as of 2025, OSCAL import in eMASS is experimental/limited. The OSCAL SSP format is complex (deeply nested JSON/XML) and would require significant effort to generate correctly. Worth revisiting when eMASS OSCAL support matures.
- **STIG Checklist (.ckl/.cklb) export**: Worth implementing alongside Excel — eMASS can import STIG checklists to populate compliance status. The `.cklb` (JSON-based) format is simpler to generate than the legacy `.ckl` (XML) format.

### eMASS Export Implementation Plan

**Library choice**: `ClosedXML` (MIT license, actively maintained, .NET 8+ compatible) for Excel generation. Avoids the COM dependency of `Microsoft.Office.Interop.Excel` and the complexity of `EPPlus` licensing (Polyform Non-Commercial since v5).

```csharp
/// <summary>
/// Service for generating eMASS-compatible export files.
/// </summary>
public interface IEmassExportService
{
    /// <summary>
    /// Export control compliance data to eMASS-compatible Excel format.
    /// </summary>
    Task<byte[]> ExportControlsToExcelAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Export POA&M items to eMASS-compatible Excel format.
    /// </summary>
    Task<byte[]> ExportPoamToExcelAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Export control compliance data to CSV.
    /// </summary>
    Task<string> ExportControlsToCsvAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import eMASS control export (Excel or CSV) with conflict resolution.
    /// </summary>
    Task<EmassImportResult> ImportControlsAsync(
        Stream fileStream,
        string registeredSystemId,
        EmassImportOptions options,
        CancellationToken cancellationToken = default);
}

public record EmassImportOptions(
    ConflictResolution OnConflict = ConflictResolution.PreferExisting,
    bool DryRun = true,
    bool ImportNarratives = true,
    bool ImportAssessmentResults = true);

public enum ConflictResolution
{
    PreferExisting,     // Keep Security Posture Intelligence Navigator data, skip eMASS conflicts
    PreferImported,     // Overwrite with eMASS data
    CreateBoth,         // Import as new version, keep existing
    PromptUser          // Flag for manual resolution
}

public record EmassImportResult(
    int TotalRows,
    int Imported,
    int Skipped,
    int Conflicts,
    List<EmassImportConflict> ConflictDetails);
```

---

## Summary: Reference Data Files Plan

| File | Source | Entries | Update Frequency |
|---|---|---|---|
| `NIST_SP-800-53_rev5_catalog.json` | NIST OSCAL (exists today) | ~1,189 controls | On NIST release (~annual) |
| `cnssi-1253-overlays.json` | CNSSI 1253 + DISA Cloud SRG | ~450-500 overlay entries | On CNSSI revision (~2-5 years) |
| `sp800-60-information-types.json` | NIST SP 800-60 Vol 2 | ~180 information types | On NIST revision (rare) |
| `cci-nist-mapping.json` | DISA CCI List | ~7,575 CCI mappings | On DISA release (~quarterly) |
| `stig-controls.json` | DISA STIG Library (curated) | ~200→880 rules | On STIG release (~quarterly per benchmark) |
| `impact-levels.json` | DISA Cloud SRG (exists today) | 5 IL entries | On SRG revision (~annual) |
| `nist-800-53-baselines.json` | NIST SP 800-53B | 3 baseline lists | On SP 800-53B update (~annual) |

## Summary: New EF Core Entities

| Entity | FK Parent | Purpose |
|---|---|---|
| `SecurityCategorization` | `RegisteredSystem` | FIPS 199 C/I/A categorization |
| `InformationType` | `SecurityCategorization` | SP 800-60 information types with impact levels |
| *(Other entities from spec Phase 1-3 not covered in this research)* | | |

## Summary: New C# Reference Data Records

| Record | Source File | Purpose |
|---|---|---|
| `Cnssi1253OverlayData` | `cnssi-1253-overlays.json` | Root container for overlay data |
| `Cnssi1253ControlOverlay` | `cnssi-1253-overlays.json` | Per-control overlay entry |
| `CiaApplicability` | `cnssi-1253-overlays.json` | C/I/A dimension applicability |
| `ImpactLevelOverride` | `cnssi-1253-overlays.json` | IL-specific parameter/guidance overrides |
| `Sp80060InformationTypeData` | `sp800-60-information-types.json` | Root container for info type taxonomy |
| `Sp80060Category` | `sp800-60-information-types.json` | Category grouping |
| `Sp80060InformationType` | `sp800-60-information-types.json` | Individual information type with provisional impacts |
| `CciMappingData` | `cci-nist-mapping.json` | Root container for CCI mappings |
| `CciMapping` | `cci-nist-mapping.json` | Single CCI→NIST control mapping |
| `StigBenchmark` | `stig-controls.json` (expanded) | STIG benchmark container |
| `EmassControlExportRow` | (export generation) | eMASS control export row |
| `EmassPoamExportRow` | (export generation) | eMASS POA&M export row |
