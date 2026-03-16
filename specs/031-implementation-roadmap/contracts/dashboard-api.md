# Dashboard API Contracts: Implementation Roadmap (031)

All endpoints follow the existing minimal API pattern in `DashboardEndpoints.cs`.

---

## GET /api/dashboard/systems/{systemId}/roadmap

Get the active roadmap for a system's dashboard view.

**Query Parameters**:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| includeItems | bool | No | true | Include per-phase item details |

**200 OK**:

```json
{
  "roadmapId": "guid",
  "systemId": "guid",
  "systemName": "Eagle Eye",
  "status": "Active",
  "baselineLevel": "Moderate",
  "totalGaps": 47,
  "totalEstimatedEffortDays": 120,
  "totalRiskPoints": 295,
  "overallCompletionPercent": 35.5,
  "phases": [
    {
      "phaseId": "guid",
      "name": "Critical Controls",
      "displayOrder": 1,
      "estimatedEffortDays": 24,
      "riskPoints": 80,
      "riskReductionPercent": 27.1,
      "targetStartWeek": 1,
      "targetEndWeek": 2,
      "status": "Complete",
      "completedItemCount": 8,
      "totalItemCount": 8,
      "items": [
        {
          "itemId": "guid",
          "controlId": "AC-2",
          "controlFamily": "Access Control",
          "gapType": "Unmapped",
          "severity": "Critical",
          "riskPoints": 10,
          "estimatedEffortDays": 4,
          "assignedRole": "Engineer",
          "dependsOn": ["IA-2"],
          "status": "Completed",
          "linkedTaskId": "guid-or-null"
        }
      ]
    }
  ],
  "createdAt": "2026-03-01T10:00:00Z",
  "updatedAt": "2026-03-15T14:30:00Z"
}
```

**404 Not Found** (no active roadmap):

```json
{
  "error": "No active roadmap found for system d258daad-b093-4357-a804-6501b7465b9c"
}
```

---

## GET /api/dashboard/systems/{systemId}/roadmap/progress

Get progress metrics and risk reduction curve data.

**200 OK**:

```json
{
  "roadmapId": "guid",
  "systemName": "Eagle Eye",
  "overallCompletionPercent": 35.5,
  "itemsCompleted": 17,
  "itemsTotal": 47,
  "riskCurve": [
    { "week": 0, "riskPoints": 295, "riskReductionPercent": 0 },
    { "week": 2, "riskPoints": 215, "riskReductionPercent": 27.1 },
    { "week": 5, "riskPoints": 130, "riskReductionPercent": 55.9 },
    { "week": 8, "riskPoints": 60, "riskReductionPercent": 79.7 },
    { "week": 12, "riskPoints": 0, "riskReductionPercent": 100 }
  ],
  "phaseProgress": [
    {
      "name": "Critical Controls",
      "displayOrder": 1,
      "completionPercent": 100,
      "status": "Complete",
      "actualRiskReductionPercent": 28.5
    },
    {
      "name": "Infrastructure Controls",
      "displayOrder": 2,
      "completionPercent": 64.3,
      "status": "InProgress",
      "isOverdue": true,
      "daysOverdue": 3,
      "actualRiskReductionPercent": 13.8
    }
  ]
}
```

---

## GET /api/dashboard/systems/{systemId}/roadmap/export

Export roadmap as a PDF document.

**Produces**: `application/pdf`

**200 OK**: Binary PDF file with `Content-Disposition: attachment; filename="Eagle_Eye_Implementation_Roadmap_2026-03-15.pdf"`

**404 Not Found**: ErrorResponse (no active roadmap)

---

## DTO Summary

| DTO | File | Purpose |
|-----|------|---------|
| `RoadmapDto` | RoadmapDto.cs | Full roadmap with phases and items |
| `RoadmapPhaseDto` | RoadmapDto.cs | Phase summary with items array |
| `RoadmapItemDto` | RoadmapDto.cs | Individual gap item within a phase |
| `RoadmapProgressDto` | RoadmapDto.cs | Progress metrics with risk curve |
| `RiskCurvePointDto` | RoadmapDto.cs | Single point on the risk reduction curve |
| `PhaseProgressDto` | RoadmapDto.cs | Per-phase progress metrics |
