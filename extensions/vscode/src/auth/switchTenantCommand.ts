// Feature 051 — VS Code Device-Code Sign-In (T107)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.4
//
// `ato.switchTenant` command:
//   1. Build a QuickPick of tenants the user can switch to.
//      Sources: (a) tenants we have persisted secrets for in this workspace
//      (`listKnownTenantsAsync`), and (b) an "Add another tenant…" item that
//      runs a fresh device-code flow for a new authority.
//   2. On selection of a known tenant, re-run sign-in with `preferTenantId`
//      set to the chosen tenant so silent renewal is attempted first.
//   3. On selection of "Add another tenant…", prompt for the tenant id and
//      re-run sign-in against `https://login.microsoftonline.{cloud}/<id>`.
//   4. The previously-active tenant's secrets are NOT deleted (FR-019).
//
// NOTE: The server-side `/api/auth/me` response does not enumerate a user's
// tenant memberships, so we cannot show "every tenant you could pick". The
// QuickPick is therefore scoped to (a) cached tenants + (b) manual entry,
// which is the operational reality for VS Code (device-code is a per-tenant
// grant).

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
import {
  runDeviceCodeSignIn,
  type SignInResult,
} from "./signInCore";
import type { SignInStatusBar } from "./statusBar";

const ADD_TENANT_ITEM_ID = "__add_tenant__";

export async function switchTenantCommand(
  context: vscode.ExtensionContext,
  statusBar: SignInStatusBar,
  outputChannel?: vscode.OutputChannel,
): Promise<SignInResult | { outcome: "cancelled" }> {
  const log = (msg: string) => outputChannel?.appendLine(msg);
  const known = await listKnownTenantsAsync(context);
  const active = getActiveTenantId(context);

  const items: vscode.QuickPickItem[] = known.map((id) => ({
    label: shortTenantLabel(id),
    description: id === active ? "(active)" : undefined,
    detail: id,
  }));
  items.push({
    label: "$(add) Sign in to another tenant…",
    detail: ADD_TENANT_ITEM_ID,
  });

  const picked = await vscode.window.showQuickPick(items, {
    placeHolder: "Choose an ATO Copilot tenant",
    ignoreFocusOut: true,
  });
  if (!picked) {
    return { outcome: "cancelled" };
  }

  const serverBaseUrl = readServerBaseUrl();

  // --- Switch to a previously-cached tenant ---
  if (picked.detail && picked.detail !== ADD_TENANT_ITEM_ID) {
    const targetTenantId = picked.detail;
    return runDeviceCodeSignIn({
      context,
      fetchLoginConfig: () => fetchLoginConfig(serverBaseUrl),
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
      preferTenantId: targetTenantId,
    });
  }

  // --- "Sign in to another tenant…" — prompt for the new tenant id ---
  const newTenantId = await vscode.window.showInputBox({
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
  if (!newTenantId) {
    return { outcome: "cancelled" };
  }
  const trimmed = newTenantId.trim();

  // We rewrite the authority on the way in by patching the fetched
  // login-config. The cloud value comes from the server (single source of
  // truth) — the validator in `runDeviceCodeSignIn` checks the device-code
  // URL against THAT cloud.
  return runDeviceCodeSignIn({
    context,
    fetchLoginConfig: async () => {
      const cfg = await fetchLoginConfig(serverBaseUrl);
      return {
        ...cfg,
        authority: rewriteAuthorityTenant(cfg.authority, trimmed),
      };
    },
    fetchActiveTenant: async (token) => {
      // The orchestrator's `finalizeSuccess` writes the per-tenant
      // secrets + active-tenant id, so this lambda only resolves the
      // tenant from the freshly-acquired bearer.
      return fetchActiveTenant(token, serverBaseUrl);
    },
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
    // Intentionally no preferTenantId — this path is "sign in to a NEW
    // tenant", so silent renewal must NOT short-circuit it.
  });
}

function shortTenantLabel(id: string): string {
  return id.length > 8 ? `Tenant ${id.slice(0, 8)}…` : `Tenant ${id}`;
}

/**
 * Replace the tenant segment in an authority URL. Accepts both
 * `https://login.microsoftonline.com/<tid>` and
 * `https://login.microsoftonline.us/<tid>`; leaves the cloud host alone.
 */
export function rewriteAuthorityTenant(
  authority: string,
  newTenantId: string,
): string {
  try {
    const u = new URL(authority);
    // path of the form /<tenant>/[...]
    const segments = u.pathname.split("/").filter((s) => s.length > 0);
    if (segments.length === 0) {
      u.pathname = `/${newTenantId}`;
    } else {
      segments[0] = newTenantId;
      u.pathname = `/${segments.join("/")}`;
    }
    return u.toString().replace(/\/$/, "");
  } catch {
    // Fall back to a best-effort string rewrite when authority is malformed.
    return authority.replace(/\/[0-9a-fA-F-]{32,36}/, `/${newTenantId}`);
  }
}
