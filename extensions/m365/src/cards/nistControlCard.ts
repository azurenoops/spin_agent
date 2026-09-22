/**
 * NIST Control Adaptive Card Builder (FR-010a)
 *
 * Displays NIST 800-53 control details including statement,
 * implementation guidance, related STIGs, and FedRAMP baseline.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface NistControlData {
  controlId: string;
  title?: string;
  statement: string;
  implementationGuidance?: string;
  stigs?: string[];
  fedRampBaseline?: string;
  controlFamily?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildNistControlCard(data: NistControlData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — NIST 800-53 Control",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: `${data.controlId}${data.title ? ` — ${data.title}` : ""}`,
      weight: "Bolder",
      size: "Medium",
    },
    {
      type: "TextBlock",
      text: data.statement,
      wrap: true,
    },
  ];

  if (data.implementationGuidance) {
    bodyItems.push(
      { type: "TextBlock", text: "Implementation Guidance", weight: "Bolder", spacing: "Medium" },
      { type: "TextBlock", text: data.implementationGuidance, wrap: true }
    );
  }

  const facts: Array<{ title: string; value: string }> = [];
  if (data.controlFamily) facts.push({ title: "Family", value: data.controlFamily });
  if (data.fedRampBaseline) facts.push({ title: "FedRAMP", value: data.fedRampBaseline });

  if (facts.length > 0) {
    bodyItems.push({ type: "FactSet", facts, spacing: "Medium" } as Record<string, unknown>);
  }

  if (data.stigs && data.stigs.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Related STIGs",
      weight: "Bolder",
      spacing: "Medium",
    });
    for (const stig of data.stigs) {
      bodyItems.push({ type: "TextBlock", text: `• ${stig}`, size: "Small" });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [];

  if (data.controlFamily) {
    actions.push({
      type: "Action.Submit",
      title: "Show Related Controls",
      data: { action: "drillDown", actionContext: { controlFamily: data.controlFamily } },
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
