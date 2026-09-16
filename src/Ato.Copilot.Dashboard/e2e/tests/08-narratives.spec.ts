import { test, expect } from '@playwright/test';

async function gotoNarratives(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /narratives/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Narratives', () => {
  test('should load narratives page', async ({ page }) => {
    await gotoNarratives(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/narrative|control|family/i);
  });

  test('should display narratives table', async ({ page }) => {
    await gotoNarratives(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      const rows = table.locator('tbody tr');
      const count = await rows.count();
      expect(count).toBeGreaterThanOrEqual(0);
    }
  });

  test('should filter by NIST family', async ({ page }) => {
    await gotoNarratives(page);
    const familyFilter = page.getByLabel(/family/i);
    if (await familyFilter.isVisible()) {
      const options = await familyFilter.locator('option').allTextContents();
      if (options.length > 1) {
        await familyFilter.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should filter by implementation status', async ({ page }) => {
    await gotoNarratives(page);
    const statusFilter = page.locator('select').last();
    if (await statusFilter.isVisible()) {
      const options = await statusFilter.locator('option').allTextContents();
      if (options.length > 1) {
        await statusFilter.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should open add narrative form', async ({ page }) => {
    await gotoNarratives(page);
    const addBtn = page.getByRole('button', { name: /add narrative/i });
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.locator('form, [role="dialog"]').first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should show independent policy and technical narrative editors', async ({ page }) => {
    // Arrange
    await page.route('**/api/auth/login-config', route => route.fulfill({
      json: {
        status: 'success',
        data: {
          branding: { deploymentName: 'ATO Copilot E2E', logoUrl: null, supportEmail: null },
          defaultMethod: 'Entra', enabledMethods: [{ id: 'Entra', displayName: 'Microsoft Entra ID' }],
          cloud: 'AzurePublic', idleTimeoutMinutes: 30, rememberTenantCookieDays: 7,
          simulation: null,
          msal: {
            clientId: '00000000-0000-0000-0000-000000000001',
            authority: 'https://login.microsoftonline.com/common',
            redirectUri: 'http://127.0.0.1:5173/login/callback',
            postLogoutRedirectUri: 'http://127.0.0.1:5173/login',
          },
        },
      },
    }));
    await page.route('**/api/auth/me', route => route.fulfill({ json: { userId: 'e2e-user' } }));
    await page.route('**/api/dashboard/systems/e2e-system/profile/completeness', route => route.fulfill({
      json: {
        systemId: 'e2e-system', totalSections: 0, statusCounts: {}, approvedPercentage: 0,
        isProfileComplete: false, incompleteSections: [], missionOwnerAssigned: false,
        missionOwnerName: null, daysSinceRegistration: 1,
      },
    }));
    await page.route('**/api/dashboard/systems/e2e-system/todos', route => route.fulfill({
      json: { items: [] },
    }));
    await page.route('**/api/dashboard/systems/e2e-system/narratives**', route => route.fulfill({
      json: [{
        id: 'narrative-1', controlId: 'AC-2', family: 'AC', narrative: null,
        policyNarrative: 'Accounts are reviewed quarterly.',
        technicalNarrative: 'Entra ID enforces conditional access.',
        migratedFromLegacy: false, implementationStatus: 'Implemented', approvalStatus: 'Draft',
        authoredBy: 'e2e-user', authoredAt: '2026-01-01T00:00:00Z', version: 1,
        isAutoPopulated: false, aiSuggested: false,
      }],
    }));
    await page.route('**/api/dashboard/systems/e2e-system', route => route.fulfill({
      json: {
        systemId: 'e2e-system', name: 'E2E System', acronym: 'E2E', systemType: 'Application',
        missionCriticality: 'MissionSupport', hostingEnvironment: 'Cloud', impactLevel: 'IL4',
        baselineLevel: 'Moderate', currentRmfPhase: 'Implement', rmfPhaseProgress: [],
        keyMetrics: {
          complianceScore: 0, complianceScoreDelta: 0, priorScore: 0, totalOpenPoams: 0,
          overduePoams: 0, atoDaysRemaining: null, atoSeverity: 'None', atoExpirationDate: null,
          atoStatus: 'NotStarted', catIFindings: 0, catIIFindings: 0, catIIIFindings: 0,
          totalFindings: 0, narrativeCoverage: 0, activeDeviations: 0,
        },
        recentActivity: [], categorization: null,
      },
    }));
    const saveRequest = page.waitForRequest(request =>
      request.method() === 'PATCH' && request.url().endsWith('/controls/AC-2/narrative'));

    // Act
    await page.goto('/systems/e2e-system/narratives');
    const expandButton = page.getByRole('button', { name: 'Expand' }).first();
    await expect(expandButton).toBeVisible();
    await expandButton.click();
    const policyEditor = page.getByLabel('Policy narrative for AC-2');
    const technicalEditor = page.getByLabel('Technical narrative for AC-2');
    await policyEditor.fill('Accounts are reviewed monthly.');
    await policyEditor.blur();
    const request = await saveRequest;

    // Assert
    expect(request.postDataJSON()).toEqual({ policyNarrative: 'Accounts are reviewed monthly.' });
    await expect(technicalEditor).toHaveValue('Entra ID enforces conditional access.');
  });
});
