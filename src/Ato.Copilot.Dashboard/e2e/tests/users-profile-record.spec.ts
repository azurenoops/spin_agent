import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a/profile/UsersAndAccess';
for (const width of [1440, 390]) {
  test(`Users compact SSP records save from the header at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!);
    let rows = [{ id: 'automation', categoryName: 'Recorded automation identity', description: 'Recorded operational processing',
      approximateCount: 1, accessMethod: 'API', dataSensitivityLevel: 'CUI', sortOrder: 0, governanceStatus: 'Draft', revision: 1,
      identityType: 'WorkloadIdentity', privilegeLevel: 'Privileged', affiliation: 'Internal', authenticationMethod: 'Managed identity',
      responsibleOwner: '', userLocations: 'CONUS', permittedEnvironments: '', authorizedDataTypes: 'Recorded inventory metadata' }];
    let scalar = '{"accessOverview":"Retained access model","customSource":"preserve"}';
    const writes: unknown[] = [];
    await context.route('**/api/dashboard/systems/system-a/profile/UsersAndAccess', async route => {
      if (route.request().method() === 'PUT') {
        const input = route.request().postDataJSON();
        rows = input.childItems;
        scalar = input.content;
        writes.push(input);
      }
      return route.fulfill({ json: { id: 'users-section', sectionType: 'UsersAndAccess', governanceStatus: 'Draft',
        canEditProfile: true, draftContent: scalar, userCategories: rows, dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
        userCategoriesReview: { approvedCount: 0, underReviewCount: 0, pendingDeletionCount: 0 } } });
    });
    // Act
    await page.goto(root);
    const header = page.locator('header').filter({ has: page.getByRole('heading', { name: 'Users & access' }) });
    const table = page.getByRole('table', { name: 'User categories' });
    await expect(table).toBeVisible();
    // Assert
    await expect(header.getByRole('button', { name: 'Save Draft', exact: true })).toBeVisible();
    expect((await header.getByRole('button', { name: 'Save Draft', exact: true }).boundingBox())!.y)
      .toBeLessThan((await table.boundingBox())!.y);
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(1);
    await expect(table).toContainText('Workload identity · Privileged · Internal');
    await expect(table).toContainText('API · Managed identity');
    await expect(table).toContainText('Recorded inventory metadata');
    await expect(page.getByText(/has no recorded owner or permitted environment/)).toBeVisible();
    await expect(page.getByRole('region', { name: 'Users documentation readiness' })).toContainText('8/10');
    // Act
    await page.getByRole('button', { name: /Record identity owner, environment and missing details/ }).click();
    await page.getByRole('button', { name: 'Edit category', exact: true }).click();
    await page.getByLabel('Responsible owner', { exact: true }).fill('Recorded mission operations team');
    await page.getByLabel('Permitted environments', { exact: true }).fill('Recorded production scope');
    await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    // Assert
    expect(writes).toEqual([]);
    await expect(page.getByRole('region', { name: 'Users documentation readiness' })).toContainText('10/10');
    // Act
    await header.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Users working data saved. This does not approve any category.')).toBeVisible();
    await page.reload();
    // Assert
    await page.getByRole('button', { name: 'Open user category Recorded automation identity' }).click();
    await expect(page.getByRole('dialog')).toContainText('Recorded mission operations team');
    await expect(page.getByRole('dialog')).toContainText('Recorded production scope');
    await expect(page.getByRole('dialog')).toContainText('Recorded operational processing');
    expect(rows[0]!.governanceStatus).toBe('Draft');
    expect(JSON.parse(scalar).customSource).toBe('preserve');
    expect(writes).toHaveLength(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
