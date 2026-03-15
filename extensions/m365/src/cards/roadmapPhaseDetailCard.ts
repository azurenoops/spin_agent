/**
 * Roadmap Phase Detail Adaptive Card Builder (Feature 031)
 *
 * Displays a detail view of items within a roadmap phase,
 * with a table of controls and a "Back to Roadmap" button.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface PhaseDetailItem {
  controlId: string;
  effortDays: number;
  assignedRole: string;
  gapType: string;
  dependsOn: string[];
  status: string;
}

export interface RoadmapPhaseDetailData {
  phaseName?: string;
  systemId?: string;
  phaseOrder?: number;
  items?: PhaseDetailItem[];
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildRoadmapPhaseDetailCard(data: RoadmapPhaseDetailData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: `ATO Copilot — ${data.phaseName ?? "Phase Detail"}`,
      weight: "Bolder",
      size: "Large",
    },
  ];

  if (data.items && data.items.length > 0) {
    // Header row
    bodyItems.push({
      type: "ColumnSet",
      columns: [
        { type: "Column", width: 1, items: [{ type: "TextBlock", text: "Control", weight: "Bolder", size: "Small" }] },
        { type: "Column", width: 1, items: [{ type: "TextBlock", text: "Effort", weight: "Bolder", size: "Small" }] },
        { type: "Column", width: 1, items: [{ type: "TextBlock", text: "Role", weight: "Bolder", size: "Small" }] },
        { type: "Column", width: 1, items: [{ type: "TextBlock", text: "Gap Type", weight: "Bolder", size: "Small" }] },
        { type: "Column", width: 1, items: [{ type: "TextBlock", text: "Status", weight: "Bolder", size: "Small" }] },
      ],
    });

    for (const item of data.items) {
      bodyItems.push({
        type: "ColumnSet",
        separator: true,
        columns: [
          { type: "Column", width: 1, items: [{ type: "TextBlock", text: item.controlId, size: "Small" }] },
          { type: "Column", width: 1, items: [{ type: "TextBlock", text: `${item.effortDays}d`, size: "Small" }] },
          { type: "Column", width: 1, items: [{ type: "TextBlock", text: item.assignedRole, size: "Small" }] },
          { type: "Column", width: 1, items: [{ type: "TextBlock", text: item.gapType, size: "Small" }] },
          { type: "Column", width: 1, items: [{ type: "TextBlock", text: item.status, size: "Small" }] },
        ],
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [
    {
      type: "Action.Submit",
      title: "Back to Roadmap",
      data: { action: "getRoadmap", actionContext: { systemId: data.systemId ?? "" } },
    },
    ...buildSuggestionButtons(data.suggestions, data.conversationId),
  ];

  return {
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
    type: "AdaptiveCard",
    version: "1.5",
    body: bodyItems,
    actions,
  };
}
