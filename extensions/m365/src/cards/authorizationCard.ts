/**
 * Authorization Decision Adaptive Card Builder (US13, T170)
 *
 * Displays AuthorizationDecision with ATO type, expiration date, risk level,
 * conditions/limitations, expiration countdown, and authorization status.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface AuthorizationCondition {
  description: string;
  status?: string;
}

export interface AuthorizationData {
  systemName: string;
  decisionType?: string;
  status?: string;
  riskLevel?: string;
  authorizedDate?: string;
  expirationDate?: string;
  daysUntilExpiration?: number;
  authorizingOfficialName?: string;
  conditions?: AuthorizationCondition[];
  riskAcceptances?: number;
  openFindings?: number;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const decisionTypeLabels: Record<string, string> = {
  ATO: "Authority to Operate",
  IATT: "Interim Authority to Test",
  DATO: "Denial of Authority to Operate",
  ATO_CC: "ATO with Conditions",
};

function getDecisionColor(decisionType: string): string {
  const normalized = decisionType.toUpperCase();
  if (normalized === "ATO") return "Good";
  if (normalized === "IATT" || normalized === "ATO_CC") return "Warning";
  if (normalized === "DATO") return "Attention";
  return "Default";
}

function getRiskColor(riskLevel: string): string {
  const normalized = riskLevel.toLowerCase();
  if (normalized === "critical" || normalized === "very high" || normalized === "high") return "Attention";
  if (normalized === "moderate" || normalized === "medium") return "Warning";
  if (normalized === "low") return "Good";
  return "Default";
}

function getExpirationBadge(days: number | undefined): { text: string; color: string } {
  if (days == null) return { text: "No Expiration Set", color: "Default" };
  if (days < 0) return { text: `EXPIRED (${Math.abs(days)} days ago)`, color: "Attention" };
  if (days === 0) return { text: "EXPIRES TODAY", color: "Attention" };
  if (days <= 30) return { text: `${days} days remaining`, color: "Attention" };
  if (days <= 90) return { text: `${days} days remaining`, color: "Warning" };
  return { text: `${days} days remaining`, color: "Good" };
}

function getConditionStatusIcon(status?: string): string {
  if (!status) return "⬜";
  const normalized = status.toLowerCase();
  if (normalized === "met" || normalized === "complete" || normalized === "resolved") return "✅";
  if (normalized === "in progress" || normalized === "partial") return "🔄";
  return "❌";
}

export function buildAuthorizationCard(data: AuthorizationData): Record<string, unknown> {
  const decisionType = data.decisionType ?? "Unknown";
  const decisionLabel = decisionTypeLabels[decisionType] ?? decisionType;
  const decisionColor = getDecisionColor(decisionType);
  const expBadge = getExpirationBadge(data.daysUntilExpiration);

  const bodyItems: Record<string, unknown>[] = [
    // Header
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Authorization Decision",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: data.systemName,
      weight: "Bolder",
      size: "Medium",
      spacing: "None",
    },

    // Decision type and status row
    {
      type: "ColumnSet",
      separator: true,
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Decision", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${decisionLabel} (${decisionType})`,
              weight: "Bolder",
              size: "Medium",
              color: decisionColor,
            },
          ],
        },
        {
          type: "Column",
          width: "auto",
          items: [
            { type: "TextBlock", text: "Status", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: data.status ?? "Pending",
              weight: "Bolder",
              color: data.status === "Active" ? "Good" : "Warning",
            },
          ],
        },
      ],
    },

    // Expiration countdown
    {
      type: "ColumnSet",
      separator: true,
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Expiration", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: expBadge.text,
              weight: "Bolder",
              size: "Medium",
              color: expBadge.color,
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Risk Level", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: data.riskLevel ?? "Not Assessed",
              weight: "Bolder",
              color: getRiskColor(data.riskLevel ?? ""),
            },
          ],
        },
      ],
    },

    // Key dates and details
    {
      type: "FactSet",
      separator: true,
      facts: [
        ...(data.authorizedDate ? [{ title: "Authorized", value: data.authorizedDate }] : []),
        ...(data.expirationDate ? [{ title: "Expires", value: data.expirationDate }] : []),
        ...(data.authorizingOfficialName
          ? [{ title: "Authorizing Official", value: data.authorizingOfficialName }]
          : []),
        ...(data.riskAcceptances != null
          ? [{ title: "Risk Acceptances", value: `${data.riskAcceptances}` }]
          : []),
        ...(data.openFindings != null
          ? [{ title: "Open Findings", value: `${data.openFindings}` }]
          : []),
      ],
    },
  ];

  // Conditions list
  if (data.conditions && data.conditions.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Conditions / Limitations",
      weight: "Bolder",
      separator: true,
      spacing: "Medium",
    });

    for (const condition of data.conditions) {
      bodyItems.push({
        type: "TextBlock",
        text: `${getConditionStatusIcon(condition.status)} ${condition.description}`,
        wrap: true,
        size: "Small",
        spacing: "Small",
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  const suggestionActions = buildSuggestionButtons(data.suggestions, data.conversationId);

  return {
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
    type: "AdaptiveCard",
    version: "1.5",
    body: bodyItems,
    actions: [
      {
        type: "Action.Submit",
        title: "View Authorization Package",
        data: { action: "viewAuthPackage", systemName: data.systemName },
      },
      {
        type: "Action.Submit",
        title: "Review Risk Acceptances",
        data: { action: "reviewRiskAcceptances", systemName: data.systemName },
      },
      {
        type: "Action.Submit",
        title: "Generate ConMon Report",
        data: { action: "generateConMon", systemName: data.systemName },
      },
      ...suggestionActions,
    ],
  };
}
