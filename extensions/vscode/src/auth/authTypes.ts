// Feature 051 — VS Code Device-Code Sign-In shared types.
// Pure type module — no runtime imports. Safe to load from plain Node mocha.

export type StatusBarStateKind =
  | "signedOut"
  | "signingIn"
  | "signedIn"
  | "error";

export interface StatusBarState {
  state: StatusBarStateKind;
  displayName?: string;
  tenant?: string;
  lastError?: string;
}

export interface StatusBarVisual {
  /** `$(icon-name) Text` per contracts/vscode-extension.md § 4. */
  text: string;
  /** Hover tooltip. */
  tooltip: string;
  /** Command id to run on click, or `undefined` for disabled. */
  command: string | undefined;
}
