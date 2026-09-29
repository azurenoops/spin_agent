import { expect, test, type Page } from '@playwright/test';

const route = '/workspaces/organizations/org-a/systems/system-a/narratives';
const statement = {
  state: 'Missing', hasContent: false, hasApprovedContent: false,
  proposalId: null, proposalStatus: null, isStale: false,
};
const item = {
  id: 'implementation-a', controlId: 'AC-2', controlTitle: 'Account Management', family: 'AC',
  implementationStatus: 'Planned', currentVersion: 3, approvalStatus: 'Draft',
  policy: statement, technical: { ...statement, state: 'Draft', hasContent: true },
  nextAction: 'AuthorPolicy', nextActionLabel: 'Add policy statement', nextActionReason: null,
};

async function installFixture(page: Page, baseURL: string) {
  await page.route(/^https?:\/\/[^/]+\/api\//, async intercepted => {
    const request = intercepted.request();
    const path = new URL(request.url()).pathname;
    const success = (data: unknown) => intercepted.fulfill({ json: { status: 'success', data } });
    if (path === '/api/auth/login-config') return success({
      branding: { deploymentName: 'SPIN narrative fixture', logoUrl: null, supportEmail: null },
      defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
      rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return success({
      oid: 'narrative-user', directoryTenantId: 'directory-a', displayName: 'Narrative reviewer', persona: 'ISSM',
      homeTenant: null, effectiveTenant: { id: 'org-a', displayName: 'Organization A', status: 'Active' },
      isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'Organization A',
        personId: 'person-a', roles: ['ISSM'], permissions: { canAccessCsp: false, canManageMemberships: false,
          canManageOrganization: false, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return success({
      systemId: 'system-a', roles: ['ISSM'], permissions: {
        canRead: true, canEditProfile: false, canManageSystem: false, canAuthorNarratives: true,
        canReviewNarratives: true, canManageEvidence: true, canRunAssessments: false,
        canManageRemediation: false, canDecideAuthorization: false,
      },
    });
    if (path === '/api/dashboard/systems/system-a/narrative-workspace') return intercepted.fulfill({ json: {
      systemId: 'system-a', counts: { needsAttention: 1, allControls: 1, approvedStatements: 0, proposedUpdates: 0 },
      items: [item], permissions: { canAuthor: true, canReview: true, canManageEvidence: true },
    } });
    if (path === '/api/dashboard/systems/system-a/narrative-workspace/AC-2') return intercepted.fulfill({ json: {
      systemId: 'system-a', id: item.id, controlId: item.controlId, controlTitle: item.controlTitle,
      family: item.family, implementationStatus: item.implementationStatus,
      approvalStatus: item.approvalStatus, currentVersion: item.currentVersion,
      statements: {
        policy: { currentContent: '', approvedContent: '', state: 'Missing' },
        technical: { currentContent: 'Entra ID enforces conditional access.', approvedContent: '', state: 'Draft' },
      },
      proposals: [], responsibilities: [],
      history: [{ versionNumber: 3, status: 'Draft', authoredBy: 'Narrative author',
        authoredAt: '2026-09-01T00:00:00Z', changeReason: 'Updated identity flow', reviews: [] }],
      permissions: { canAuthor: true, authorReason: null, canReview: true, reviewReason: null,
        canManageEvidence: true, evidenceReason: null },
    } });
    if (path === '/api/dashboard/systems/system-a/narrative-library') return intercepted.fulfill({ json: [] });
    if (path === '/api/dashboard/systems/system-a/narrative-library/proposals') return intercepted.fulfill({ json: [] });
    if (path === '/api/dashboard/systems/system-a/narrative-library/access') return intercepted.fulfill({ json: {
      tenantId: 'org-a', systemName: 'Synthetic system', canAuthor: true, canPublishShared: false,
      canGenerate: true, capabilities: [],
    } });
    if (path === '/api/dashboard/systems/system-a/profile/completeness') return intercepted.fulfill({ json: {
      systemId: 'system-a', totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false,
      incompleteSections: [], missionOwnerAssigned: false, missionOwnerName: null, daysSinceRegistration: 1,
    } });
    if (path === '/api/dashboard/systems/system-a/todos') return intercepted.fulfill({ json: { items: [] } });
    if (path === '/api/dashboard/systems/system-a') return intercepted.fulfill({ json: {
      systemId: 'system-a', name: 'Synthetic system', acronym: 'SYN', systemType: 'Application',
      missionCriticality: 'MissionSupport', hostingEnvironment: 'Cloud', impactLevel: 'IL4',
      baselineLevel: 'Moderate', currentRmfPhase: 'Implement', rmfPhaseProgress: [], keyMetrics: {},
      recentActivity: [], categorization: null,
    } });
    return intercepted.fulfill({ status: 404, json: { title: `No fixture for ${path}` } });
  });
}

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`preserves narrative list state and provides an accessible drawer at ${viewport.width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize(viewport);
    await installFixture(page, baseURL!);

    // Act
    await page.goto(`${route}?view=all&search=account`);

    // Assert
    await expect(page.getByRole('heading', { name: 'Document how your controls work' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'All controls (1)' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByText('Account Management')).toBeVisible();
    await page.locator('html').evaluate(element => element.classList.add('dark'));
    await expect(page.locator('.cnw-page')).toHaveCSS('color', 'rgb(232, 235, 243)');

    // Act
    await page.getByRole('button', { name: /Account Management AC-2/ }).click();

    // Assert
    const drawer = page.getByRole('dialog', { name: 'AC-2 Account Management' });
    await expect(drawer).toBeVisible();
    await page.reload();
    await expect(drawer).toBeVisible();
    await drawer.getByRole('tab', { name: 'Technical statement' }).click();
    await expect(drawer.getByText('Entra ID enforces conditional access.')).toBeVisible();
    await drawer.getByRole('tab', { name: 'History' }).click();
    await expect(drawer.getByText('Updated identity flow')).toBeVisible();
    if (viewport.width < 700) {
      const box = await drawer.boundingBox();
      expect(box?.width).toBeGreaterThanOrEqual(viewport.width - 1);
    }

    // Act
    await page.goBack();

    // Assert
    await expect(drawer).toBeHidden();
    await expect(page.getByLabel('Search controls')).toHaveValue('account');
    expect(new URL(page.url()).searchParams.get('view')).toBe('all');

    // Act / Assert
    await page.getByRole('button', { name: /Account Management AC-2/ }).click();
    await expect(drawer).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
  });
}
