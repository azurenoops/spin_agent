import { expect, test, type Page } from '@playwright/test';

const systemId = 'authorization-package-gate-system';

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
        branding: { deploymentName: 'Security Posture Intelligence Navigator', logoUrl: null, supportEmail: null },
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
      name: 'Authorization Package Gate System',
      acronym: 'APGS',
      currentRmfPhase: 'Authorize',
      rmfPhaseProgress: [],
      keyMetrics: {},
      recentActivity: [],
      categorization: null,
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/documents`, (route) => route.fulfill({
    json: {
      systemId,
      systemName: 'Authorization Package Gate System',
      currentPhase: 'Authorize',
      ssp: { narrativeCompletionPct: 100, totalNarratives: 10, completedNarratives: 10 },
      sap: null,
      sar: null,
      authorization: null,
      poamCount: 0,
      poamOverdueCount: 0,
      hasBaseline: true,
      baselineControlCount: 325,
      pta: null,
      pia: null,
      interconnections: [],
      conMon: null,
      sspSections: [],
      activeWaiverCount: 0,
      narrativeGovernance: null,
      importHistory: [],
      inventoryItemCount: 0,
    },
  }));
  await page.route(`**/api/v1/systems/${systemId}/packages`, (route) => route.fulfill({
    json: { items: [], totalCount: 0, limit: 10, offset: 0 },
  }));
  await page.route(`**/api/v1/systems/${systemId}/packages/validate`, (route) => route.fulfill({
    json: {
      isValid: false,
      errorCount: 1,
      warningCount: 0,
      validatedAt: '2026-04-10T14:30:00Z',
      findings: [{
        severity: 'error',
        category: 'authorization-decision',
        artifactType: 'ato-letter',
        description: 'No active authorization decision found, or the latest decision has expired.',
        remediation: 'Go to Authorize and have an Authorizing Official issue a current decision before generating the package.',
      }],
    },
  }));
}

test('blocks package generation without a current AO decision and links to remediation', async ({ page }) => {
  // Arrange
  await page.addInitScript(() => {
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' }));
  });
  await mockDashboard(page);

  // Act
  await page.goto(`/systems/${systemId}/documents`);
  await page.getByRole('button', { name: 'Generate Package' }).click();

  // Assert
  const dialog = page.getByRole('dialog', { name: 'Generate Authorization Package' });
  await expect(dialog).toContainText('No active authorization decision found');
  await expect(dialog.getByRole('button', { name: 'Generate Package' })).not.toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Open Authorize' })).toBeVisible();
});