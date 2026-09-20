import { test, expect, type Page } from '@playwright/test';
import { CapabilitiesPage } from '../pages/capabilities.page';

const mixedCatalogSystemId = 'mixed-capability-system';

async function mockCapabilityCoverageShell(page: Page) {
  await page.route('**/api/csp/onboarding/state', (route) => route.fulfill({ status: 404 }));
  await page.route('**/api/onboarding/organization-context', (route) => route.fulfill({
    json: { ok: true, data: null },
  }));
  await page.route('**/api/onboarding/state', (route) => route.fulfill({
    json: {
      ok: true,
      data: {
        tenantId: 'tenant-1',
        status: 'Completed',
        lastStep: 'Roles',
        startedAt: '2026-03-01T12:00:00Z',
        completedAt: '2026-03-01T12:05:00Z',
        lastReRunAt: null,
        steps: [
          { step: 'OrganizationContext', status: 'Completed', completedAt: '2026-03-01T12:01:00Z', durationMs: 1000 },
          { step: 'Roles', status: 'Completed', completedAt: '2026-03-01T12:02:00Z', durationMs: 1000 },
        ],
      },
    },
  }));
  await page.route('**/api/onboarding/tenant/state', (route) => route.fulfill({
    json: {
      status: 'success',
      data: {
        tenantId: 'tenant-1',
        currentStep: 'Submitted',
        completedSteps: ['Submitted'],
        onboardingState: 'Active',
        firstOrganizationId: 'organization-1',
      },
    },
  }));
  await page.route('**/api/deployment/mode', (route) => route.fulfill({
    json: { mode: 'MultiTenant' },
  }));
  await page.route('**/api/auth/login-config', (route) => route.fulfill({
    json: {
      status: 'success',
      data: {
        branding: { deploymentName: 'ATO Copilot', logoUrl: null, supportEmail: null },
        defaultMethod: 'Simulation',
        enabledMethods: [{ id: 'Simulation', displayName: 'Simulation' }],
        cloud: 'AzurePublic',
        idleTimeoutMinutes: 30,
        rememberTenantCookieDays: 7,
        simulation: { identities: [] },
        msal: {
          clientId: '00000000-0000-0000-0000-000000000001',
          authority: 'https://login.microsoftonline.com/common',
          redirectUri: 'http://localhost:5173/login/callback',
          postLogoutRedirectUri: 'http://localhost:5173/login',
        },
      },
    },
  }));
  await page.route('**/api/auth/me', (route) => route.fulfill({
    json: {
      oid: 'e2e-user',
      displayName: 'E2E User',
      persona: 'ISSO',
      homeTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      effectiveTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      isImpersonating: false,
      impersonation: null,
      pimRoles: [],
      isCspAdmin: false,
      isSocAnalyst: false,
      tenantMemberships: [],
    },
  }));
  await page.route(`**/api/dashboard/systems/${mixedCatalogSystemId}/profile/completeness`, (route) =>
    route.fulfill({
      json: {
        systemId: mixedCatalogSystemId,
        totalSections: 0,
        statusCounts: {},
        approvedPercentage: 0,
        isProfileComplete: false,
        incompleteSections: [],
        missionOwnerAssigned: false,
        missionOwnerName: null,
        daysSinceRegistration: 1,
      },
    }));
  await page.route(`**/api/dashboard/systems/${mixedCatalogSystemId}/todos`, (route) =>
    route.fulfill({ json: { items: [] } }));
  await page.route(`**/api/dashboard/systems/${mixedCatalogSystemId}`, (route) => route.fulfill({
    json: {
      systemId: mixedCatalogSystemId,
      name: 'Mixed Capability System',
      acronym: 'MCS',
      systemType: 'MajorApplication',
      missionCriticality: 'MissionSupport',
      hostingEnvironment: 'AzureGovernment',
      impactLevel: 'IL4',
      baselineLevel: 'Moderate',
      currentRmfPhase: 'Implement',
      rmfPhaseProgress: [],
      keyMetrics: {},
      recentActivity: [],
      categorization: null,
    },
  }));
  await page.route(
    `**/api/dashboard/systems/${mixedCatalogSystemId}/capability-coverage`,
    (route) => route.fulfill({
      json: {
        systemId: mixedCatalogSystemId,
        systemName: 'Mixed Capability System',
        capabilities: [],
        summary: {
          totalCapabilities: 0,
          totalMappedControls: 0,
          totalNarrativesPopulated: 0,
          totalNarrativesCustom: 0,
          totalNarrativesEmpty: 0,
          coveragePercent: 0,
        },
      },
    }),
  );
  await page.route(
    `**/api/dashboard/systems/${mixedCatalogSystemId}/available-capabilities**`,
    (route) => route.fulfill({
      json: {
        totalCount: 2,
        excludedCount: 0,
        items: [
          {
            id: 'org-capability',
            name: 'Organization Endpoint Protection',
            description: 'Organization-managed endpoint protection',
            provider: 'Organization SOC',
            category: 'SI',
            source: 'Organization',
            mappedControlIds: ['SI-3'],
            mappedControlCount: 1,
          },
          {
            id: 'csp-capability',
            name: 'CSP Managed Audit Logging',
            description: 'Provider-managed audit collection',
            provider: 'Azure Government',
            category: 'Service',
            source: 'CSP',
            mappedControlIds: ['AU-2'],
            mappedControlCount: 1,
          },
        ],
      },
    }),
  );
}

test.describe('Capabilities Library (Org-wide)', () => {
  let caps: CapabilitiesPage;

  test.beforeEach(async ({ page }) => {
    caps = new CapabilitiesPage(page);
    await caps.goto();
  });

  test('should load capabilities library', async () => {
    await caps.expectLoaded();
  });

  test('should display capabilities list', async ({ page }) => {
    // At least one capability should exist from seed data
    const cards = page.locator('[class*="card"], [class*="capability"]');
    await expect(cards.first()).toBeVisible({ timeout: 15_000 });
  });

  test('should open add-capability form', async ({ page }) => {
    await caps.openAddForm();
    await expect(page.getByLabel(/^name/i).first()).toBeVisible();
  });

  test('should create a capability', async ({ page }) => {
    const name = `E2E Capability ${Date.now().toString().slice(-6)}`;
    await caps.createCapability({
      name,
      provider: 'E2E Test Provider',
    });
    await page.waitForTimeout(1_000);
    await caps.expectCapabilityListed(name);
  });

  test('should search capabilities', async () => {
    await caps.search('Backup');
    // Wait for filter to apply
  });

  test('should expand a capability to see details', async ({ page }) => {
    const firstCard = page.locator('[class*="card"] h3, [class*="capability"] h3, [class*="card"] h4').first();
    if (await firstCard.isVisible()) {
      const capName = await firstCard.textContent();
      await caps.expandCapability(capName!.trim());
      await page.waitForTimeout(500);
      // Expanded content should show description/mappings
      const body = await page.textContent('body');
      expect(body).toMatch(/description|mapping|control|provider/i);
    }
  });

  test('should edit a capability', async ({ page }) => {
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form', { state: 'visible' });
      await expect(page.getByLabel(/^name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click();
    }
  });

  test('should show capability impact preview on edit', async ({ page }) => {
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form', { state: 'visible' });
      // Modify description to trigger impact preview
      const descField = page.getByLabel(/description/i);
      if (await descField.isVisible()) {
        await descField.fill('Updated description for impact test');
        await page.getByRole('button', { name: /save|update/i }).click();
        await page.waitForTimeout(1_000);
        // Impact preview dialog may appear
        const preview = page.getByText(/impact|affected|narrative/i);
        if (await preview.isVisible({ timeout: 3_000 }).catch(() => false)) {
          await expect(preview).toBeVisible();
          // Cancel to avoid making real changes
          await page.getByRole('button', { name: /cancel/i }).click();
        }
      }
    }
  });
});

test.describe('System Capability Coverage', () => {
  async function gotoCoverage(page: import('@playwright/test').Page) {
    await page.goto('/systems');
    await page.waitForLoadState('networkidle');
    await page.locator('table tbody tr a').first().click();
    await page.waitForLoadState('networkidle');
    await page.getByRole('link', { name: /capabilit/i }).click();
    await page.waitForLoadState('networkidle');
  }

  test('should load system capability coverage', async ({ page }) => {
    await gotoCoverage(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/capabilit|coverage|mapped|control/i);
  });

  test('should display summary metrics', async ({ page }) => {
    await gotoCoverage(page);
    await expect(page.getByText(/total|mapped|coverage/i).first()).toBeVisible();
  });

  test('should expand a capability to see mappings', async ({ page }) => {
    await gotoCoverage(page);
    const card = page.locator('[class*="card"] h3, [class*="capability"] h3').first();
    if (await card.isVisible()) {
      await card.click();
      await page.waitForTimeout(500);
    }
  });

  test('should add capability to system', async ({ page }) => {
    await gotoCoverage(page);
    const addBtn = page.getByRole('button', { name: /add/i }).first();
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForTimeout(500);
      // Dialog to pick capability should appear
      const dialog = page.locator('[role="dialog"], form');
      if (await dialog.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await page.getByRole('button', { name: /cancel/i }).click();
      }
    }
  });

  test('shows both capability sources and subscribes to a CSP capability', async ({ page }) => {
    // Arrange
    await mockCapabilityCoverageShell(page);
    let subscriptionBody: unknown;
    await page.route(
      `**/api/dashboard/systems/${mixedCatalogSystemId}/capability-subscriptions`,
      async (route) => {
        subscriptionBody = route.request().postDataJSON();
        await route.fulfill({ status: 201, json: { id: 'subscription-1', alreadySubscribed: false } });
      },
    );

    // Act
    await page.goto(`/systems/${mixedCatalogSystemId}/capability-coverage`);
    await page.getByRole('button', { name: 'Add Capability' }).click();

    // Assert
    await expect(page.getByText('Organization', { exact: true })).toBeVisible();
    await expect(page.getByText('CSP', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: /CSP Managed Audit Logging/ }).click();
    await page.getByRole('button', { name: 'Add Capability' }).last().click();
    await expect.poll(() => subscriptionBody).toEqual({ capabilityId: 'csp-capability' });
    await expect(page.getByText('CSP Managed Audit Logging')).not.toBeVisible();
  });
});
