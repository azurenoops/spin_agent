import * as vscode from "vscode";
import type { McpClient } from "../services/mcpClient";

/**
 * "Request False Positive" command handler (Feature 035 — T041).
 *
 * Opens a justification input prompt and calls the compliance_request_deviation
 * MCP tool to create a FalsePositive deviation for the selected finding.
 */
export async function requestFalsePositive(mcpClient: McpClient): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor) {
    vscode.window.showWarningMessage("No active editor.");
    return;
  }

  // Try to find an ATO Copilot diagnostic at the cursor position
  const diagnostics = vscode.languages
    .getDiagnostics(editor.document.uri)
    .filter(
      (d) =>
        d.source === "ATO Copilot" &&
        d.range.contains(editor.selection.active),
    );

  const diagnostic = diagnostics[0];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const finding = diagnostic ? (diagnostic as any)._iacFinding : undefined;

  const justification = await vscode.window.showInputBox({
    prompt: "Enter justification for false positive request",
    placeHolder: "This finding is a false positive because...",
    validateInput: (value) =>
      value.trim().length < 10
        ? "Justification must be at least 10 characters"
        : undefined,
  });

  if (!justification) return; // User cancelled

  try {
    await mcpClient.sendMessage({
      conversationId: "deviation-request",
      message: "",
      conversationHistory: [],
      context: {
        source: "vscode-copilot",
        platform: "VSCode",
        targetAgent: "compliance",
        metadata: { routingHint: "deviation" },
      },
      action: "requestDeviation",
      actionContext: {
        deviationType: "FalsePositive",
        justification,
        findingId: finding?.findingId,
        controlId: finding?.controlId ?? diagnostic?.code?.toString(),
        catSeverity: finding?.severity ?? "CatIII",
        fileName: editor.document.fileName,
      },
    });

    vscode.window.showInformationMessage(
      "False positive request submitted for review.",
    );
  } catch (err) {
    const message =
      err instanceof Error ? err.message : "Unknown error occurred";
    vscode.window.showErrorMessage(
      `Failed to submit false positive request: ${message}`,
    );
  }
}
