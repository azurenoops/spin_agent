import { expect, test, type Page } from '@playwright/test';

const systemId = 'emass-system-1';

async function mockAuthAndSystem(page: Page) {
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
      oid: 'isso-user', displayName: 'ISSO User', persona: 'ISSO',
      homeTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      effectiveTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      isImpersonating: false, impersonation: null, pimRoles: [],
      isCspAdmin: false, isSocAnalyst: false,
      tenantMemberships: [{ id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' }],
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}`, (route) => route.fulfill({
    json: {
      systemId, name: 'Mission Analytics', acronym: 'MAP',
      systemType: 'MajorApplication', missionCriticality: 'MissionCritical',
      hostingEnvironment: 'AzureGovernment', impactLevel: 'IL5', baselineLevel: 'Moderate',
      currentRmfPhase: 'Authorize', rmfPhaseProgress: [], recentActivity: [], categorization: null,
      keyMetrics: { totalControls: 100, implementedControls: 96, complianceScore: 96, openPoams: 2, totalFindings: 2, narrativeCoverage: 100, activeDeviations: 0 },
    },
  }));
}

test('ISSO reviews readiness, syncs an eMASS workbook, and keeps the SPIN value', async ({ page }) => {
  // Arrange
  await page.addInitScript(() => {
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSO' }));
  });
  await mockAuthAndSystem(page);
  let conflicts = [{
    id: 'conflict-1', entityType: 'ControlImplementation', entityId: 'AC-2',
    fieldName: 'ControlImplementation.ImplementationStatus', spinValue: 'Implemented',
    emassValue: 'Partially Implemented', conflictStatus: 'Unresolved',
    detectedAt: '2026-03-02T12:00:00Z', resolvedAt: null, resolvedBy: null,
  }];

  await page.route(`**/api/dashboard/systems/${systemId}/emass/status`, (route) => route.fulfill({
    json: { data: {
      systemId, overallStatus: conflicts.length ? 'HasConflicts' : 'UpToDate',
      lastExportedAt: '2026-03-01T12:00:00Z', lastSyncedAt: '2026-03-02T12:00:00Z',
      unresolvedConflictCount: conflicts.length,
      exportSummary: [{ category: 'Controls', exportedCount: 100, pendingCount: 4, lastExportedAt: '2026-03-01T12:00:00Z' }],
      readinessStatus: { isReady: false, blockingGapCount: 1, advisoryGapCount: 0 },
    }, meta: {}, errors: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/emass/readiness`, (route) => route.fulfill({
    json: { data: {
      systemId, isReady: false, checkedAt: '2026-03-03T12:00:00Z',
      gaps: [{ fieldName: 'EmassSystemId', description: 'Register the eMASS ID.', severity: 'Blocking', fixUrl: '/settings' }],
    }, meta: {}, errors: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/emass/conflicts*`, (route) => route.fulfill({
    json: { data: conflicts, meta: { total: conflicts.length, limit: 50, offset: 0 }, errors: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/emass/sync*`, async (route) => {
    expect(route.request().method()).toBe('POST');
    expect(route.request().postDataBuffer()?.length).toBeGreaterThan(0);
    await route.fulfill({ json: { data: {
      batchId: 'batch-1', systemId, conflictsCreated: 1, identicalFields: 20,
      skippedUnresolved: 0, syncedAt: '2026-03-03T12:00:00Z',
    }, meta: {}, errors: [] } });
  });
  await page.route(`**/api/dashboard/systems/${systemId}/emass/conflicts/conflict-1`, async (route) => {
    expect(route.request().postDataJSON()).toEqual({ resolution: 'KeepSpin' });
    conflicts = [];
    await route.fulfill({ json: { data: {
      id: 'conflict-1', entityType: 'ControlImplementation', entityId: 'AC-2',
      fieldName: 'ControlImplementation.ImplementationStatus', spinValue: 'Implemented',
      emassValue: 'Partially Implemented', conflictStatus: 'KeepSpin',
      detectedAt: '2026-03-02T12:00:00Z', resolvedAt: '2026-03-03T12:00:00Z', resolvedBy: 'isso-user',
    }, meta: {}, errors: [] } });
  });

  // Act
  await page.goto(`/systems/${systemId}/emass/status`);

  // Assert
  await expect(page.getByRole('heading', { name: 'eMASS workflow' })).toBeVisible();
  await expect(page.getByText('Register the eMASS ID.')).toBeVisible();
  await expect(page.getByText('Partially Implemented')).toBeVisible();

  // Act
  await page.getByLabel('eMASS workbook').setInputFiles({
    name: 'controls.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: Buffer.from('deterministic workbook fixture'),
  });
  await page.getByRole('button', { name: 'Sync workbook' }).click();
  await page.getByRole('button', { name: 'Keep SPIN' }).click();

  // Assert
  await expect(page.getByText('No unresolved conflicts.')).toBeVisible();
});