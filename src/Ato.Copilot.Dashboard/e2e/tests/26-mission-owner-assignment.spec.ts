import { expect, test, type Page } from '@playwright/test';

const systemId = 'mission-owner-workflow-system';
const personId = '11111111-1111-1111-1111-111111111111';

function roleSnapshot(assigned: boolean, source: 'override' | 'org-fallback' = 'override') {
  const roles = [
    'AuthorizingOfficial',
    'Issm',
    'Isso',
    'Sca',
    'SystemOwner',
    'MissionOwner',
    'Administrator',
  ].map((role) => ({
    role,
    person: role === 'MissionOwner' && assigned
      ? { id: personId, displayName: 'Morgan Owner' }
      : null,
    source: role === 'MissionOwner' && assigned ? source : 'not-assigned',
  }));

  return { status: 'success', data: { systemId, roles } };
}

async function mockSystem(page: Page, effectiveRole: string | null) {
  let systemAssigned = false;
  let organizationAssigned = false;

  await page.route(/^https?:\/\/[^/]+\/api\//, (route) => route.fulfill({ status: 500, json: { error: 'not used' } }));
  await page.route('**/api/csp/onboarding/state', (route) => route.fulfill({ status: 404 }));
  await page.route('**/api/onboarding/organization-context', (route) => route.fulfill({
    json: { ok: true, data: null },
  }));
  await page.route('**/api/onboarding/persons', (route) => route.fulfill({
    json: {
      ok: true,
      data: [{ id: personId, displayName: 'Morgan Owner', email: 'morgan@example.mil' }],
    },
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
      oid: '22222222-2222-2222-2222-222222222222',
      displayName: 'Role Tester',
      persona: effectiveRole,
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
      name: 'Mission Workflow System',
      acronym: 'MWS',
      systemType: 'MajorApplication',
      missionCriticality: 'MissionEssential',
      hostingEnvironment: 'Azure Government',
      impactLevel: 'Moderate',
      baselineLevel: 'Moderate',
      currentRmfPhase: 'Prepare',
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
        atoStatus: 'NotStarted',
        catIFindings: 0,
        catIIFindings: 0,
        catIIIFindings: 0,
        totalFindings: 0,
        narrativeCoverage: 0,
        activeDeviations: 0,
      },
      recentActivity: [],
      categorization: null,
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/todos`, (route) => route.fulfill({
    json: { systemId, systemName: 'Mission Workflow System', currentPhase: 'Prepare', nextPhase: null, items: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/profile/completeness`, (route) => route.fulfill({
    json: {
      systemId,
      totalSections: 5,
      statusCounts: { NotStarted: 5 },
      approvedPercentage: 0,
      isProfileComplete: false,
      incompleteSections: [],
      missionOwnerAssigned: organizationAssigned,
      missionOwnerName: organizationAssigned ? 'Morgan Owner' : null,
      daysSinceRegistration: 33,
    },
  }));
  await page.route('**/api/roles/effective', (route) => route.fulfill({
    json: { status: 'success', data: { effectiveRole, isTenantAdministrator: false } },
  }));
  await page.route(`**/api/roles/system/${systemId}`, async (route) => {
    if (route.request().method() === 'POST') {
      expect(route.request().postDataJSON()).toEqual({ role: 'MissionOwner', personId });
      systemAssigned = true;
      await route.fulfill({
        json: { status: 'success', data: roleSnapshot(true).data.roles[5] },
      });
      return;
    }
    await route.fulfill({ json: roleSnapshot(systemAssigned) });
  });
  await page.route('**/api/roles/organization', async (route) => {
    expect(route.request().postDataJSON()).toEqual({
      role: 'MissionOwner',
      personId,
      bootstrap: false,
    });
    organizationAssigned = true;
    await route.fulfill({
      json: { status: 'success', data: roleSnapshot(true, 'org-fallback').data.roles[5] },
    });
  });
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' }));
  });
});

test('assigns a per-system Mission Owner and preserves the override after refresh', async ({ page }) => {
  // Arrange
  await mockSystem(page, 'Issm');
  await page.goto(`/systems/${systemId}/roles`);

  // Act
  await expect(page.getByRole('link', { name: 'Roles & Permissions' })).toBeVisible();
  const missionOwnerRow = page.getByTestId('role-row-MissionOwner');
  await missionOwnerRow.getByRole('button', { name: 'Assign' }).click();
  await expect(page.getByRole('option', { name: 'Morgan Owner' })).toBeAttached();
  await page.getByLabel('Person').selectOption(personId);
  await page.getByRole('dialog').getByRole('button', { name: 'Assign' }).click();

  // Assert
  await expect(missionOwnerRow).toContainText('Morgan Owner');
  await expect(missionOwnerRow.getByRole('button', { name: 'Remove override' })).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('role-row-MissionOwner')).toContainText('Morgan Owner');
});

test('organization Mission Owner assignment clears the stale banner after refresh', async ({ page }) => {
  // Arrange
  await mockSystem(page, 'Issm');
  await page.goto(`/systems/${systemId}`);
  const banner = page.getByText('No Mission Owner Assigned');
  await expect(banner).toBeVisible();

  // Act
  await page.getByRole('button', { name: 'Assign Mission Owner' }).click();
  await expect(page.getByRole('option', { name: 'Morgan Owner' })).toBeAttached();
  await page.getByLabel('Person').selectOption(personId);
  await page.getByRole('dialog').getByRole('button', { name: 'Assign' }).click();

  // Assert
  await expect(banner).not.toBeVisible();
});

test('read-only caller sees permission guidance and no Mission Owner action', async ({ page }) => {
  // Arrange
  await mockSystem(page, null);

  // Act
  await page.goto(`/systems/${systemId}/roles`);
  const missionOwnerRow = page.getByTestId('role-row-MissionOwner');

  // Assert
  await expect(page.getByText(/read-only access/i)).toBeVisible();
  await expect(missionOwnerRow.getByRole('button', { name: 'Assign' })).toHaveCount(0);
});