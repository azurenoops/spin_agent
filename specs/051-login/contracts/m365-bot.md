# Phase 1 — M365 Teams Bot Contract: SSO + OAuthPrompt

**Feature**: 051-login
**Plan**: [../plan.md](../plan.md)
**Spec**: [../spec.md](../spec.md) (US6, FR-021 — FR-022)
**Research**: [../research.md](../research.md) (§ R4, § R12)
**Date**: 2026-05-28

This document pins the Teams bot's auth contract — when to attempt Bot
Framework SSO, when to fall back to `OAuthPrompt`, and how the manifest
constrains the choice. All code lives under `extensions/m365/src/auth/`.

## 1. The three-mode contract (Q1)

`Auth:TeamsSso:Mode` is deployment-wide and takes one of three values:

| Mode | Behavior on first user `@mention` from unlinked Teams account |
|---|---|
| `Required` | MUST use Bot Framework SSO via `BotFrameworkAdapter.getUserToken`. If the manifest does not advertise SSO (`webApplicationInfo.id` is missing), startup MUST FAIL via `IValidateOptions<AuthOptions>` (per [research.md § R12](../research.md)) — runtime is never reached. |
| `Optional` (default) | Attempts SSO once via `getUserToken`. On `null` token (no SSO consent), falls back to `OAuthPrompt`. |
| `Disabled` | Never attempts SSO. Always runs `OAuthPrompt`. |

The fallback path renders an Adaptive Card with a sign-in button that
opens the OAuth consent URL in the user's browser (Teams handles the
return via the bot's configured `connectionName`).

## 2. Manifest constraint (FR-021)

When `Mode = Required`, the deployed Teams manifest MUST include:

```jsonc
{
  "webApplicationInfo": {
    "id": "<entra-client-id>",
    "resource": "api://<bot-domain>/<entra-client-id>"
  }
}
```

The `IValidateOptions<AuthOptions>` validator reads the local manifest
file at startup (path: `extensions/m365/appPackage/manifest.json` in dev,
or the unzipped published package in prod) and fails fast if
`webApplicationInfo.id` is empty.

## 3. Code shape

### 3.1 Auth dispatcher

```ts
// extensions/m365/src/auth/dispatcher.ts
import type { TurnContext } from 'botbuilder';
import type { OAuthPrompt } from 'botbuilder-dialogs';

export interface AuthDispatcherDeps {
  mode: 'Required' | 'Optional' | 'Disabled';
  connectionName: string;
  oauthPrompt: OAuthPrompt;
  identityStore: IIdentityStore;
}

export class AuthDispatcher {
  constructor(private readonly deps: AuthDispatcherDeps) {}

  /**
   * Resolve a usable token for the current turn. Returns the token, or
   * null if the bot needs the user to complete OAuthPrompt (caller will
   * start the prompt dialog).
   */
  async resolveToken(turnContext: TurnContext): Promise<string | null> {
    // 1. Check local identity store (per-Teams-tenant cache from FR-022).
    const teamsTenantId = turnContext.activity.channelData?.tenant?.id;
    if (teamsTenantId) {
      const cached = await this.deps.identityStore.get(teamsTenantId, turnContext.activity.from.id);
      if (cached && !this.isExpired(cached)) return cached.accessToken;
    }

    if (this.deps.mode === 'Disabled') return null;     // -> OAuthPrompt

    // 2. Bot Framework SSO
    try {
      const adapter = turnContext.adapter as any;
      const tokenResponse = await adapter.getUserToken(
        turnContext,
        this.deps.connectionName,
        undefined,                                      // no magic code
      );
      if (tokenResponse?.token) {
        await this.deps.identityStore.persist(teamsTenantId!, turnContext.activity.from.id, tokenResponse);
        return tokenResponse.token;
      }
    } catch (err) {
      if (this.deps.mode === 'Required') {
        // Required + SSO failed — surface a clear error; this is rare
        // because the validator caught the misconfig case.
        throw err;
      }
      // Optional — fall through to OAuthPrompt
    }

    return null;                                        // -> OAuthPrompt
  }

  private isExpired(record: IdentityRecord): boolean {
    return record.expiresAt.getTime() - Date.now() < 60_000;  // refresh 60s before exp
  }
}
```

### 3.2 Identity store (FR-022)

```ts
// extensions/m365/src/auth/identityStore.ts
export interface IdentityRecord {
  teamsTenantId: string;
  teamsUserId: string;
  oid: string;
  accessToken: string;
  expiresAt: Date;
}

export interface IIdentityStore {
  get(teamsTenantId: string, teamsUserId: string): Promise<IdentityRecord | null>;
  persist(teamsTenantId: string, teamsUserId: string, tokenResponse: { token: string; expiration: string; }): Promise<void>;
  delete(teamsTenantId: string, teamsUserId: string): Promise<void>;
}
```

In production the store is backed by `IConversationStateManager` from
`Ato.Copilot.State` (per the existing Feature 013 pattern). In dev it is
backed by an in-process `Map`.

### 3.3 OAuthPrompt fallback card

When `resolveToken` returns `null`, the dialog stack runs the existing
`OAuthPrompt` with the configured `connectionName`. The Adaptive Card
shape:

```jsonc
{
  "type": "AdaptiveCard",
  "version": "1.5",
  "body": [
    { "type": "TextBlock", "text": "Sign in to Security Posture Intelligence Navigator", "weight": "Bolder", "size": "Medium" },
    { "type": "TextBlock", "text": "Connect your Microsoft account so I can answer questions about your ATO packages.", "wrap": true }
  ],
  "actions": [
    {
      "type": "Action.Submit",
      "title": "Sign In",
      "data": { "msteams": { "type": "signin", "value": "<oauth-url>" } }
    }
  ]
}
```

## 4. Multi-tenant identity link (FR-022)

- The identity store key is `(teamsTenantId, teamsUserId)`. A user who
  switches Teams tenants (e.g., guests in two organizations) gets a
  fresh row per Teams tenant.
- The bot's OAuth consent flow may bind to a different Entra `tid` than
  the user's Teams home tenant; the identity store records the resulting
  `oid` so the bot can show "You're signed in as X (Tenant Y)" on demand.
- On any `LoginFailure` from the MCP server (the bot calls the MCP
  server via the same `/api/*` endpoints), the bot clears the identity
  record and re-prompts.

## 5. Sign-out

The Teams bot does NOT call `POST /api/auth/signout` directly (that
endpoint is dashboard-only per R-Summary item 1). Sign-out is via:

1. User says `sign out` (intent matcher).
2. Bot calls `adapter.signOutUser(turnContext, connectionName, userId)`.
3. Bot calls `identityStore.delete(teamsTenantId, teamsUserId)`.
4. Bot emits an Adaptive Card confirming sign-out.

The MCP server's audit row in this case comes from the next MCP call
returning `401 UNAUTHORIZED` and the bot retrying through the auth
dispatcher; the dispatcher does NOT emit its own server-side audit.

## 6. Cross-reference matrix

| FR | Contract section |
|---|---|
| FR-021 | § 1, § 2, § 3.1 |
| FR-022 | § 3.2, § 4 |
| Q1 | § 1 (the three-mode table) |
