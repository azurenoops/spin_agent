// Feature 051 — VS Code Device-Code Sign-In (T104, RED → GREEN)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.2 + § 2.1
//
// Validates the pure orchestration logic of the sign-in command via the
// dependency-injection seam exposed by signInCommand.runDeviceCodeSignIn.
// PublicClientApplication is mocked; the SecretStorage stub from T103 is
// re-used so the tests confirm tokens land under the correct tenant key.

import { expect } from "chai";
import * as sinon from "sinon";
import type {
  AccountInfo,
  AuthenticationResult,
  DeviceCodeRequest,
} from "@azure/msal-node";

// `DeviceCodeResponse` is re-exported from @azure/msal-common but not from
// the msal-node entrypoint. Mirror the shape locally to avoid a transitive
// import that varies between MSAL minor versions.
interface DeviceCodeResponse {
  userCode: string;
  deviceCode: string;
  verificationUri: string;
  verificationUriComplete?: string;
  expiresIn: number;
  interval: number;
  message: string;
}

import {
  runDeviceCodeSignIn,
  type SignInDependencies,
  type SignInResult,
} from "../../src/auth/signInCore";
import type { VsCodeLoginConfig } from "../../src/auth/msalNode";
import type { SecretStorageContext } from "../../src/auth/secretStorage";
import {
  getActiveTenantId,
  readAccountAsync,
  readTokenAsync,
} from "../../src/auth/secretStorage";
import type { StatusBarState } from "../../src/auth/authTypes";

interface StubContext extends SecretStorageContext {
  secretsMap: Map<string, string>;
  stateMap: Map<string, unknown>;
}

function makeContext(): StubContext {
  const secretsMap = new Map<string, string>();
  const stateMap = new Map<string, unknown>();
  return {
    secretsMap,
    stateMap,
    secrets: {
      get: async (key: string) => secretsMap.get(key),
      store: async (key: string, value: string) => {
        secretsMap.set(key, value);
      },
      delete: async (key: string) => {
        secretsMap.delete(key);
      },
    },
    workspaceState: {
      get: <T,>(key: string): T | undefined =>
        stateMap.get(key) as T | undefined,
      update: async (key: string, value: unknown) => {
        if (value === undefined) {
          stateMap.delete(key);
        } else {
          stateMap.set(key, value);
        }
      },
    },
  };
}

const TENANT_ID = "11111111-1111-1111-1111-111111111111";

function publicLoginConfig(): VsCodeLoginConfig {
  return {
    clientId: "client-id",
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,
    scopes: ["api://ato-copilot/.default"],
    serverBaseUrl: "http://localhost:3001",
    cloud: "AzurePublic",
  };
}

function govLoginConfig(): VsCodeLoginConfig {
  return {
    clientId: "client-id",
    authority: `https://login.microsoftonline.us/${TENANT_ID}`,
    scopes: ["api://ato-copilot/.default"],
    serverBaseUrl: "http://localhost:3001",
    cloud: "AzureUSGovernment",
  };
}

function makeAccount(): AccountInfo {
  return {
    homeAccountId: `${TENANT_ID}.user`,
    environment: "login.microsoftonline.com",
    tenantId: TENANT_ID,
    username: "user@example.com",
    localAccountId: "user-local",
    name: "Test User",
  } as AccountInfo;
}

function makeAuthResult(
  account: AccountInfo,
  tenantId: string,
): AuthenticationResult {
  return {
    accessToken: "access-token-xyz",
    account: { ...account, tenantId },
    tenantId,
    scopes: ["api://ato-copilot/.default"],
    authority: `https://login.microsoftonline.com/${tenantId}`,
    uniqueId: "unique-id",
    tokenType: "Bearer",
    fromCache: false,
    expiresOn: new Date(Date.now() + 60 * 60 * 1000),
    idToken: "id-token",
    idTokenClaims: { tid: tenantId } as never,
    correlationId: "corr-id",
  } as AuthenticationResult;
}

interface DepsHarness {
  context: StubContext;
  deps: SignInDependencies;
  pcaSilentStub: sinon.SinonStub;
  pcaDeviceStub: sinon.SinonStub;
  statusBarStates: StatusBarState[];
  showInfoStub: sinon.SinonStub;
  showErrorStub: sinon.SinonStub;
  openExternalStub: sinon.SinonStub;
  writeClipboardStub: sinon.SinonStub;
  fetchLoginConfigStub: sinon.SinonStub;
  fetchActiveTenantStub: sinon.SinonStub;
}

interface HarnessOptions {
  config?: VsCodeLoginConfig;
  tenantId?: string;
  silentResult?: AuthenticationResult | null;
  silentThrows?: Error;
  deviceCodeResult?: AuthenticationResult | null;
  deviceCodeImpl?: (
    req: DeviceCodeRequest,
  ) => Promise<AuthenticationResult | null>;
  resolvedTenant?: { id: string; displayName: string };
  showInfoChoice?: string;
}

function makeHarness(options: HarnessOptions = {}): DepsHarness {
  const context = makeContext();
  const config = options.config ?? govLoginConfig();
  const tenantId = options.tenantId ?? TENANT_ID;
  const account = makeAccount();
  const resolvedTenant =
    options.resolvedTenant ?? { id: tenantId, displayName: "Test Tenant" };

  const fetchLoginConfigStub = sinon.stub().resolves(config);
  const fetchActiveTenantStub = sinon.stub().resolves(resolvedTenant);

  const pcaSilentStub = sinon.stub();
  if (options.silentThrows) {
    pcaSilentStub.rejects(options.silentThrows);
  } else {
    pcaSilentStub.resolves(options.silentResult ?? null);
  }

  const pcaDeviceStub = sinon.stub();
  if (options.deviceCodeImpl) {
    pcaDeviceStub.callsFake(options.deviceCodeImpl);
  } else if ("deviceCodeResult" in options) {
    pcaDeviceStub.resolves(options.deviceCodeResult ?? null);
  } else {
    pcaDeviceStub.resolves(makeAuthResult(account, tenantId));
  }

  const showInfoStub = sinon
    .stub()
    .resolves(options.showInfoChoice ?? undefined);
  const showErrorStub = sinon.stub().resolves(undefined);
  const openExternalStub = sinon.stub().resolves(true);
  const writeClipboardStub = sinon.stub().resolves(undefined);

  const statusBarStates: StatusBarState[] = [];

  const deps: SignInDependencies = {
    context,
    fetchLoginConfig: fetchLoginConfigStub,
    fetchActiveTenant: fetchActiveTenantStub,
    createPca: () => ({
      acquireTokenSilent: pcaSilentStub,
      acquireTokenByDeviceCode: pcaDeviceStub,
    }),
    showInfoMessage: showInfoStub as unknown as SignInDependencies["showInfoMessage"],
    showErrorMessage:
      showErrorStub as unknown as SignInDependencies["showErrorMessage"],
    openExternal: openExternalStub as unknown as SignInDependencies["openExternal"],
    writeClipboard:
      writeClipboardStub as unknown as SignInDependencies["writeClipboard"],
    updateStatusBar: (state) => {
      statusBarStates.push(state);
    },
    log: () => {
      /* swallowed */
    },
  };

  return {
    context,
    deps,
    pcaSilentStub,
    pcaDeviceStub,
    statusBarStates,
    showInfoStub,
    showErrorStub,
    openExternalStub,
    writeClipboardStub,
    fetchLoginConfigStub,
    fetchActiveTenantStub,
  };
}

describe("Feature 051 — auth/signInCommand", () => {
  describe("happy path", () => {
    it("fetches /api/auth/login-config from the server, runs device-code, persists token under the resolved tenant key, and updates the status bar to signedIn", async () => {
      // Arrange
      const harness = makeHarness();

      // Act
      const result: SignInResult = await runDeviceCodeSignIn(harness.deps);

      // Assert
      expect(result.outcome).to.equal("signedIn");
      expect(result.tenantId).to.equal(TENANT_ID);
      expect(harness.fetchLoginConfigStub.calledOnce).to.equal(true);
      expect(harness.pcaDeviceStub.calledOnce).to.equal(true);

      const deviceArg = harness.pcaDeviceStub.firstCall
        .args[0] as DeviceCodeRequest;
      expect(deviceArg.scopes).to.deep.equal(["api://ato-copilot/.default"]);

      // Token stored under per-tenant key
      expect(await readTokenAsync(harness.context, TENANT_ID)).to.equal(
        "access-token-xyz",
      );
      // Account info persisted for silent renewal next time
      expect(await readAccountAsync(harness.context, TENANT_ID)).to.not.equal(
        undefined,
      );
      // Active tenant set + status bar transitioned: signingIn → signedIn
      expect(getActiveTenantId(harness.context)).to.equal(TENANT_ID);
      const lastState =
        harness.statusBarStates[harness.statusBarStates.length - 1];
      expect(lastState.state).to.equal("signedIn");
      expect(harness.statusBarStates.map((s) => s.state)).to.include(
        "signingIn",
      );
    });

    it("shows a device-code notification with the verification URL + code", async () => {
      // Arrange
      let captured: DeviceCodeResponse | undefined;
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        captured = {
          userCode: "ABCD-1234",
          deviceCode: "device-code-secret",
          verificationUri: "https://microsoft.us/devicelogin",
          verificationUriComplete: "https://microsoft.us/devicelogin?code=ABCD-1234",
          expiresIn: 900,
          interval: 5,
          message: "ignored",
        };
        req.deviceCodeCallback(captured);
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({ deviceCodeImpl });

      // Act
      await runDeviceCodeSignIn(harness.deps);

      // Assert
      expect(captured?.userCode).to.equal("ABCD-1234");
      // showInfo is called twice: once for the device-code prompt, then
      // again for the "Signed in as ..." success notification. The first
      // call carries the device-code prompt.
      expect(harness.showInfoStub.called).to.equal(true);
      const [msg, ...actions] = harness.showInfoStub.firstCall.args as [
        string,
        ...string[],
      ];
      expect(msg).to.contain("ABCD-1234");
      expect(msg).to.contain("https://microsoft.us/devicelogin");
      expect(actions).to.include("Open Sign-In Page");
      expect(actions).to.include("Copy Code");
    });

    it("'Open Sign-In Page' action invokes openExternal", async () => {
      // Arrange
      let captured: DeviceCodeResponse | undefined;
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        captured = {
          userCode: "XYZ-9",
          deviceCode: "dev",
          verificationUri: "https://microsoft.us/devicelogin",
          expiresIn: 900,
          interval: 5,
          message: "",
        };
        req.deviceCodeCallback(captured);
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({
        deviceCodeImpl,
        showInfoChoice: "Open Sign-In Page",
      });

      // Act
      await runDeviceCodeSignIn(harness.deps);
      // The action handler is wrapped in showInfoMessage.then(...) — give
      // the microtask queue a tick to flush.
      await new Promise((r) => setImmediate(r));

      // Assert
      expect(harness.openExternalStub.calledOnce).to.equal(true);
      expect(harness.openExternalStub.firstCall.args[0]).to.equal(
        captured?.verificationUri,
      );
    });

    it("'Copy Code' action invokes writeClipboard with the user code", async () => {
      // Arrange
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        req.deviceCodeCallback({
          userCode: "PASTE-ME",
          deviceCode: "dev",
          verificationUri: "https://microsoft.us/devicelogin",
          expiresIn: 900,
          interval: 5,
          message: "",
        });
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({
        deviceCodeImpl,
        showInfoChoice: "Copy Code",
      });

      // Act
      await runDeviceCodeSignIn(harness.deps);
      await new Promise((r) => setImmediate(r));

      // Assert
      expect(harness.writeClipboardStub.calledOnce).to.equal(true);
      expect(harness.writeClipboardStub.firstCall.args[0]).to.equal("PASTE-ME");
    });
  });

  describe("FR-017 / analysis C14 — cloud URL validation", () => {
    it("accepts microsoft.com/devicelogin when cloud=AzurePublic", async () => {
      // Arrange
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        req.deviceCodeCallback({
          userCode: "ABC-123",
          deviceCode: "dev",
          verificationUri: "https://microsoft.com/devicelogin",
          expiresIn: 900,
          interval: 5,
          message: "",
        });
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({
        config: publicLoginConfig(),
        deviceCodeImpl,
      });

      // Act
      const result = await runDeviceCodeSignIn(harness.deps);

      // Assert
      expect(result.outcome).to.equal("signedIn");
      expect(await readTokenAsync(harness.context, TENANT_ID)).to.equal(
        "access-token-xyz",
      );
    });

    it("rejects microsoft.com/devicelogin when cloud=AzureUSGovernment, aborts sign-in, persists nothing", async () => {
      // Arrange
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        // Wrong-cloud URI — the validator must throw, aborting the flow.
        req.deviceCodeCallback({
          userCode: "ABC-123",
          deviceCode: "dev",
          verificationUri: "https://microsoft.com/devicelogin",
          expiresIn: 900,
          interval: 5,
          message: "",
        });
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({
        config: govLoginConfig(),
        deviceCodeImpl,
      });

      // Act
      const result = await runDeviceCodeSignIn(harness.deps);

      // Assert — outcome=error, NO token persisted, status bar reverts to signedOut.
      expect(result.outcome).to.equal("error");
      expect(await readTokenAsync(harness.context, TENANT_ID)).to.equal(
        undefined,
      );
      expect(getActiveTenantId(harness.context)).to.equal(undefined);
      const lastState =
        harness.statusBarStates[harness.statusBarStates.length - 1];
      expect(lastState.state).to.equal("signedOut");
      expect(harness.showErrorStub.calledOnce).to.equal(true);
      const errMsg = harness.showErrorStub.firstCall.args[0] as string;
      expect(errMsg.toLowerCase()).to.contain("cloud");
    });

    it("rejects microsoft.us/devicelogin when cloud=AzurePublic", async () => {
      // Arrange
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        req.deviceCodeCallback({
          userCode: "ABC-123",
          deviceCode: "dev",
          verificationUri: "https://microsoft.us/devicelogin",
          expiresIn: 900,
          interval: 5,
          message: "",
        });
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({
        config: publicLoginConfig(),
        deviceCodeImpl,
      });

      // Act
      const result = await runDeviceCodeSignIn(harness.deps);

      // Assert
      expect(result.outcome).to.equal("error");
      expect(await readTokenAsync(harness.context, TENANT_ID)).to.equal(
        undefined,
      );
    });

    it("accepts microsoft.us/devicelogin when cloud=AzureUSGovernment", async () => {
      // Arrange
      const deviceCodeImpl = async (
        req: DeviceCodeRequest,
      ): Promise<AuthenticationResult | null> => {
        req.deviceCodeCallback({
          userCode: "GOV-001",
          deviceCode: "dev",
          verificationUri: "https://microsoft.us/devicelogin",
          expiresIn: 900,
          interval: 5,
          message: "",
        });
        return makeAuthResult(makeAccount(), TENANT_ID);
      };
      const harness = makeHarness({
        config: govLoginConfig(),
        deviceCodeImpl,
      });

      // Act
      const result = await runDeviceCodeSignIn(harness.deps);

      // Assert
      expect(result.outcome).to.equal("signedIn");
      expect(await readTokenAsync(harness.context, TENANT_ID)).to.equal(
        "access-token-xyz",
      );
    });
  });

  describe("cancellation + silent-first behavior", () => {
    it("returns cancelled when acquireTokenByDeviceCode returns null and does NOT update active tenant", async () => {
      // Arrange
      const harness = makeHarness({ deviceCodeResult: null });

      // Act
      const result = await runDeviceCodeSignIn(harness.deps);

      // Assert
      expect(result.outcome).to.equal("cancelled");
      expect(await readTokenAsync(harness.context, TENANT_ID)).to.equal(
        undefined,
      );
      expect(getActiveTenantId(harness.context)).to.equal(undefined);
      // The status bar must end in signedOut, not error.
      const lastState =
        harness.statusBarStates[harness.statusBarStates.length - 1];
      expect(lastState.state).to.equal("signedOut");
    });

    it("when a known account is cached, calls acquireTokenSilent FIRST and skips device-code on success", async () => {
      // Arrange
      const context = makeContext();
      const account = makeAccount();
      // Pre-seed the SecretStorage so the silent path is attempted.
      await context.secrets.store(
        `ato.auth.account.${TENANT_ID}`,
        JSON.stringify(account),
      );
      const silentResult = makeAuthResult(account, TENANT_ID);
      const harness = makeHarness({ silentResult });
      // Replace the context (the inner makeContext call inside makeHarness)
      // with one that already has the cached account.
      harness.context.secretsMap.set(
        `ato.auth.account.${TENANT_ID}`,
        JSON.stringify(account),
      );

      // Act
      const result = await runDeviceCodeSignIn({
        ...harness.deps,
        // Hint the runner toward the cached tenant so it can pull the account.
        preferTenantId: TENANT_ID,
      });

      // Assert
      expect(result.outcome).to.equal("signedIn");
      expect(harness.pcaSilentStub.calledOnce).to.equal(true);
      expect(harness.pcaDeviceStub.called).to.equal(false);
    });
  });
});
