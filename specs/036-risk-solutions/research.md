# Research: Org-Wide Risk Solutions & Context-Aware Narrative Generation

**Date**: 2026-03-17 | **Feature**: 036-risk-solutions

## Research Task 1: Narrative Template Enrichment Strategy

**Question**: How should `NarrativeTemplateService.GenerateNarrative()` be modified to include linked component and boundary context without breaking existing callers?

### Decision: Extend BoundaryMappingContext, add ComponentContext

**Rationale**: The existing `BoundaryMappingContext` record is the data carrier for narrative generation. Rather than changing the `GenerateNarrative()` signature (which would break all callers), we extend the composite narrative path via `BoundaryMappingContext` with component context fields.

**Current signature**:
```csharp
public record BoundaryMappingContext(
    string CapabilityName,
    string Provider,
    string Description,
    string? BoundaryName);
```

**Proposed signature**:
```csharp
public record BoundaryMappingContext(
    string CapabilityName,
    string Provider,
    string Description,
    string? BoundaryName,
    IReadOnlyList<ComponentContext>? Components = null);

public record ComponentContext(
    string Name,
    string ComponentType,  // Person, Place, Thing
    string? Owner);
```

**Impact**:
- `GenerateNarrative()` stays unchanged (simple 5-param method for single mappings without components)
- `GenerateCompositeNarrative()` is enhanced to use the optional `Components` collection
- New overload `GenerateEnrichedNarrative()` accepts full context for single-mapping cases with component data
- All existing callers continue to work since `Components` defaults to null

**Alternatives Considered**:
- Breaking change to `GenerateNarrative()` signature → rejected, too many callers to update
- Separate method `GenerateEnrichedNarrative()` → accepted as approach for single-capability enriched narratives

---

## Research Task 2: SystemComponent Refactor to Org-Wide

**Question**: What is the safest way to make `SystemComponent.RegisteredSystemId` optional while maintaining backward compatibility and supporting the new `ComponentSystemAssignment` pattern?

### Decision: Make RegisteredSystemId nullable + add ComponentSystemAssignment join entity

**Rationale**: The current `SystemComponent` has `[Required] RegisteredSystemId`. Making it nullable allows components to exist without a direct system binding. The new `ComponentSystemAssignment` entity provides the many-to-many relationship between components and systems with boundary scoping.

**Migration approach**:
1. Add `ComponentSystemAssignment` table via `EnsureNewTablesAsync`
2. Add nullable column migration for `RegisteredSystemId` via `EnsureSchemaAdditionsAsync`
3. At startup: for each existing `SystemComponent` with a `RegisteredSystemId`, create a `ComponentSystemAssignment` record linking it to that system with its current boundary
4. null out `RegisteredSystemId` on the migrated components (they're now org-wide)

**Risk assessment**: Low risk — minimal production data exists in the components table.

**Alternatives Considered**:
- New entity `OrgComponent` separate from `SystemComponent` → rejected, too much duplication and two codepaths
- Keep `RegisteredSystemId` required, add `IsOrgWide` boolean → rejected, doesn't support true many-to-many

---

## Research Task 3: Cascade Narrative Regeneration via Component Changes

**Question**: When a component is renamed, how do we find all narratives that need regeneration through the component → capability → control mapping chain?

### Decision: Traverse ComponentCapabilityLink → CapabilityControlMapping → ControlImplementation

**Rationale**: The chain is:
1. `ComponentCapabilityLink` links a `SystemComponent` to a `SecurityCapability`
2. `CapabilityControlMapping` links that `SecurityCapability` to a `ControlId` (with optional system/boundary scope)
3. `ControlImplementation` is the narrative record for that `ControlId` + `SystemId`

**Query strategy**:
```
Component → ComponentCapabilityLink.SecurityCapabilityId
         → CapabilityControlMapping (WHERE SecurityCapabilityId = ...)
         → ControlImplementation (WHERE ControlId = mapping.ControlId AND RegisteredSystemId = ...)
```

**Per-system transactional approach**:
- Group affected `ControlImplementation` records by `RegisteredSystemId`
- For each system: begin transaction, regenerate all narratives, create NarrativeVersions, commit
- If any system fails: log error, continue to next system, report failed systems to user

**Alternatives Considered**:
- Full cross-system transaction → rejected per FR-015 (one system failure shouldn't block others)
- Background job queue → rejected, adds infrastructure complexity for a <10s operation

---

## Research Task 4: Impact Preview Implementation

**Question**: How should the impact preview work without actually modifying data?

### Decision: Dry-run count query with the same traversal logic

**Rationale**: The preview endpoint performs the same component → capability → control traversal but only counts affected records grouped by system. No writes, no transactions.

**Response shape**:
```json
{
  "totalNarratives": 243,
  "totalSystems": 3,
  "customSkipped": 12,
  "bySystem": [
    { "systemId": "...", "systemName": "Eagle Eye", "narrativeCount": 81, "customSkipped": 4 },
    { "systemId": "...", "systemName": "Falcon", "narrativeCount": 81, "customSkipped": 5 },
    { "systemId": "...", "systemName": "Hawk", "narrativeCount": 81, "customSkipped": 3 }
  ]
}
```

**Trigger points**: Called before saving capability edits (provider/description change) and before saving component edits (name/owner/boundary change).

**Alternatives Considered**:
- Client-side estimation based on cached mapping counts → rejected, could be stale and inaccurate
- Preview as part of the save response instead of before → rejected, user needs to see impact before committing

---

## Research Task 5: Org-Wide Component Library UI Pattern

**Question**: What UI pattern should the org-wide component library follow?

### Decision: Follow existing CapabilityLibrary page pattern

**Rationale**: The `CapabilityLibrary.tsx` page is already an org-wide library with:
- Search & filter bar (by category, status)
- Card/list view with expandable details
- Create/Edit/Delete modals
- Mapping panels for control associations

The new `ComponentLibrary.tsx` page should follow the same layout with:
- Search & filter by type (Person/Place/Thing), status (Active/Planned/Decommissioned)
- Component cards showing linked capabilities and system assignments
- Expandable panel showing which systems use this component and which boundary
- Create/Edit/Delete with impact preview for name changes
- "Assign to System" action with boundary selector

**Route**: `/components` — standalone top-level page alongside `/capabilities` and `/` (Portfolio)

**Alternatives Considered**:
- Tabbed view combining Capabilities and Components → rejected per clarification Q5, user wants them separate
- System-scoped only → rejected per clarification Q5, components are org-wide

---

## Research Task 6: AI-Assisted Narrative Generation Strategy

**Question**: How should `NarrativeTemplateService` integrate AI (via `IChatClient`) to produce richer, more natural SSP-appropriate narratives without losing deterministic fallback?

### Decision: Optional AI mode via constructor-injected IChatClient with deterministic fallback

**Rationale**: The existing project already has full Azure OpenAI infrastructure:
- `IChatClient` wired via DI in `BaseAgent` (supports both direct Azure OpenAI and Foundry)
- `AzureAiOptions` provides master switch (`Enabled`), endpoint, deployment, temperature
- `AiSuggested` flag on `ControlImplementation` already distinguishes AI-generated narratives
- All agents use prompt files externalized as `*.prompt.txt`

The NarrativeTemplateService should:
1. Accept `IChatClient?` and `AzureAiOptions?` via constructor (nullable — AI is optional)
2. When AI is enabled and configured: build a prompt containing capability metadata, component names/types, boundary context, control family guidance, and NIST control ID/title. Call `IChatClient.GetResponseAsync()` to generate a natural SSP-appropriate narrative.
3. When AI is disabled, unavailable, or call fails: fall back to existing deterministic template
4. Set `AiSuggested = true` on ControlImplementation when AI generates the narrative

**System prompt strategy**:
- Create `src/Ato.Copilot.Core/Prompts/NarrativeGeneration.prompt.txt`
- Prompt instructs the LLM to write a formal SSP control implementation narrative
- Include: capability name, provider, description, component names grouped by type, boundary name, control ID, control title, control family guidance
- Constrain: output must be a single paragraph, 100-300 words, formal government tone, no markdown

**Performance considerations**:
- AI calls add ~1-3 seconds per narrative
- For cascade operations (500 narratives), AI generation could take 500-1500 seconds — unacceptable
- **Decision**: Use AI for single narrative generation (on mapping creation) and deterministic templates for bulk cascade operations. Expose a "Regenerate with AI" action per-narrative for users who want AI quality.

**Alternatives Considered**:
- AI for all cascade operations (batched) → rejected, exceeds 10s performance constraint (SC-001) for bulk operations
- AI-only with no fallback → rejected, AI may be disabled in air-gapped environments
- AI as a separate service class → rejected, narrative generation is a single concern; AI is just an implementation detail within the same service
