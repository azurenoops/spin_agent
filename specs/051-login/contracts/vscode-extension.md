# Phase 1 — VS Code Extension Contract: Device-Code Sign-In

**Feature**: 051-login
**Plan**: [../plan.md](../plan.md)
**Spec**: [../spec.md](../spec.md) (US6, FR-017 — FR-020)
**Research**: [../research.md](../research.md)
**Date**: 2026-05-28

This document pins the device-code authentication flow for the VS Code
extension. All code lives under `extensions/vscode/src/auth/`.

## 1. Dependency added

- `@azure/msal-node` 2.x — `PublicClientApplication` configured for
  device-code flow (per `Auth:VSCode:Mode = "DeviceCode"`).

The extension does NOT pull in `@azure/msal-browser` — Node-side flow
only.

## 2. MSAL Node configuration

```ts
// extensions/vscode/src/auth/msalNode.ts
import { PublicClientApplication, type Configuration } from '@azure/msal-node';

export function buildMsalNodeConfig(login: VsCodeLoginConfig): Configuration {
  return {
    auth: {
      clientId: login.clientId,
      authority: login.authority,            // e.g. https://login.microsoftonline.us/<tid>
    },
    system: {
      loggerOptions: {
        loggerCallback: (_lvl, message, _containsPii) =>
          extensionLogger.debug(`[msal] ${message}`),
        piiLoggingEnabled: false,
        logLevel: 3,                          // Verbose
      },
    },
  };
}

export interface VsCodeLoginConfig {
  clientId: string;
  authority: string;
  scopes: string[];
  serverBaseUrl: string;                      // The MCP server's base URL
  cloud: 'AzurePublic' | 'AzureUSGovernment'; // mirrors Auth:Cloud
}
```

The extension fetches `VsCodeLoginConfig` from the MCP server's
`GET /api/auth/login-config` endpoint at command-invocation time (NOT
at activation — the user may not have a server configured until they
run `@ato sign in`).

### 2.1 Cloud → device-code verification URL mapping (FR-017)

FR-017 mandates the displayed verification URL be cloud-correct.
`@azure/msal-node` emits the verification URL via the
`DeviceCodeResponse.verificationUri` field, but the extension MUST
validate it against the expected base for the configured `Auth:Cloud`
value to defend against an authority-misconfiguration that would
otherwise route a Gov-tenant user to the public endpoint:

| `cloud` value         | Expected verification URL base    |
|-----------------------|-----------------------------------|
| `AzurePublic`         | `https://microsoft.com/devicelogin` |
| `AzureUSGovernment`   | `https://microsoft.us/devicelogin`  |

The sign-in command (§ 3.2) MUST compare `response.verificationUri`
against the table above and abort with a clear error if they do not
match. Test T104 asserts this invariant for both cloud values.

## 3. Sign-in command contract

### 3.1 Command surface

| Command id | Title | Trigger |
|---|---|---|
| `ato.signIn` | "Security Posture Intelligence Navigator: Sign In" | User invokes `@ato sign in` in chat OR clicks the status-bar item. |
| `ato.signOut` | "Security Posture Intelligence Navigator: Sign Out" | User invokes `@ato sign out` OR clicks the signed-in status-bar item. |
| `ato.switchTenant` | "Security Posture Intelligence Navigator: Switch Tenant" | User invokes `@ato switch tenant`. |

### 3.2 `ato.signIn` flow

```ts
// extensions/vscode/src/auth/signInCommand.ts (sketch)
export async function signInCommand(): Promise<void> {
  const config = await fetchLoginConfig();           // GET /api/auth/login-config
  const pca = new PublicClientApplication(buildMsalNodeConfig(config));

  const deviceCodeRequest: DeviceCodeRequest = {
    scopes: config.scopes,
    deviceCodeCallback: (response) => {
      // FR-018 — display verification URI + code in a notification.
      const message =
        `To sign in to Security Posture Intelligence Navigator, visit ${response.verificationUri} ` +
        `and enter code ${response.userCode}. Code expires in ` +
        `${response.expiresIn / 60} minutes.`;
      vscode.window.showInformationMessage(message, 'Open Sign-In Page', 'Copy Code')
        .then(async (choice) => {
          if (choice === 'Open Sign-In Page')
            await vscode.env.openExternal(vscode.Uri.parse(response.verificationUri));
          else if (choice === 'Copy Code')
            await vscode.env.clipboard.writeText(response.userCode);
        });
    },
  };

  try {
    const result = await pca.acquireTokenByDeviceCode(deviceCodeRequest);
    if (!result) throw new Error('Sign-in cancelled.');

    // Resolve tenant via /api/auth/me using the new token
    const tenant = await fetchActiveTenant(result.accessToken, config.serverBaseUrl);

    // Persist per-tenant
    await context.secrets.store(secretKey(tenant.id), result.accessToken);

    updateStatusBar({ state: 'signedIn', displayName: result.account?.name, tenant: tenant.displayName });
    vscode.window.showInformationMessage(
      `Signed in to Security Posture Intelligence Navigator as ${result.account?.name} (${tenant.displayName}).`,
    );
  } catch (err) {
    handleSignInError(err);                            // FR-015 error classes
  }
}

function secretKey(tenantId: string): string {
  return `ato.auth.token.${tenantId}`;
}
```

### 3.3 `ato.signOut` flow

1. Read the currently-active tenant from `context.workspaceState.get('ato.auth.activeTenantId')`.
2. Call `POST /api/auth/signout` with the bearer.
3. Delete `context.secrets.delete(secretKey(tenantId))`.
4. `pca.getTokenCache().removeAccount(account)`.
5. Update the status bar to `{ state: 'signedOut' }`.

### 3.4 `ato.switchTenant` flow

1. Call `GET /api/auth/me` to discover the user's tenant list (via the
   home tenant's token). FR-019 requires reauth per tenant; we re-prompt
   for device code against the target tenant's authority.
2. Show a `QuickPick` of tenants.
3. On selection, re-run `acquireTokenByDeviceCode` with the target
   tenant's authority.
4. Persist the new token under the target tenant's key. The previous
   tenant's token is NOT deleted (it remains usable per FR-019).
5. Update `context.workspaceState.set('ato.auth.activeTenantId', tenantId)`
   and the status bar.

## 4. Status-bar item

| State | Text | Tooltip | Action |
|---|---|---|---|
| `signedOut` | `$(account) ATO: Sign In` | "Security Posture Intelligence Navigator — Click to sign in" | Runs `ato.signIn` |
| `signingIn` | `$(sync~spin) ATO: Signing In…` | "Waiting for device-code grant" | Disabled |
| `signedIn` | `$(verified) ATO: {displayName}` | "Signed in as {displayName} ({tenantName})" | Runs `ato.signOut` |
| `error` | `$(error) ATO: Sign-In Failed` | "Last error: {errorClass}" | Runs `ato.signIn` |

## 5. SecretStorage key naming

Tokens are stored per-tenant per FR-019:

- `ato.auth.token.{tenantId}` — access token for that tenant
- `ato.auth.account.{tenantId}` — serialized `AccountInfo` for silent
  refresh
- `ato.auth.activeTenantId` (workspaceState, not secret) — which tenant
  is "active" for chat / MCP calls

Per FR-018 + R-Summary item 1, there is NO bespoke refresh-token storage
— MSAL Node's `tokenCache` owns refresh. The extension calls
`pca.acquireTokenSilent({ account, scopes })` on every command
invocation; if it returns null OR throws `InteractionRequiredAuthError`,
the extension falls back to `acquireTokenByDeviceCode`.

## 6. Error handling (FR-015)

| Server error | VS Code action |
|---|---|
| `UNAUTHORIZED` | Re-trigger device-code flow. |
| `NO_TENANT_ASSIGNMENT` | Show error notification with support email; no auto-retry. |
| `TOO_MANY_LOGINS` (429) | Show error notification with `Retry-After`; suggest waiting; do NOT auto-retry. |
| Network failure | Show error notification with retry button. |
| `InteractionRequiredAuthError` | Re-trigger device-code flow. |

The extension MUST set `ErrorClass` in its own log entry but does NOT
need to render it to the user — the message strings above are sufficient
for VS Code surface.

## 7. Multi-tenant identity persistence (FR-019)

- Each tenant gets its own SecretStorage entry.
- `@ato switch tenant` swaps the active tenant; commands made AFTER
  the swap use the new tenant's token.
- Signing out of one tenant does NOT sign out of others.
- The status bar always reflects the currently-active tenant.

## 8. Cross-reference matrix

| FR | Contract section |
|---|---|
| FR-017 | § 4 status bar |
| FR-018 | § 2, § 3.2 |
| FR-019 | § 5, § 7 |
| FR-020 | § 6 |
