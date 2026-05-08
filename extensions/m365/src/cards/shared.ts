/**
 * Shared Adaptive Card Utilities (FR-013, FR-023a)
 *
 * Common helpers for agent attribution footer and suggestion buttons
 * used across all card builders.
 */

export { buildTenantImpersonationBadge } from "./tenantBadge";

/**
 * Builds an agent attribution footer element.
 * Shows "Processed by: {agentUsed}" in accent color, right-aligned.
 */
export function buildAgentAttribution(agentUsed?: string): Record<string, unknown> | null {
  if (!agentUsed) return null;
  return {
    type: "TextBlock",
    text: `Processed by: ${agentUsed}`,
    size: "Small",
    color: "Accent",
    horizontalAlignment: "Right",
    spacing: "Medium",
  };
}

/**
 * Builds Action.Submit buttons from the suggestions array.
 * Each suggestion becomes a clickable button that re-submits as a new message.
 */
export function buildSuggestionButtons(
  suggestions?: string[],
  conversationId?: string
): Array<Record<string, unknown>> {
  if (!suggestions || suggestions.length === 0) return [];
  return suggestions.map((suggestion) => ({
    type: "Action.Submit",
    title: suggestion,
    data: { message: suggestion, conversationId: conversationId ?? "" },
  }));
}
