/**
 * Remediation Plan Adaptive Card Builder (FR-010a)
 *
 * Displays a phased remediation plan with risk reduction projection,
 * prioritized finding list, and "Start Remediation" action.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface RemediationPhase {
  name: string;
  duration?: string;
  findings?: number;
}

export interface RemediationPlanData {
  planId?: string;
  riskReduction?: number;
  findings?: Array<{ title: string; severity: string; controlId?: string }>;
  phases?: RemediationPhase[];
  steps?: string[];
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildRemediationPlanCard(data: RemediationPlanData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Remediation Plan",
      weight: "Bolder",
      size: "Large",
    },
  ];

  if (data.riskReduction != null) {
    bodyItems.push({
      type: "TextBlock",
      text: `Risk Reduction: ${data.riskReduction}%`,
      size: "ExtraLarge",
      color: "Good",
      weight: "Bolder",
    });
  }

  if (data.findings && data.findings.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Prioritized Findings",
      weight: "Bolder",
      spacing: "Medium",
    });
    for (const finding of data.findings) {
      bodyItems.push({
        type: "TextBlock",
        text: `• [${finding.severity}] ${finding.title}${finding.controlId ? ` (${finding.controlId})` : ""}`,
        wrap: true,
        size: "Small",
      });
    }
  }

  if (data.phases && data.phases.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Timeline",
      weight: "Bolder",
      spacing: "Medium",
    });
    bodyItems.push({
      type: "FactSet",
      facts: data.phases.map((phase) => ({
        title: phase.name,
        value: phase.duration ?? "TBD",
      })),
    });
  }

  if (data.steps && data.steps.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Steps",
      weight: "Bolder",
      spacing: "Medium",
    });
    for (let i = 0; i < data.steps.length; i++) {
      bodyItems.push({
        type: "TextBlock",
        text: `${i + 1}. ${data.steps[i]}`,
        wrap: true,
        size: "Small",
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [
    {
      type: "Action.Submit",
      title: "Start Remediation",
      data: { action: "remediate", actionContext: { planId: data.planId ?? "" } },
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
