/**
 * Clarification Adaptive Card Builder (FR-010a)
 *
 * Displays missing fields with optional input dropdowns and
 * a submit action for multi-turn clarification.
 */

import { buildAgentAttribution } from "./shared";

export interface ClarificationData {
  followUpPrompt: string;
  missingFields: string[];
  agentUsed?: string;
  conversationId?: string;
}

export function buildClarificationCard(data: ClarificationData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Additional Information Needed",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: data.followUpPrompt,
      wrap: true,
    },
  ];

  // Render each missing field as an Input.Text
  for (const field of data.missingFields) {
    bodyItems.push({
      type: "Input.Text",
      id: field,
      label: field,
      placeholder: `Enter ${field}`,
    });
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  return {
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
    type: "AdaptiveCard",
    version: "1.5",
    body: bodyItems,
    actions: [
      {
        type: "Action.Submit",
        title: "Submit",
        data: { action: "drillDown", conversationId: data.conversationId ?? "" },
      },
    ],
  };
}
