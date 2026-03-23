# Research: Unified Security Capabilities Hub

**Feature**: 045-capabilities-hub | **Date**: 2026-03-22

## R1: Import Pipeline Transaction Pattern

**Decision**: Use implicit transactions via single `SaveChangesAsync` call (existing project pattern)

**Rationale**: All existing import operations (CRM import, CKL/SCAP import, Prisma/Nessus import) use the same pattern — accumulate all entity changes in the EF Core change tracker and persist with a single `SaveChangesAsync`. EF Core treats a single `SaveChangesAsync` as an implicit transaction. This is sufficient for our use case (creating ~200 records in one import) and consistent with the codebase.

**Alternatives Considered**:
- Explicit `BeginTransactionAsync` + `CommitAsync` — unnecessary overhead since all changes are within a single DbContext and single `SaveChangesAsync` already provides atomicity
- Unit of Work wrapper — over-engineering for this pattern

## R2: CSP Profile Schema Extension (services[] Format)

**Decision**: Extend `CspProfile` DTO with optional `Services` property; `CspProfileService` constructor tries `services[]` first, falls back to flat `controls[]`

**Rationale**: The current `CspProfile` class has a `Controls` property (`List<ProfileControlMapping>`). Adding a `Services` property (`List<CspProfileService>`) with each service containing its own `Controls` list. The `MatchProfile()` method flattens services into a unified control list internally, so downstream logic stays unchanged.

**Implementation**:
```csharp
// New DTO
public class CspService {
    public string Name { get; set; }         // e.g., "Microsoft Entra ID"
    public string Category { get; set; }     // NIST family, e.g., "Access Control"
    public string Description { get; set; }
    public List<ProfileControlMapping> Controls { get; set; } = new();
}

// Extended CspProfile
public class CspProfile {
    // ... existing fields ...
    public List<ProfileControlMapping> Controls { get; set; } = new();  // Legacy flat format
    public List<CspService> Services { get; set; } = new();             // New grouped format
}
```

**Backward Compatibility**: If `Services` is empty/null, use `Controls`. If `Services` is populated, ignore `Controls`. The `MatchProfile()` method derives a flat control list from `Services` internally.

## R3: Capability Deduplication Strategy

**Decision**: Deduplicate by Name + Provider (case-insensitive) for capabilities; by Name for components

**Rationale**: The existing `CapabilityService.CreateCapabilityAsync` only checks `Name == request.Name` for duplicates — no provider check. This is insufficient for our import pipeline where the same capability name might exist from different providers. We need name+provider dedup. For components, the `Name` field plus `ComponentType == "Thing"` is sufficient since CSP service names are unique.

**Implementation**: The new `CapabilityImportService` will query for existing capabilities matching `(Name, Provider)` case-insensitive before creating. Existing ones are reused; only new control mappings are added.

**Alternatives Considered**:
- Add dedup to `CapabilityService.CreateCapabilityAsync` itself — too invasive, changes behavior for manual creation path
- Use a separate upsert method — chosen approach: `FindOrCreateCapabilityAsync` in `CapabilityImportService`

## R4: Narrative Generation Integration

**Decision**: Call `GenerateEnrichedNarrative()` directly from `CapabilityImportService` after creating control mappings

**Rationale**: Narrative generation is not event-driven — it's called explicitly. The `NarrativeTemplateService.GenerateEnrichedNarrative()` method accepts: capability name, provider, description, control ID, control title, optional component contexts, optional boundary name. This is the deterministic path (no AI needed). AI narrative generation (`GenerateNarrativeWithAiAsync`) falls back to this method when AI is disabled.

**Parameters Available During Import**:
- Capability metadata: ✅ (name, provider, description from CSP profile service definition)
- Control metadata: ✅ (controlId from mapping, controlTitle from NistControl lookup)
- Component contexts: ✅ (component Name, ComponentType, Owner from just-created components)
- Boundary name: ❌ (org-level import doesn't target a specific boundary — use null)

**Note**: For org-wide mappings (no system scope), narratives are generated with component context but without boundary context. When org inheritance cascades to individual systems, system-level narrative enrichment can add boundary context.

## R5: Coverage API Architecture

**Decision**: New `GET /capabilities/coverage` endpoint computes coverage server-side; separate from portfolio endpoint

**Rationale**: The Portfolio Risk Profile page calls `GET /portfolio` which returns per-system summaries. Adding coverage here would require cross-cutting the portfolio query with capability mapping data — messy. A dedicated `GET /capabilities/coverage` endpoint is cleaner and can be called from both the Capabilities page (full breakdown) and the Portfolio Risk Profile page (single % number).

**Response Shape**:
```json
{
  "orgWide": {
    "totalCapabilities": 35,
    "mappedControls": 289,
    "unmappedControls": 36,
    "coveragePercent": 88.9,
    "baselineLevel": "High",
    "baselineControlCount": 325,
    "perFamily": [
      { "family": "AC", "mapped": 22, "total": 25, "percent": 88.0 },
      ...
    ]
  },
  "perSystem": [
    {
      "systemId": "...",
      "systemName": "...",
      "baselineLevel": "High",
      "coveragePercent": 88.9,
      "mappedControls": 289,
      "totalControls": 325
    }
  ]
}
```

**Zero-Systems Fallback**: When no systems are registered, the denominator falls back to the imported CSP profile's declared baseline level (e.g., High = 325). When neither systems nor CSP profiles exist, `coveragePercent` is null and `baselineLevel` is null — the UI renders "N/A — import a CSP profile to establish a baseline."

**Portfolio KPI Card**: `PortfolioRiskProfile.tsx` calls `GET /capabilities/coverage` separately (not via the portfolio endpoint) and renders only `orgWide.coveragePercent` as a KPI card (or "N/A" when null).

## R6: ComponentCapabilityLink Bulk Creation

**Decision**: Create links in-line during import pipeline within the same `SaveChangesAsync` batch

**Rationale**: Existing `ComponentService` creates links one-by-one with validation (`AnyAsync` per capability ID). During bulk import, we already know the capability IDs because we just created them. Skip the per-item validation and batch-add links directly to `_db.ComponentCapabilityLinks`, then persist with the single `SaveChangesAsync` at the end of the pipeline.

**Alternatives Considered**:
- Call `ComponentService.UpdateComponentAsync` per component — too many round-trips and unnecessary reconcile (remove+re-add) logic
- Create a bulk link endpoint — unnecessary; links are a side-effect of import, not a user action during import

## R7: Control Inheritance Page Simplification

**Decision**: Remove CSP Profile button (~line 425) and CRM Import button (~line 442) from `ControlInheritance.tsx`; add cross-link banner at top of page

**Rationale**: Per clarification Q5, the old import buttons are removed entirely (not relocated to a dropdown). The `CspProfileDialog` and `CrmImportDialog` components referenced in `ControlInheritance.tsx` will be relocated to `CapabilityLibrary.tsx`. The component context tooltips on org default indicators will query the capability's linked components via an expanded response from the list endpoint.

## R8: CSP Profile Service Groupings for Azure Government FedRAMP High

**Decision**: Group the ~160 controls in the existing profile into ~10 Azure Government services

**Rationale**: Based on analysis of the current `azure-gov-fedramp-high.json` (165 lines, flat controls array), controls map to the following Azure Government services:

| Service | NIST Families | Approx Controls |
|---------|---------------|-----------------|
| Microsoft Entra ID | AC, IA | ~30 |
| Azure Monitor / Log Analytics | AU, SI | ~25 |
| Azure Key Vault | SC (crypto) | ~10 |
| Azure Policy / Compliance | CA, CM, SA | ~20 |
| Microsoft Defender for Cloud | RA, SI | ~15 |
| Azure Networking (NSG/Firewall) | AC, SC (network) | ~15 |
| Azure Storage | MP, SC (data) | ~10 |
| Azure Backup / Site Recovery | CP | ~10 |
| Azure DevOps / Pipelines | SA, CM | ~10 |
| Organizational Policies (Customer) | PL, PS, AT, PE | ~20 |

This grouping creates ~10 components and ~20 capabilities (each service × relevant NIST families).
