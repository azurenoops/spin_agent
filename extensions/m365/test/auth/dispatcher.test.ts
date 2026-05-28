import { expect } from "chai";
import { AuthDispatcher, type TeamsTurnContextLike, type GetUserTokenFn } from "../../src/auth/dispatcher";
import { InMemoryIdentityStore, type SsoTokenResponse } from "../../src/auth/identityStore";

/**
 * Feature 051 T115 — AuthDispatcher contract per
 * `specs/051-login/contracts/m365-bot.md § 3.1`.
 *
 * Verifies the three-mode branching (Required / Optional / Disabled),
 * the cache-first lookup, and the expiration window. Test framework is
 * Mocha + Chai to match the rest of `extensions/m365/test/`.
 */
describe("AuthDispatcher (FR-021 / FR-022 — contracts/m365-bot.md § 3.1)", () => {
  const TEAMS_TENANT = "tenant-aaa";
  const TEAMS_USER = "user-111";
  const CONNECTION_NAME = "Entra-Government";

  function ctx(): TeamsTurnContextLike {
    return { teamsTenantId: TEAMS_TENANT, teamsUserId: TEAMS_USER };
  }

  function farFutureExpiration(): string {
    return new Date(Date.now() + 30 * 60_000).toISOString();
  }

  function makeStubGetUserToken(behavior: "token" | "null" | "throws"): {
    fn: GetUserTokenFn;
    callCount: () => number;
  } {
    let calls = 0;
    const fn: GetUserTokenFn = async () => {
      calls += 1;
      if (behavior === "throws") {
        throw new Error("Bot Framework SSO failed");
      }
      if (behavior === "null") return null;
      return { token: "fresh-sso-token", expiration: farFutureExpiration() };
    };
    return { fn, callCount: () => calls };
  }

  describe("Mode = Disabled", () => {
    it("never calls SSO and returns null when cache miss", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Disabled",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal(null);
      expect(sso.callCount()).to.equal(0);
    });

    it("still consults the identity store first (cache hit returns token)", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      await store.persist(TEAMS_TENANT, TEAMS_USER, {
        token: "cached-token",
        expiration: farFutureExpiration(),
      });
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Disabled",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal("cached-token");
      expect(sso.callCount()).to.equal(0);
    });
  });

  describe("Mode = Optional", () => {
    it("returns cached token without invoking SSO", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      await store.persist(TEAMS_TENANT, TEAMS_USER, {
        token: "cached-token",
        expiration: farFutureExpiration(),
      });
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal("cached-token");
      expect(sso.callCount()).to.equal(0);
    });

    it("invokes SSO on cache miss and persists the token", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal("fresh-sso-token");
      expect(sso.callCount()).to.equal(1);
      const persisted = await store.get(TEAMS_TENANT, TEAMS_USER);
      expect(persisted).to.not.equal(null);
      expect(persisted!.accessToken).to.equal("fresh-sso-token");
    });

    it("returns null when SSO returns no token (caller will run OAuthPrompt fallback)", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      const sso = makeStubGetUserToken("null");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal(null);
      expect(sso.callCount()).to.equal(1);
      const persisted = await store.get(TEAMS_TENANT, TEAMS_USER);
      expect(persisted).to.equal(null);
    });

    it("swallows SSO errors and returns null (falls back to OAuthPrompt)", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      const sso = makeStubGetUserToken("throws");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal(null);
      expect(sso.callCount()).to.equal(1);
    });
  });

  describe("Mode = Required", () => {
    it("returns cached token without invoking SSO", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      await store.persist(TEAMS_TENANT, TEAMS_USER, {
        token: "cached-token",
        expiration: farFutureExpiration(),
      });
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Required",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal("cached-token");
      expect(sso.callCount()).to.equal(0);
    });

    it("re-throws when SSO throws (unreachable case in correctly-validated deployment)", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      const sso = makeStubGetUserToken("throws");
      const dispatcher = new AuthDispatcher({
        mode: "Required",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act / Assert
      let caught: unknown = null;
      try {
        await dispatcher.resolveToken(ctx());
      } catch (err) {
        caught = err;
      }
      expect(caught).to.be.instanceOf(Error);
      expect((caught as Error).message).to.equal("Bot Framework SSO failed");
    });
  });

  describe("Expiration window (60s refresh-ahead)", () => {
    it("treats a record expiring within 60s as missing and re-attempts SSO", async () => {
      // Arrange — cached token expires in 30s, inside the 60s refresh window.
      const store = new InMemoryIdentityStore();
      await store.persist(TEAMS_TENANT, TEAMS_USER, {
        token: "stale-token",
        expiration: new Date(Date.now() + 30_000).toISOString(),
      });
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal("fresh-sso-token");
      expect(sso.callCount()).to.equal(1);
    });

    it("uses cached token when expiration is well beyond the 60s window", async () => {
      // Arrange
      const store = new InMemoryIdentityStore();
      await store.persist(TEAMS_TENANT, TEAMS_USER, {
        token: "good-token",
        expiration: new Date(Date.now() + 5 * 60_000).toISOString(),
      });
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken(ctx());

      // Assert
      expect(token).to.equal("good-token");
      expect(sso.callCount()).to.equal(0);
    });
  });

  describe("Missing teamsTenantId", () => {
    it("skips identity-store lookup but still attempts SSO under Optional", async () => {
      // Arrange — guest scenarios may lack channelData.tenant.id
      const store = new InMemoryIdentityStore();
      const sso = makeStubGetUserToken("token");
      const dispatcher = new AuthDispatcher({
        mode: "Optional",
        connectionName: CONNECTION_NAME,
        identityStore: store,
        getUserToken: sso.fn,
      });

      // Act
      const token = await dispatcher.resolveToken({
        teamsTenantId: undefined,
        teamsUserId: TEAMS_USER,
      });

      // Assert
      expect(token).to.equal("fresh-sso-token");
      expect(sso.callCount()).to.equal(1);
    });
  });
});
