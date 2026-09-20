import { expect, test, type Page } from '@playwright/test';

const systemId = 'phase-history-system';

async function mockDashboard(page: Page) {
  await page.route(/^https?:\/\/[^/]+\/api\//, (route) => route.fulfill({ status: 500, json: { error: 'not used' } }));
  await page.route('**/api/csp/onboarding/state', (route) => route.fulfill({ status: 404 }));
  await page.route('**/api/onboarding/organization-context', (route) => route.fulfill({
    json: { ok: true, data: null },
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
      oid: 'issm@contoso.com',
      displayName: 'Test ISSM',
      persona: 'ISSM',
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
  await page.route(`**/api/dashboard/systems/${systemId}`, (route) => route.fulfill({
    json: {
      systemId,
      name: 'Phase History System',
      acronym: 'PHS',
      systemType: 'MajorApplication',
      missionCriticality: 'MissionCritical',
      hostingEnvironment: 'AzureGovernment',
      impactLevel: 'Moderate',
      baselineLevel: 'Moderate',
      currentRmfPhase: 'Categorize',
      rmfPhaseProgress: [],
      keyMetrics: {
        complianceScore: 0,
        complianceScoreDelta: 0,
        priorScore: 0,
        totalOpenPoams: 0,
        overduePoams: 0,
        atoDaysRemaining: null,
        atoSeverity: 'none',
        atoExpirationDate: null,
        atoStatus: 'Not Authorized',
        catIFindings: 0,
        catIIFindings: 0,
        catIIIFindings: 0,
        totalFindings: 0,
        narrativeCoverage: 0,
        activeDeviations: 0,
      },
      recentActivity: [],
      categorization: null,
      rmfPhaseTransitions: [{
        id: 'transition-1',
        previousPhase: 'Prepare',
        targetPhase: 'Categorize',
        actor: 'issm@contoso.com',
        timestamp: '2026-04-10T14:30:00Z',
        forced: true,
        notes: 'Approved readiness exception',
      }],
    },
  }));
}

test('shows immutable RMF phase history on desktop and mobile', async ({ page }) => {
  // Arrange
  await mockDashboard(page);

  // Act
  await page.goto(`/systems/${systemId}`);

  // Assert
  const history = page.getByRole('region', { name: 'RMF phase transition history' });
  await expect(history).toBeVisible();
  await expect(history).toContainText('Prepare');
  await expect(history).toContainText('Categorize');
  await expect(history).toContainText('issm@contoso.com');
  await expect(history).toContainText('Forced');
  await expect(history).toContainText('Approved readiness exception');

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(history).toBeVisible();
  const bounds = await history.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(390);
});
