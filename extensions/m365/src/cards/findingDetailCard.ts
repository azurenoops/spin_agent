/**
 * Finding Detail Adaptive Card Builder (FR-010a)
 *
 * Displays individual compliance finding with 5-level severity badge,
 * control context, resource info, remediation guidance, and "Apply Fix" action.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface FindingDetailData {
  findingId?: string;
  severity: string;
  controlId?: string;
  controlFamily?: string;
  title: string;
  description?: string;
  resourceId?: string;
  resourceType?: string;
  remediationGuidance?: string;
  autoRemediable?: boolean;
  riskLevel?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const severityColors: Record<string, string> = {
  Critical: "Default",
  High: "Attention",
  Medium: "Warning",
  Low: "Good",
  Informational: "Accent",
};

const severityEmojis: Record<string, string> = {
  Critical: "🟣",
  High: "🔴",
  Medium: "🟠",
  Low: "🟡",
  Informational: "🔵",
};

export function buildFindingDetailCard(data: FindingDetailData): Record<string, unknown> {
  const emoji = severityEmojis[data.severity] ?? "⚪";
  const color = severityColors[data.severity] ?? "Default";

  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Finding Detail",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "ColumnSet",
      columns: [
        {
          type: "Column",
          width: "auto",
          items: [
            {
              type: "TextBlock",
              text: `${emoji} ${data.severity}`,
              weight: "Bolder",
              color,
            },
          ],
        },
        ...(data.autoRemediable
          ? [
              {
                type: "Column",
                width: "auto",
                items: [
                  {
                    type: "TextBlock",
                    text: "🔧 Auto-Remediable",
                    weight: "Bolder",
                    color: "Good" as const,
                  },
                ],
              },
            ]
          : []),
      ],
    },
    {
      type: "TextBlock",
      text: data.title,
      weight: "Bolder",
      size: "Medium",
      wrap: true,
    },
  ];

  if (data.description) {
    bodyItems.push({
      type: "TextBlock",
      text: data.description,
      wrap: true,
    });
  }

  const facts: Array<{ title: string; value: string }> = [];
  if (data.controlId) facts.push({ title: "Control", value: data.controlId });
  if (data.controlFamily) facts.push({ title: "Family", value: data.controlFamily });
  if (data.resourceId) facts.push({ title: "Resource", value: data.resourceId });
  if (data.resourceType) facts.push({ title: "Type", value: data.resourceType });
  if (data.riskLevel) facts.push({ title: "Risk", value: data.riskLevel });

  if (facts.length > 0) {
    bodyItems.push({ type: "FactSet", facts });
  }

  if (data.remediationGuidance) {
    bodyItems.push(
      { type: "TextBlock", text: "Remediation", weight: "Bolder", spacing: "Medium" },
      { type: "TextBlock", text: data.remediationGuidance, wrap: true }
    );
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [];

  if (data.autoRemediable && data.findingId) {
    actions.push({
      type: "Action.Submit",
      title: "Apply Fix",
      data: { action: "remediate", actionContext: { findingId: data.findingId } },
    });
  }

  if (data.resourceId) {
    actions.push({
      type: "Action.OpenUrl",
      title: "View in Azure Portal",
      url: `https://portal.azure.us/#resource${data.resourceId}`,
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
