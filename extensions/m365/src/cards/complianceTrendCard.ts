/**
 * Compliance Trend Adaptive Card Builder (FR-010a)
 *
 * Displays compliance score trend with sparkline approximation,
 * direction indicator, and significant events.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface TrendDataPoint {
  date: string;
  score: number;
}

export interface ComplianceTrendData {
  dataPoints: TrendDataPoint[];
  direction: "improving" | "declining" | "stable";
  significantEvents?: Array<{ date: string; event: string }>;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const directionIndicators: Record<string, string> = {
  improving: "📈 Improving",
  declining: "📉 Declining",
  stable: "➡️ Stable",
};

function buildSparkline(dataPoints: TrendDataPoint[]): string {
  if (dataPoints.length === 0) return "";
  const scores = dataPoints.map((dp) => dp.score);
  const min = Math.min(...scores);
  const max = Math.max(...scores);
  const range = max - min || 1;
  const blocks = "▁▂▃▄▅▆▇█";
  return scores
    .map((s) => {
      const idx = Math.round(((s - min) / range) * (blocks.length - 1));
      return blocks[idx];
    })
    .join("");
}

export function buildComplianceTrendCard(data: ComplianceTrendData): Record<string, unknown> {
  const indicator = directionIndicators[data.direction] ?? "➡️ Unknown";
  const sparkline = buildSparkline(data.dataPoints);
  const latest = data.dataPoints.length > 0 ? data.dataPoints[data.dataPoints.length - 1] : null;

  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Compliance Trend",
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
            { type: "TextBlock", text: indicator, weight: "Bolder", size: "Medium" },
          ],
        },
        ...(latest
          ? [
              {
                type: "Column",
                width: "auto",
                items: [
                  {
                    type: "TextBlock",
                    text: `Current: ${latest.score}%`,
                    weight: "Bolder",
                    size: "Medium",
                  },
                ],
              },
            ]
          : []),
      ],
    },
  ];

  if (sparkline) {
    bodyItems.push({
      type: "TextBlock",
      text: sparkline,
      fontType: "Monospace",
      size: "ExtraLarge",
      spacing: "Medium",
    });
  }

  if (data.dataPoints.length > 0) {
    const first = data.dataPoints[0];
    const last = data.dataPoints[data.dataPoints.length - 1];
    bodyItems.push({
      type: "TextBlock",
      text: `${first.date} → ${last.date}`,
      isSubtle: true,
      size: "Small",
    });
  }

  if (data.significantEvents && data.significantEvents.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Significant Events",
      weight: "Bolder",
      spacing: "Medium",
    });
    for (const event of data.significantEvents) {
      bodyItems.push({
        type: "TextBlock",
        text: `📌 ${event.date}: ${event.event}`,
        wrap: true,
        size: "Small",
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  return {
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
    type: "AdaptiveCard",
    version: "1.5",
    body: bodyItems,
    actions: buildSuggestionButtons(data.suggestions, data.conversationId),
  };
}
