/**
 * Knowledge Base Adaptive Card Builder (FR-009)
 *
 * Displays knowledge base answers with source references,
 * "Learn More" action, and agent attribution footer.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface KnowledgeBaseData {
  answer: string;
  sources?: Array<{ title: string; url: string }>;
  controlId?: string;
  controlFamily?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildKnowledgeBaseCard(data: KnowledgeBaseData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Knowledge Base",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: data.answer,
      wrap: true,
    },
  ];

  if (data.controlId) {
    bodyItems.push({
      type: "FactSet",
      facts: [
        ...(data.controlId ? [{ title: "Control", value: data.controlId }] : []),
        ...(data.controlFamily ? [{ title: "Family", value: data.controlFamily }] : []),
      ],
    });
  }

  if (data.sources && data.sources.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Sources",
      weight: "Bolder",
      spacing: "Medium",
    });
    for (const source of data.sources) {
      bodyItems.push({
        type: "TextBlock",
        text: `[${source.title}](${source.url})`,
        wrap: true,
        size: "Small",
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [];

  if (data.sources && data.sources.length > 0) {
    actions.push({
      type: "Action.OpenUrl",
      title: "Learn More",
      url: data.sources[0].url,
    });
  }

  actions.push(...buildSuggestionButtons(data.suggestions, data.conversationId));

  return {
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
    type: "AdaptiveCard",
    version: "1.5",
    body: bodyItems,
    actions,
  };
}
