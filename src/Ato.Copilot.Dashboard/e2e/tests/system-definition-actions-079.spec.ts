import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';

const root = '/workspaces/organizations/org-a/systems/system-a';
const pages = [
  ['Mission', 'profile/MissionAndPurpose', 'Mission & purpose'],
  ['Users', 'profile/UsersAndAccess', 'Users & access'],
  ['Environment & hosting', 'profile/EnvironmentAndDeployment', 'Environment & hosting'],
  ['Data', 'profile/DataTypes', 'Data types & sensitivity'],
  ['Inventory & boundary', 'boundaries', 'Inventory & system boundary'],
  ['Ports & interconnections', 'profile/PortsProtocolsAndServices', 'Ports & interconnections'],
];

for (const width of [1440, 390]) {
  test(`System definition layout and saved-draft actions at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic responses exercise the real UI without changing demo records.
    await page.setViewportSize({ width, height: 1100 });
    await installSystemCapabilityFixture(context, baseURL!);
    const writes: { method: string; path: string }[] = [];
    let rejectSave = true;
    let content = JSON.stringify({ missionStatement: 'Coordinate support.', businessPurpose: 'Maintain records.',
      operationalJustification: 'Preserve continuity.', customSourceKey: 'retained' });
    await context.route('**/api/roles/system/system-a', route => route.fulfill({ json: { status: 'success', data: {
      systemId: 'system-a', roles: [{ role: 'SystemOwner', person: { id: 'owner-a', displayName: 'Recorded Owner' }, source: 'override' }],
    } } }));
    await context.route('**/api/dashboard/systems/system-a/profile/*', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      const sectionType = path.split('/').at(-1);
      if (sectionType === 'completeness') return route.fallback();
      if (request.method() !== 'GET') writes.push({ method: request.method(), path });
      if (sectionType === 'submit') return route.fulfill({ json: {
        submittedSections: [], skippedSections: [{ sectionType: 'MissionAndPurpose', reason: 'Resolve required source records.' }],
      } });
      if (request.method() === 'PUT') {
        if (rejectSave) return route.fulfill({ status: 409, json: { error: 'Refresh permission before retrying.' } });
        content = request.postDataJSON().content;
      }
      return route.fulfill({ json: {
        id: `section-${sectionType}`, sectionType, canEditProfile: true, governanceStatus: 'Draft', draftContent: content,
        approvedContent: null, lastEditedAt: '2026-09-27T12:00:00Z', userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
      } });
    });
    await context.route('**/api/**/systems/system-a/provider-relationships?*', route => route.fulfill({
      json: { items: [], total: 0, page: 1, pageSize: 25 },
    }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => route.fulfill({ json: { items: [] } }));
    await context.route('**/api/dashboard/systems/system-a/components?*', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/documents', route => route.fulfill({ json: { systemId: 'system-a', interconnections: [] } }));
    await context.route('**/api/dashboard/systems/system-a/interconnections?*', route => route.fulfill({
      json: { items: [], total: 0, page: 1, pageSize: 50, canManageInterconnections: true },
    }));

    // Act / Assert: all six tabs remain distinct and below their page heading.
    await page.goto(`${root}/profile/MissionAndPurpose`);
    for (const [label, path, title] of pages) {
      const navigation = page.getByRole('navigation', { name: 'System task views' });
      await navigation.getByRole('link', { name: label, exact: true }).click();
      await expect(page).toHaveURL(`${baseURL}${root}/${path}`);
      const heading = page.getByRole('heading', { name: title, exact: true });
      await expect(heading).toBeVisible();
      await expect(navigation.getByRole('link')).toHaveCount(7);
      await expect(navigation.locator('[aria-current="page"]')).toHaveText(label);
      expect((await heading.boundingBox())!.y).toBeLessThan((await navigation.boundingBox())!.y);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      if (label === 'Ports & interconnections') {
        await page.getByRole('button', { name: 'Add connection', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Add network interface', exact: true }).click();
        await expect(page.getByRole('dialog').getByRole('button', { name: 'Save network interface', exact: true })).toBeVisible();
        await page.keyboard.press('Escape');
        await page.getByRole('button', { name: 'Add connection', exact: true }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Add interconnection', exact: true }).click();
        await expect(page.getByRole('dialog', { name: 'Add interconnection', exact: true })).toBeVisible();
        await page.keyboard.press('Escape');
      }
      await page.screenshot({ path: info.outputPath(`definition-${path.replaceAll('/', '-')}-${width}.png`), fullPage: true });
    }
    expect(writes).toEqual([]);

    // Act / Assert: header save uses current fields, keeps failures, and cannot submit unsaved edits.
    await page.goto(`${root}/profile/MissionAndPurpose`);
    await expect(page.getByLabel('System owner', { exact: true })).toHaveValue('Recorded Owner');
    await expect(page.getByRole('link', { name: 'Preview contribution', exact: true })).toHaveAttribute('href', `${root}/documents/preview?contribution=MissionAndPurpose`);
    await expect(page.getByRole('link', { name: 'View package readiness', exact: true })).toHaveAttribute('href', `${root}/documents?purpose=InitialSubmission`);
    await page.getByLabel('Mission statement', { exact: false }).fill('Updated mission.');
    await expect(page.getByRole('button', { name: 'Submit for Review', exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Refresh permission before retrying.', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Mission statement', { exact: false })).toHaveValue('Updated mission.');
    rejectSave = false;
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
    expect(JSON.parse(content)).toEqual({ missionStatement: 'Updated mission.', businessPurpose: 'Maintain records.',
      operationalJustification: 'Preserve continuity.', customSourceKey: 'retained' });
    await page.getByRole('button', { name: 'Submit for Review', exact: true }).click();
    await expect(page.getByText('Resolve required source records.', { exact: true })).toBeVisible();
    await expect(page.getByText('Section submitted for review.', { exact: true })).toHaveCount(0);

    // Act / Assert: focused row CRUD is a local draft operation, not a hidden save.
    await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Users', exact: true }).click();
    const beforeDialog = writes.length;
    const add = page.getByRole('button', { name: 'Add user category', exact: true });
    await add.click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(add).toBeFocused();
    expect(writes).toHaveLength(beforeDialog);
  });
}
