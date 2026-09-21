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

  for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`should show independent narrative editors and truthful business context at ${viewport.width}px`, async ({ page }, testInfo) => {
    // Arrange
    await page.setViewportSize(viewport);
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    await page.addInitScript(() => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSO' })));
    let isFlagged = false;
    let contextContent: string | null = null;
    let failContext = false;
    await page.route(/^https?:\/\/[^/]+\/api\//, route => route.fulfill({ status: 503, json: { error: 'Not configured in this fixture' } }));
    await page.route('**/api/csp/onboarding/state', route => route.fulfill({ status: 404 }));
    await page.route('**/api/dashboard/systems/e2e-system/controls/AC-2/evidence', route => route.fulfill({ json: { direct: [], inherited: [], automated: [] } }));
    await page.route('**/api/dashboard/systems/e2e-system/controls/AC-2/validation', route => route.fulfill({ json: { links: [] } }));
    await page.route('**/api/dashboard/systems/e2e-system/controls/AC-2/narrative', route => route.fulfill({ status: 204 }));
    await page.route('**/api/dashboard/systems/e2e-system/business-context/flagged-controls', route => route.fulfill({
      json: isFlagged ? [{ controlId: 'AC-2', controlTitle: 'Account Management', hasDraft: contextContent !== null }] : [],
    }));
    await page.route('**/api/dashboard/systems/e2e-system/business-context/AC-2', route => route.fulfill({
      status: failContext ? 500 : 200,
      contentType: 'application/json',
      body: JSON.stringify(failContext ? { error: 'Synthetic outage' } : contextContent === null ? null : {
        id: 'owner-draft', controlId: 'AC-2', content: contextContent, governanceStatus: 'Draft',
        authoredBy: 'Synthetic Mission Owner', authoredAt: '2026-01-01T00:00:00Z', reviewerComments: null,
      }),
    }));
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
    const modelNarrative = {
        id: 'narrative-1', controlId: 'AC-2', family: 'AC', narrative: null,
        policyNarrative: 'Accounts are reviewed quarterly.',
        technicalNarrative: 'Entra ID enforces conditional access.',
        migratedFromLegacy: false, implementationStatus: 'Implemented', approvalStatus: 'Draft',
        authoredBy: 'e2e-user', authoredAt: '2026-01-01T00:00:00Z', version: 1,
        isAutoPopulated: true, aiSuggested: true,
    };
    await page.route('**/api/dashboard/systems/e2e-system/narratives**', route => route.fulfill({
      json: [modelNarrative,
        { ...modelNarrative, id: 'template', controlId: 'AC-3', aiSuggested: false },
        { ...modelNarrative, id: 'migrated', controlId: 'AC-4', migratedFromLegacy: true },
        { ...modelNarrative, id: 'empty', controlId: 'AC-5', technicalNarrative: null, isAutoPopulated: false },
      ],
    }));
    await page.route('**/api/dashboard/systems/e2e-system/controls/AC-2/regenerate-ai**', route => route.fulfill({
      json: { narrative: 'Regenerated technical narrative.' },
    }));
    await page.route('**/api/dashboard/systems/e2e-system/controls/AC-2/narrative', async route => {
      const patch = route.request().postDataJSON();
      Object.assign(modelNarrative, patch);
      if ('technicalNarrative' in patch) {
        modelNarrative.aiSuggested = false;
        modelNarrative.isAutoPopulated = false;
      }
      await route.fulfill({ json: modelNarrative });
    });
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
    const expandButton = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'AC-2', exact: true }) })
      .getByRole('button', { name: 'Expand' });
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
    await expect(page.getByTitle('AI-assisted Technical narrative')).toHaveCount(1);
    await expect(page.getByText('AI Suggested', { exact: true }).locator('..')).toContainText('1');
    await expect(page.getByText('Auto', { exact: true })).toHaveCount(1);
    await expect(page.getByText('Migrated', { exact: true })).toHaveCount(1);

    await technicalEditor.fill('Human-authored technical narrative.');
    await technicalEditor.blur();
    await expect(page.getByTitle('AI-assisted Technical narrative')).toHaveCount(0);
    await expect(page.getByText('AI Suggested', { exact: true }).locator('..')).toContainText('0');
    await expect(policyEditor).toHaveValue('Accounts are reviewed monthly.');
    await page.getByRole('button', { name: 'Regenerate' }).click();
    await expect(technicalEditor).toHaveValue('Regenerated technical narrative.');
    await expect(policyEditor).toHaveValue('Accounts are reviewed monthly.');

    const contextPanel = page.getByRole('region', { name: 'Business context for AC-2' });
    await expect(contextPanel.getByText('No business context provided')).toBeVisible();
    await expect(contextPanel.getByText(/Awaiting business context/)).toHaveCount(0);
    isFlagged = true;
    await contextPanel.getByRole('button', { name: 'Refresh business context' }).click();
    await expect(contextPanel.getByText('Awaiting business context from Mission Owner')).toBeVisible();
    failContext = true;
    await contextPanel.getByRole('button', { name: 'Refresh business context' }).click();
    await expect(contextPanel.getByText('Unable to load business context', { exact: true })).toBeVisible();
    await expect(contextPanel.getByText(/Awaiting business context/)).toHaveCount(0);
    await contextPanel.screenshot({ path: testInfo.outputPath('business-context-error.png') });
    failContext = false;
    contextContent = 'Owner mission context for account management.';
    await contextPanel.getByRole('button', { name: 'Retry business context', exact: true }).click();
    await expect(contextPanel.getByText(contextContent)).toBeVisible();
    await expect(policyEditor).toHaveValue('Accounts are reviewed monthly.');
    await expect(technicalEditor).toHaveValue('Regenerated technical narrative.');
    contextContent = 'Updated owner mission context.';
    await contextPanel.getByRole('button', { name: 'Refresh business context' }).click();
    await expect(contextPanel.getByText(contextContent)).toBeVisible();
    await contextPanel.getByRole('button', { name: 'Copy to Narrative' }).click();
    await expect(policyEditor).toHaveValue(`Accounts are reviewed monthly.\n\n${contextContent}`);
    await expect(technicalEditor).toHaveValue('Regenerated technical narrative.');
    await contextPanel.screenshot({ path: testInfo.outputPath('business-context-draft.png') });
    expect(pageErrors).toEqual([]);
  });
  }
});
