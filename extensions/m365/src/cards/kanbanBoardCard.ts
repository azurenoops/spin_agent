/**
 * Kanban Board Adaptive Card Builder (FR-010a)
 *
 * Displays remediation kanban board with status columns (To Do, In Progress, Done/Verified),
 * task cards with severity badges, and "Move" action buttons.
 */

import { buildAgentAttribution, buildSuggestionButtons } from "./shared";

export interface KanbanTask {
  taskId: string;
  title: string;
  severity?: string;
  assignedTo?: string;
  status: string;
}

export interface KanbanBoardData {
  boardTitle?: string;
  tasks?: KanbanTask[];
  board?: Record<string, unknown>;
  columns?: string[];
  agentUsed?: string;
  suggestions?: string[];
  conversationId?: string;
}

const severityBadges: Record<string, string> = {
  Critical: "🟣",
  High: "🔴",
  Medium: "🟠",
  Low: "🟡",
  Informational: "🔵",
};

function groupTasksByStatus(tasks: KanbanTask[]): Record<string, KanbanTask[]> {
  const groups: Record<string, KanbanTask[]> = {
    "To Do": [],
    "In Progress": [],
    "Done": [],
  };
  for (const task of tasks) {
    const normalized = task.status.toLowerCase();
    if (normalized.includes("progress")) {
      groups["In Progress"].push(task);
    } else if (normalized.includes("done") || normalized.includes("verified") || normalized.includes("complete")) {
      groups["Done"].push(task);
    } else {
      groups["To Do"].push(task);
    }
  }
  return groups;
}

export function buildKanbanBoardCard(data: KanbanBoardData): Record<string, unknown> {
  const tasks = data.tasks ?? [];
  const grouped = groupTasksByStatus(tasks);

  const bodyItems: Record<string, unknown>[] = [
    {
      type: "TextBlock",
      text: data.boardTitle ?? "Security Posture Intelligence Navigator — Remediation Board",
      weight: "Bolder",
      size: "Large",
    },
    {
      type: "TextBlock",
      text: `${tasks.length} tasks — ${grouped["To Do"].length} to do, ${grouped["In Progress"].length} in progress, ${grouped["Done"].length} done`,
      isSubtle: true,
      size: "Small",
    },
  ];

  // Render each column
  for (const column of ["To Do", "In Progress", "Done"]) {
    const columnTasks = grouped[column] ?? [];
    bodyItems.push({
      type: "TextBlock",
      text: `${column} (${columnTasks.length})`,
      weight: "Bolder",
      spacing: "Medium",
    });

    if (columnTasks.length === 0) {
      bodyItems.push({
        type: "TextBlock",
        text: "No tasks",
        isSubtle: true,
        size: "Small",
      });
    }

    for (const task of columnTasks) {
      const badge = severityBadges[task.severity ?? ""] ?? "⚪";
      const assignee = task.assignedTo ? ` → ${task.assignedTo}` : "";
      bodyItems.push({
        type: "TextBlock",
        text: `${badge} ${task.title}${assignee}`,
        wrap: true,
        size: "Small",
      });
    }
  }

  const attribution = buildAgentAttribution(data.agentUsed);
  if (attribution) bodyItems.push(attribution);

  // Build actions for tasks in "To Do" and "In Progress"
  const actions: Array<Record<string, unknown>> = [];

  const todoTasks = grouped["To Do"];
  if (todoTasks.length > 0) {
    actions.push({
      type: "Action.Submit",
      title: "Move to In Progress",
      data: {
        action: "moveKanbanTask",
        actionContext: { taskId: todoTasks[0].taskId, status: "InProgress" },
      },
    });
  }

  const inProgressTasks = grouped["In Progress"];
  if (inProgressTasks.length > 0) {
    actions.push({
      type: "Action.Submit",
      title: "Mark Complete",
      data: {
        action: "moveKanbanTask",
        actionContext: { taskId: inProgressTasks[0].taskId, status: "Done" },
      },
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
