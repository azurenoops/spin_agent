/**
 * Evidence Collection Adaptive Card Builder (FR-010a)
 *
 * Displays evidence collection status with completeness meter,
 * evidence items with hashes, and "Collect More" action.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface EvidenceItem {
  name: string;
  hash?: string;
  status?: string;
}

export interface EvidenceCollectionData {
  completeness: number;
  items: EvidenceItem[];
  framework?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

function buildProgressBar(percentage: number): string {
  const filled = Math.round(percentage / 10);
  const empty = 10 - filled;
  return "█".repeat(filled) + "░".repeat(empty) + ` ${percentage}%`;
}

export function buildEvidenceCollectionCard(data: EvidenceCollectionData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Evidence Collection",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: buildProgressBar(data.completeness),
      fontType: "Monospace",
      size: "Medium",
      color: data.completeness >= 80 ? "Good" : data.completeness >= 50 ? "Warning" : "Attention",
    },
  ];

  if (data.framework) {
    bodyItems.push({
      type: "TextBlock",
      text: `Framework: ${data.framework}`,
      isSubtle: true,
      size: "Small",
    });
  }

  if (data.items.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: `Evidence Items (${data.items.length})`,
      weight: "Bolder",
      spacing: "Medium",
    });
    for (const item of data.items) {
      const hashInfo = item.hash ? ` — ${item.hash.substring(0, 8)}...` : "";
      const statusEmoji = item.status === "verified" ? "✅" : "📄";
      bodyItems.push({
        type: "TextBlock",
        text: `${statusEmoji} ${item.name}${hashInfo}`,
        size: "Small",
        wrap: true,
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [
    {
      type: "Action.Submit",
      title: "Collect More",
      data: { action: "collectEvidence", actionContext: { framework: data.framework ?? "" } },
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
