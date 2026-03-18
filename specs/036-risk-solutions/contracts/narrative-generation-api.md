# API Contract: Narrative Generation & Cascade Updates

**Feature**: 036-risk-solutions | **Base Path**: `/api/dashboard`

## Modified Endpoints

### 1. Capability Impact Preview (NEW)

```
GET /api/dashboard/capabilities/{capabilityId}/impact-preview
```

**Response** `200 OK`:
```json
{
  "totalNarratives": 243,
  "totalSystems": 3,
  "customSkipped": 12,
  "bySystem": [
    {
      "systemId": "guid",
      "systemName": "Eagle Eye",
      "narrativeCount": 81,
      "customSkipped": 4
    }
  ]
}
```

**Purpose**: Shows how many narratives would be regenerated if this capability's provider, name, or description is changed. Called by the UI before saving edits.

---

### 2. Update Capability (MODIFIED)

```
PUT /api/dashboard/capabilities/{capabilityId}
```

Existing endpoint. **Changed behavior**: When provider, name, or description changes, the cascade regeneration now includes component and boundary context in the generated narratives (previously only capability metadata was used).

**Cascade logic**:
1. Query `CapabilityControlMapping` for all control mappings of this capability
2. Group by `RegisteredSystemId` (null = org-wide → apply to all systems)
3. For each system:
   - Begin transaction
   - For each mapped control:
     - Query `ComponentCapabilityLink` → get linked components
     - Query `ComponentSystemAssignment` → get component boundary assignments for this system
     - Build `BoundaryMappingContext` with `ComponentContext` list
     - Call `GenerateNarrative()` or `GenerateCompositeNarrative()`
     - Skip if `IsManuallyCustomized == true`
     - Create `NarrativeVersion` with change reason
     - Update `ControlImplementation` narrative + increment version
   - Commit transaction
4. Return summary

**Response** `200 OK`:
```json
{
  "capability": { /* updated capability object */ },
  "cascadeResult": {
    "totalUpdated": 231,
    "totalSkipped": 12,
    "totalFailed": 0,
    "bySystem": [
      {
        "systemId": "guid",
        "systemName": "Eagle Eye",
        "updated": 77,
        "skipped": 4,
        "failed": 0
      }
    ],
    "failedSystems": []
  }
}
```

---

### 3. Create Capability Control Mapping (MODIFIED)

```
POST /api/dashboard/capabilities/{capabilityId}/mappings
```

Existing endpoint. **Changed behavior**: When creating a mapping, the auto-generated narrative now includes component and boundary context.

**Enriched query chain**:
```
Capability
  → ComponentCapabilityLink → SystemComponent (linked components)
  → ComponentSystemAssignment (per-system boundary context)
  → AuthorizationBoundaryDefinition (boundary name)
```

**Generated narrative template** (enriched):
```
{ControlFamily} is implemented by {CapabilityName} using {Provider}.
{Description}
Components: {ThingComponents joined by ", "}.
Responsible personnel: {PersonComponents joined by ", "}.
Operating within the {BoundaryName} boundary.
```

Falls back to current template when no components are linked.

---

## Internal Service Contracts

### NarrativeTemplateService

#### GenerateNarrative (UNCHANGED — deterministic fallback)

```csharp
string GenerateNarrative(
    string capabilityName,
    string provider,
    string description,
    string controlId,
    string controlTitle);
```

Deterministic template — used as fallback when AI is disabled or for bulk cascade operations.

#### GenerateNarrativeWithAiAsync (NEW — AI-assisted)

```csharp
Task<string?> GenerateNarrativeWithAiAsync(
    string capabilityName,
    string provider,
    string description,
    string controlId,
    string controlTitle,
    IReadOnlyList<ComponentContext>? components,
    string? boundaryName,
    CancellationToken cancellationToken = default);
```

**Behavior**: Uses `IChatClient` to generate a contextually rich, SSP-appropriate narrative. Sends a system prompt from `NarrativeGeneration.prompt.txt` with all available context (capability, components, boundary, control family). Returns `null` if AI is disabled or the call fails (caller falls back to deterministic template).

**When used**:
- Single narrative generation on mapping creation (FR-018)
- User-triggered "Regenerate with AI" action per narrative

**Not used for**:
- Bulk cascade operations (performance constraint — SC-001 requires 500 narratives in 10s)

**AI output constraints**:
- Single paragraph, 100-300 words
- Formal government SSP tone
- Must reference specific component names, boundary, and responsible personnel
- No markdown formatting in output

#### GenerateCompositeNarrative (MODIFIED)

```csharp
string GenerateCompositeNarrative(
    string controlId,
    string controlTitle,
    IReadOnlyList<BoundaryMappingContext> contexts);
```

**Changed behavior**: When `BoundaryMappingContext.Components` is non-null and non-empty, the generated narrative includes:
- Component names grouped by type (Things, Persons, Places)
- Boundary context for each section
- Responsible personnel from Person-type components

**Template structure** (enriched):
```
{ControlTitle} ({ControlId}):

{CapabilityName} provides this control using {Provider}. {Description}

Implementation Details:
- Technology: {ThingComponents}
- Personnel: {PersonComponents}
- Infrastructure: {PlaceComponents}

{If boundary-scoped}: This implementation operates within the {BoundaryName} authorization boundary.
```

#### BoundaryMappingContext (MODIFIED)

```csharp
public record BoundaryMappingContext(
    string CapabilityName,
    string Provider,
    string Description,
    string? BoundaryName,
    IReadOnlyList<ComponentContext>? Components = null);

public record ComponentContext(
    string Name,
    string ComponentType,  // "Person", "Place", "Thing"
    string? Owner);
```

### Cascade Trigger Points

| Trigger | Source Entity | Service Method | Scope |
|---------|-------------|---------------|-------|
| Provider/Name/Description change | SecurityCapability | CapabilityService.UpdateCapabilityAsync | All systems with mappings |
| Name/Description/Owner change | SystemComponent | ComponentService.UpdateComponentAsync | All systems with assignments |
| Boundary reassignment | ComponentSystemAssignment | ComponentService.UpdateAssignmentAsync | Affected system only |
| Component deleted | SystemComponent | ComponentService.DeleteComponentAsync | All systems with assignments |
| Capability link added/removed | ComponentCapabilityLink | ComponentService.LinkCapabilityAsync | All systems with mappings for that capability |

### NarrativeVersion Creation

Every cascade-triggered narrative update creates a `NarrativeVersion`:

```json
{
  "controlImplementationId": "guid",
  "versionNumber": "prev + 1",
  "content": "new narrative text",
  "status": "Draft",
  "authoredBy": "system",
  "changeReason": "Auto-regenerated: {trigger description}"
}
```

Change reason examples:
- `"Auto-regenerated: capability provider changed from 'Duo' to 'Okta'"`
- `"Auto-regenerated: component 'ISSO — John Smith' renamed to 'ISSO — Jane Doe'"`
- `"Auto-regenerated: component 'Azure Firewall' reassigned to boundary 'Dev/Test'"`
- `"AI-generated: user-triggered regeneration with AI"`

### Regenerate Single Narrative with AI (NEW)

```
POST /api/dashboard/systems/{systemId}/controls/{controlId}/regenerate-ai
```

**Purpose**: User-triggered action to regenerate a single narrative using AI instead of the deterministic template. Only available when `AzureAiOptions.Enabled == true`.

**Response** `200 OK`:
```json
{
  "controlId": "ac-2",
  "narrative": "The organization implements...",
  "aiSuggested": true,
  "versionNumber": 3,
  "changeReason": "AI-generated: user-triggered regeneration with AI"
}
```

**Response** `503 Service Unavailable`: AI is not enabled or configured.

**Side Effects**: Creates a NarrativeVersion, sets `AiSuggested = true` on ControlImplementation.
