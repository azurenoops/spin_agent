// Feature 051 — VS Code Device-Code Sign-In (T105, pure core)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.2 + § 5 + § 6
//
// Pure orchestrator for the device-code sign-in flow. All side effects
// (VS Code UI, HTTP, MSAL, SecretStorage) reach in through `deps`, so this
// module compiles + runs in plain Node mocha without an Extension Host.
// `signInCommand.ts` is the thin production wrapper that binds the seam.

import {
  InteractionRequiredAuthError,
  type AccountInfo,
  type AuthenticationResult,
  type DeviceCodeRequest,
} from "@azure/msal-node";

import {
  CloudVerificationMismatchError,
  validateVerificationUri,
  type VsCodeLoginConfig,
} from "./msalNode";
import {
  getActiveTenantId,
  listKnownTenantsAsync,
  persistAccountAsync,
  persistTokenAsync,
  readAccountAsync,
  setActiveTenantId,
  type SecretStorageContext,
} from "./secretStorage";
import type { StatusBarState } from "./authTypes";

/**
 * Minimal `PublicClientApplication` surface the orchestrator depends on.
 * The real `PublicClientApplication` satisfies it natively.
 */
export interface PcaLike {
  acquireTokenSilent(req: {
    account: AccountInfo;
    scopes: string[];
  }): Promise<AuthenticationResult | null>;
  acquireTokenByDeviceCode(
    req: DeviceCodeRequest,
  ): Promise<AuthenticationResult | null>;
}

/**
 * Promise-or-Thenable shim so the seam can be satisfied by either VS Code's
 * `Thenable<T>` (production) or a plain `Promise<T>` (tests).
 */
type Awaitable<T> = T | PromiseLike<T>;

export interface SignInDependencies {
  context: SecretStorageContext;
  fetchLoginConfig: (serverBaseUrl: string) => Promise<VsCodeLoginConfig>;
  fetchActiveTenant: (
    accessToken: string,
    serverBaseUrl: string,
  ) => Promise<{ id: string; displayName: string }>;
  createPca: (
    config: VsCodeLoginConfig,
    log: (msg: string) => void,
  ) => PcaLike;
  showInfoMessage: (
    message: string,
    ...actions: string[]
  ) => Awaitable<string | undefined>;
  showErrorMessage: (message: string) => Awaitable<string | undefined>;
  openExternal: (uri: string) => Awaitable<boolean>;
  writeClipboard: (text: string) => Awaitable<void>;
  updateStatusBar: (state: StatusBarState) => void;
  log: (message: string) => void;
  /**
   * Optional hint — when set, the silent-renewal path tries to use the
   * cached `AccountInfo` for THIS tenant before falling back to device code.
   * The switch-tenant command sets it to the target tenant id.
   */
  preferTenantId?: string;
}

export type SignInOutcome = "signedIn" | "cancelled" | "error";

export interface SignInResult {
  outcome: SignInOutcome;
  tenantId?: string;
  tenantDisplayName?: string;
  accessToken?: string;
  errorMessage?: string;
}

/**
 * Run the device-code sign-in flow end-to-end against the seam dependencies.
 *
 *   1. Fetch `/api/auth/login-config` to discover MSAL + cloud config.
 *   2. Pick a candidate tenant for silent renewal:
 *      `preferTenantId` ?? `getActiveTenantId(context)` ?? first known tenant.
 *      If an `AccountInfo` is cached for it, call `acquireTokenSilent` FIRST.
 *      On success, persist + activate the tenant and return.
 *   3. On `InteractionRequiredAuthError` (or no cached account / silent null),
 *      fall back to `acquireTokenByDeviceCode`.
 *   4. Validate `DeviceCodeResponse.verificationUri` against the configured
 *      cloud (FR-017 / analysis C14). Mismatch aborts with no token persisted.
 *   5. On AcquireToken success, resolve the tenant via `GET /api/auth/me`,
 *      persist the token + account, set the active tenant id, transition the
 *      status bar to `signedIn`.
 */
export async function runDeviceCodeSignIn(
  deps: SignInDependencies,
): Promise<SignInResult> {
  deps.updateStatusBar({ state: "signingIn" });

  let config: VsCodeLoginConfig;
  try {
    config = await deps.fetchLoginConfig("");
  } catch (err) {
    return failFlow(deps, err, "Could not load ATO Copilot login config.");
  }

  const pca = deps.createPca(config, deps.log);

  // --- 1. Silent-renewal attempt (FR-018 / R-Summary item 1) ---
  const silent = await trySilentRenewal(deps, pca, config);
  if (silent.outcome === "signedIn") {
    return silent;
  }
  // If a hard error happened during silent (other than InteractionRequired),
  // surface and stop — DO NOT downgrade silently to device-code on, say, a
  // network failure that the user can fix.
  if (silent.outcome === "error") {
    return silent;
  }

  // --- 2. Device-code fallback ---
  let authResult: AuthenticationResult | null;
  let mismatchError: CloudVerificationMismatchError | undefined;
  try {
    authResult = await pca.acquireTokenByDeviceCode({
      scopes: config.scopes,
      deviceCodeCallback: (response) => {
        // FR-017 — validate the cloud URL BEFORE prompting the user.
        try {
          validateVerificationUri(response.verificationUri, config.cloud);
        } catch (e) {
          if (e instanceof CloudVerificationMismatchError) {
            mismatchError = e;
            deps.log(
              `[signIn] cloud mismatch: ${e.received} vs expected ${e.expected}`,
            );
            return;
          }
          throw e;
        }
        const minutes = Math.max(1, Math.round(response.expiresIn / 60));
        const message =
          `To sign in to ATO Copilot, visit ${response.verificationUri} ` +
          `and enter code ${response.userCode}. Code expires in ${minutes} ` +
          `minute${minutes === 1 ? "" : "s"}.`;
        // Fire-and-forget — the user action is async but we don't await it
        // to avoid blocking the device-code poll loop.
        void Promise.resolve(
          deps.showInfoMessage(message, "Open Sign-In Page", "Copy Code"),
        ).then(async (choice) => {
          if (choice === "Open Sign-In Page") {
            await deps.openExternal(response.verificationUri);
          } else if (choice === "Copy Code") {
            await deps.writeClipboard(response.userCode);
          }
        });
      },
    });
  } catch (err) {
    if (mismatchError !== undefined) {
      return failFlow(deps, mismatchError, mismatchError.message);
    }
    return failFlow(
      deps,
      err,
      mapServerErrorMessage(err) ?? "Sign-in failed.",
    );
  }

  if (mismatchError !== undefined) {
    return failFlow(deps, mismatchError, mismatchError.message);
  }

  if (!authResult || !authResult.accessToken) {
    // User cancelled — info notification, status bar reverts to signedOut.
    deps.updateStatusBar({ state: "signedOut" });
    await deps.showInfoMessage("ATO Copilot sign-in cancelled.");
    return { outcome: "cancelled" };
  }

  // --- 3. Resolve tenant + persist ---
  try {
    return await finalizeSuccess(deps, config, authResult);
  } catch (err) {
    return failFlow(
      deps,
      err,
      mapServerErrorMessage(err) ?? "Failed to resolve tenant after sign-in.",
    );
  }
}

// ---------- silent renewal helper ----------

async function trySilentRenewal(
  deps: SignInDependencies,
  pca: PcaLike,
  config: VsCodeLoginConfig,
): Promise<SignInResult> {
  const candidate =
    deps.preferTenantId ??
    getActiveTenantId(deps.context) ??
    (await listKnownTenantsAsync(deps.context))[0];

  if (!candidate) {
    return { outcome: "cancelled" };
  }

  const cached = await readAccountAsync(deps.context, candidate);
  if (!cached) {
    return { outcome: "cancelled" };
  }

  try {
    const result = await pca.acquireTokenSilent({
      account: cached,
      scopes: config.scopes,
    });
    if (!result || !result.accessToken) {
      return { outcome: "cancelled" };
    }
    return await finalizeSuccess(deps, config, result);
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      return { outcome: "cancelled" };
    }
    return failFlow(
      deps,
      err,
      mapServerErrorMessage(err) ?? "Silent token renewal failed.",
    );
  }
}

// ---------- success finalizer ----------

async function finalizeSuccess(
  deps: SignInDependencies,
  config: VsCodeLoginConfig,
  authResult: AuthenticationResult,
): Promise<SignInResult> {
  const tenant = await deps.fetchActiveTenant(
    authResult.accessToken,
    config.serverBaseUrl,
  );

  await persistTokenAsync(deps.context, tenant.id, authResult.accessToken);
  if (authResult.account) {
    await persistAccountAsync(deps.context, tenant.id, authResult.account);
  }
  await setActiveTenantId(deps.context, tenant.id);

  const displayName =
    authResult.account?.name ?? authResult.account?.username ?? "Signed In";
  deps.updateStatusBar({
    state: "signedIn",
    displayName,
    tenant: tenant.displayName,
  });
  await deps.showInfoMessage(
    `Signed in to ATO Copilot as ${displayName} (${tenant.displayName}).`,
  );

  return {
    outcome: "signedIn",
    tenantId: tenant.id,
    tenantDisplayName: tenant.displayName,
    accessToken: authResult.accessToken,
  };
}

// ---------- error helpers ----------

function failFlow(
  deps: SignInDependencies,
  err: unknown,
  message: string,
): SignInResult {
  deps.log(`[signIn] error: ${stringifyError(err)}`);
  if (err instanceof CloudVerificationMismatchError) {
    // The contract treats cloud mismatch as a hard error but the status bar
    // reverts to signedOut (NOT 'error') — the next click should re-attempt
    // sign-in cleanly without a stale red icon.
    deps.updateStatusBar({ state: "signedOut" });
  } else {
    deps.updateStatusBar({ state: "error", lastError: message });
  }
  void Promise.resolve(deps.showErrorMessage(message));
  return { outcome: "error", errorMessage: message };
}

function stringifyError(err: unknown): string {
  if (err instanceof Error) return `${err.name}: ${err.message}`;
  try {
    return JSON.stringify(err);
  } catch {
    return String(err);
  }
}

/**
 * Map well-known server error codes (FR-015) to a user-friendly string.
 * Returns `undefined` when no match — caller supplies a default.
 */
export function mapServerErrorMessage(err: unknown): string | undefined {
  const anyErr = err as {
    response?: {
      status?: number;
      data?: { code?: string; errorCode?: string; message?: string };
    };
  };
  const status = anyErr?.response?.status;
  const code =
    anyErr?.response?.data?.code ?? anyErr?.response?.data?.errorCode;

  if (code === "NO_TENANT_ASSIGNMENT") {
    return (
      "Your account is authenticated but not assigned to any ATO Copilot " +
      "tenant. Contact your administrator."
    );
  }
  if (code === "TOO_MANY_LOGINS" || status === 429) {
    return "Too many sign-in attempts. Please wait a minute and try again.";
  }
  if (status === 401) {
    return "Authentication required. Please sign in again.";
  }
  return undefined;
}
