import { expect, test, type Page } from '@playwright/test';

const systemId = 'boundary-picker-system';
const boundaryId = 'boundary-picker-primary';

async function mockShell(page: Page) {
  await page.route('**/api/csp/onboarding/state', (route) => route.fulfill({ status: 404 }));
  await page.route('**/api/onboarding/organization-context', (route) => route.fulfill({ json: { ok: true, data: null } }));
  await page.route('**/api/onboarding/state', (route) => route.fulfill({ json: { ok: true, data: null } }));
  await page.route('**/api/onboarding/tenant/state', (route) => route.fulfill({
    json: { status: 'success', data: { onboardingState: 'Active', currentStep: 'Submitted', completedSteps: ['Submitted'] } },
  }));
  await page.route('**/api/auth/login-config', (route) => route.fulfill({
    json: {
      status: 'success',
      data: {
        branding: { deploymentName: 'Security Posture Intelligence Navigator', logoUrl: null, supportEmail: null },
        defaultMethod: 'Simulation', enabledMethods: [{ id: 'Simulation', displayName: 'Simulation' }],
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
      oid: 'tester', displayName: 'Boundary Tester', persona: 'ISSM',
      homeTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      effectiveTenant: { id: 'tenant-1', displayName: 'Test Tenant', status: 'Active' },
      isImpersonating: false, impersonation: null, pimRoles: [], isCspAdmin: false,
      isSocAnalyst: false, tenantMemberships: [],
    },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/profile/completeness`, (route) => route.fulfill({
    json: { systemId, totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false, incompleteSections: [] },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/todos`, (route) => route.fulfill({ json: { items: [] } }));
  await page.route(`**/api/dashboard/systems/${systemId}`, (route) => route.fulfill({
    json: {
      systemId, name: 'Boundary Picker System', acronym: 'BPS', systemType: 'MajorApplication',
      missionCriticality: 'MissionSupport', hostingEnvironment: 'AzureGovernment', currentRmfPhase: 'Prepare',
      rmfPhaseProgress: [], recentActivity: [], categorization: null, keyMetrics: {},
    },
  }));
}

test('boundary picker shows eligible tenant and CSP sources and submits CSP provenance', async ({ page }) => {
  // Arrange
  await mockShell(page);
  let assignmentPayload: Record<string, unknown> | undefined;
  await page.route('**/api/dashboard/components**', (route) => route.fulfill({
    json: { items: [{ id: 'count-only' }], totalCount: 1, page: 1, pageSize: 1 },
  }));
  await page.route(`**/api/dashboard/systems/${systemId}/boundary-definitions`, (route) => route.fulfill({
    json: {
      items: [{
        id: boundaryId, name: 'Primary Boundary', boundaryType: 'Logical', description: 'Primary scope',
        isPrimary: true, componentCount: 0, controlCount: 0, coveragePercent: 0,
      }],
      totalCount: 1,
    },
  }));
  await page.route(`**/api/dashboard/boundary-definitions/${boundaryId}/resources`, (route) => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route(`**/api/dashboard/boundary-definitions/${boundaryId}/components`, (route) => route.fulfill({ json: { items: [], totalCount: 0 } }));
  await page.route(`**/api/dashboard/systems/${systemId}/boundary-definitions/${boundaryId}/lock`, (route) => route.fulfill({ json: { locked: false } }));
  await page.route(`**/api/dashboard/systems/${systemId}/boundary-definitions/${boundaryId}/components**`, async (route) => {
    if (route.request().method() === 'POST') {
      assignmentPayload = route.request().postDataJSON() as Record<string, unknown>;
      await route.fulfill({
        status: 201,
        json: {
          assignmentId: 'assignment-csp', componentId: 'csp-1', componentName: 'Azure Key Vault',
          componentType: 'Thing', source: 'CSP', isInScope: true, createdAt: '2026-03-01T00:00:00Z', createdBy: 'tester',
        },
      });
      return;
    }
    await route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 50 } });
  });
  await page.route(`**/api/dashboard/systems/${systemId}/boundary-definitions/${boundaryId}/component-candidates**`, (route) => route.fulfill({
    json: {
      items: [
        { id: 'org-1', name: 'Organization Policy', componentType: 'Policy', description: null, source: 'Organization' },
        { id: 'system-thing-1', name: 'System Workload', componentType: 'Thing', description: null, source: 'System' },
        { id: 'csp-1', name: 'Azure Key Vault', componentType: 'Thing', description: 'Managed key service', source: 'CSP' },
      ],
    },
  }));

  // Act
  await page.goto(`/systems/${systemId}/boundaries`);
  await page.getByText('Primary Boundary', { exact: true }).click();
  await page.getByRole('button', { name: /assign component/i }).first().click();

  // Assert
  await expect(page.getByText('Organization Policy')).toBeVisible();
  await expect(page.getByText('System Workload')).toBeVisible();
  await expect(page.getByText('Azure Key Vault')).toBeVisible();
  await expect(page.getByText('Person', { exact: true })).toHaveCount(0);
  await expect(page.getByText('CSP', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Add', exact: true }).last().click();
  await expect.poll(() => assignmentPayload).toEqual({ componentId: 'csp-1', source: 'CSP', isInScope: true });
});