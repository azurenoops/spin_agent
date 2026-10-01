import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { allocationResponse } from '../../src/__tests__/provider-relationships/fixtures';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`Environment matches field and provider scope composition at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: source-backed shapes, explicitly synthetic records.
    await page.setViewportSize({ width, height: 1050 });
    await installWorkspaceFixture(context, baseURL!);
    let content = JSON.stringify({ hostingModel: 'Hybrid', cloudProvider: '["Azure Government"]',
      additionalDetails: 'Recorded deployment.', retainedKey: 'preserve', rtoRpo: 'Recorded recovery target' });
    const writes: string[] = [];
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/') && request.method() !== 'GET') writes.push(`${request.method()} ${path}`);
    });
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => {
      if (route.request().method() === 'PUT') content = route.request().postDataJSON().content;
      return route.fulfill({ json: { id: 'environment-a', sectionType: 'EnvironmentAndDeployment',
        canEditProfile: true, governanceStatus: 'Draft', draftContent: content,
        userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [
        { ...allocationResponse, relationshipId: 'associated-a', offeringName: 'Associated Azure offering', providerName: 'Provider A', hostingScopeName: 'Production allocation', reviewRequired: true },
        { ...allocationResponse, assignmentId: 'available-a', offeringId: 'offering-b', relationshipId: null,
          offeringName: 'Available collaboration service', providerName: 'Provider B', hostingScopeName: 'Candidate scope' },
      ], page: 1, pageSize: 25, total: 2 },
    } }));
    await context.route('**/api/workspaces/organizations/org-a/systems/system-a/security-capabilities?*', route => route.fulfill({ json: { data: {
      items: [], page: 1, pageSize: 10, total: 0, scope: 'applied', grouping: 'capability', boundaries: [],
      permissions: { canRead: true, canManage: false, canReviewResponsibilities: false, canManageEvidence: false,
        canAuthorNarratives: false, canReviewNarratives: false },
    } } }));

    // Act / Assert: one mock-shaped page, not two competing hosting panels.
    await page.goto(`${root}/profile/EnvironmentAndDeployment`);
    const header = page.locator('main header').first();
    await expect(header.getByRole('heading', { name: 'Environment & hosting', exact: true })).toBeVisible();
    await expect(header.getByRole('link', { name: 'Review hosting scope', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Choose provider hosting', exact: true })).toBeVisible();
    await expect(header.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
    const tabs = page.getByRole('navigation', { name: 'System task views' });
    await expect(tabs.getByRole('link')).toHaveCount(7);
    await expect(tabs.locator('[aria-current="page"]')).toHaveText('Environment & hosting');
    const model = page.getByRole('combobox', { name: 'Hosting model', exact: true });
    const cloud = page.getByRole('combobox', { name: 'Cloud environment', exact: true });
    const description = page.getByRole('textbox', { name: 'Deployment description', exact: true });
    await expect(cloud).toContainText('Azure Government');
    const network = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'Network zones & deployment locations' }) });
    const recovery = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'Recovery, availability & operating details' }) });
    await expect(network).toHaveAttribute('open', '');
    await expect(recovery).toHaveAttribute('open', '');
    await expect(network).toContainText('0 of 2 fields recorded');
    await expect(recovery).toContainText('1 of 5 fields recorded');
    await expect(page.getByRole('combobox', { name: 'Network Zones', exact: true })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Availability Tier', exact: true })).toBeVisible();
    await page.getByRole('combobox', { name: 'Network Zones', exact: true }).click();
    await page.getByRole('option', { name: 'DMZ', exact: true }).click();
    await page.keyboard.press('Escape');
    await expect(network).toContainText('1 of 2 fields recorded');
    if (width === 1440) {
      const left = (await model.boundingBox())!, right = (await cloud.boundingBox())!, full = (await description.boundingBox())!;
      expect(Math.abs(left.y - right.y)).toBeLessThan(3);
      expect(right.x).toBeGreaterThan(left.x);
      expect(full.y).toBeGreaterThan(left.y);
      expect(full.width).toBeGreaterThan(left.width * 1.8);
    }
    await expect(page.getByRole('heading', { name: 'Provider hosting', exact: true })).toHaveCount(1);
    await expect(page.getByRole('heading', { name: 'Associated hosting & security capabilities', exact: true })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Documentation & review', exact: true })).toBeVisible();
    await expect(page.getByText('Contributes to your SSP’s environment and hosting section.', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Preview contribution', exact: true })).toBeVisible();
    const associated = page.getByRole('table', { name: 'Associated provider scope', exact: true });
    await expect(associated).toContainText('Associated Azure offering');
    await expect(associated).not.toContainText('Available collaboration service');
    await associated.getByRole('button', { name: 'Open', exact: true }).click();
    let dialog = page.getByRole('dialog', { name: 'Provider scope details', exact: true });
    await dialog.getByText('Details', { exact: true }).click();
    await expect(dialog).toContainText('Production allocation');
    await expect(dialog).toContainText('associated-a');
    await expect(dialog.getByRole('link', { name: 'Open hosting task', exact: true })).toHaveAttribute('href', `${root}/profile/EnvironmentAndDeployment/hosting`);
    await page.keyboard.press('Escape');
    await expect(page.getByText('Available collaboration service', { exact: false })).toHaveCount(0);
    await page.getByRole('button', { name: 'Choose provider hosting', exact: true }).click();
    const availableRow = page.getByRole('dialog', { name: 'Choose provider hosting', exact: true }).getByRole('listitem').filter({ hasText: 'Available collaboration service' });
    await availableRow.getByRole('button', { name: 'Open', exact: true }).click();
    dialog = page.getByRole('dialog', { name: 'Provider scope details', exact: true });
    await dialog.getByText('Details', { exact: true }).click();
    await expect(dialog).toContainText('available-a');
    await expect(dialog.getByRole('button', { name: 'Copy hosting description to draft', exact: true })).toHaveCount(0);
    await page.keyboard.press('Escape');
    expect(writes).toEqual([]);

    // Act / Assert: real input keys are preserved, without silently associating or adopting.
    await description.fill('Updated deployment description.');
    await cloud.click();
    await page.getByPlaceholder('Type to filter...').fill('AWS GovCloud');
    await page.getByPlaceholder('Type to filter...').press('Enter');
    expect(writes).toEqual([]);
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
    expect(JSON.parse(content)).toEqual({ hostingModel: 'Hybrid', cloudProvider: '["Azure Government","AWS GovCloud"]',
      additionalDetails: 'Updated deployment description.', networkZones: '["DMZ"]', retainedKey: 'preserve', rtoRpo: 'Recorded recovery target' });
    expect(writes).toEqual(['PUT /api/dashboard/systems/system-a/profile/EnvironmentAndDeployment']);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await header.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`environment-hosting-${width}.png`) });
  });
}
