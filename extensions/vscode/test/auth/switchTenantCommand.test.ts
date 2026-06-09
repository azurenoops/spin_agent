// Feature 051 — VS Code `ato.switchTenant` command tests (T108 RED → GREEN)
// Contract: specs/051-login/contracts/vscode-extension.md § 3.4
//
// Tests the pure `runSwitchTenant(deps)` orchestrator in `switchTenantCore.ts`
// via its DI seam — no VS Code Extension Host required.
// Mirrors the pattern from `signInCommand.test.ts` (T104) which tests
// `runDeviceCodeSignIn` from `signInCore.ts` directly.
//
// Behaviours pinned:
//   1. QuickPick dismissed  → { outcome: "cancelled" }; runSignIn not called.
//   2. Known tenant selected → runSignIn called with preferTenantId set.
//   3. Active tenant marked "(active)" in the picker list.
//   4. Inactive tenant NOT marked "(active)".
//   5. "Add another tenant…" → InputBox dismissed → { outcome: "cancelled" }.
//   6. "Add another tenant…" → tenant id entered → runSignIn called without
//      preferTenantId; override fetchLoginConfig rewrites the authority.
// Additionally covers `rewriteAuthorityTenant` (pure function, no stubs).

import { expect } from "chai";
import * as sinon from "sinon";

import {
  runSwitchTenant,
  rewriteAuthorityTenant,
  type SwitchTenantDependencies,
  type TenantPickItem,
} from "../../src/auth/switchTenantCore";
import type { SignInDependencies, SignInResult } from "../../src/auth/signInCore";
import type { SecretStorageContext } from "../../src/auth/secretStorage";
import type { VsCodeLoginConfig } from "../../src/auth/msalNode";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const TENANT_A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const TENANT_B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const NEW_TENANT = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const SERVER_URL = "http://localhost:5000";

function makeContext(): SecretStorageContext {
  const secretsMap = new Map<string, string>();
  const stateMap = new Map<string, unknown>();
  return {
    secrets: {
      get: async (k: string) => secretsMap.get(k),
      store: async (k: string, v: string) => { secretsMap.set(k, v); },
      delete: async (k: string) => { secretsMap.delete(k); },
    },
    workspaceState: {
      get: <T,>(k: string): T | undefined => stateMap.get(k) as T | undefined,
      update: async (k: string, v: unknown) => {
        if (v === undefined) stateMap.delete(k);
        else stateMap.set(k, v);
      },
    },
  };
}

function makeLoginConfig(authorityTenantId: string = TENANT_A): VsCodeLoginConfig {
  return {
    clientId: "test-client-id",
    authority: `https://login.microsoftonline.com/${authorityTenantId}`,
    scopes: ["api://ato-copilot/.default"],
    serverBaseUrl: SERVER_URL,
    cloud: "AzurePublic",
  };
}

interface HarnessOptions {
  knownTenants?: string[];
  activeTenantId?: string | undefined;
  quickPickResult?: TenantPickItem | undefined;
  inputBoxResult?: string | undefined;
  loginConfig?: VsCodeLoginConfig;
}

function makeHarness(options: HarnessOptions = {}) {
  const context = makeContext();
  const capturedSignInDeps: SignInDependencies[] = [];

  const listKnownTenants = sinon.stub().resolves(options.knownTenants ?? [TENANT_A, TENANT_B]);
  const getActiveTenantIdStub = sinon.stub().returns(options.activeTenantId ?? TENANT_A);
  const showQuickPick = sinon.stub().resolves(options.quickPickResult);
  const showInputBox = sinon.stub().resolves(options.inputBoxResult);
  const fetchLoginConfig = sinon.stub().resolves(options.loginConfig ?? makeLoginConfig());

  const runSignIn = sinon.stub().callsFake(async (deps: SignInDependencies) => {
    capturedSignInDeps.push(deps);
    return {
      outcome: "signedIn" as const,
      tenantId: deps.preferTenantId ?? NEW_TENANT,
    };
  });

  const buildSignInDeps = sinon.stub().callsFake(
    (overrideFetch?: SignInDependencies["fetchLoginConfig"], preferTenantId?: string): SignInDependencies => ({
      context,
      fetchLoginConfig: overrideFetch ?? fetchLoginConfig,
      fetchActiveTenant: async () => ({ id: preferTenantId ?? TENANT_A, displayName: "Test" }),
      createPca: () => ({
        acquireTokenSilent: sinon.stub().resolves(null),
        acquireTokenByDeviceCode: sinon.stub().resolves(null),
      }),
      showInfoMessage: sinon.stub().resolves(undefined),
      showErrorMessage: sinon.stub().resolves(undefined),
      openExternal: sinon.stub().resolves(true),
      writeClipboard: sinon.stub().resolves(),
      updateStatusBar: sinon.stub(),
      log: () => { /* noop */ },
      preferTenantId,
    }),
  );

  const deps: SwitchTenantDependencies = {
    context,
    listKnownTenants,
    getActiveTenantId: getActiveTenantIdStub,
    showQuickPick,
    showInputBox,
    fetchLoginConfig,
    runSignIn,
    buildSignInDeps,
  };

  return {
    deps,
    listKnownTenants,
    getActiveTenantIdStub,
    showQuickPick,
    showInputBox,
    fetchLoginConfig,
    runSignIn,
    buildSignInDeps,
    capturedSignInDeps,
  };
}

// ---------------------------------------------------------------------------
// Tests — runSwitchTenant orchestration
// ---------------------------------------------------------------------------

describe("Feature 051 — auth/switchTenantCore (T108)", () => {

  // ─── 1. QuickPick cancelled ──────────────────────────────────────────────

  it("returns { outcome: 'cancelled' } and never calls runSignIn when QuickPick is dismissed", async () => {
    const { deps, runSignIn } = makeHarness({ quickPickResult: undefined });

    const result = await runSwitchTenant(deps);

    expect(result).to.deep.equal({ outcome: "cancelled" });
    expect(runSignIn.called).to.be.false;
  });

  // ─── 2. Known tenant selected ────────────────────────────────────────────

  it("calls runSignIn with preferTenantId set when a known tenant is selected", async () => {
    const item: TenantPickItem = {
      label: `Tenant ${TENANT_B.slice(0, 8)}…`,
      detail: TENANT_B,
    };
    const { deps, runSignIn, buildSignInDeps } = makeHarness({ quickPickResult: item });

    const result = (await runSwitchTenant(deps)) as SignInResult;

    expect(result.outcome).to.equal("signedIn");
    expect(result.tenantId).to.equal(TENANT_B);
    expect(runSignIn.calledOnce).to.be.true;
    const [, preferTenantId] = buildSignInDeps.firstCall.args as [unknown, string];
    expect(preferTenantId).to.equal(TENANT_B);
  });

  // ─── 3. Active-tenant label ──────────────────────────────────────────────

  it("marks the active tenant with description '(active)' in the QuickPick items", async () => {
    const { deps, showQuickPick } = makeHarness({
      activeTenantId: TENANT_A,
      quickPickResult: undefined,
    });

    await runSwitchTenant(deps);

    const items = showQuickPick.firstCall.args[0] as TenantPickItem[];
    const activeTenantItem = items.find((i) => i.detail === TENANT_A);
    expect(activeTenantItem).to.not.be.undefined;
    expect(activeTenantItem!.description).to.equal("(active)");
  });

  it("does NOT mark inactive tenants with '(active)'", async () => {
    const { deps, showQuickPick } = makeHarness({
      activeTenantId: TENANT_A,
      quickPickResult: undefined,
    });

    await runSwitchTenant(deps);

    const items = showQuickPick.firstCall.args[0] as TenantPickItem[];
    const inactiveItem = items.find((i) => i.detail === TENANT_B);
    expect(inactiveItem).to.not.be.undefined;
    expect(inactiveItem!.description).to.be.undefined;
  });

  // ─── 4. "Add another tenant…" — InputBox cancelled ──────────────────────

  it("returns { outcome: 'cancelled' } when InputBox is dismissed on the add-tenant path", async () => {
    const addTenantItem: TenantPickItem = {
      label: "$(add) Sign in to another tenant…",
      detail: "__add_tenant__",
    };
    const { deps, runSignIn } = makeHarness({
      quickPickResult: addTenantItem,
      inputBoxResult: undefined,
    });

    const result = await runSwitchTenant(deps);

    expect(result).to.deep.equal({ outcome: "cancelled" });
    expect(runSignIn.called).to.be.false;
  });

  // ─── 5. "Add another tenant…" — new tenant id entered ───────────────────

  it("calls runSignIn WITHOUT preferTenantId when a new tenant id is entered", async () => {
    const addTenantItem: TenantPickItem = {
      label: "$(add) Sign in to another tenant…",
      detail: "__add_tenant__",
    };
    const { deps, runSignIn, buildSignInDeps } = makeHarness({
      quickPickResult: addTenantItem,
      inputBoxResult: NEW_TENANT,
    });

    await runSwitchTenant(deps);

    expect(runSignIn.calledOnce).to.be.true;
    const [overrideFetch, preferTenantId] = buildSignInDeps.firstCall.args as [
      SignInDependencies["fetchLoginConfig"] | undefined,
      string | undefined,
    ];
    // preferTenantId MUST be undefined (new tenant = fresh sign-in, no silent renewal)
    expect(preferTenantId).to.be.undefined;
    // An override fetchLoginConfig MUST be supplied to rewrite the authority
    expect(overrideFetch).to.be.a("function");
  });

  it("rewrites the authority to the new tenant id in the override fetchLoginConfig", async () => {
    const addTenantItem: TenantPickItem = {
      label: "$(add) Sign in to another tenant…",
      detail: "__add_tenant__",
    };
    const originalConfig = makeLoginConfig(TENANT_A);
    const { deps, buildSignInDeps } = makeHarness({
      quickPickResult: addTenantItem,
      inputBoxResult: NEW_TENANT,
      loginConfig: originalConfig,
    });

    await runSwitchTenant(deps);

    const [overrideFetch] = buildSignInDeps.firstCall.args as [
      SignInDependencies["fetchLoginConfig"],
    ];
    // Invoke the override and verify the authority has been rewritten.
    const cfg = await overrideFetch(originalConfig.serverBaseUrl);
    expect(cfg.authority).to.include(NEW_TENANT);
    expect(cfg.authority).to.not.include(TENANT_A);
  });
});

// ---------------------------------------------------------------------------
// rewriteAuthorityTenant — pure function; no stubs required
// ---------------------------------------------------------------------------

describe("rewriteAuthorityTenant", () => {
  const NEW_TID = "dddddddd-dddd-dddd-dddd-dddddddddddd";

  it("rewrites AzurePublic authority", () => {
    const result = rewriteAuthorityTenant(
      `https://login.microsoftonline.com/${TENANT_A}`,
      NEW_TID,
    );
    expect(result).to.equal(`https://login.microsoftonline.com/${NEW_TID}`);
  });

  it("rewrites AzureUSGovernment authority", () => {
    const result = rewriteAuthorityTenant(
      `https://login.microsoftonline.us/${TENANT_A}`,
      NEW_TID,
    );
    expect(result).to.equal(`https://login.microsoftonline.us/${NEW_TID}`);
  });

  it("handles authorities with a trailing path segment", () => {
    const result = rewriteAuthorityTenant(
      `https://login.microsoftonline.com/${TENANT_A}/v2.0`,
      NEW_TID,
    );
    expect(result).to.include(NEW_TID);
    expect(result).to.include("/v2.0");
    expect(result).to.not.include(TENANT_A);
  });

  it("handles authority with no existing tenant path segment", () => {
    const result = rewriteAuthorityTenant(
      "https://login.microsoftonline.com",
      NEW_TID,
    );
    expect(result).to.include(NEW_TID);
  });

  it("falls back gracefully for a malformed authority URL string", () => {
    const malformed = `not-a-url/${TENANT_A}`;
    const result = rewriteAuthorityTenant(malformed, NEW_TID);
    expect(result).to.include(NEW_TID);
  });
});
