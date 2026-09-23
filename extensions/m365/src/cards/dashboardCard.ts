/**
 * Multi-System Dashboard Adaptive Card Builder (US13, T171)
 *
 * Displays a dashboard summarizing multiple registered systems with
 * RMF step distribution, overall compliance posture, and per-system status rows.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface DashboardSystem {
  systemName: string;
  acronym?: string;
  currentRmfStep?: string;
  complianceScore?: number;
  impactLevel?: string;
  atoStatus?: string;
  activeAlerts?: number;
}

export interface RmfStepDistribution {
  step: string;
  count: number;
}

export interface DashboardData {
  title?: string;
  systems: DashboardSystem[];
  totalSystems?: number;
  averageComplianceScore?: number;
  rmfDistribution?: RmfStepDistribution[];
  criticalAlerts?: number;
  expiringAtos?: number;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const stepEmojis: Record<string, string> = {
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

function getAtoStatusIcon(status?: string): string {
  if (!status) return "⬜";
  const normalized = status.toLowerCase();
  if (normalized === "active" || normalized === "ato") return "🟢";
  if (normalized === "iatt" || normalized === "conditional") return "🟡";
  if (normalized === "expired" || normalized === "dato") return "🔴";
  if (normalized === "pending") return "⏳";
  return "⬜";
}

export function buildDashboardCard(data: DashboardData): Record<string, unknown> {
  const totalSystems = data.totalSystems ?? data.systems.length;
  const avgScore = data.averageComplianceScore ?? (
    data.systems.length > 0
      ? Math.round(
          data.systems.reduce((sum, s) => sum + (s.complianceScore ?? 0), 0) / data.systems.length
        )
      : 0
  );

  const bodyItems: Record<string, unknown>[] = [
    // Header
    {
      type: "TextBlock",
      text: data.title ?? "Security Posture Intelligence Navigator — Multi-System Dashboard",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: `${totalSystems} registered system${totalSystems !== 1 ? "s" : ""}`,
      isSubtle: true,
      size: "Small",
      spacing: "None",
    },

    // Summary metrics row
    {
      type: "ColumnSet",
      separator: true,
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Avg Compliance", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${avgScore}%`,
              weight: "Bolder",
              size: "ExtraLarge",
              color: getScoreColor(avgScore),
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Critical Alerts", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${data.criticalAlerts ?? 0}`,
              weight: "Bolder",
              size: "ExtraLarge",
              color: (data.criticalAlerts ?? 0) > 0 ? "Attention" : "Good",
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Expiring ATOs", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${data.expiringAtos ?? 0}`,
              weight: "Bolder",
              size: "ExtraLarge",
              color: (data.expiringAtos ?? 0) > 0 ? "Warning" : "Good",
            },
          ],
        },
      ],
    },
  ];

  // RMF step distribution
  if (data.rmfDistribution && data.rmfDistribution.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "RMF Step Distribution",
      weight: "Bolder",
      separator: true,
      spacing: "Medium",
    });

    const distColumns = data.rmfDistribution.map((d) => ({
      type: "Column",
      width: "stretch",
      items: [
        {
          type: "TextBlock",
          text: `${stepEmojis[d.step] ?? "❓"}`,
          horizontalAlignment: "Center",
          size: "Small",
        },
        {
          type: "TextBlock",
          text: `${d.count}`,
          weight: "Bolder",
          horizontalAlignment: "Center",
        },
        {
          type: "TextBlock",
          text: d.step,
          size: "Small",
          isSubtle: true,
          horizontalAlignment: "Center",
        },
      ],
    }));

    bodyItems.push({
      type: "ColumnSet",
      columns: distColumns,
    });
  }

  // Per-system rows
  if (data.systems.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Systems",
      weight: "Bolder",
      separator: true,
      spacing: "Medium",
    });

    // Header row
    bodyItems.push({
      type: "ColumnSet",
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [{ type: "TextBlock", text: "System", weight: "Bolder", size: "Small" }],
        },
        {
          type: "Column",
          width: "auto",
          items: [{ type: "TextBlock", text: "RMF", weight: "Bolder", size: "Small" }],
        },
        {
          type: "Column",
          width: "auto",
          items: [{ type: "TextBlock", text: "Score", weight: "Bolder", size: "Small" }],
        },
        {
          type: "Column",
          width: "auto",
          items: [{ type: "TextBlock", text: "ATO", weight: "Bolder", size: "Small" }],
        },
      ],
    });

    // System rows (max 15)
    const displaySystems = data.systems.slice(0, 15);
    for (const sys of displaySystems) {
      const score = sys.complianceScore ?? 0;
      bodyItems.push({
        type: "ColumnSet",
        spacing: "Small",
        selectAction: {
          type: "Action.Submit",
          data: { action: "viewSystem", systemName: sys.systemName },
        },
        columns: [
          {
            type: "Column",
            width: "stretch",
            items: [
              {
                type: "TextBlock",
                text: sys.acronym ? `${sys.systemName} (${sys.acronym})` : sys.systemName,
                size: "Small",
                wrap: true,
              },
            ],
          },
          {
            type: "Column",
            width: "auto",
            items: [
              {
                type: "TextBlock",
                text: `${stepEmojis[sys.currentRmfStep ?? ""] ?? "❓"} ${sys.currentRmfStep ?? "—"}`,
                size: "Small",
              },
            ],
          },
          {
            type: "Column",
            width: "auto",
            items: [
              {
                type: "TextBlock",
                text: `${score}%`,
                size: "Small",
                color: getScoreColor(score),
                weight: "Bolder",
              },
            ],
          },
          {
            type: "Column",
            width: "auto",
            items: [
              {
                type: "TextBlock",
                text: `${getAtoStatusIcon(sys.atoStatus)} ${sys.atoStatus ?? "—"}`,
                size: "Small",
              },
            ],
          },
        ],
      });
    }

    if (data.systems.length > 15) {
      bodyItems.push({
        type: "TextBlock",
        text: `+ ${data.systems.length - 15} more systems`,
        isSubtle: true,
        size: "Small",
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
        title: "View All Systems",
        data: { action: "listSystems" },
      },
      {
        type: "Action.Submit",
        title: "Compliance Summary",
        data: { action: "complianceSummary" },
      },
      {
        type: "Action.Submit",
        title: "Expiring ATOs",
        data: { action: "expiringAtos" },
      },
      ...suggestionActions,
    ],
  };
}
