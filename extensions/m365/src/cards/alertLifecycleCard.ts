/**
 * Alert Lifecycle Adaptive Card Builder (FR-010a)
 *
 * Displays security alerts with severity, affected resources,
 * SLA countdown, and lifecycle action buttons (Acknowledge/Dismiss/Escalate).
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface AlertLifecycleData {
  alertId: string;
  severity: string;
  title?: string;
  description?: string;
  affectedResources?: string[];
  slaDeadline?: string;
  status?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const alertSeverityColors: Record<string, string> = {
  Critical: "Attention",
  High: "Attention",
  Medium: "Warning",
  Low: "Good",
  Informational: "Accent",
};

export function buildAlertLifecycleCard(data: AlertLifecycleData): Record<string, unknown> {
  const color = alertSeverityColors[data.severity] ?? "Default";

  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Security Alert",
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
            { type: "TextBlock", text: `🚨 ${data.severity}`, weight: "Bolder", color },
          ],
        },
        ...(data.status
          ? [
              {
                type: "Column",
                width: "auto",
                items: [
                  { type: "TextBlock", text: data.status, isSubtle: true },
                ],
              },
            ]
          : []),
      ],
    },
  ];

  if (data.title) {
    bodyItems.push({
      type: "TextBlock",
      text: data.title,
      weight: "Bolder",
      size: "Medium",
      wrap: true,
    });
  }

  if (data.description) {
    bodyItems.push({ type: "TextBlock", text: data.description, wrap: true });
  }

  if (data.affectedResources && data.affectedResources.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Affected Resources",
      weight: "Bolder",
      spacing: "Medium",
    });
    for (const resource of data.affectedResources) {
      bodyItems.push({ type: "TextBlock", text: `• ${resource}`, size: "Small" });
    }
  }

  if (data.slaDeadline) {
    bodyItems.push({
      type: "TextBlock",
      text: `⏱️ SLA Deadline: ${data.slaDeadline}`,
      color: "Attention",
      spacing: "Medium",
    });
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [
    {
      type: "Action.Submit",
      title: "Acknowledge",
      data: { action: "acknowledgeAlert", actionContext: { alertId: data.alertId } },
    },
    {
      type: "Action.Submit",
      title: "Dismiss",
      data: { action: "dismissAlert", actionContext: { alertId: data.alertId } },
    },
    {
      type: "Action.Submit",
      title: "Escalate",
      data: { action: "escalateAlert", actionContext: { alertId: data.alertId } },
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
