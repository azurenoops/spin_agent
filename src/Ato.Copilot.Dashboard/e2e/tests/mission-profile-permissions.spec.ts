import { expect, test } from '@playwright/test';

for (const width of [1440, 390]) {
  test(`profile capability save and reload at ${width}px`, async ({ page }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    let canEditProfile = true;
    let governanceStatus = 'NotStarted';
    let draftContent: string | null = JSON.stringify({ operationalJustification: 'Retained operational need',
      businessFunctions: 'Retained functions', customSource: 'Retained provenance' });
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
        emassId: 'EMASS-SYN', ditprId: 'DITPR-SYN',
        currentRmfStep: 'Prepare', rmfPhase: 'Prepare', hostingEnvironment: 'Synthetic cloud',
        systemType: 'MajorApplication', missionCriticality: 'MissionEssential',
        activeAssessments: [], roleAssignments: [], boundaryResources: [],
      });
      if (endpoint === '/api/roles/system/synthetic-system') return json({ status: 'success', data: {
        systemId: 'synthetic-system', roles: [{ role: 'SystemOwner',
          person: { id: 'synthetic-owner', displayName: 'Synthetic System Owner' }, source: 'override' }],
      } });
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
    const card = page.getByRole('region', { name: 'System record', exact: true });
    const sidebar = page.getByRole('complementary', { name: 'Document contribution and next tasks' });
    await expect(sidebar.getByText('Used in your package', { exact: true })).toBeVisible();
    await expect(sidebar.getByText('Review & ownership', { exact: true })).toBeVisible();
    await expect(sidebar.getByText('Related work', { exact: true })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: 'Preview contribution', exact: true })).toHaveAttribute('href',
      '/systems/synthetic-system/documents/preview?contribution=MissionAndPurpose');
    await expect(sidebar.getByRole('link', { name: 'View package readiness', exact: true })).toHaveAttribute('href',
      '/systems/synthetic-system/documents?purpose=InitialSubmission');
    await expect(page.getByRole('link', { name: 'Preview contribution', exact: true })).toHaveCount(1);
    await expect(page.getByRole('link', { name: 'View package readiness', exact: true })).toHaveCount(1);
    const cardBox = (await card.boundingBox())!;
    const sidebarBox = (await sidebar.boundingBox())!;
    if (width > 1050) {
      expect(sidebarBox.x).toBeGreaterThan(cardBox.x + cardBox.width);
      expect(sidebarBox.y).toBe(cardBox.y);
    } else {
      expect(sidebarBox.y).toBeGreaterThan(cardBox.y + cardBox.height);
    }
    await expect(card.getByLabel('System owner', { exact: true })).toHaveValue('Synthetic System Owner');
    await expect(card.getByLabel('System name', { exact: true })).toHaveValue('Synthetic Profile System');
    await expect(card.getByLabel('eMASS system ID', { exact: true })).toHaveValue('EMASS-SYN');
    await expect(card.getByLabel('DITPR identifier', { exact: true })).toHaveValue('DITPR-SYN');
    for (const label of ['System name', 'System owner', 'System acronym', 'eMASS system ID', 'DITPR identifier']) {
      await expect(card.getByLabel(label, { exact: true })).toHaveAttribute('readonly');
    }
    const nameBox = (await card.getByLabel('System name', { exact: true }).boundingBox())!;
    const ownerBox = (await card.getByLabel('System owner', { exact: true }).boundingBox())!;
    const missionBox = (await card.getByLabel('Mission statement', { exact: true }).boundingBox())!;
    const purposeBox = (await card.getByLabel('Business purpose', { exact: true }).boundingBox())!;
    if (width > 650) {
      expect(nameBox.y).toBe(ownerBox.y);
      expect(ownerBox.x).toBeGreaterThan(nameBox.x + nameBox.width);
      expect(missionBox.width).toBeGreaterThan(nameBox.width * 2);
    } else {
      expect(ownerBox.y).toBeGreaterThan(nameBox.y + nameBox.height);
      expect(ownerBox.x).toBe(nameBox.x);
      expect(missionBox.width).toBe(nameBox.width);
    }
    expect(missionBox.height).toBe(72);
    expect(purposeBox.height).toBe(72);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(card.getByRole('button')).toHaveCount(0);
    await expect(card.getByText(/4,000/)).toHaveCount(0);
    await card.getByLabel('System version / release', { exact: true }).fill('Release 4.2');
    await card.getByLabel('Responsible organization', { exact: true }).fill('Synthetic Mission Directorate');
    await card.getByLabel('Program office / division', { exact: true }).fill('Synthetic Operations Division');
    const mission = page.getByPlaceholder("Describe the system's mission...");
    await mission.fill('Synthetic mission saved by assigned SystemOwner');
    await page.getByPlaceholder('Describe the business purpose...').fill('Synthetic purpose');
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Section saved as Draft.')).toBeVisible();
    await page.reload();

    // Assert
    await expect(mission).toHaveValue('Synthetic mission saved by assigned SystemOwner');
    await expect(card.getByLabel('Business purpose', { exact: true })).toHaveValue('Synthetic purpose');
    await expect(card.getByLabel('System version / release', { exact: true })).toHaveValue('Release 4.2');
    await expect(card.getByLabel('Responsible organization', { exact: true })).toHaveValue('Synthetic Mission Directorate');
    await expect(card.getByLabel('Program office / division', { exact: true })).toHaveValue('Synthetic Operations Division');
    expect(JSON.parse(draftContent!)).toEqual({
      operationalJustification: 'Retained operational need', businessFunctions: 'Retained functions', customSource: 'Retained provenance',
      systemVersion: 'Release 4.2', responsibleOrganization: 'Synthetic Mission Directorate',
      programOffice: 'Synthetic Operations Division', missionStatement: 'Synthetic mission saved by assigned SystemOwner',
      businessPurpose: 'Synthetic purpose',
    });
    await page.screenshot({ path: info.outputPath(`mission-record-${width}.png`), fullPage: true });
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
    await page.screenshot({ path: info.outputPath(`mission-record-readonly-${width}.png`), fullPage: true });
    expect(errors).toEqual([]);
  });
}