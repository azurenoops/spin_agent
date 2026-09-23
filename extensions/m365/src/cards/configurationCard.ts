/**
 * Configuration Adaptive Card Builder (FR-010)
 *
 * Displays current Security Posture Intelligence Navigator configuration settings as a table
 * with "Update" action buttons for each setting.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface ConfigurationData {
  framework?: string;
  baseline?: string;
  subscriptionId?: string;
  cloudEnvironment?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildConfigurationCard(data: ConfigurationData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Configuration",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "FactSet",
      facts: [
        { title: "Framework", value: data.framework ?? "Not configured" },
        { title: "Baseline", value: data.baseline ?? "Not configured" },
        { title: "Subscription", value: data.subscriptionId ?? "Not configured" },
        { title: "Environment", value: data.cloudEnvironment ?? "Not configured" },
      ],
    },
  ];

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [
    {
      type: "Action.Submit",
      title: "Update Framework",
      data: { action: "drillDown", actionContext: { setting: "framework" } },
    },
    {
      type: "Action.Submit",
      title: "Update Baseline",
      data: { action: "drillDown", actionContext: { setting: "baseline" } },
    },
    {
      type: "Action.Submit",
      title: "Update Subscription",
      data: { action: "drillDown", actionContext: { setting: "subscription" } },
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
