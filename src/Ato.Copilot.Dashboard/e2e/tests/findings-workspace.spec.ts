import { expect, test, type Page } from '@playwright/test';
import { rawRemediationWorkspace } from '../../src/__tests__/fixtures/remediationWorkspace';

const path = '/workspaces/organizations/org-a/systems/system-a/remediation';
async function fixture(page: Page, baseURL: string) {
  const state = { edits: 0, failLoad: false, moves: 0 };
  await page.route(/\/hubs\//, request => request.fulfill({ status: 503 }));
  await page.route(/^https?:\/\/[^/]+\/api\//, async intercepted => {
    const request = intercepted.request(), url = new URL(request.url()), path = url.pathname;
    const ok = (data: unknown) => intercepted.fulfill({ json: { status: 'success', data } });
    if (path === '/api/auth/login-config') return ok({
      branding: { deploymentName: 'Finding fixture', logoUrl: null, supportEmail: null }, defaultMethod: 'Entra',
      enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30, rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return ok({
      oid: 'assessor', directoryTenantId: 'directory-a', displayName: 'Alex Assessor', persona: 'SCA', homeTenant: null,
      effectiveTenant: { id: 'org-a', displayName: 'SPIN Demo Organization', status: 'Active' }, isImpersonating: false,
      impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [], tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'SPIN Demo Organization', personId: 'person-a',
        roles: ['Sca', 'ISSM'], permissions: { canAccessCsp: false, canManageMemberships: false, canManageOrganization: false, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return ok({ systemId: 'system-a', roles: ['Sca', 'ISSM'], permissions: {
      canRead: true, canEditProfile: true, canManageSystem: false, canAuthorNarratives: false, canReviewNarratives: false,
      canManageEvidence: true, canRunAssessments: true, canManageRemediation: true, canDecideAuthorization: false,
    } });
    if (path === '/api/dashboard/notifications/capabilities') return intercepted.fulfill({ json: {
      recipientId: 'assessor', rest: { available: true, reasonCode: null },
      realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
        reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: [] }, fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
    } });
    const root = '/api/dashboard/systems/system-a/remediation-workspace';
    if (path === root) return state.failLoad ? intercepted.fulfill({ status: 403, json: { error: 'Permission revoked. No queue loaded.' } })
      : intercepted.fulfill({ json: rawRemediationWorkspace });
    if (path === `${root}/tasks/task-a` && request.method() === 'PUT') {
      const body = request.postDataJSON();
      if (body.rowVersion !== 'task-revision-a') return intercepted.fulfill({ status: 409, json: { error: 'Stale revision.' } });
      state.edits++; return intercepted.fulfill({ json: { id: 'task-a' } });
    }
    if (path === '/api/dashboard/systems/system-a/tasks/task-a/ticket') return intercepted.fulfill({ json: {
      configured: true, canManage: true, mode: 'ManualPullOnly', webhooksSupported: false, bidirectionalSupported: false,
      link: { id: 'synthetic-link', provider: 'Jira', externalRef: 'TEST-42', externalUrl: 'https://tickets.example/browse/TEST-42',
        externalStatus: 'Closed', externalAssignee: 'Synthetic owner', lastSuccessfulSyncAt: '2026-09-29T14:00:00Z',
        state: 'Linked', rowVersion: '11111111-1111-1111-1111-111111111111', correlationKey: 'spin-task-synthetic', lastError: null },
    } });
    if (path.endsWith('/profile/completeness')) return intercepted.fulfill({ json: {
      systemId: 'system-a', totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false,
      incompleteSections: [], missionOwnerAssigned: false, missionOwnerName: null, daysSinceRegistration: 1,
    } });
    if (path.endsWith('/todos')) return intercepted.fulfill({ json: { items: [] } });
    if (path === '/api/dashboard/systems/system-a') return intercepted.fulfill({ json: {
      systemId: 'system-a', name: 'SPIN Demo System', acronym: 'SPIN', systemType: 'Application', missionCriticality: 'MissionSupport',
      hostingEnvironment: 'Cloud', impactLevel: 'IL4', baselineLevel: 'Moderate', currentRmfPhase: 'Assess',
      rmfPhaseProgress: [], keyMetrics: {}, recentActivity: [], categorization: null,
    } });
    return intercepted.fulfill({ status: 404, json: { error: `No synthetic fixture for ${path}` } });
  });
  return state;
}

for (const width of [1440, 390]) for (const theme of ['light', 'dark']) {
  test(`findings shared work and focus ${width}px ${theme}`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const state = await fixture(page, baseURL!);
    await page.goto(path);
    await expect(page.getByRole('heading', { name: 'Resolve assessment findings' })).toBeVisible();
    await page.locator('html').evaluate((el, dark) => el.classList.toggle('dark', dark), theme === 'dark');
    // Act
    await page.getByRole('searchbox').fill('timeout');
    await page.getByRole('button', { name: 'Board', exact: true }).click();
    await page.getByRole('button', { name: /Verify session timeout correction.*AC-12/ }).click();
    let drawer = page.getByRole('dialog', { name: 'Verify session timeout correction' });
    // Assert
    await expect(drawer.getByText('Plan revision 1', { exact: true })).toBeVisible();
    await drawer.getByRole('button', { name: 'Close dialog' }).focus();
    await page.keyboard.press('Shift+Tab');
    await expect(drawer.getByRole('button', { name: 'Review verification evidence' })).toBeFocused();
    // Act
    await drawer.getByRole('button', { name: 'Linked work', exact: true }).click();
    // Assert
    await expect(drawer.getByText('Session management', { exact: true })).toBeVisible();
    await expect(drawer.getByText('Pending', { exact: true })).toBeVisible();
    await expect(drawer.getByText('Due Oct 5, 2026', { exact: true }).first()).toBeVisible();
    await expect(drawer.getByText(/Linking does not accept risk/)).toBeVisible();
    await page.screenshot({ path: `test-results/findings-linked-${width}-${theme}.png`, fullPage: true });
    // Act
    await drawer.getByRole('button', { name: 'Correct session timeout settings', exact: true }).click();
    drawer = page.getByRole('dialog', { name: 'Correct session timeout settings' });
    await drawer.getByRole('button', { name: 'Edit task', exact: true }).click();
    const editor = page.getByRole('dialog', { name: 'Edit remediation task' });
    await editor.getByRole('textbox', { name: 'Corrective action' }).fill('Apply the approved session timeout setting.');
    await editor.getByRole('button', { name: 'Save task', exact: true }).click();
    // Assert
    await expect(drawer).toBeVisible();
    expect(state.edits).toBe(1);
    expect(state.moves).toBe(0);
    await drawer.getByRole('button', { name: 'Linked work', exact: true }).click();
    await expect(drawer.getByText('TEST-42', { exact: true })).toBeVisible();
    await expect(drawer.getByText('Closed', { exact: true })).toBeVisible();
    await expect(drawer.getByText(/External closure does not close this task/)).toBeVisible();
    await page.screenshot({ path: `test-results/findings-task-ticket-${width}-${theme}.png`, fullPage: true });
    await drawer.getByRole('button', { name: 'Overview', exact: true }).click();
    await expect(drawer.getByText('Ready to verify', { exact: true })).toBeVisible();
    const box = await drawer.boundingBox();
    expect(box!.width).toBeLessThanOrEqual(width);
    // Act
    await drawer.getByRole('button', { name: 'Close dialog' }).click();
    const finding = page.getByRole('dialog', { name: 'Verify session timeout correction' });
    await expect(finding).toBeVisible();
    await finding.getByRole('button', { name: 'Close dialog' }).click();
    // Assert
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('searchbox')).toHaveValue('timeout');
    await page.screenshot({ path: `test-results/findings-queue-${width}-${theme}.png`, fullPage: true });
  });
}
test('revoked queue access is not displayed as an empty assessment', async ({ page, baseURL }) => {
  // Arrange
  const state = await fixture(page, baseURL!); state.failLoad = true;
  // Act
  await page.goto(path);
  // Assert
  await expect(page.getByRole('alert').filter({ hasText: 'Permission revoked' })).toBeVisible();
  await expect(page.getByText('No findings recorded', { exact: true })).toHaveCount(0);
  // Act
  state.failLoad = false;
  await page.getByRole('button', { name: 'Retry workspace' }).click();
  // Assert
  await expect(page.getByRole('button', { name: 'Verify session timeout correction', exact: true })).toBeVisible();
});
