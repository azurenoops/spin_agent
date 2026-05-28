/**
 * Feature 051 T114 — M365 Teams bot identity store (FR-022).
 *
 * Per contracts/m365-bot.md § 3.2 / § 4: the identity link is keyed by
 * `(teamsTenantId, teamsUserId)` so a user who is a guest in two Teams
 * tenants gets a fresh record per tenant. One sign-in satisfies all
 * clients (mobile, desktop, web) of the same Teams user in the same
 * Teams tenant.
 *
 * Production deployments should back the store with the C# Feature 013
 * `IConversationStateManager` (via the MCP server's HTTP API) so identity
 * records survive bot restarts. The Express bot ships with the in-memory
 * `InMemoryIdentityStore` for dev / tests. A TS port / adapter for
 * `IConversationStateManager` is a follow-up — see the TODO below.
 */

/**
 * A cached Bot Framework SSO token paired with the OID resolved during
 * OAuth consent. The OID lets the bot answer "who am I signed in as?"
 * even when the Teams tenant differs from the user's Entra home tenant.
 */
export interface IdentityRecord {
  teamsTenantId: string;
  teamsUserId: string;
  oid: string;
  accessToken: string;
  expiresAt: Date;
}

/**
 * Raw shape of a Bot Framework `getUserToken` response we accept on
 * `persist`. Bot Framework returns `expiration` as an ISO string.
 */
export interface SsoTokenResponse {
  token: string;
  expiration: string;
  // Optional — only present on certain Teams SSO flows. Falls back to
  // an empty string if the token-decode step is not wired.
  oid?: string;
}

export interface IIdentityStore {
  get(teamsTenantId: string, teamsUserId: string): Promise<IdentityRecord | null>;
  persist(
    teamsTenantId: string,
    teamsUserId: string,
    tokenResponse: SsoTokenResponse,
  ): Promise<void>;
  delete(teamsTenantId: string, teamsUserId: string): Promise<void>;
}

/**
 * In-memory `Map`-backed identity store. Suitable for dev and unit
 * tests. NOT suitable for production: records are lost on bot restart
 * and not shared across bot replicas.
 */
export class InMemoryIdentityStore implements IIdentityStore {
  private readonly store = new Map<string, IdentityRecord>();

  async get(teamsTenantId: string, teamsUserId: string): Promise<IdentityRecord | null> {
    return this.store.get(this.key(teamsTenantId, teamsUserId)) ?? null;
  }

  async persist(
    teamsTenantId: string,
    teamsUserId: string,
    tokenResponse: SsoTokenResponse,
  ): Promise<void> {
    const record: IdentityRecord = {
      teamsTenantId,
      teamsUserId,
      oid: tokenResponse.oid ?? "",
      accessToken: tokenResponse.token,
      expiresAt: this.parseExpiration(tokenResponse.expiration),
    };
    this.store.set(this.key(teamsTenantId, teamsUserId), record);
  }

  async delete(teamsTenantId: string, teamsUserId: string): Promise<void> {
    this.store.delete(this.key(teamsTenantId, teamsUserId));
  }

  /** Test helper — clears the entire store. */
  clear(): void {
    this.store.clear();
  }

  private key(teamsTenantId: string, teamsUserId: string): string {
    return `${teamsTenantId}::${teamsUserId}`;
  }

  private parseExpiration(expiration: string): Date {
    const parsed = new Date(expiration);
    // Bot Framework occasionally returns invalid dates on `null` token
    // responses. Treat unparseable values as already-expired so the next
    // resolveToken() retries SSO.
    return Number.isNaN(parsed.getTime()) ? new Date(0) : parsed;
  }
}

// TODO(Feature 051 follow-up): provide a `ConversationStateIdentityStore`
// adapter that delegates to the C# `IConversationStateManager`
// (Ato.Copilot.State) via the MCP server's HTTP API for shared, durable
// identity storage in multi-replica deployments. The in-memory store
// above is the only implementation shipped in Phase 9.
