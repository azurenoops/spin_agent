/**
 * Generic Response Adaptive Card Builder (FR-013, FR-023a)
 *
 * Fallback card for unclassified intent types — renders
 * the plain-text response from the MCP server with agent
 * attribution footer and suggestion buttons.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface GenericData {
  response: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildGenericCard(data: GenericData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: data.response,
      wrap: true,
    },
  ];

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const suggestionActions = buildSuggestionButtons(data.suggestions, data.conversationId);

  return {
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
    type: "AdaptiveCard",
    version: "1.5",
    body: bodyItems,
    ...(suggestionActions.length > 0 ? { actions: suggestionActions } : {}),
  };
}
