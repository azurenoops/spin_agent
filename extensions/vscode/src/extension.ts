import * as vscode from "vscode";
import { McpClient } from "./services/mcpClient";
import { createParticipantHandler } from "./participant";
import { checkHealth } from "./commands/health";
import { configure } from "./commands/configure";
import { IacDiagnosticsProvider } from "./diagnostics/iacDiagnosticsProvider";
import { IacCodeActionProvider } from "./codeActions/iacCodeActionProvider";

let outputChannel: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext): void {
  outputChannel = vscode.window.createOutputChannel("ATO Copilot");
  const mcpClient = new McpClient(outputChannel);

  // Register @ato chat participant
  const participant = vscode.chat.createChatParticipant(
    "ato",
    createParticipantHandler(mcpClient)
  );
  participant.iconPath = vscode.Uri.joinPath(
    context.extensionUri,
    "media",
    "icon.png"
  );
  // isSticky keeps the participant selected across chat interactions
  (participant as any).isSticky = true;
  context.subscriptions.push(participant);

  // Register commands
  context.subscriptions.push(
    vscode.commands.registerCommand("ato.checkHealth", () =>
      checkHealth(mcpClient)
    )
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("ato.configure", () => configure())
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "ato.analyzeCurrentFile",
      async () => {
        const { analyzeCurrentFile } = await import(
          "./commands/analyzeFile"
        );
        await analyzeCurrentFile(mcpClient);
      }
    )
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(
      "ato.analyzeWorkspace",
      async () => {
        const { analyzeWorkspace } = await import(
          "./commands/analyzeWorkspace"
        );
        await analyzeWorkspace(mcpClient);
      }
    )
  );

  // Follow-up suggestion command — wired to suggestion buttons in chat responses
  context.subscriptions.push(
    vscode.commands.registerCommand(
      "ato.followUpSuggestion",
      async (prompt: string) => {
        await vscode.commands.executeCommand("workbench.action.chat.open", {
          query: `@ato ${prompt}`,
          isPartialQuery: false,
        });
      }
    )
  );

  // Request False Positive — opens justification input and calls deviation MCP tool
  context.subscriptions.push(
    vscode.commands.registerCommand("ato.requestFalsePositive", async () => {
      const { requestFalsePositive } = await import(
        "./commands/requestFalsePositive"
      );
      await requestFalsePositive(mcpClient);
    })
  );

  // Save template internal command (for stream.button() actions)
  context.subscriptions.push(
    vscode.commands.registerCommand(
      "ato.saveTemplate",
      async (template: {
        name: string;
        type: string;
        content: string;
        language: string;
      }) => {
        const { saveTemplate } = await import(
          "./services/workspaceService"
        );
        await saveTemplate(template);
      }
    )
  );

  // Refresh client when settings change
  context.subscriptions.push(
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration("ato-copilot")) {
        mcpClient.refreshClient();
        outputChannel.appendLine("Configuration updated — client refreshed");
      }
    })
  );

  // Silent background health check on activation (FR-034)
  checkHealth(mcpClient, true);

  // IaC compliance diagnostics — inline squiggly underlines for findings (US6)
  const iacDiagnostics = new IacDiagnosticsProvider(mcpClient);
  context.subscriptions.push(iacDiagnostics);

  // Quick Fix code actions for auto-remediable IaC findings (US6)
  const iacCodeActions = new IacCodeActionProvider();
  const iacSelector: vscode.DocumentSelector = [
    { language: "bicep" },
    { language: "terraform" },
    { language: "hcl" },
    { language: "json", pattern: "**/*.json" },
    { language: "jsonc", pattern: "**/*.json" },
  ];
  context.subscriptions.push(
    vscode.languages.registerCodeActionsProvider(iacSelector, iacCodeActions, {
      providedCodeActionKinds: IacCodeActionProvider.providedCodeActionKinds,
    })
  );

  outputChannel.appendLine("ATO Copilot extension activated");
}

export function deactivate(): void {
  if (outputChannel) {
    outputChannel.dispose();
  }
}
