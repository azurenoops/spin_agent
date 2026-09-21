import { expect, test } from '@playwright/test';

const scenarios = [1440, 390].flatMap(width =>
  ['mission-purpose', 'MISSION-PURPOSE/', '%6dission-purpose'].map(alias => ({ width, alias })),
);

for (const { width, alias } of scenarios) {
  test(`legacy ${alias} deep link retains query, fragment and refresh at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    const tenant = { id: 'synthetic-org', displayName: 'Synthetic Organization', status: 'Active' };
    let profileReads = 0;
    await page.route(/^https?:\/\/[^/]+\/api\//, async route => {
      const endpoint = new URL(route.request().url()).pathname;
      const json = (data: unknown) => route.fulfill({ json: data });
      if (endpoint === '/api/auth/login-config') {
        return json({ status: 'success', data: {
          branding: { deploymentName: 'Workspace route fixture', logoUrl: null, supportEmail: null },
          defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
          rememberTenantCookieDays: 0, simulation: null,
          msal: {
            clientId: '11111111-1111-1111-1111-111111111111',
            authority: 'https://login.microsoftonline.com/common',
            redirectUri: `${baseURL}/login/callback`,
            postLogoutRedirectUri: `${baseURL}/`,
          },
        } });
      }
      if (endpoint === '/api/auth/me') {
        return json({ status: 'success', data: {
          oid: 'synthetic-owner', displayName: 'Synthetic Owner', persona: 'MissionOwner',
          homeTenant: tenant, effectiveTenant: tenant, tenantMemberships: [tenant],
          isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
        } });
      }
      if (endpoint.endsWith('/organization-context')) return json({ ok: true, data: null });
      if (endpoint.endsWith('/profile/completeness')) {
        return json({ statusCounts: {}, totalSections: 6, approvedPercentage: 0, incompleteSections: [] });
      }
      if (endpoint.endsWith('/todos')) return json({ items: [], currentPhase: 'Prepare', nextPhase: 'Categorize' });
      if (endpoint.endsWith('/systems/synthetic-system')) {
        return json({
          systemId: 'synthetic-system', name: 'Synthetic Workspace System', acronym: 'SYN',
          currentRmfStep: 'Prepare', rmfPhase: 'Prepare', hostingEnvironment: 'Synthetic cloud',
          systemType: 'MajorApplication', missionCriticality: 'MissionEssential',
          activeAssessments: [], roleAssignments: [], boundaryResources: [],
        });
      }
      if (endpoint.endsWith('/profile/MissionAndPurpose')) {
        profileReads++;
        return json({
          id: 'synthetic-profile', sectionType: 'MissionAndPurpose', canEditProfile: false,
          governanceStatus: 'Draft',
          draftContent: JSON.stringify({ missionStatement: 'Synthetic approved-scope mission' }),
          userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
        });
      }
      return route.fulfill({ status: 404, json: { error: 'Endpoint not part of this routing fixture' } });
    });

    // Act
    await page.goto(`/systems/synthetic-system/${alias}?view=review#mission`);
    await expect(page).toHaveURL(/\/systems\/synthetic-system\/profile\/MissionAndPurpose\?view=review#mission$/);
    const mission = page.getByPlaceholder("Describe the system's mission...");
    await expect(mission).toHaveValue('Synthetic approved-scope mission');
    await page.reload();

    // Assert
    await expect(page).toHaveURL(/\/systems\/synthetic-system\/profile\/MissionAndPurpose\?view=review#mission$/);
    await expect(mission).toHaveValue('Synthetic approved-scope mission');
    await expect(page.getByText('Read-only', { exact: true })).toBeVisible();
    expect(profileReads).toBeGreaterThanOrEqual(2);
    expect(errors).toEqual([]);
  });
}
