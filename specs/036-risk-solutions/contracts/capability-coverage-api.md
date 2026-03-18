# API Contract: Capability Coverage View

**Feature**: 036-risk-solutions | **Base Path**: `/api/dashboard`

## New Endpoint

### Capability Coverage Per System

```
GET /api/dashboard/systems/{systemId}/capability-coverage
```

**Purpose**: Returns all capabilities assigned to a system via `CapabilityControlMapping`, with their linked components, mapped control counts, and narrative generation status.

**Response** `200 OK`:
```json
{
  "systemId": "guid",
  "systemName": "Eagle Eye",
  "capabilities": [
    {
      "capabilityId": "guid",
      "capabilityName": "Multi-Factor Authentication",
      "provider": "Microsoft Entra ID",
      "category": "IA",
      "implementationStatus": "Implemented",
      "owner": "ISSO — John Smith",
      "role": "Primary",
      "mappedControlCount": 81,
      "narrativeStatus": {
        "populated": 78,
        "custom": 2,
        "empty": 1,
        "aiGenerated": 15
      },
      "components": [
        {
          "componentId": "guid",
          "name": "Azure Conditional Access",
          "componentType": "Thing",
          "owner": "Cloud Team",
          "status": "Active",
          "boundaryName": "Production",
          "boundaryDefinitionId": "guid"
        },
        {
          "componentId": "guid",
          "name": "ISSO — John Smith",
          "componentType": "Person",
          "owner": null,
          "status": "Active",
          "boundaryName": "Production",
          "boundaryDefinitionId": "guid"
        }
      ]
    }
  ],
  "summary": {
    "totalCapabilities": 5,
    "totalMappedControls": 120,
    "totalNarrativesPopulated": 118,
    "totalNarrativesCustom": 4,
    "totalNarrativesEmpty": 2,
    "coveragePercent": 98.3
  }
}
```

**Response** `404 Not Found`: System does not exist.

**Query behavior**:
- Capabilities are queried via `CapabilityControlMapping WHERE RegisteredSystemId = {systemId} OR RegisteredSystemId IS NULL`
- Components are resolved via `ComponentCapabilityLink` → `ComponentSystemAssignment WHERE RegisteredSystemId = {systemId}`
- Narrative status is counted from `ControlImplementation WHERE RegisteredSystemId = {systemId}`
- Capabilities with role `Primary` are listed first, then `Supporting`, then `Shared`
- `aiGenerated` count uses `AiSuggested == true` on `ControlImplementation`
