import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a/profile/DataTypes';
for (const width of [1440, 390]) {
  test(`Data handling records save from the top header at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!);
    let items = [{ id: 'data-a', dataTypeName: 'Recorded security data', description: 'Recorded security documentation',
      sensitivityClassification: 'CUI', source: 'Collectors', destination: 'Archive', applicableRegulations: 'Recorded regulations', sortOrder: 0,
      cuiCategory: '', confidentialityImpact: 'Moderate', integrityImpact: 'Moderate', availabilityImpact: 'Low',
      privacyApplicability: 'ReviewRequired', retentionRule: '', disposalMethod: '', categorizationReference: '', categorizationRationale: '' }];
    let scalar = '{"dataOverview":"Retained data context","customSource":"preserve"}';
    const writes: unknown[] = [];
    await context.route('**/api/dashboard/systems/system-a/profile/DataTypes', async route => {
      if (route.request().method() === 'PUT') {
        const input = route.request().postDataJSON();
        items = input.childItems; scalar = input.content; writes.push(input);
      }
      return route.fulfill({ json: { id: 'data-section', sectionType: 'DataTypes', governanceStatus: 'Draft', canEditProfile: true,
        draftContent: scalar, dataTypeEntries: items, userCategories: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    // Act
    await page.goto(root);
    const header = page.locator('header').filter({ has: page.getByRole('heading', { name: 'Data types & sensitivity' }) });
    const table = page.getByRole('table', { name: 'Information types' });
    await expect(table).toBeVisible();
    // Assert
    await expect(header.getByRole('button', { name: 'Save Draft', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(1);
    expect((await header.getByRole('button', { name: 'Save Draft', exact: true }).boundingBox())!.y).toBeLessThan((await table.boundingBox())!.y);
    await expect(table).toContainText('M / M / L · declared');
    await expect(table).toContainText('Privacy review required');
    await expect(table).toContainText('Section: Draft');
    await expect(page.getByRole('region', { name: 'Data documentation readiness' })).toContainText('6/11');
    // Act
    await page.getByRole('button', { name: /Complete Recorded security data handling details/ }).click();
    await page.getByRole('button', { name: 'Edit data type', exact: true }).click();
    await page.getByLabel('CUI category', { exact: true }).fill('Recorded systems information');
    await page.getByLabel('Privacy applicability', { exact: true }).selectOption('NoPii');
    await page.getByLabel('Retention rule', { exact: true }).fill('Retain for recorded six years');
    await page.getByLabel('Disposal / destruction method', { exact: true }).fill('Recorded cryptographic erasure');
    await page.getByLabel('Categorization source reference URL').fill('https://example.invalid/categorization');
    await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    // Assert
    expect(writes).toEqual([]);
    await expect(page.getByRole('region', { name: 'Data documentation readiness' })).toContainText('11/11');
    // Act
    await header.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
    await page.reload();
    // Assert
    await expect(table).toContainText('CUI · Recorded systems information');
    await expect(table).toContainText('No PII declared · Retain for recorded six years');
    await expect(table).toContainText('Section: Draft');
    await page.getByRole('button', { name: 'Open data type Recorded security data' }).click();
    await expect(page.getByRole('dialog')).toContainText('Recorded cryptographic erasure');
    await expect(page.getByRole('dialog')).toContainText('Collectors');
    expect(JSON.parse(scalar).customSource).toBe('preserve');
    expect(writes).toHaveLength(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
