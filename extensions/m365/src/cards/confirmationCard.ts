/**
 * Remediation Confirmation Adaptive Card Builder (FR-018e)
 *
 * Displays remediation script preview, affected resource info,
 * risk level, and Confirm/Cancel actions.
 */

import { buildAgentAttribution } from "./shared";

export interface ConfirmationData {
  findingId: string;
  scriptPreview?: string;
  resourceId?: string;
  resourceType?: string;
  riskLevel?: string;
  controlId?: string;
  severity?: string;
  pimRoleStatus?: string;
  agentUsed?: string;
  conversationId?: string;
}

export function buildConfirmationCard(data: ConfirmationData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Confirm Remediation",
      weight: "Bolder",
      size: "Large",
      color: "Warning",
    },
    {
      type: "TextBlock",
      text: "⚠️ Please review and confirm the following remediation action:",
      wrap: true,
    },
  ];

  const facts: Array<{ title: string; value: string }> = [];
  if (data.findingId) facts.push({ title: "Finding", value: data.findingId });
  if (data.controlId) facts.push({ title: "Control", value: data.controlId });
  if (data.severity) facts.push({ title: "Severity", value: data.severity });
  if (data.resourceId) facts.push({ title: "Resource", value: data.resourceId });
  if (data.resourceType) facts.push({ title: "Type", value: data.resourceType });
  if (data.riskLevel) facts.push({ title: "Risk", value: data.riskLevel });
  if (data.pimRoleStatus) facts.push({ title: "PIM Role", value: data.pimRoleStatus });

  if (facts.length > 0) {
    bodyItems.push({ type: "FactSet", facts });
  }

  if (data.scriptPreview) {
    bodyItems.push(
      { type: "TextBlock", text: "Remediation Script Preview", weight: "Bolder", spacing: "Medium" },
      {
        type: "TextBlock",
        text: data.scriptPreview,
        fontType: "Monospace",
        wrap: true,
        size: "Small",
      }
    );
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
        title: "Confirm Remediation",
        data: {
          action: "remediate",
          actionContext: { findingId: data.findingId, confirmed: "true" },
        },
      },
      {
        type: "Action.Submit",
        title: "Cancel",
        data: {},
      },
    ],
  };
}
