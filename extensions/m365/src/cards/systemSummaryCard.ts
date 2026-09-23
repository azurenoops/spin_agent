/**
 * System Summary Adaptive Card Builder (US13, T168)
 *
 * Displays a RegisteredSystem summary with system name, acronym, type,
 * hosting environment, current RMF step, mission criticality,
 * compliance score, and active alerts.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface SystemSummaryData {
  systemName: string;
  acronym?: string;
  systemType?: string;
  hostingEnvironment?: string;
  currentRmfStep?: string;
  rmfStepNumber?: number;
  missionCriticality?: string;
  impactLevel?: string;
  complianceScore?: number;
  activeAlerts?: number;
  isActive?: boolean;
  authorizedDate?: string;
  atoExpiration?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const rmfStepColors: Record<string, string> = {
  Prepare: "Accent",
  Categorize: "Accent",
  Select: "Accent",
  Implement: "Warning",
  Assess: "Warning",
  Authorize: "Attention",
  Monitor: "Good",
};

const rmfStepIcons: Record<string, string> = {
  Prepare: "📋",
  Categorize: "🏷️",
  Select: "✅",
  Implement: "🔧",
  Assess: "🔍",
  Authorize: "🛡️",
  Monitor: "📊",
};

function getScoreColor(score: number): string {
  if (score >= 80) return "Good";
  if (score >= 60) return "Warning";
  return "Attention";
}

function getCriticalityColor(criticality: string): string {
  const normalized = criticality.toLowerCase();
  if (normalized.includes("high") || normalized.includes("mission essential")) return "Attention";
  if (normalized.includes("moderate") || normalized.includes("mission support")) return "Warning";
  return "Default";
}

export function buildSystemSummaryCard(data: SystemSummaryData): Record<string, unknown> {
  const step = data.currentRmfStep ?? "Unknown";
  const stepIcon = rmfStepIcons[step] ?? "❓";
  const stepColor = rmfStepColors[step] ?? "Default";
  const statusText = data.isActive !== false ? "Active" : "Inactive";
  const statusColor = data.isActive !== false ? "Good" : "Attention";

  const bodyItems: Record<string, unknown>[] = [
    // Header
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — System Summary",
      weight: "Bolder",
      size: "Large",
    },

    // System identity row
    {
      type: "ColumnSet",
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            {
              type: "TextBlock",
              text: data.systemName,
              weight: "Bolder",
              size: "Medium",
              wrap: true,
            },
            {
              type: "TextBlock",
              text: data.acronym ? `(${data.acronym})` : "",
              isSubtle: true,
              size: "Small",
              spacing: "None",
            },
          ],
        },
        {
          type: "Column",
          width: "auto",
          items: [
            {
              type: "TextBlock",
              text: statusText,
              color: statusColor,
              weight: "Bolder",
              horizontalAlignment: "Right",
            },
          ],
        },
      ],
    },

    // RMF Step badge
    {
      type: "ColumnSet",
      columns: [
        {
          type: "Column",
          width: "auto",
          items: [
            {
              type: "TextBlock",
              text: "RMF Step",
              isSubtle: true,
              size: "Small",
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            {
              type: "TextBlock",
              text: `${stepIcon} ${step}${data.rmfStepNumber != null ? ` (Step ${data.rmfStepNumber})` : ""}`,
              color: stepColor,
              weight: "Bolder",
            },
          ],
        },
      ],
    },

    // Details grid
    {
      type: "FactSet",
      facts: [
        ...(data.systemType ? [{ title: "System Type", value: data.systemType }] : []),
        ...(data.hostingEnvironment ? [{ title: "Hosting", value: data.hostingEnvironment }] : []),
        ...(data.missionCriticality ? [{ title: "Mission Criticality", value: data.missionCriticality }] : []),
        ...(data.impactLevel ? [{ title: "Impact Level", value: data.impactLevel }] : []),
        ...(data.authorizedDate ? [{ title: "Authorized", value: data.authorizedDate }] : []),
        ...(data.atoExpiration ? [{ title: "ATO Expiration", value: data.atoExpiration }] : []),
      ],
    },

    // Metrics row
    {
      type: "ColumnSet",
      separator: true,
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Compliance Score", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: data.complianceScore != null ? `${data.complianceScore}%` : "N/A",
              weight: "Bolder",
              size: "ExtraLarge",
              color: data.complianceScore != null ? getScoreColor(data.complianceScore) : "Default",
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Active Alerts", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: data.activeAlerts != null ? `${data.activeAlerts}` : "0",
              weight: "Bolder",
              size: "ExtraLarge",
              color: (data.activeAlerts ?? 0) > 0 ? "Attention" : "Good",
            },
          ],
        },
        ...(data.missionCriticality
          ? [
              {
                type: "Column",
                width: "stretch",
                items: [
                  { type: "TextBlock", text: "Criticality", isSubtle: true, size: "Small" },
                  {
                    type: "TextBlock",
                    text: data.missionCriticality,
                    weight: "Bolder",
                    color: getCriticalityColor(data.missionCriticality),
                  },
                ],
              },
            ]
          : []),
      ],
    },
  ];

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
        title: "View Compliance Details",
        data: { action: "viewCompliance", systemName: data.systemName },
      },
      {
        type: "Action.Submit",
        title: "Check RMF Progress",
        data: { action: "checkRmfProgress", systemName: data.systemName },
      },
      {
        type: "Action.Submit",
        title: "View Authorization Status",
        data: { action: "viewAuthorization", systemName: data.systemName },
      },
      ...suggestionActions,
    ],
  };
}
