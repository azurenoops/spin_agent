// Feature 051 — VS Code `ato.switchTenant` — pure orchestration core (T108)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.4
//
// Pure module — no `vscode` imports. Safe to unit-test from plain Node mocha.
// `switchTenantCommand.ts` is the thin VS Code wrapper that binds the seam.
// Mirrors the signInCore.ts / signInCommand.ts split pattern.

import type { VsCodeLoginConfig } from "./msalNode";
import type { SecretStorageContext } from "./secretStorage";
import type { SignInDependencies, SignInResult } from "./signInCore";

const ADD_TENANT_ITEM_ID = "__add_tenant__";

// ─── DI seam types ──────────────────────────────────────────────────────────

/** Single QuickPick item shape. */
export interface TenantPickItem {
  label: string;
  description?: string;
  detail?: string;
}

/**
 * Injectable dependencies for `runSwitchTenant`. VS Code APIs are never
 * referenced here — the production wrapper in `switchTenantCommand.ts`
 * resolves them from the live extension context.
 */
export interface SwitchTenantDependencies {
  context: SecretStorageContext;
  listKnownTenants: () => Promise<string[]>;
  getActiveTenantId: (ctx: SecretStorageContext) => string | undefined;
  showQuickPick: (items: TenantPickItem[]) => Promise<TenantPickItem | undefined>;
  showInputBox: () => Promise<string | undefined>;
  fetchLoginConfig: () => Promise<VsCodeLoginConfig>;
  runSignIn: (
    deps: SignInDependencies,
  ) => Promise<SignInResult | { outcome: "cancelled" }>;
  buildSignInDeps: (
    overrideFetchLoginConfig?: SignInDependencies["fetchLoginConfig"],
    preferTenantId?: string,
  ) => SignInDependencies;
}

// ─── Core orchestration ─────────────────────────────────────────────────────

/**
 * Pure orchestration logic for the tenant-switch flow. VS Code-free; suitable
 * for direct unit testing (T108 acceptance). Called by `switchTenantCommand`.
 */
export async function runSwitchTenant(
  deps: SwitchTenantDependencies,
): Promise<SignInResult | { outcome: "cancelled" }> {
  const known = await deps.listKnownTenants();
  const active = deps.getActiveTenantId(deps.context);

  const items: TenantPickItem[] = known.map((id) => ({
    label: shortTenantLabel(id),
    description: id === active ? "(active)" : undefined,
    detail: id,
  }));
  items.push({
    label: "$(add) Sign in to another tenant…",
    detail: ADD_TENANT_ITEM_ID,
  });

  const picked = await deps.showQuickPick(items);
  if (!picked) {
    return { outcome: "cancelled" };
  }

  // --- Switch to a previously-cached tenant ---
  if (picked.detail && picked.detail !== ADD_TENANT_ITEM_ID) {
    const targetTenantId = picked.detail;
    return deps.runSignIn(deps.buildSignInDeps(undefined, targetTenantId));
  }

  // --- "Sign in to another tenant…" — prompt for the new tenant id ---
  const newTenantId = await deps.showInputBox();
  if (!newTenantId) {
    return { outcome: "cancelled" };
  }
  const trimmed = newTenantId.trim();

  // Rewrite the authority fetched from the server to the chosen tenant.
  // The cloud value comes from the server (single source of truth) — the
  // validator in `runDeviceCodeSignIn` checks the device-code URL against it.
  const overrideFetch: SignInDependencies["fetchLoginConfig"] = async () => {
    const cfg = await deps.fetchLoginConfig();
    return {
      ...cfg,
      authority: rewriteAuthorityTenant(cfg.authority, trimmed),
    };
  };

  // Intentionally no preferTenantId — fresh sign-in to a NEW tenant must
  // NOT short-circuit via silent renewal (FR-019).
  return deps.runSignIn(deps.buildSignInDeps(overrideFetch, undefined));
}

// ─── Utilities ───────────────────────────────────────────────────────────────

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
