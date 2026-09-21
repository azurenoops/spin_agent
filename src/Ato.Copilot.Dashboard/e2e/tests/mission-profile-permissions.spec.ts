import { expect, test } from '@playwright/test';

for (const width of [1440, 390]) {
  test(`profile capability save and reload at ${width}px`, async ({ page }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    let canEditProfile = true;
    let governanceStatus = 'NotStarted';
    let draftContent: string | null = null;
    let writes = 0;
    const tenant = { id: 'synthetic-tenant', displayName: 'Synthetic Organization', status: 'Active' };
    await page.route(/^https?:\/\/[^/]+\/api\//, async route => {
      const endpoint = new URL(route.request().url()).pathname;
      const json = (body: unknown) => route.fulfill({ json: body });
      if (endpoint === '/api/auth/login-config') return json({ status: 'success', data: {
        branding: { deploymentName: 'Synthetic', logoUrl: null, supportEmail: null },
        defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
        rememberTenantCookieDays: 0, simulation: null,
        msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common', redirectUri: 'http://localhost:4178/login/callback', postLogoutRedirectUri: 'http://localhost:4178/' },
      } });
      if (endpoint === '/api/auth/me') return json({ status: 'success', data: {
        oid: 'synthetic-owner', displayName: 'Synthetic Owner', persona: 'Engineer',
        homeTenant: tenant, effectiveTenant: tenant, tenantMemberships: [tenant],
        isImpersonating: false, isCspAdmin: false, pimRoles: [],
      } });
      if (endpoint.endsWith('/organization-context')) return json({ organizationName: 'Synthetic Organization' });
      if (endpoint.endsWith('/profile/completeness')) return json({ statusCounts: {}, totalSections: 5, approvedPercentage: 0, incompleteSections: [] });
      if (endpoint.endsWith('/todos')) return json({ items: [], currentPhase: 'Prepare', nextPhase: 'Categorize' });
      if (endpoint.endsWith('/systems/synthetic-system')) return json({
        systemId: 'synthetic-system', name: 'Synthetic Profile System', acronym: 'SYN',
        currentRmfStep: 'Prepare', rmfPhase: 'Prepare', hostingEnvironment: 'Synthetic cloud',
        systemType: 'MajorApplication', missionCriticality: 'MissionEssential',
        activeAssessments: [], roleAssignments: [], boundaryResources: [],
      });
      if (endpoint.endsWith('/profile/MissionAndPurpose')) {
        if (route.request().method() === 'PUT') {
          writes++;
          if (!canEditProfile) return route.fulfill({ status: 403, json: { error: 'Profile author access was revoked.', errorCode: 'UNAUTHORIZED' } });
          draftContent = route.request().postDataJSON().content;
          governanceStatus = 'Draft';
        }
        return json({ id: 'synthetic-section', sectionType: 'MissionAndPurpose', canEditProfile,
          governanceStatus, draftContent, userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] });
      }
      return route.fulfill({ status: 404, json: { error: 'Not part of this synthetic fixture' } });
    });

    // Act
    await page.goto('/systems/synthetic-system/profile/MissionAndPurpose');
    const mission = page.getByPlaceholder("Describe the system's mission...");
    await mission.fill('Synthetic mission saved by assigned SystemOwner');
    await page.getByPlaceholder('Describe the business purpose...').fill('Synthetic purpose');
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Section saved as Draft.')).toBeVisible();
    await page.reload();

    // Assert
    await expect(mission).toHaveValue('Synthetic mission saved by assigned SystemOwner');
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toBeVisible();
    expect(writes).toBe(1);
    canEditProfile = false;
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Profile author access was revoked.')).toBeVisible();
    await page.reload();
    await expect(page.getByText('Read-only', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
    canEditProfile = true;
    governanceStatus = 'UnderReview';
    await page.reload();
    await expect(page.getByText('Read-only', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
    await page.screenshot({ path: `/tmp/ato-968-profile-${width}.png`, fullPage: true });
    expect(errors).toEqual([]);
  });
}