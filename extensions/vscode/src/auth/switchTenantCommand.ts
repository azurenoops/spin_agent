// Feature 051 — VS Code Device-Code Sign-In (T107 / T108)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.4
//
// VS Code entry-point for `ato.switchTenant`. Binds live VS Code APIs to the
// pure `runSwitchTenant` core in `switchTenantCore.ts`.
//
// See switchTenantCore.ts for documentation on the switch-tenant flow and the
// DI seam used for unit testing (T108).

import * as vscode from "vscode";
import { PublicClientApplication } from "@azure/msal-node";

import { buildMsalNodeConfig } from "./msalNode";
import {
  getActiveTenantId,
  listKnownTenantsAsync,
} from "./secretStorage";
import {
  fetchLoginConfig,
  readServerBaseUrl,
  fetchActiveTenant,
} from "./signInCommand";
import { runDeviceCodeSignIn } from "./signInCore";
import type { SignInDependencies, SignInResult } from "./signInCore";
import type { SignInStatusBar } from "./statusBar";
import {
  runSwitchTenant,
  rewriteAuthorityTenant,
  type TenantPickItem,
  type SwitchTenantDependencies,
} from "./switchTenantCore";

// Re-export the pure helpers so callers that import from this module continue
// to work without a code-path change.
export { runSwitchTenant, rewriteAuthorityTenant };
export type { TenantPickItem, SwitchTenantDependencies };

export async function switchTenantCommand(
  context: vscode.ExtensionContext,
  statusBar: SignInStatusBar,
  outputChannel?: vscode.OutputChannel,
): Promise<SignInResult | { outcome: "cancelled" }> {
  const log = (msg: string) => outputChannel?.appendLine(msg);
  const serverBaseUrl = readServerBaseUrl();

  return runSwitchTenant({
    context,
    listKnownTenants: () => listKnownTenantsAsync(context),
    getActiveTenantId: (ctx) => getActiveTenantId(ctx as vscode.ExtensionContext),
    showQuickPick: async (items: TenantPickItem[]) => {
      const result = await vscode.window.showQuickPick(items, {
        placeHolder: "Choose an ATO Copilot tenant",
        ignoreFocusOut: true,
      });
      return result as TenantPickItem | undefined;
    },
    showInputBox: async () => {
      return await vscode.window.showInputBox({
        title: "Sign in to another ATO Copilot tenant",
        prompt:
          "Enter the Entra tenant id (GUID) to sign in against. The previous tenant's session is kept.",
        placeHolder: "00000000-0000-0000-0000-000000000000",
        validateInput: (value) =>
          /^[0-9a-fA-F-]{32,36}$/.test(value.trim())
            ? undefined
            : "Enter a valid GUID.",
        ignoreFocusOut: true,
      });
    },
    fetchLoginConfig: () => fetchLoginConfig(serverBaseUrl),
    runSignIn: (signInDeps) => runDeviceCodeSignIn(signInDeps),
    buildSignInDeps: (overrideFetch, preferTenantId): SignInDependencies => ({
      context,
      fetchLoginConfig: overrideFetch ?? (() => fetchLoginConfig(serverBaseUrl)),
      fetchActiveTenant: (token) => fetchActiveTenant(token, serverBaseUrl),
      createPca: (config, msalLog) =>
        new PublicClientApplication(
          buildMsalNodeConfig(config, { debug: msalLog }),
        ),
      showInfoMessage: (msg, ...actions) =>
        vscode.window.showInformationMessage(msg, ...actions),
      showErrorMessage: (msg) => vscode.window.showErrorMessage(msg),
      openExternal: (uri) => vscode.env.openExternal(vscode.Uri.parse(uri)),
      writeClipboard: (text) => vscode.env.clipboard.writeText(text),
      updateStatusBar: (state) => statusBar.update(state),
      log,
      preferTenantId,
    }),
  });
}
