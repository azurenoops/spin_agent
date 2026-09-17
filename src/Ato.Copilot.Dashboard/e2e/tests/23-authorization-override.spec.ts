import { expect, test, type Page } from '@playwright/test';

const systemId = 'authorization-system-1';

async function mockSessionAndSystem(page: Page) {
  await page.route('**/api/csp/onboarding/state', (route) => route.fulfill({ status: 404 }));
  await page.route('**/api/onboarding/organization-context', (route) => route.fulfill({
    json: { ok: true, data: null },
  }));
  await page.route('**/api/onboarding/state', (route) => route.fulfill({
    json: {
      ok: true,
      data: {
        tenantId: 'tenant-1', status: 'Completed', lastStep: 'Roles',
        startedAt: '2026-03-01T12:00:00Z', completedAt: '2026-03-01T12:05:00Z',
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
        tenantId: 'tenant-1', currentStep: 'Submitted', completedSteps: ['Submitted'],
        onboardingState: 'Active', firstOrganizationId: 'organization-1',
      },
    },
  }));
  await page.route('**/api/auth/login-config', (route) => route.fulfill({
    json: {
      status: 'success',
      data: {
        branding: { deploymentName: 'ATO Copilot', logoUrl: null, supportEmail: null },
        defaultMethod: 'Simulation',
        enabledMethods: [{ id: 'Simulation', displayName: 'Simulation' }],
        cloud: 'AzurePublic', idleTimeoutMinutes: 30, rememberTenantCookieDays: 7,
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
      oid: 'ao-123', displayName: 'Alex Official', persona: 'AO',
      homeTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      effectiveTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      isImpersonating: false, impersonation: null, pimRoles: [],
      isCspAdmin: false, isSocAnalyst: false,
      tenantMemberships: [{ id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' }],
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/profile/completeness`, (route) => route.fulfill({
    json: {
      systemId, totalSections: 0, statusCounts: {}, approvedPercentage: 0,
      isProfileComplete: false, incompleteSections: [], missionOwnerAssigned: false,
      missionOwnerName: null, daysSinceRegistration: 1,
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/todos`, (route) => route.fulfill({
    json: { items: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}`, (route) => route.fulfill({
    json: {
      systemId, name: 'Mission Authorization Platform', acronym: 'MAP',
      systemType: 'MajorApplication', missionCriticality: 'MissionCritical',
      hostingEnvironment: 'AzureGovernment', impactLevel: 'IL5', baselineLevel: 'Moderate',
      currentRmfPhase: 'Authorize', rmfPhaseProgress: [], recentActivity: [], categorization: null,
      keyMetrics: { totalControls: 100, implementedControls: 96, complianceScore: 96, openPoams: 2, totalFindings: 2, narrativeCoverage: 100, activeDeviations: 0 },
    },
  }));
}

test('AO override persists after reload without replacing the underlying verdict', async ({ page }) => {
  // Arrange
  await page.addInitScript(() => {
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'AO' }));
  });
  await mockSessionAndSystem(page);
  let activeOverride: Record<string, string> | null = null;
  await page.route(`**/api/dashboard/systems/${systemId}/authorization`, async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({ json: {
        id: 'decision-1', decisionType: 'Dato', decisionDate: '2026-03-01T12:00:00Z',
        expirationDate: null, residualRiskLevel: 'High', issuedBy: 'ao-original',
        issuedByName: 'Original Official', override: activeOverride,
      } });
      return;
    }
    await route.fallback();
  });
  await page.route(`**/api/dashboard/systems/${systemId}/authorization/override`, async (route) => {
    const request = route.request().postDataJSON() as { overrideStatus: string; justification: string; expirationDate: string };
    activeOverride = {
      id: 'override-1', overrideStatus: request.overrideStatus, appliedBy: 'ao-123',
      appliedByName: 'Alex Official', appliedAt: '2026-03-02T12:00:00Z',
      justification: request.justification, expirationDate: request.expirationDate,
    };
    await route.fulfill({ status: 201, json: activeOverride });
  });
  await page.route(`**/api/dashboard/systems/${systemId}/risk-acceptances`, (route) => route.fulfill({ json: [] }));

  // Act
  await page.goto(`/systems/${systemId}/authorize`);
  await page.getByRole('button', { name: 'Apply Temporary Override' }).click();
  await page.getByLabel('Override Status').selectOption('ATO');
  await page.getByLabel('Override Expiration').fill('2026-12-31');
  await page.getByLabel('Justification').fill('Temporary mission authorization pending formal review.');
  await page.getByRole('button', { name: 'Apply Override' }).click();
  await page.reload();

  // Assert
  const verdict = page.getByRole('region', { name: 'Active authorization decision' });
  await expect(verdict.getByRole('heading', { name: 'Underlying AO Verdict' })).toBeVisible();
  await expect(verdict.getByText('Dato', { exact: true })).toBeVisible();
  const annotation = page.getByRole('region', { name: 'Active authorization override' });
  await expect(annotation.getByText('ATO', { exact: true })).toBeVisible();
  await expect(annotation.getByText(/Alex Official \(ao-123\)/)).toBeVisible();
  await expect(annotation.getByText('Temporary mission authorization pending formal review.')).toBeVisible();
});