import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

for (const width of [1440, 390]) {
  test(`provider setup entry and resume preserve workspace context at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    const requests = await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await context.route('**/api/csp/onboarding/setup', route => route.fulfill({ json: { status: 'success', data: {
      providerId: 'provider-a',
      profile: { cspProfileId: 'provider-a', onboardingState: 'Active', currentStep: 'Complete',
        identity: { legalEntityName: 'Synthetic operator', displayName: 'Synthetic Provider', logoUrl: null } },
      profileRevision: 1,
      draft: { draftId: 'provider-draft', revision: 2, schemaVersion: 1, currentScreen: 'p-sources',
        savedAt: '2026-09-30T12:00:00Z', savedBy: 'Synthetic Owner', fields: {}, completion: null },
      access: { state: 'Authorized', scope: 'Provider', actor: { displayName: 'Synthetic Owner' } },
      handling: { state: 'Unknown', uploadsPermitted: false, analysisPermitted: false },
      uploadIntents: [], facts: [],
    } } }));
    await context.route('**/api/csp/organization-onboarding/drafts?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [], page: 1, pageSize: 25, total: 0 },
    } }));
    await page.setViewportSize({ width, height: 1000 });
    // Act
    await page.goto('/workspaces/csp/setup');
    // Assert
    await expect(page.getByRole('heading', { level: 1, name: 'What are you setting up?' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Set up provider' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Set up organization' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Set up system' })).toHaveCount(0);
    const stepNavigation = page.getByRole('navigation', { name: 'Start setup steps', includeHidden: true });
    if (width === 390) await expect(stepNavigation).toBeHidden();
    else await expect(stepNavigation).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: test.info().outputPath('setup-start.png'), fullPage: true });
    // Act
    await page.getByRole('button', { name: 'View saved setup' }).click();
    // Assert
    await expect(page).toHaveURL('/workspaces/csp/setup/resume');
    await expect(page.getByRole('heading', { name: 'Continue your setup', level: 1 })).toBeFocused();
    await expect(page.getByRole('link', { name: 'Continue Synthetic Provider' })).toHaveAttribute('href', /\/workspaces\/csp\/onboarding\/csp\?reentry=resume/);
    await expect(page.getByText('No saved setup records.')).toHaveCount(0);
    await page.screenshot({ path: test.info().outputPath('setup-resume.png'), fullPage: true });
    // Act
    await page.reload();
    await page.goBack();
    // Assert
    await expect(page.getByRole('heading', { level: 1, name: 'What are you setting up?' })).toBeVisible();
    expect(requests.filter(request => request.method !== 'GET')).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('organization setup entry uses server create permission and does not probe provider data', async ({ page, context, baseURL }) => {
  // Arrange
  const requests = await installWorkspaceFixture(context, baseURL!);
  await context.route('**/api/workspaces/organizations/org-a/systems/setup-access', route => route.fulfill({ json: {
    status: 'success', data: { canCreateSystem: true },
  } }));
  await context.route('**/api/workspaces/organizations/org-a/systems/setup-drafts?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [], nextCursor: null },
  } }));
  // Act
  await page.goto('/workspaces/organizations/org-a/setup');
  // Assert
  await expect(page.getByRole('link', { name: 'Set up system' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/new');
  await expect(page.getByRole('link', { name: 'Set up provider' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Set up organization' })).toHaveCount(0);
  expect(requests.filter(request => request.path.startsWith('/api/csp/'))).toEqual([]);
});

test('unavailable saved setup offers retry rather than a successful empty result', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!);
  await context.route('**/api/workspaces/organizations/org-a/systems/setup-access', route => route.fulfill({ json: {
    status: 'success', data: { canCreateSystem: false },
  } }));
  await context.route('**/api/workspaces/organizations/org-a/systems/setup-drafts?*', route => route.fulfill({
    status: 503, json: { status: 'error', error: { message: 'Saved setup is unavailable.', errorCode: 'UNAVAILABLE' } },
  }));
  // Act
  await page.goto('/workspaces/organizations/org-a/setup/resume');
  // Assert
  await expect(page.getByRole('alert')).toContainText('Saved setup is unavailable.');
  await expect(page.getByRole('button', { name: 'Retry loading setup' })).toBeVisible();
  await expect(page.getByText('No saved setup records.')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Set up system' })).toHaveCount(0);
});

test('pending organization activation resumes its saved draft before system setup', async ({ page, context, baseURL }) => {
  // Arrange
  const requests = await installWorkspaceFixture(context, baseURL!);
  await context.route('**/api/auth/me', async route => {
    await route.fulfill({ json: { status: 'success', data: {
      oid: 'synthetic-admin', directoryTenantId: 'synthetic-directory', displayName: 'Synthetic Administrator',
      persona: 'Administrator', homeTenant: { id: 'org-a', displayName: 'Organization A', status: 'Active' },
      effectiveTenant: { id: 'org-a', displayName: 'Organization A', status: 'Active' },
      isImpersonating: false, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', displayName: 'Organization A', mode: 'ordinary',
        personId: 'person-a', roles: ['Administrator'], permissions: {
          canManageMemberships: true, canManageOrganization: true, canAccessCsp: false,
        } },
    } } });
  });
  await context.route('**/api/onboarding/tenant/state', route => route.fulfill({ json: { status: 'success', data: {
    tenantId: 'org-a', currentStep: 'Tenant.LegalEntity', completedSteps: [], onboardingState: 'InWizard',
    firstOrganizationId: null, draftRevision: 1,
    draft: { schemaVersion: 1, revision: 1, currentStep: 'Tenant.LegalEntity',
      values: { legalEntity: { legalEntityName: 'Saved tenant draft' } }, savedAt: '2026-09-30T12:00:00Z' },
  } } }));
  // Act
  await page.goto('/workspaces/organizations/org-a/setup/resume');
  // Assert
  await expect(page.getByRole('link', { name: 'Continue Organization activation' })).toHaveAttribute('href', '/workspaces/organizations/org-a/onboarding/tenant');
  await expect(page.getByText('Draft saved; tenant activation is unfinished.')).toBeVisible();
  expect(requests.filter(request => request.path.includes('/systems/setup-'))).toEqual([]);
});

test('unfinished provider survives saved-exit reload without blocked organization probes', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const organizationReads: string[] = [];
  await context.route('**/api/csp/onboarding/state', route => route.fulfill({ json: { status: 'success', data: {
    cspProfileId: 'provider-a', onboardingState: 'InWizard', currentStep: 'Identity', identity: null,
  } } }));
  await context.route('**/api/csp/onboarding/setup', route => route.fulfill({ json: { status: 'success', data: {
    providerId: 'provider-a', profile: { onboardingState: 'InWizard', identity: { displayName: 'Pending provider' } },
    draft: { draftId: 'saved-provider', completion: null }, uploadIntents: [], facts: [],
  } } }));
  await context.route('**/api/csp/organization-onboarding/drafts?*', route => {
    organizationReads.push(route.request().url());
    return route.fulfill({ status: 503, json: { status: 'error', error: { message: 'CSP onboarding incomplete.' } } });
  });
  // Act
  await page.goto('/workspaces/csp/setup/resume');
  await page.reload();
  // Assert
  await expect(page.getByRole('heading', { name: 'Continue your setup', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Continue Pending provider' })).toBeVisible();
  await expect(page).toHaveURL('/workspaces/csp/setup/resume');
  expect(organizationReads).toEqual([]);
});
