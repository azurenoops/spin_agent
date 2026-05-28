// Feature 051 — VS Code Device-Code Sign-In (T106)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.3
//
// `ato.signOut` command:
//   1. Resolve active tenant id.
//   2. Best-effort `POST /api/auth/signout` with the bearer.
//   3. Delete the per-tenant token + account from SecretStorage.
//   4. Remove the MSAL token-cache account so silent renewal cannot revive
//      the deleted session.
//   5. Clear active tenant id + transition status bar to `signedOut`.
//
// Best-effort server call: if `POST /signout` fails (network, 401), we STILL
// purge local secrets so the user sees a consistent signed-out state. The
// server failure is surfaced to the OutputChannel but does not block the
// local sign-out.

import * as vscode from "vscode";
import axios from "axios";
import { PublicClientApplication } from "@azure/msal-node";

import { buildMsalNodeConfig } from "./msalNode";
import {
  deleteTenantSecretsAsync,
  getActiveTenantId,
  readAccountAsync,
  readTokenAsync,
  setActiveTenantId,
} from "./secretStorage";
import { fetchLoginConfig, readServerBaseUrl } from "./signInCommand";
import type { SignInStatusBar } from "./statusBar";

export interface SignOutResult {
  outcome: "signedOut" | "noActiveTenant" | "error";
  tenantId?: string;
  serverCallSucceeded?: boolean;
}

export async function signOutCommand(
  context: vscode.ExtensionContext,
  statusBar: SignInStatusBar,
  outputChannel?: vscode.OutputChannel,
): Promise<SignOutResult> {
  const log = (msg: string) => outputChannel?.appendLine(msg);
  const tenantId = getActiveTenantId(context);
  if (!tenantId) {
    statusBar.update({ state: "signedOut" });
    void vscode.window.showInformationMessage(
      "ATO Copilot: you are not signed in.",
    );
    return { outcome: "noActiveTenant" };
  }

  const serverBaseUrl = readServerBaseUrl();
  const token = await readTokenAsync(context, tenantId);
  const account = await readAccountAsync(context, tenantId);

  // 1) Best-effort server-side sign-out.
  let serverCallSucceeded = false;
  if (token) {
    try {
      await axios.post(
        `${serverBaseUrl}/api/auth/signout`,
        {},
        { headers: { Authorization: `Bearer ${token}` } },
      );
      serverCallSucceeded = true;
    } catch (err) {
      // Non-fatal — proceed with local cleanup.
      log(
        `[signOut] server sign-out failed (continuing with local cleanup): ${stringifyError(err)}`,
      );
    }
  }

  // 2) Remove the MSAL token-cache entry for this account (best-effort).
  if (account) {
    try {
      const config = await fetchLoginConfig(serverBaseUrl);
      const pca = new PublicClientApplication(
        buildMsalNodeConfig(config, { debug: log }),
      );
      await pca.getTokenCache().removeAccount(account);
    } catch (err) {
      log(
        `[signOut] MSAL cache removeAccount failed (continuing): ${stringifyError(err)}`,
      );
    }
  }

  // 3) Purge SecretStorage + clear active tenant id.
  await deleteTenantSecretsAsync(context, tenantId);
  await setActiveTenantId(context, undefined);

  // 4) Status bar.
  statusBar.update({ state: "signedOut" });
  void vscode.window.showInformationMessage("Signed out of ATO Copilot.");

  return { outcome: "signedOut", tenantId, serverCallSucceeded };
}

function stringifyError(err: unknown): string {
  if (err instanceof Error) return `${err.name}: ${err.message}`;
  try {
    return JSON.stringify(err);
  } catch {
    return String(err);
  }
}
