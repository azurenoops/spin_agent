import { test, expect, type Page } from '@playwright/test';

const systemId = 'system-1';
const controlId = 'AC-2';
const resourceId = '/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Storage/storageAccounts/store1';

async function mockNarrativeApis(page: Page) {
  let links = [{
    id: 'link-1',
    linkType: 'AzureResource',
    linkTarget: resourceId,
    description: 'Storage encryption configuration',
    addedBy: 'iac-scan',
    addedAt: '2026-06-01T12:00:00Z',
    validatedAt: '2026-06-01T12:00:00Z',
    isAutomated: true,
  }];

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
      oid: 'sca-user',
      displayName: 'SCA User',
      persona: 'SCA',
      homeTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      effectiveTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      isImpersonating: false,
      impersonation: null,
      pimRoles: [],
      isCspAdmin: false,
      isSocAnalyst: false,
      tenantMemberships: [{ id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' }],
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}`, (route) => route.fulfill({
    json: {
      systemId,
      name: 'Validation System',
      acronym: 'VS',
      systemType: 'MajorApplication',
      missionCriticality: 'MissionCritical',
      hostingEnvironment: 'AzureGovernment',
      impactLevel: 'IL5',
      baselineLevel: 'Moderate',
      currentRmfPhase: 'Assess',
      rmfPhaseProgress: [],
      keyMetrics: {
        totalControls: 1,
        implementedControls: 1,
        complianceScore: 100,
        openPoams: 0,
        totalFindings: 0,
        narrativeCoverage: 100,
        activeDeviations: 0,
      },
      recentActivity: [],
      categorization: null,
    },
  }));

  await page.route(`**/api/dashboard/systems/${systemId}/narratives*`, (route) => route.fulfill({
    json: [{
      id: 'implementation-1',
      controlId,
      family: 'AC',
      narrative: 'Account management is implemented.',
      implementationStatus: 'Implemented',
      approvalStatus: 'UnderReview',
      authoredBy: 'ISSO User',
      authoredAt: '2026-05-20T12:00:00Z',
      version: 1,
      isAutoPopulated: false,
      aiSuggested: false,
    }],
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/controls/${controlId}/evidence`, (route) => route.fulfill({
    json: { direct: [], inherited: [], automated: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/controls/${controlId}/validation`, async (route) => {
    if (route.request().method() === 'POST') {
      const payload = route.request().postDataJSON();
      expect(payload).toEqual({
        linkType: 'ExternalUrl',
        linkTarget: 'https://example.test/assessment/ac-2',
        description: 'Independent assessment report',
      });
      links = [...links, {
        id: 'link-2',
        linkType: payload.linkType,
        linkTarget: payload.linkTarget,
        description: payload.description,
        addedBy: 'sca-user',
        addedAt: '2026-06-02T12:00:00Z',
        validatedAt: null,
        isAutomated: false,
      }];
      await route.fulfill({ status: 201, json: links[1] });
      return;
    }

    await route.fulfill({ json: { systemId, controlId, total: links.length, links } });
  });
  await page.route(`**/api/dashboard/systems/${systemId}/controls/${controlId}/business-context`, (route) => route.fulfill({ status: 404 }));
}

test('SCA reviews and adds control validation evidence', async ({ page }) => {
  // Arrange
  await page.addInitScript(() => {
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'SCA' }));
  });
  await mockNarrativeApis(page);

  // Act
  await page.goto(`/systems/${systemId}/narratives`);
  await page.getByTitle('Expand').click();

  // Assert
  const panel = page.getByRole('region', { name: 'Validation evidence' });
  await expect(panel.getByText('Storage encryption configuration')).toBeVisible();
  await expect(panel.getByText('Azure Resource')).toBeVisible();
  await expect(panel.getByText('Auto')).toBeVisible();
  await expect(panel.getByRole('link', { name: 'Open validation target' })).toHaveAttribute(
    'href',
    `https://portal.azure.com/#resource/${resourceId}`,
  );

  // Act
  await panel.getByRole('button', { name: 'Add validation link' }).click();
  await page.getByLabel('Type').selectOption('ExternalUrl');
  await page.getByRole('textbox', { name: 'Target' }).fill('https://example.test/assessment/ac-2');
  await page.getByLabel('Description').fill('Independent assessment report');
  await page.getByRole('button', { name: 'Add link' }).click();

  // Assert
  await expect(panel.getByText('Independent assessment report')).toBeVisible();
  await expect(panel.getByText('Manual')).toBeVisible();
});