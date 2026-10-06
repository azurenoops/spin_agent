import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`a newly saved empty boundary remains in the register after reload at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange: isolate the create/read workflow from the live database.
    await page.setViewportSize({ width, height: 1050 });
    await installSystemCapabilityFixture(context, baseURL!);
    const definition = { id: 'boundary-a', name: 'mission-api', description: null, boundaryType: 'Logical',
      isPrimary: true, registeredSystemId: 'system-a', componentCount: 0, resourceCount: 0, coveragePercent: 0 };
    let saved = false;
    const writes: string[] = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/') && request.method() !== 'GET')
        writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', async route => {
      if (route.request().method() === 'POST') {
        expect(route.request().postDataJSON()).toMatchObject({ name: 'mission-api', boundaryType: 'Logical' });
        saved = true;
        return route.fulfill({ json: definition });
      }
      return route.fulfill({ json: { items: saved ? [definition] : [], totalCount: saved ? 1 : 0 } });
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/boundary-a/components{,?*}',
      route => route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 25 } }));
    await context.route('**/api/dashboard/boundary-definitions/boundary-a/components',
      route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/boundary-a/lock',
      route => route.fulfill({ json: { locked: false, lockedBy: null, lockedAt: null, expiresAt: null } }));
    // Act: create only the boundary, without adding any components.
    await page.goto(`${root}/boundaries`);
    await page.getByRole('button', { name: 'Add System Boundary', exact: true }).click();
    const form = page.getByRole('dialog', { name: 'Create Boundary', exact: true });
    await form.getByRole('textbox', { name: 'Name *', exact: true }).fill('mission-api');
    await form.getByRole('button', { name: 'Create Boundary', exact: true }).click();
    // Assert: both initial refresh and a full page reload show the boundary.
    await expect(page.getByRole('cell', { name: 'mission-api', exact: true })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('cell', { name: 'mission-api', exact: true })).toBeVisible();
    await expect(page.getByRole('status', { name: 'Boundary record status' })).toContainText('1 boundary defined');
    await expect(page.getByRole('table').getByRole('row')).toHaveCount(2);
    await page.getByRole('button', { name: 'Open boundary mission-api', exact: true }).click();
    const drawer = page.getByRole('dialog', { name: 'mission-api — Details', exact: true });
    await expect(drawer.getByText('No components assigned to this boundary yet.', { exact: true })).toBeVisible();
    await expect(drawer.getByRole('button', { name: 'Add components to boundary', exact: true })).toBeVisible();
    expect(writes).toEqual(['POST /api/dashboard/systems/system-a/boundary-definitions']);
  });

  test(`empty boundary offers a cancellable create dialog at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    await installSystemCapabilityFixture(context, baseURL!);
    const writes: string[] = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/') && request.method() !== 'GET') writes.push(request.url());
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => route.fulfill({ json: { items: [] } }));
    // Act
    await page.goto(`${root}/boundaries`);
    const create = page.getByRole('button', { name: 'Add System Boundary', exact: true });
    await create.click();
    // Assert
    await expect(page.getByRole('dialog', { name: 'Create Boundary', exact: true })).toBeVisible();
    expect(writes).toEqual([]);
    // Act
    await page.keyboard.press('Escape');
    // Assert
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(create).toBeFocused();
    await expect(page.getByRole('button', { name: 'Review boundary', exact: true })).toHaveCount(0);
    expect(writes).toEqual([]);
  });

  test(`boundary mock shows recorded inventory and read-only review at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: scoped synthetic placement data; no claims of a complete HW/SW workbook.
    await page.setViewportSize({ width, height: 1050 });
    await installSystemCapabilityFixture(context, baseURL!);
    const writes: string[] = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/') && request.method() !== 'GET') writes.push(request.url());
    });
    const boundaries = [
      { id: 'boundary-a', name: 'Production boundary', description: 'Mission application and storage, with shared logging.',
        boundaryType: 'Logical', isPrimary: true, registeredSystemId: 'system-a', componentCount: 2, resourceCount: 2, coveragePercent: 50 },
      { id: 'boundary-b', name: 'Recovery boundary', description: 'Disaster recovery resources.', boundaryType: 'Logical',
        isPrimary: false, registeredSystemId: 'system-a', componentCount: 1, resourceCount: 1, coveragePercent: 0 },
    ];
    const component = { assignmentId: 'placement-a', componentId: 'component-a', componentName: 'Mission API', componentType: 'Thing',
      subType: 'Application service', source: 'System', isInScope: true, exclusionRationale: null, inheritanceProvider: null,
      azureResourceId: '/subscriptions/demo/resourceGroups/mission/providers/Microsoft.Web/sites/api', azureResourceType: 'Microsoft.Web/sites',
      azureResourceGroup: 'mission', azureLocation: 'usgovvirginia', createdAt: '2026-09-28T12:00:00Z', createdBy: 'Recorder' };
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => route.fulfill({ json: { items: boundaries } }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/*/components{,?*}', route => {
      const recovery = route.request().url().includes('/boundary-b/');
      const items = recovery ? [{ ...component, assignmentId: 'placement-b', componentId: 'component-b', componentName: 'Recovery store' }]
        : [component, { ...component, assignmentId: 'placement-c', componentId: 'component-c', componentName: 'Shared logging',
          source: 'CSP', inheritanceProvider: 'Provider logging', isInScope: false, exclusionRationale: 'Outside the mission boundary; documented dependency.' }];
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 25 } });
    });
    await context.route('**/api/dashboard/boundary-definitions/*/components', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/dashboard/boundary-definitions/*/resources', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/components?*', route => route.fulfill({ json: {
      systemId: 'system-a', items: [], totalCount: 2, summary: { totalCount: 2, personCount: 0, placeCount: 0, thingCount: 2, policyCount: 0 },
    } }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/*/lock', route => route.fulfill({ json: {
      locked: false, lockedBy: null, lockedAt: null, expiresAt: null,
    } }));
    // Act / Assert: mock heading/tabs/status/table and exact boundary selection.
    await page.goto(`${root}/boundaries`);
    const heading = page.getByRole('heading', { name: 'Components & system scope', exact: true });
    await expect(heading).toBeVisible();
    const tabs = page.getByRole('navigation', { name: 'System task views' });
    await expect(tabs.getByRole('link')).toHaveCount(7);
    await expect(tabs.locator('[aria-current="page"]')).toHaveText('Components & system scope');
    expect((await heading.boundingBox())!.y).toBeLessThan((await tabs.boundingBox())!.y);
    await expect(page.getByRole('cell', { name: 'Production boundary', exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Recovery boundary', exact: true })).toBeVisible();
    await expect(page.getByRole('status', { name: 'Boundary record status' })).toContainText('2 boundaries defined');
    await expect(page.getByRole('link', { name: 'Manage component inventory', exact: true })).toHaveCount(0);
    await expect(page.getByText('SSP · Boundary description and inventory', { exact: true })).toHaveCount(0);
    const openComponent = page.getByRole('button', { name: 'Open boundary Production boundary', exact: true });
    await openComponent.click();
    const placement = page.getByRole('dialog', { name: 'Production boundary — Details', exact: true });
    await placement.getByRole('region', { name: 'Placement Mission API', exact: true })
      .getByText('Component details', { exact: true }).click();
    await expect(placement.getByText('Outside the mission boundary; documented dependency.', { exact: true })).toBeVisible();
    await expect(placement).toContainText('Microsoft.Web/sites');
    await expect(placement).toContainText('usgovvirginia');
    await expect(placement).toContainText('component-a');
    await expect(placement.getByRole('button', { name: 'Add components to boundary', exact: true })).toBeVisible();
    await placement.getByText('Inventory & scope guidance', { exact: true }).click();
    await expect(placement.getByRole('link', { name: 'Manage component inventory', exact: true })).toHaveAttribute('href', `${root}/security-capabilities/inventory`);
    const placementBox = (await placement.boundingBox())!;
    expect(placementBox.y).toBe(0);
    expect(Math.abs(placementBox.x + placementBox.width - width)).toBeLessThan(2);
    expect(writes).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(openComponent).toBeFocused();
    const create = page.getByRole('button', { name: 'Add System Boundary', exact: true });
    await create.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog', { name: 'Create Boundary', exact: true })).toBeVisible();
    expect(writes).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(create).toBeFocused();
    await page.getByRole('button', { name: 'Open boundary Recovery boundary', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Recovery boundary — Details', exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Placement Recovery store', exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Placement Mission API', exact: true })).toHaveCount(0);
    await page.keyboard.press('Escape');
    expect(writes).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await heading.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`boundary-mock-${width}.png`) });
  });
}
