import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`canonical inventory fixes are editable and survive reload at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange: synthetic HTTP data exercises the production UI without touching demo inventory.
    await installSystemCapabilityFixture(context, baseURL!);
    await page.setViewportSize({ width, height: 1100 });
    const api = '/api/dashboard/systems/system-a/inventory-items';
    let item: Record<string, unknown> | null = null;
    const writes: Record<string, unknown>[] = [];
    await context.route(`**${api}{,?*}`, route => {
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON();
        writes.push(body);
        expect(body.type).toBe(1);
        expect(body).not.toHaveProperty('serialNumber');
        expect(body).not.toHaveProperty('ipAddress');
        item = { ...body, id: 'item-a', systemId: 'system-a', type: 'Software', status: 'Active',
          softwareFunction: 'Database', hardwareFunction: null, parentHardwareId: null };
        return route.fulfill({ json: item });
      }
      return route.fulfill({ json: { systemId: 'system-a', items: item ? [item] : [], totalCount: item ? 1 : 0,
        page: 1, pageSize: 50, canManage: true } });
    });
    await context.route(`**${api}/item-a`, route => {
      if (route.request().method() === 'PUT') {
        const body = route.request().postDataJSON(); writes.push(body);
        item = { ...item, ...body, type: 'Software', softwareFunction: 'Database', parentHardwareId: null };
      }
      return route.fulfill({ json: item });
    });
    const returnTo = `/systems/system-a/documents?purpose=InitialSubmission&run=run-a&check=inventory`;
    await page.goto(`${root}/security-capabilities/inventory?${new URLSearchParams({
      tab: 'hardware-software', readinessReturn: returnTo,
    })}`);
    await expect(page.getByRole('heading', { name: 'Hardware/software inventory', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Return to package readiness', exact: true }))
      .toHaveAttribute('href', `${root}/documents?purpose=InitialSubmission&run=run-a&check=inventory`);
    // Act: document a managed service without inventing physical fields.
    await page.getByRole('button', { name: 'Add inventory item', exact: true }).click();
    let drawer = page.getByRole('dialog');
    await drawer.getByLabel('Item name', { exact: true }).fill('Mission database');
    await drawer.getByRole('combobox', { name: 'Function', exact: true }).selectOption('1');
    await drawer.getByLabel('Vendor', { exact: true }).fill('Synthetic provider');
    await drawer.getByLabel('Version', { exact: true }).fill('2026.09');
    await drawer.getByRole('button', { name: 'Save inventory item', exact: true }).click();
    await expect(drawer).toHaveCount(0);
    await expect(page.getByRole('cell', { name: 'Mission database', exact: true })).toBeVisible();
    await page.reload();
    // Act: update the same persisted record, retaining identity.
    await page.getByRole('button', { name: 'Open inventory item Mission database', exact: true }).click();
    drawer = page.getByRole('dialog');
    await expect(drawer.getByLabel('Version', { exact: true })).toHaveValue('2026.09');
    await drawer.getByLabel('Version', { exact: true }).fill('2026.10');
    await drawer.getByRole('button', { name: 'Save inventory item', exact: true }).click();
    await expect(drawer).toHaveCount(0);
    // Assert
    await expect(page.getByRole('cell', { name: '2026.10', exact: true })).toBeVisible();
    expect(writes).toHaveLength(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
