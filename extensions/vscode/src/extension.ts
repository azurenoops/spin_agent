import * as vscode from "vscode";
import { McpClient } from "./services/mcpClient";
import { createParticipantHandler } from "./participant";
import { checkHealth } from "./commands/health";
import { configure } from "./commands/configure";
import { IacDiagnosticsProvider } from "./diagnostics/iacDiagnosticsProvider";
import { IacCodeActionProvider } from "./codeActions/iacCodeActionProvider";
import { TenantStatusBar } from "./services/tenantStatusBar";
import { createStatusBarItem } from "./auth/statusBar";
import { signInCommand } from "./auth/signInCommand";
import { signOutCommand } from "./auth/signOutCommand";
import { switchTenantCommand } from "./auth/switchTenantCommand";
import { getActiveTenantToken } from "./auth/tokenProvider";
import {
  getActiveTenantId,
  readTokenAsync,
} from "./auth/secretStorage";

let outputChannel: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext): void {
  outputChannel = vscode.window.createOutputChannel("ATO Copilot");
  const mcpClient = new McpClient(outputChannel);

  // Tenant status bar — surfaces home/impersonated tenant per FR-024 (T141).
  const tenantStatusBar = new TenantStatusBar();
  context.subscriptions.push(tenantStatusBar);
  mcpClient.setTenantContextProvider(() =>
    tenantStatusBar.getOutboundContext()
  );

  // Feature 051 (T108/T109) — sign-in status bar + device-code commands.
  const signInStatusBar = createStatusBarItem(context);
  // If we already have a cached token at activation, reflect that on the
  // status bar without forcing an interactive flow.
  void (async () => {
    const tid = getActiveTenantId(context);
    if (tid && (await readTokenAsync(context, tid))) {
      signInStatusBar.update({
        state: "signedIn",
        displayName: "Signed In",
        tenant: tid,
      });
    }
  })();

  // Feature 051 (T110) — wire the per-request bearer-token provider. Reads
  // the cached token if present and falls back to the device-code flow
  // (which itself attempts silent renewal first per R-Summary item 1).
  mcpClient.setTokenProvider(async () => {
    const tid = getActiveTenantId(context);
    if (tid) {
      const cached = await readTokenAsync(context, tid);
      if (cached) return cached;
    }
    // Returning undefined lets the request fly without the header; the
    // server's 401 will trigger an interactive sign-in via the explicit
    // `ato.signIn` command flow rather than here, so we don't disrupt
    // background calls like the health check.
    return undefined;
  });

  // Register @ato chat participant
  const participant = vscode.chat.createChatParticipant(
    "ato",
    createParticipantHandler(mcpClient, {
      onAuthCommand: async (kind) => {
        if (kind === "signIn") {
          await signInCommand(context, signInStatusBar, outputChannel);
        } else if (kind === "signOut") {
          await signOutCommand(context, signInStatusBar, outputChannel);
        } else if (kind === "switchTenant") {
          await switchTenantCommand(context, signInStatusBar, outputChannel);
        }
      },
    })
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

  // Feature 051 (T109) — device-code sign-in commands.
  context.subscriptions.push(
    vscode.commands.registerCommand("ato.signIn", () =>
      signInCommand(context, signInStatusBar, outputChannel)
    )
  );
  context.subscriptions.push(
    vscode.commands.registerCommand("ato.signOut", () =>
      signOutCommand(context, signInStatusBar, outputChannel)
    )
  );
  context.subscriptions.push(
    vscode.commands.registerCommand("ato.switchTenant", () =>
      switchTenantCommand(context, signInStatusBar, outputChannel)
    )
  );
  // Exported for any future internal caller that needs a bearer with the
  // sign-in-if-needed semantics. Registering as a command keeps the
  // surface discoverable while keeping the helper available as a TS
  // import for in-process callers.
  context.subscriptions.push(
    vscode.commands.registerCommand("ato.internal.getActiveTenantToken", () =>
      getActiveTenantToken(context, signInStatusBar, outputChannel)
    )
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
