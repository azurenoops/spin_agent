// Feature 051 — VS Code Device-Code Sign-In (T105 wrapper)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.2
//
// Thin VS Code wrapper around the pure `runDeviceCodeSignIn` orchestrator
// in `signInCore.ts`. Keeps all vscode runtime imports out of the tested
// surface so the unit suite (T104) can run from plain Node mocha.

import * as vscode from "vscode";
import axios from "axios";
import { PublicClientApplication } from "@azure/msal-node";

import { buildMsalNodeConfig, type VsCodeLoginConfig } from "./msalNode";
import { runDeviceCodeSignIn, type SignInResult } from "./signInCore";
import type { SignInStatusBar } from "./statusBar";

// Re-export the seam types so callers (e.g. `extension.ts`, `switchTenantCommand`)
// can import everything from one place.
export {
  runDeviceCodeSignIn,
  mapServerErrorMessage,
  type PcaLike,
  type SignInDependencies,
  type SignInOutcome,
  type SignInResult,
} from "./signInCore";

/**
 * `ato.signIn` command body. Builds the real dependencies and delegates to
 * the pure orchestrator.
 */
export async function signInCommand(
  context: vscode.ExtensionContext,
  statusBar: SignInStatusBar,
  outputChannel?: vscode.OutputChannel,
): Promise<SignInResult> {
  const log = (msg: string) => outputChannel?.appendLine(msg);
  const serverBaseUrl = readServerBaseUrl();

  return runDeviceCodeSignIn({
    context,
    fetchLoginConfig: () => fetchLoginConfig(serverBaseUrl),
    fetchActiveTenant: (token) => fetchActiveTenant(token, serverBaseUrl),
    createPca: (config, msalLog) =>
      new PublicClientApplication(buildMsalNodeConfig(config, { debug: msalLog })),
    showInfoMessage: (message, ...actions) =>
      vscode.window.showInformationMessage(message, ...actions),
    showErrorMessage: (message) => vscode.window.showErrorMessage(message),
    openExternal: (uri) => vscode.env.openExternal(vscode.Uri.parse(uri)),
    writeClipboard: (text) => vscode.env.clipboard.writeText(text),
    updateStatusBar: (state) => statusBar.update(state),
    log,
  });
}

export function readServerBaseUrl(): string {
  return (
    vscode.workspace
      .getConfiguration("ato-copilot")
      .get<string>("apiUrl", "http://localhost:3001") ?? "http://localhost:3001"
  );
}

/**
 * Fetch `/api/auth/login-config` and translate it into the seam-friendly
 * `VsCodeLoginConfig`. The server envelope is
 * `{ status, data: { msal, cloud, ... } }` per contracts/http-api.md § 1.4;
 * we tolerate both the wrapped and unwrapped shape so tests can mock either.
 */
export async function fetchLoginConfig(
  serverBaseUrl: string,
): Promise<VsCodeLoginConfig> {
  const res = await axios.get(`${serverBaseUrl}/api/auth/login-config`);
  const data = res.data?.data ?? res.data;
  return {
    clientId: data.msal.clientId,
    authority: data.msal.authority,
    // The login-config response does not currently expose scopes; we default
    // to the same shared constant the dashboard uses (msalInstance.ts
    // DEFAULT_API_SCOPES) so the two surfaces request identical audiences.
    scopes: data.scopes ?? ["api://ato-copilot/.default"],
    serverBaseUrl,
    cloud: data.cloud,
  };
}

/**
 * Resolve the active tenant id + display name from `GET /api/auth/me` using
 * the freshly-acquired bearer token.
 */
export async function fetchActiveTenant(
  accessToken: string,
  serverBaseUrl: string,
): Promise<{ id: string; displayName: string }> {
  const res = await axios.get(`${serverBaseUrl}/api/auth/me`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  const me = res.data?.data ?? res.data;
  const tenant = me.effectiveTenant ?? me.homeTenant;
  if (!tenant?.id) {
    throw new Error("Could not resolve active tenant from /api/auth/me.");
  }
  return { id: tenant.id, displayName: tenant.displayName ?? tenant.id };
}
