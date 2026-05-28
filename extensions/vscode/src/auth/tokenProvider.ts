// Feature 051 — VS Code Device-Code Sign-In (T110)
// Contract: specs/051-login/contracts/vscode-extension.md § 5 + § 6
//
// `getActiveTenantToken` is the single source of bearer tokens for every
// MCP request from the VS Code extension. It:
//
//   1. Reads the active tenant id from workspaceState.
//   2. Reads the cached access token via SecretStorage.
//   3. If no token, triggers `signInCommand` and returns the new token.
//
// MSAL silent renewal is owned by the next `signInCommand` invocation —
// `runDeviceCodeSignIn` calls `acquireTokenSilent` BEFORE falling back to
// device-code (R-Summary item 1). This module does NOT cache or refresh
// tokens itself.

import * as vscode from "vscode";

import {
  getActiveTenantId,
  readTokenAsync,
} from "./secretStorage";
import { signInCommand } from "./signInCommand";
import type { SignInStatusBar } from "./statusBar";

/**
 * Resolve a bearer token for the currently-active tenant. If none is
 * cached, prompts the user to sign in. Throws if the user cancels.
 */
export async function getActiveTenantToken(
  context: vscode.ExtensionContext,
  statusBar: SignInStatusBar,
  outputChannel?: vscode.OutputChannel,
): Promise<string> {
  const tenantId = getActiveTenantId(context);
  if (tenantId) {
    const cached = await readTokenAsync(context, tenantId);
    if (cached) return cached;
  }

  // No active tenant, or no cached token — fall back to interactive sign-in.
  const result = await signInCommand(context, statusBar, outputChannel);
  if (result.outcome === "signedIn" && result.accessToken) {
    return result.accessToken;
  }
  throw new Error(
    result.errorMessage ??
      "ATO Copilot sign-in is required for this action. Cancelled.",
  );
}
