import { test as base, expect, type BrowserContext, type Route } from '@playwright/test';
import {
  configurationUrl, environment, environmentPath, historicalAssessment, legacySubscriptionId,
  profileCompleteness, profileSection, readiness, readinessPath, subscriptionId,
  systemDetail, systemId,
} from '../../src/__tests__/fixtures/assessmentEnvironment';

const origin = 'http://localhost:4179';
type Handler = (route: Route) => Promise<void>;

class SyntheticApi {
  readonly handlers = new Map<string, Handler>();
  readonly unexpected: string[] = [];
  readonly writes: { method: string; path: string; body: unknown }[] = [];

  respond(path: string, data: unknown, status = 200, method = 'GET') {
    this.handlers.set(`${method} ${path}`, (route) => route.fulfill({ status, json: data }));
  }

  async install(context: BrowserContext) {
    await context.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.origin !== origin) {
        this.unexpected.push(`External request blocked: ${request.method()} ${url.href}`);
        await route.abort('blockedbyclient');
        return;
      }
      if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) {
        const method = request.method();
        if (method !== 'GET') this.writes.push({ method, path: url.pathname, body: request.postData() ? request.postDataJSON() : null });
        const handler = this.handlers.get(`${method} ${url.pathname}`);
        if (!handler) {
          this.unexpected.push(`Unmocked API blocked: ${method} ${url.pathname}`);
          await route.abort('blockedbyclient');
          return;
        }
        await handler(route);
        return;
      }
      // Only local Vite documents, modules and assets are allowed through.
      await route.continue();
    });
    await context.routeWebSocket('**/*', (socket) => {
      const url = new URL(socket.url());
      if (url.host !== new URL(origin).host) this.unexpected.push(`External websocket blocked: ${url.href}`);
      socket.close();
    });
  }
}

const test = base.extend<{ api: SyntheticApi }>({
  api: async ({ context }, use) => {
    const api = new SyntheticApi();
    await api.install(context);
    api.respond('/api/auth/login-config', {
      status: 'success',
      data: {
        branding: { deploymentName: 'Synthetic local dashboard', logoUrl: null, supportEmail: null },
        defaultMethod: 'Simulation',
        enabledMethods: [{ id: 'Simulation', displayName: 'Simulation' }],
        cloud: 'AzurePublic',
        idleTimeoutMinutes: 30,
        rememberTenantCookieDays: 7,
        simulation: { identities: [] },
        msal: {
          clientId: '00000000-0000-4000-8000-000000000001',
          authority: 'https://login.microsoftonline.com/common',
          redirectUri: `${origin}/login/callback`,
          postLogoutRedirectUri: `${origin}/login`,
        },
      },
    });
    api.respond('/api/auth/me', {
      oid: 'synthetic-writer', displayName: 'Synthetic Writer', persona: 'ISSM',
      homeTenant: { id: 'synthetic-tenant', displayName: 'Synthetic Tenant', status: 'Active' },
      effectiveTenant: { id: 'synthetic-tenant', displayName: 'Synthetic Tenant', status: 'Active' },
      isImpersonating: false, impersonation: null, pimRoles: [],
      isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [],
    });
    api.respond('/api/csp/onboarding/state', {}, 404);
    api.respond('/api/onboarding/organization-context', { ok: true, data: null });
    api.respond('/api/onboarding/state', {
      ok: true, data: { steps: [{ step: 'OrganizationContext', status: 'Completed' }, { step: 'Roles', status: 'Completed' }] },
    });
    api.respond('/api/onboarding/tenant/state', {
      status: 'success',
      data: {
        tenantId: 'synthetic-tenant',
        currentStep: 'Submitted',
        completedSteps: ['Tenant.LegalEntity', 'Tenant.HqAddress', 'Tenant.Classification', 'Tenant.Ao', 'Tenant.PrimaryPoc', 'Org.Profile', 'Submitted'],
        onboardingState: 'Active',
        firstOrganizationId: 'synthetic-organization',
      },
    });
    api.respond('/api/deployment/mode', { mode: 'SingleTenant' });
    api.respond('/api/tenants', [], 403);
    api.respond('/api/dashboard/notifications', { items: [] });
    api.respond('/api/dashboard/notifications/summary', { unreadCount: 0, totalCount: 0 });
    api.respond(`/api/dashboard/systems/${systemId}`, systemDetail);
    api.respond(`/api/dashboard/systems/${systemId}/todos`, { items: [] });
    api.respond(`/api/dashboard/systems/${systemId}/profile/completeness`, profileCompleteness);
    api.respond(`/api/dashboard/systems/${systemId}/profile/EnvironmentAndDeployment`, profileSection);
    api.respond('/api/dashboard/assessments', [historicalAssessment]);
    api.respond(`/api/v1/systems/${systemId}/sap`, { error: 'No SAP exists.' }, 404);
    api.respond(`/api/v1/systems/${systemId}/sar`, { error: 'No SAR exists.' }, 404);
    api.respond(`/api/dashboard${readinessPath()}`, readiness());
    api.respond(`/api/dashboard${environmentPath()}`, environment());
    await context.addInitScript(() => {
      localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' }));
    });
    await use(api);
    expect(api.unexpected, 'The suite must not access unmocked APIs or external resources').toEqual([]);
  },
});

test('missing environment blocks Run and Configure Environment opens the actual attachment panel', async ({ page, api }) => {
  // Arrange
  await page.goto(`/systems/${systemId}/assessments`);

  // Act
  await expect(page.getByText(readiness().message)).toBeVisible();

  // Assert
  await expect(page.getByRole('button', { name: 'Run Assessment', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Generate SAR', exact: true })).toBeEnabled();
  await expect(page.getByRole('table')).toContainText('75%');
  await expect(page.getByRole('table')).not.toContainText('Azure');
  const configure = page.getByRole('link', { name: /Configure Environment/i });
  await expect(configure).toHaveAttribute('href', configurationUrl());

  // Act
  await configure.click();

  // Assert
  await expect(page).toHaveURL(`${origin}${configurationUrl()}`);
  await expect(page.getByRole('region', { name: /Azure assessment environment/i })).toHaveAttribute('id', 'azure-assessment-environment');
  expect(api.writes).toEqual([]);
});

test('loading and normalized readiness errors fail closed, then retry permits a ready run', async ({ page, api }) => {
  // Arrange
  let release!: () => void;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  api.handlers.set(`GET /api/dashboard${readinessPath()}`, async (route) => {
    await gate;
    await route.fulfill({ status: 503, json: {
      error: 'Azure access could not be verified.', errorCode: 'AZURE_UNAVAILABLE',
      suggestion: 'Restore connectivity and retry.',
    } });
  });
  await page.goto(`/systems/${systemId}/assessments`);

  // Assert
  await expect(page.getByRole('button', { name: 'Run Assessment', exact: true })).toBeDisabled();

  // Act
  release();
  await expect(page.getByText('Azure access could not be verified.')).toBeVisible();
  api.respond(`/api/dashboard${readinessPath()}`, readiness(true));
  await page.getByRole('button', { name: /retry/i }).click();

  // Assert
  await expect(page.getByRole('button', { name: 'Run Assessment', exact: true })).toBeEnabled();
  expect(api.writes).toEqual([]);
});

test('a server-side prerequisite rejection stays actionable even after readiness succeeds', async ({ page, api }) => {
  // Arrange
  api.respond(`/api/dashboard${readinessPath()}`, readiness(true));
  api.respond(`/api/dashboard/systems/${systemId}/run-assessment`, {
    error: 'The Azure environment was detached.', errorCode: 'ASSESSMENT_ENVIRONMENT_NOT_CONFIGURED',
    suggestion: 'Attach an Azure subscription before trying again.',
  }, 409, 'POST');
  await page.goto(`/systems/${systemId}/assessments`);
  await expect(page.getByRole('button', { name: 'Run Assessment', exact: true })).toBeEnabled();

  // Act
  await page.getByRole('button', { name: 'Run Assessment', exact: true }).click();
  await page.getByRole('button', { name: 'Run Assessment', exact: true }).last().click();

  // Assert
  await expect(page.getByText('The Azure environment was detached.')).toBeVisible();
  await expect(page.getByText('Attach an Azure subscription before trying again.')).toBeVisible();
  await expect(page.getByRole('link', { name: /Configure Environment/i }).first()).toHaveAttribute('href', configurationUrl());
  expect(api.writes).toHaveLength(1);
});

test('save selects only eligible subscriptions; detach and readiness are separate operations', async ({ page, api }) => {
  // Arrange
  let attached = false;
  let checks = 0;
  api.handlers.set(`GET /api/dashboard${readinessPath()}`, (route) => {
    checks += 1;
    return route.fulfill({ json: attached
      ? { ...readiness(), message: 'Azure connectivity has not been verified.' }
      : readiness() });
  });
  api.handlers.set(`PUT /api/dashboard${environmentPath()}`, (route) => {
    attached = true;
    const saved = { ...environment(), cloudEnvironment: 'Commercial', subscriptionIds: [subscriptionId] };
    api.respond(`/api/dashboard${environmentPath()}`, saved);
    return route.fulfill({ json: saved });
  });
  api.handlers.set(`DELETE /api/dashboard${environmentPath()}`, (route) => {
    attached = false;
    api.respond(`/api/dashboard${environmentPath()}`, environment());
    return route.fulfill({ status: 204 });
  });
  page.on('dialog', (dialog) => dialog.accept());
  await page.goto(configurationUrl());
  const panel = page.getByRole('region', { name: /Azure assessment environment/i });
  await expect(panel.getByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).not.toBeChecked();
  await expect(panel.getByRole('checkbox', { name: /Synthetic Government/i })).toBeDisabled();
  await expect(panel.getByRole('checkbox', { name: /Synthetic Unavailable/i })).toBeDisabled();
  await expect(panel.getByRole('checkbox', { name: /Synthetic Unknown Cloud/i })).toBeDisabled();
  const initialChecks = checks;

  // Act
  await panel.getByRole('checkbox', { name: /Synthetic Commercial Alpha/i }).check();
  await panel.getByRole('button', { name: /save environment/i }).click();

  // Assert
  await expect(panel.getByText('Azure connectivity has not been verified.')).toBeVisible();
  expect(checks).toBeGreaterThan(initialChecks);
  expect(api.writes).toEqual([{
    method: 'PUT', path: `/api/dashboard${environmentPath()}`,
    body: { cloudEnvironment: 'Commercial', subscriptionIds: [subscriptionId] },
  }]);
  const savedChecks = checks;

  // Act
  await panel.getByRole('button', { name: /detach environment/i }).click();

  // Assert
  await expect(panel.getByText(readiness().message)).toBeVisible();
  expect(checks).toBeGreaterThan(savedChecks);
  expect(api.writes.at(-1)?.method).toBe('DELETE');
});

test('writer-forbidden configuration is actionable without an editable attachment', async ({ page, api }) => {
  // Arrange
  api.respond(`/api/dashboard${environmentPath()}`, {
    error: 'Configuration requires ComplianceWriter.', errorCode: 'FORBIDDEN',
    suggestion: 'Ask an authorized writer to attach the environment.',
  }, 403);

  // Act
  await page.goto(configurationUrl());

  // Assert
  const panel = page.getByRole('region', { name: /Azure assessment environment/i });
  await expect(panel.getByText('Configuration requires ComplianceWriter.')).toBeVisible();
  await expect(panel.getByText('Ask an authorized writer to attach the environment.')).toBeVisible();
  await expect(panel.getByRole('checkbox')).toHaveCount(0);
  expect(api.writes).toEqual([]);
});

test('legacy invalid attachment remains visible for repair instead of selecting tenant subscriptions', async ({ page, api }) => {
  // Arrange
  api.respond(`/api/dashboard${environmentPath()}`, {
    ...environment(), cloudEnvironment: 'GovernmentAirGappedIl5', subscriptionIds: [legacySubscriptionId],
  });

  // Act
  await page.goto(configurationUrl());

  // Assert
  const panel = page.getByRole('region', { name: /Azure assessment environment/i });
  await expect(panel.getByText(legacySubscriptionId, { exact: false })).toBeVisible();
  await expect(panel.getByRole('button', { name: /save environment/i })).toBeDisabled();
  await expect(panel.getByRole('button', { name: /detach environment/i })).toBeEnabled();
  await expect(panel.getByRole('checkbox', { name: /Synthetic Commercial Alpha/i })).not.toBeChecked();
  expect(api.writes).toEqual([]);
});

test('CSP All-organizations context requires explicit organization selection for run and configuration', async ({ page, api }) => {
  // Arrange
  api.respond('/api/auth/me', {
    oid: 'synthetic-csp-admin', displayName: 'Synthetic CSP Administrator', persona: 'ISSM',
    homeTenant: { id: 'synthetic-csp', displayName: 'Synthetic CSP', status: 'Active' },
    effectiveTenant: { id: 'synthetic-csp', displayName: 'Synthetic CSP', status: 'Active' },
    isImpersonating: false, impersonation: null, pimRoles: [],
    isCspAdmin: true, isSocAnalyst: false, tenantMemberships: [],
  });
  const message = 'Select the system organization before assessing its Azure environment.';
  const suggestion = 'Use the organization selector to choose the system organization, then retry.';
  api.respond(`/api/dashboard${readinessPath()}`, {
    ...readiness(), errorCode: 'ASSESSMENT_AZURE_ORGANIZATION_REQUIRED', message, suggestion,
  });
  api.respond(`/api/dashboard${environmentPath()}`, {
    error: 'Select the system organization before configuring its Azure environment.',
    errorCode: 'ASSESSMENT_AZURE_ORGANIZATION_REQUIRED', suggestion,
  }, 403);

  // Act
  await page.goto(`/systems/${systemId}/assessments`);

  // Assert
  await expect(page.getByText(message)).toBeVisible();
  await expect(page.getByText(suggestion)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Run Assessment', exact: true })).toBeDisabled();
  expect(api.writes).toEqual([]);

  // Act
  await page.getByRole('link', { name: /Configure Environment/i }).click();

  // Assert
  const panel = page.getByRole('region', { name: /Azure assessment environment/i });
  await expect(panel.getByText('Select the system organization before configuring its Azure environment.')).toBeVisible();
  await expect(panel.getByText(suggestion)).toBeVisible();
  await expect(panel.getByRole('checkbox')).toHaveCount(0);
  expect(api.writes).toEqual([]);
});

test('readiness 403 preserves actionable writer guidance and leaves historical assessment controls available', async ({ page, api }) => {
  // Arrange
  api.respond(`/api/dashboard${readinessPath()}`, {
    error: 'You do not have permission to run assessments.',
    errorCode: 'ASSESSMENT_PERMISSION_REQUIRED',
    suggestion: 'Ask a ComplianceWriter to configure and run this assessment.',
  }, 403);

  // Act
  await page.goto(`/systems/${systemId}/assessments`);

  // Assert
  await expect(page.getByText('You do not have permission to run assessments.')).toBeVisible();
  await expect(page.getByText('Ask a ComplianceWriter to configure and run this assessment.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Run Assessment', exact: true })).toBeDisabled();
  await expect(page.getByRole('table')).toContainText('75%');
  await expect(page.getByRole('button', { name: 'Generate SAR', exact: true })).toBeEnabled();
  expect(api.writes).toEqual([]);
});
