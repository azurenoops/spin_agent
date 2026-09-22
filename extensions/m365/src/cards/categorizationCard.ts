/**
 * Security Categorization Adaptive Card Builder (US13, T169)
 *
 * Displays SecurityCategorization with FIPS 199 notation, DoD Impact Level,
 * Confidentiality/Integrity/Availability impact levels, and information types.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface InformationTypeItem {
  name: string;
  confidentiality: string;
  integrity: string;
  availability: string;
}

export interface CategorizationData {
  systemName: string;
  fipsCategory?: string;
  impactLevel?: string;
  confidentialityImpact?: string;
  integrityImpact?: string;
  availabilityImpact?: string;
  overallImpact?: string;
  informationTypes?: InformationTypeItem[];
  justification?: string;
  categorizedDate?: string;
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const impactColors: Record<string, string> = {
  High: "Attention",
  Moderate: "Warning",
  Low: "Good",
};

const impactIcons: Record<string, string> = {
  High: "🔴",
  Moderate: "🟡",
  Low: "🟢",
};

function getImpactColor(impact: string): string {
  return impactColors[impact] ?? "Default";
}

function getImpactIcon(impact: string): string {
  return impactIcons[impact] ?? "⚪";
}

export function buildCategorizationCard(data: CategorizationData): Record<string, unknown> {
  const bodyItems: Record<string, unknown>[] = [
    // Header
    {
      type: "TextBlock",
      text: "Security Posture Intelligence Navigator — Security Categorization",
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

    // FIPS 199 notation
    {
      type: "ColumnSet",
      separator: true,
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "FIPS 199 Category", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: data.fipsCategory ?? "Not Categorized",
              weight: "Bolder",
              size: "ExtraLarge",
              color: getImpactColor(data.overallImpact ?? ""),
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "DoD Impact Level", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: data.impactLevel ?? "N/A",
              weight: "Bolder",
              size: "ExtraLarge",
              color: "Accent",
            },
          ],
        },
      ],
    },

    // C/I/A impact columns
    {
      type: "ColumnSet",
      separator: true,
      columns: [
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Confidentiality", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${getImpactIcon(data.confidentialityImpact ?? "")} ${data.confidentialityImpact ?? "N/A"}`,
              weight: "Bolder",
              color: getImpactColor(data.confidentialityImpact ?? ""),
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Integrity", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${getImpactIcon(data.integrityImpact ?? "")} ${data.integrityImpact ?? "N/A"}`,
              weight: "Bolder",
              color: getImpactColor(data.integrityImpact ?? ""),
            },
          ],
        },
        {
          type: "Column",
          width: "stretch",
          items: [
            { type: "TextBlock", text: "Availability", isSubtle: true, size: "Small" },
            {
              type: "TextBlock",
              text: `${getImpactIcon(data.availabilityImpact ?? "")} ${data.availabilityImpact ?? "N/A"}`,
              weight: "Bolder",
              color: getImpactColor(data.availabilityImpact ?? ""),
            },
          ],
        },
      ],
    },
  ];

  // Information types table
  if (data.informationTypes && data.informationTypes.length > 0) {
    bodyItems.push({
      type: "TextBlock",
      text: "Information Types",
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
          items: [{ type: "TextBlock", text: "Type", weight: "Bolder", size: "Small" }],
        },
        {
          type: "Column",
          width: "auto",
          items: [{ type: "TextBlock", text: "C", weight: "Bolder", size: "Small" }],
        },
        {
          type: "Column",
          width: "auto",
          items: [{ type: "TextBlock", text: "I", weight: "Bolder", size: "Small" }],
        },
        {
          type: "Column",
          width: "auto",
          items: [{ type: "TextBlock", text: "A", weight: "Bolder", size: "Small" }],
        },
      ],
    });

    // Data rows (max 10 to avoid overflow)
    const displayTypes = data.informationTypes.slice(0, 10);
    for (const infoType of displayTypes) {
      bodyItems.push({
        type: "ColumnSet",
        spacing: "Small",
        columns: [
          {
            type: "Column",
            width: "stretch",
            items: [{ type: "TextBlock", text: infoType.name, size: "Small", wrap: true }],
          },
          {
            type: "Column",
            width: "auto",
            items: [
              {
                type: "TextBlock",
                text: getImpactIcon(infoType.confidentiality),
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
                text: getImpactIcon(infoType.integrity),
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
                text: getImpactIcon(infoType.availability),
                size: "Small",
              },
            ],
          },
        ],
      });
    }

    if (data.informationTypes.length > 10) {
      bodyItems.push({
        type: "TextBlock",
        text: `+ ${data.informationTypes.length - 10} more information types`,
        isSubtle: true,
        size: "Small",
      });
    }
  }

  // Justification
  if (data.justification) {
    bodyItems.push({
      type: "TextBlock",
      text: "Justification",
      weight: "Bolder",
      separator: true,
      spacing: "Medium",
    });
    bodyItems.push({
      type: "TextBlock",
      text: data.justification,
      wrap: true,
      size: "Small",
    });
  }

  // Categorization date
  if (data.categorizedDate) {
    bodyItems.push({
      type: "TextBlock",
      text: `Categorized: ${data.categorizedDate}`,
      isSubtle: true,
      size: "Small",
      spacing: "Medium",
    });
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
        title: "Select Control Baseline",
        data: { action: "selectBaseline", systemName: data.systemName },
      },
      {
        type: "Action.Submit",
        title: "Edit Categorization",
        data: { action: "editCategorization", systemName: data.systemName },
      },
      ...suggestionActions,
    ],
  };
}
