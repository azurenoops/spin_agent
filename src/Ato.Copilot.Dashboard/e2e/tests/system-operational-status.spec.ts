import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`operational status is explicitly recorded without changing profile or authorization at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await installSystemCapabilityFixture(context, baseURL!);
    await page.setViewportSize({ width, height: 1050 });
    let status: string | null = null;
    const writes: string[] = [];
    await context.route('**/api/dashboard/systems/system-a/operational-status', route => {
      if (route.request().method() === 'PUT') {
        status = route.request().postDataJSON().operationalStatus;
        writes.push(status!);
      }
      return route.fulfill({ json: { systemId: 'system-a', operationalStatus: status, canManage: true } });
    });
    await context.route('**/api/dashboard/systems/system-a/profile/MissionAndPurpose', route => {
      expect(route.request().method()).toBe('GET');
      return route.fulfill({ json: { id: 'profile-a', sectionType: 'MissionAndPurpose', governanceStatus: 'Draft',
        canEditProfile: true, draftContent: '{"missionStatement":"Synthetic mission","businessPurpose":"Synthetic purpose"}',
        approvedContent: null, userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    const returnTo = '/systems/system-a/documents?purpose=InitialSubmission&run=run-a';
    await page.goto(`${root}/profile/MissionAndPurpose?${new URLSearchParams({ readinessReturn: returnTo })}`);
    // Act
    await page.getByRole('button', { name: 'Manage operational status', exact: true }).click();
    const drawer = page.getByRole('dialog', { name: 'Record operational status', exact: true });
    await expect(drawer.getByRole('combobox', { name: 'Operational status', exact: true })).toHaveValue('');
    await expect(drawer.getByRole('button', { name: 'Save operational status', exact: true })).toBeDisabled();
    await drawer.getByRole('combobox', { name: 'Operational status', exact: true }).selectOption('UnderDevelopment');
    await drawer.getByRole('button', { name: 'Save operational status', exact: true }).click();
    // Assert
    await expect(drawer).toHaveCount(0);
    await page.reload();
    await expect(page.getByText('Under development', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Return to package readiness', exact: true }))
      .toHaveAttribute('href', `${root}/documents?purpose=InitialSubmission&run=run-a`);
    expect(writes).toEqual(['UnderDevelopment']);
  });
}
