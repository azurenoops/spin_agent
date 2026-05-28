/**
 * Feature 051 T116 — M365 Teams bot auth dispatcher per
 * `specs/051-login/contracts/m365-bot.md § 3.1` and `research.md § R4`.
 *
 * Three-mode contract (driven by `Auth:TeamsSso:Mode` from the MCP
 * server's `AuthOptions`):
 *
 * - **Required** — MUST use Bot Framework SSO via the injected
 *   `getUserToken` callback. The matching server-side
 *   `AuthOptionsValidator` (T113) already failed startup if the Teams
 *   manifest does not advertise SSO, so the "SSO throws" branch below is
 *   defense-in-depth.
 * - **Optional** (default) — Attempts SSO once. Returns `null` on no
 *   token / SSO error so the caller renders the OAuthPrompt sign-in
 *   Adaptive Card (contracts/m365-bot.md § 3.3).
 * - **Disabled** — Never attempts SSO. Always returns `null` (caller
 *   runs OAuthPrompt).
 *
 * The dispatcher is intentionally framework-agnostic: it accepts a
 * lightweight `TeamsTurnContextLike` shape rather than a Bot Framework
 * `TurnContext`. The bot wires this up by extracting
 * `channelData.tenant.id` + `from.id` from the inbound Teams activity.
 */

import type { IdentityRecord, IIdentityStore, SsoTokenResponse } from "./identityStore";

/** Subset of a Bot Framework activity the dispatcher needs. */
export interface TeamsTurnContextLike {
  /** From `activity.channelData.tenant.id`. May be undefined for guest contexts. */
  teamsTenantId: string | undefined;
  /** From `activity.from.id`. Required. */
  teamsUserId: string;
}

/**
 * Pluggable SSO call. In Bot Framework deployments this wraps
 * `BotFrameworkAdapter.getUserToken(turnContext, connectionName)`. In
 * the Express-only deployment it may be a no-op that returns `null`
 * (forcing the OAuthPrompt fallback path).
 *
 * Returns `null` when no SSO consent exists for the user (Bot Framework
 * native behavior) or throws on hard failures.
 */
export type GetUserTokenFn = (
  ctx: TeamsTurnContextLike,
  connectionName: string,
) => Promise<SsoTokenResponse | null>;

export type TeamsSsoMode = "Required" | "Optional" | "Disabled";

export interface AuthDispatcherDeps {
  mode: TeamsSsoMode;
  connectionName: string;
  identityStore: IIdentityStore;
  getUserToken: GetUserTokenFn;
}

/**
 * Refresh tokens whose expiration is within this window (60s) on the
 * next `resolveToken` call. Matches contracts/m365-bot.md § 3.1.
 */
const REFRESH_AHEAD_MS = 60_000;

export class AuthDispatcher {
  constructor(private readonly deps: AuthDispatcherDeps) {}

  /**
   * Resolve a usable Bot Framework SSO token for the current turn.
   * Returns the token string, or `null` if the caller must render the
   * OAuthPrompt sign-in Adaptive Card.
   *
   * Per contract § 3.1:
   *
   * 1. Check identity store first (multi-tenant cache, FR-022).
   * 2. If `Mode = Disabled`, return null (skip SSO entirely).
   * 3. Otherwise call `getUserToken`. On a token, persist + return.
   * 4. On `Mode = Required` + SSO throws, re-throw so the host surfaces
   *    the misconfiguration. On `Mode = Optional` + any SSO failure
   *    (throw OR null result), fall through to `null` so the caller
   *    renders OAuthPrompt.
   */
  async resolveToken(ctx: TeamsTurnContextLike): Promise<string | null> {
    // 1. Cache lookup (only when we have a tenant key — guests may not).
    if (ctx.teamsTenantId) {
      const cached = await this.deps.identityStore.get(ctx.teamsTenantId, ctx.teamsUserId);
      if (cached && !this.isExpired(cached)) {
        return cached.accessToken;
      }
    }

    // 2. Mode = Disabled — never attempt SSO.
    if (this.deps.mode === "Disabled") {
      return null;
    }

    // 3. Bot Framework SSO.
    try {
      const tokenResponse = await this.deps.getUserToken(ctx, this.deps.connectionName);
      if (tokenResponse?.token) {
        if (ctx.teamsTenantId) {
          await this.deps.identityStore.persist(
            ctx.teamsTenantId,
            ctx.teamsUserId,
            tokenResponse,
          );
        }
        return tokenResponse.token;
      }
    } catch (err) {
      if (this.deps.mode === "Required") {
        // Defense-in-depth — the matching AuthOptionsValidator (T113)
        // should have failed startup before this branch is reachable.
        throw err;
      }
      // Optional — swallow and fall through to OAuthPrompt fallback.
    }

    return null;
  }

  private isExpired(record: IdentityRecord): boolean {
    return record.expiresAt.getTime() - Date.now() < REFRESH_AHEAD_MS;
  }
}
