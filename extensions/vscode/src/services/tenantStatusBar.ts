import * as vscode from "vscode";

/**
 * Tenant status bar — surfaces the active tenant and impersonation badge for
 * the VS Code extension. Implements feature 048 spec FR-024 / T141.
 *
 * Reads `ato-copilot.tenantId` and `ato-copilot.impersonatedTenantId` from
 * settings. When `impersonatedTenantId` is set, the badge renders as
 * `Impersonating: <short-id>` with a warning background to make the elevated
 * scope visually distinct.
 */
export class TenantStatusBar implements vscode.Disposable {
  private readonly item: vscode.StatusBarItem;
  private readonly subscriptions: vscode.Disposable[] = [];

  constructor() {
    this.item = vscode.window.createStatusBarItem(
      vscode.StatusBarAlignment.Right,
      100
    );
    this.item.command = "ato.configure";
    this.refresh();
    this.item.show();

    this.subscriptions.push(
      vscode.workspace.onDidChangeConfiguration((e) => {
        if (e.affectsConfiguration("ato-copilot")) {
          this.refresh();
        }
      })
    );
  }

  /** Re-read settings and update the status bar text/tooltip. */
  public refresh(): void {
    const config = vscode.workspace.getConfiguration("ato-copilot");
    const tenantId = (config.get<string>("tenantId") ?? "").trim();
    const impersonatedTenantId = (
      config.get<string>("impersonatedTenantId") ?? ""
    ).trim();

    if (impersonatedTenantId) {
      this.item.text = `$(eye) ATO: Impersonating ${shortId(impersonatedTenantId)}`;
      this.item.tooltip =
        `ATO Copilot — impersonating tenant ${impersonatedTenantId}\n` +
        (tenantId ? `Home tenant: ${tenantId}\n` : "") +
        "Click to configure";
      this.item.backgroundColor = new vscode.ThemeColor(
        "statusBarItem.warningBackground"
      );
    } else if (tenantId) {
      this.item.text = `$(organization) ATO: ${shortId(tenantId)}`;
      this.item.tooltip = `ATO Copilot — tenant ${tenantId}\nClick to configure`;
      this.item.backgroundColor = undefined;
    } else {
      this.item.text = "$(organization) ATO: no tenant";
      this.item.tooltip =
        "ATO Copilot — no tenant configured. Click to configure ato-copilot.tenantId.";
      this.item.backgroundColor = undefined;
    }
  }

  /**
   * Returns the current tenant scope to forward on MCP requests, suitable for
   * embedding in the request `context` block. Returns `null` when no tenant
   * is configured.
   */
  public getOutboundContext(): {
    tenantId: string;
    impersonatedTenantId?: string;
  } | null {
    const config = vscode.workspace.getConfiguration("ato-copilot");
    const tenantId = (config.get<string>("tenantId") ?? "").trim();
    const impersonatedTenantId = (
      config.get<string>("impersonatedTenantId") ?? ""
    ).trim();
    if (!tenantId) return null;
    return impersonatedTenantId
      ? { tenantId, impersonatedTenantId }
      : { tenantId };
  }

  public dispose(): void {
    for (const s of this.subscriptions) s.dispose();
    this.item.dispose();
  }
}

function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}
