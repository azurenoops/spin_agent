import { expect, test, type Page } from '@playwright/test';

const systemId = 'async-error-system';
const systemDetail = {
  systemId,
  name: 'Async Error Test System',
  acronym: 'AETS',
  systemType: 'MajorApplication',
  missionCriticality: 'MissionSupport',
  hostingEnvironment: 'AzureGovernment',
  impactLevel: 'IL4',
  baselineLevel: 'Moderate',
  currentRmfPhase: 'Assess',
  rmfPhaseProgress: [],
  keyMetrics: {
    complianceScore: 0,
    priorScore: 0,
    totalOpenPoams: 0,
    overduePoams: 0,
    totalFindings: 0,
    narrativeCoverage: 0,
    activeDeviations: 0,
  },
  recentActivity: [],
  categorization: null,
};

async function mockSystemShell(page: Page) {
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
  await page.route(`**/api/dashboard/systems/${systemId}/profile/completeness`, (route) =>
    route.fulfill({
      json: {
        systemId,
        totalSections: 0,
        statusCounts: {},
        approvedPercentage: 0,
        isProfileComplete: false,
        incompleteSections: [],
        missionOwnerAssigned: false,
        missionOwnerName: null,
        daysSinceRegistration: 1,
      },
    }),
  );
  await page.route(`**/api/dashboard/systems/${systemId}/todos`, (route) =>
    route.fulfill({ json: { items: [] } }),
  );
  await page.route(`**/api/dashboard/systems/${systemId}`, (route) => route.fulfill({
    json: systemDetail,
  }));
}

test.describe('Dashboard async error states', () => {
  test('Gap Analysis shows a user-readable error and retry when loading fails', async ({ page }) => {
    // Arrange
    await mockSystemShell(page);
    await page.route(`**/api/dashboard/systems/${systemId}/gap-analysis`, (route) =>
      route.fulfill({ status: 500, json: { error: 'internal-code-123' } }),
    );

    // Act
    await page.goto(`/systems/${systemId}/gap-analysis`);

    // Assert
    await expect(page.getByRole('alert')).toContainText('Unable to load gap analysis');
    await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
    await expect(page.getByText('internal-code-123')).not.toBeVisible();
    await expect(page.getByText(/endpoint may not be configured/i)).not.toBeVisible();
  });

  test('Gap Analysis identifies a failed refresh while retaining stale data', async ({ page }) => {
    // Arrange
    await mockSystemShell(page);
    let shouldFail = false;
    await page.route(`**/api/dashboard/systems/${systemId}/gap-analysis`, (route) => shouldFail
      ? route.fulfill({ status: 500, json: { error: 'refresh-internal-code' } })
      : route.fulfill({
        json: {
          totalGaps: 1,
          criticalCount: 1,
          highCount: 0,
          moderateCount: 0,
          lowCount: 0,
          items: [{ controlId: 'AC-2', controlTitle: 'Account Management', gapType: 'Narrative', severity: 'Critical', description: 'Evidence required' }],
        },
      }),
    );
    await page.goto(`/systems/${systemId}/gap-analysis`);
    await expect(page.getByText('AC-2')).toBeVisible();

    // Act
    shouldFail = true;
    await page.getByRole('button', { name: /refresh/i }).click();

    // Assert
    await expect(page.getByRole('alert')).toContainText('Unable to refresh gap analysis');
    await expect(page.getByText('AC-2')).toBeVisible();
    await expect(page.getByText('refresh-internal-code')).not.toBeVisible();
  });

  test('System Detail shows a user-readable error and recovers on retry', async ({ page }) => {
    // Arrange
    await mockSystemShell(page);
    let shouldFail = true;
    await page.route(`**/api/dashboard/systems/${systemId}`, (route) => {
      if (shouldFail) {
        return route.fulfill({ status: 500, json: { error: 'system-internal-code' } });
      }
      return route.fulfill({ json: systemDetail });
    });

    // Act
    await page.goto(`/systems/${systemId}`);

    // Assert
    await expect(page.getByRole('alert')).toContainText('Unable to load system detail');
    await expect(page.getByText('system-internal-code')).not.toBeVisible();
    shouldFail = false;
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByText('Async Error Test System').first()).toBeVisible();
  });

  test('Mission Profile shows a user-readable load error and retry', async ({ page }) => {
    // Arrange
    await mockSystemShell(page);
    let shouldFail = true;
    await page.route(
      `**/api/dashboard/systems/${systemId}/profile/MissionAndPurpose`,
      (route) => shouldFail
        ? route.fulfill({ status: 500, json: { error: 'profile-internal-code' } })
        : route.fulfill({
          json: {
            systemId,
            sectionType: 'MissionAndPurpose',
            governanceStatus: 'Draft',
            draftContent: '',
            reviewerComments: null,
          },
        }),
    );

    // Act
    await page.goto(`/systems/${systemId}/profile/MissionAndPurpose`);

    // Assert
    await expect(page.getByRole('alert')).toContainText('Unable to load profile section');
    await expect(page.getByText('profile-internal-code')).not.toBeVisible();
    shouldFail = false;
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByRole('alert')).not.toBeVisible();
    await expect(page.getByRole('heading', { name: 'Mission & Purpose' })).toBeVisible();
  });

  test('POA&M shows a user-readable list error and recovers on retry', async ({ page }) => {
    // Arrange
    await mockSystemShell(page);
    let shouldFail = true;
    await page.route(`**/api/dashboard/systems/${systemId}/poam**`, (route) => shouldFail
      ? route.fulfill({ status: 500, json: { error: 'poam-internal-code' } })
      : route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 25 } }),
    );
    await page.route(`**/api/dashboard/systems/${systemId}/poam/metrics`, (route) =>
      route.fulfill({
        json: {
          totalOpen: 0,
          overdue: 0,
          dueSoon: 0,
          catICount: 0,
          catIICount: 0,
          catIIICount: 0,
        },
      }),
    );

    // Act
    await page.goto(`/systems/${systemId}/poam`);

    // Assert
    await expect(page.getByRole('alert')).toContainText('Unable to load POA&M items');
    await expect(page.getByText('poam-internal-code')).not.toBeVisible();
    shouldFail = false;
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByRole('alert')).not.toBeVisible();
    await expect(page.getByText('No POA&M items found')).toBeVisible();
  });

  test('POA&M detail drawer shows a user-readable error and recovers on retry', async ({ page }) => {
    // Arrange
    await mockSystemShell(page);
    const poamItem = {
      id: 'poam-1',
      controlId: 'AC-2',
      weakness: 'Account review evidence is incomplete.',
      catSeverity: 'II',
      status: 'Ongoing',
      components: [],
      poc: 'ISSO',
      dueDate: '2026-12-01T00:00:00Z',
      daysRemaining: 60,
      milestoneProgress: { completed: 0, total: 0 },
      deviationType: null,
      externalTicketRef: null,
      remediationTaskId: null,
      remediationTaskStatus: null,
      isOverdue: false,
      systemId,
      systemName: 'Async Error Test System',
    };
    await page.route(`**/api/dashboard/systems/${systemId}/poam**`, (route) =>
      route.fulfill({ json: { items: [poamItem], totalCount: 1, page: 1, pageSize: 25 } }),
    );
    await page.route(`**/api/dashboard/systems/${systemId}/poam/metrics`, (route) =>
      route.fulfill({
        json: { totalOpen: 1, overdue: 0, dueSoon: 0, catICount: 0, catIICount: 1, catIIICount: 0 },
      }),
    );
    let shouldFail = true;
    await page.route('**/api/dashboard/poam/poam-1', (route) => shouldFail
      ? route.fulfill({ status: 500, json: { error: 'detail-internal-code' } })
      : route.fulfill({
        json: {
          ...poamItem,
          weaknessSource: 'Assessment',
          pocEmail: null,
          resourcesRequired: null,
          costEstimate: null,
          scheduledCompletionDate: poamItem.dueDate,
          actualCompletionDate: null,
          comments: null,
          findingId: null,
          deviationId: null,
          createdAt: '2026-09-01T00:00:00Z',
          modifiedAt: null,
          createdBy: null,
          rowVersion: '1',
          milestones: [],
          history: [],
          ticketSync: null,
        },
      }),
    );

    // Act
    await page.goto(`/systems/${systemId}/poam`);
    await page.getByText('AC-2').click();

    // Assert
    await expect(page.getByRole('alert')).toContainText('Unable to load POA&M details');
    await expect(page.getByText('detail-internal-code')).not.toBeVisible();
    shouldFail = false;
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByRole('heading', { name: 'AC-2' })).toBeVisible();
  });
});