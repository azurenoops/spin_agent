/**
 * Roadmap Summary Adaptive Card Builder (Feature 031)
 *
 * Displays a roadmap summary with phase rows, effort totals,
 * risk projections, and action buttons for Kanban/PDF/Details.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface RoadmapPhaseRow {
  name: string;
  timeline: string;
  controlCount: number;
  effortDays: number;
  riskReductionPercent: number;
}

export interface RoadmapCardData {
  roadmapId?: string;
  systemId?: string;
  systemName?: string;
  totalGaps?: number;
  phaseCount?: number;
  totalEffortDays?: number;
  riskReduction?: number;
  phases?: RoadmapPhaseRow[];
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildRoadmapCard(data: RoadmapCardData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: `ATO Copilot — Implementation Roadmap`,
      weight: "Bolder",
      size: "Large",
    },
  ];

  if (data.systemName) {
    bodyItems.push({
      type: "TextBlock",
      text: data.systemName,
      size: "Medium",
      color: "Accent",
    });
  }

  // Summary metrics
  const facts: { title: string; value: string }[] = [];
  if (data.totalGaps != null) facts.push({ title: "Total Gaps", value: `${data.totalGaps}` });
  if (data.phaseCount != null) facts.push({ title: "Phases", value: `${data.phaseCount}` });
  if (data.totalEffortDays != null) facts.push({ title: "Total Effort", value: `${data.totalEffortDays} days` });
  if (data.riskReduction != null) facts.push({ title: "Risk Reduction", value: `${data.riskReduction}%` });

  if (facts.length > 0) {
    bodyItems.push({ type: "FactSet", facts });
  }

  // Phase rows
  if (data.phases && data.phases.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Phases",
      weight: "Bolder",
      spacing: "Medium",
    });

    for (const phase of data.phases) {
      bodyItems.push({
        type: "ColumnSet",
        columns: [
          {
            type: "Column",
            width: "stretch",
            items: [
              { type: "TextBlock", text: phase.name, weight: "Bolder", size: "Small" },
              { type: "TextBlock", text: `${phase.timeline} · ${phase.controlCount} controls · ${phase.effortDays}d · ${phase.riskReductionPercent.toFixed(1)}% risk reduction`, size: "Small", isSubtle: true, wrap: true },
            ],
          },
        ],
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [
    {
      type: "Action.Submit",
      title: "Create Kanban Board",
      data: { action: "createBoardFromRoadmap", actionContext: { systemId: data.systemId ?? "" } },
    },
    {
      type: "Action.Submit",
      title: "Export PDF",
      data: { action: "exportRoadmapPdf", actionContext: { systemId: data.systemId ?? "" } },
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
