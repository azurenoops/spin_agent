/**
 * Deviation Adaptive Card Builder (Feature 035)
 *
 * Displays deviation details (control, severity, justification, evidence count)
 * with Approve/Deny Action.Submit buttons for review workflow.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface DeviationData {
  deviationId: string;
  controlId: string;
  deviationType: string;
  catSeverity: string;
  status: string;
  justification: string;
  compensatingControls?: string;
  expirationDate?: string;
  evidenceCount?: number;
  requestedBy?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

export function buildDeviationCard(data: DeviationData): Record<string, unknown> {
  const severityColor =
    data.catSeverity === "CatI"
      ? "Attention"
      : data.catSeverity === "CatII"
        ? "Warning"
        : "Good";

  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "ATO Copilot — Deviation Request",
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
              text: data.controlId,
              weight: "Bolder",
              size: "ExtraLarge",
            },
          ],
        },
        {
          type: "Column",
          width: "auto",
          items: [
            {
              type: "TextBlock",
              text: data.catSeverity,
              color: severityColor,
              weight: "Bolder",
              size: "Medium",
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            {
              type: "TextBlock",
              text: data.deviationType,
              size: "Medium",
              horizontalAlignment: "Right",
            },
          ],
        },
      ],
    },
    {
      type: "FactSet",
      facts: [
        { title: "Status", value: data.status },
        { title: "Type", value: data.deviationType },
        ...(data.requestedBy ? [{ title: "Requested By", value: data.requestedBy }] : []),
        ...(data.expirationDate ? [{ title: "Expires", value: data.expirationDate }] : []),
        ...(data.evidenceCount != null
          ? [{ title: "Evidence", value: `${data.evidenceCount} item(s)` }]
          : []),
      ],
    },
    {
      type: "TextBlock",
      text: "Justification",
      weight: "Bolder",
      spacing: "Medium",
    },
    {
      type: "TextBlock",
      text: data.justification,
      wrap: true,
      size: "Small",
    },
  ];

  if (data.compensatingControls) {
    bodyItems.push(
      {
        type: "TextBlock",
        text: "Compensating Controls",
        weight: "Bolder",
        spacing: "Medium",
      },
      {
        type: "TextBlock",
        text: data.compensatingControls,
        wrap: true,
        size: "Small",
      },
    );
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const actions: Array<Record<string, unknown>> = [];

  if (data.status === "Pending") {
    actions.push(
      {
        type: "Action.Submit",
        title: "Approve",
        style: "positive",
        data: {
          action: "review_deviation",
          actionContext: { deviationId: data.deviationId, decision: "Approved" },
        },
      },
      {
        type: "Action.Submit",
        title: "Deny",
        style: "destructive",
        data: {
          action: "review_deviation",
          actionContext: { deviationId: data.deviationId, decision: "Denied" },
        },
      },
    );
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
