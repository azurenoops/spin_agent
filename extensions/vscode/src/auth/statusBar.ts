// Feature 051 — VS Code Device-Code Sign-In (T108)
// Contract: specs/051-login/contracts/vscode-extension.md § 4
//
// 4-state status bar for the ATO sign-in flow. The pure `renderStatusBar`
// helper is exported so tests can verify the visual contract without a
// vscode runtime; `createStatusBarItem` is the thin production wrapper.

import * as vscode from "vscode";
import {
  type StatusBarState,
  type StatusBarStateKind,
  type StatusBarVisual,
} from "./authTypes";

export {
  type StatusBarState,
  type StatusBarStateKind,
  type StatusBarVisual,
} from "./authTypes";

/**
 * Pure projection: state → visual contract. Tested in isolation.
 */
export function renderStatusBar(state: StatusBarState): StatusBarVisual {
  switch (state.state) {
    case "signedOut":
      return {
        text: "$(account) ATO: Sign In",
        tooltip: "ATO Copilot — Click to sign in",
        command: "ato.signIn",
      };
    case "signingIn":
      return {
        text: "$(sync~spin) ATO: Signing In…",
        tooltip: "Waiting for device-code grant",
        command: undefined,
      };
    case "signedIn": {
      const name = state.displayName ?? "Signed In";
      const tenant = state.tenant ?? "tenant";
      return {
        text: `$(verified) ATO: ${name}`,
        tooltip: `Signed in as ${name} (${tenant})`,
        command: "ato.signOut",
      };
    }
    case "error":
      return {
        text: "$(error) ATO: Sign-In Failed",
        tooltip: state.lastError
          ? `Last error: ${state.lastError}`
          : "Last error: unknown",
        command: "ato.signIn",
      };
    default: {
      const exhaustive: never = state.state;
      throw new Error(`Unknown status bar state: ${String(exhaustive)}`);
    }
  }
}

/**
 * Production wrapper. Owns a single `vscode.StatusBarItem` registered with
 * `context.subscriptions` and exposes `update(state)` to mutate it.
 */
export class SignInStatusBar implements vscode.Disposable {
  private readonly item: vscode.StatusBarItem;

  constructor() {
    // Align Left so the existing tenant status bar (right-aligned, priority 100
    // from `TenantStatusBar`) is unaffected.
    this.item = vscode.window.createStatusBarItem(
      vscode.StatusBarAlignment.Left,
      120,
    );
    this.update({ state: "signedOut" });
    this.item.show();
  }

  public update(state: StatusBarState): void {
    const visual = renderStatusBar(state);
    this.item.text = visual.text;
    this.item.tooltip = visual.tooltip;
    this.item.command = visual.command;
  }

  public dispose(): void {
    this.item.dispose();
  }
}

/**
 * Convenience factory used by `extension.ts` so the lifecycle is symmetric
 * with the other helpers in `src/services/`.
 */
export function createStatusBarItem(
  context: vscode.ExtensionContext,
): SignInStatusBar {
  const bar = new SignInStatusBar();
  context.subscriptions.push(bar);
  return bar;
}
