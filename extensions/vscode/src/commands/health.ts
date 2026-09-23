import * as vscode from "vscode";
import { McpClient, McpError } from "../services/mcpClient";

/**
 * Check MCP Server health (FR-029, FR-034).
 * Shows informational message on success, warning with "Configure Connection" on failure.
 */
export async function checkHealth(
  mcpClient: McpClient,
  silent = false
): Promise<void> {
  try {
    await mcpClient.checkHealth();

    if (!silent) {
      vscode.window.showInformationMessage("Security Posture Intelligence Navigator API is healthy");
    }
  } catch (error) {
    const mcpError = error as McpError;
    const message = mcpError.message ?? "Security Posture Intelligence Navigator API is unreachable";

    if (silent) {
      // Background check — log only, no UI
      return;
    }

    const action = await vscode.window.showWarningMessage(
      message,
      mcpError.actionButton ?? "Configure Connection"
    );

    if (action === "Configure Connection") {
      vscode.commands.executeCommand(
        "workbench.action.openSettings",
        "@ext:ato-copilot.ato-copilot-vscode"
      );
    }
  }
}
