import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = '/workspaces/organizations/org-a/systems/system-a';
const assignmentPath = '/api/dashboard/systems/system-a/boundary-definitions/boundary-a/components';
for (const width of [1440, 390]) {
  test(`boundary records open their component management drawer at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: populated boundary and eligible components use synthetic API fixtures.
    await page.setViewportSize({ width, height: 1050 });
    await installSystemCapabilityFixture(context, baseURL!);
    const first = { assignmentId: 'assignment-a', componentId: 'component-a', componentName: 'mission-api',
      componentType: 'Thing', subType: 'Application service', source: 'System', isInScope: true, exclusionRationale: null,
      inheritanceProvider: null, azureResourceId: '/subscriptions/demo/resourceGroups/mission/providers/Microsoft.Web/sites/api',
      azureResourceType: 'Microsoft.Web/sites', azureResourceGroup: 'mission', azureLocation: 'usgovvirginia',
      createdAt: '2026-09-28T12:00:00Z', createdBy: 'Recorder' };
    const added = { ...first, assignmentId: 'assignment-b', componentId: 'component-b', componentName: 'mission-storage', subType: 'Storage account' };
    const items = [first];
    let finishAdd: (() => void) | undefined;
    const writes: { method: string; path: string; body: unknown }[] = [];
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/') && request.method() !== 'GET') writes.push({
        method: request.method(), path, body: request.postDataJSON(),
      });
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => route.fulfill({ json: { items: [{
      id: 'boundary-a', name: 'Production boundary', description: 'Production application and storage, with recorded shared dependencies.',
      boundaryType: 'Logical', isPrimary: true, registeredSystemId: 'system-a', componentCount: items.length, coveragePercent: 0,
    }] } }));
    await context.route(`**${assignmentPath}{,?*}`, async route => {
      if (route.request().method() === 'POST') {
        expect(route.request().postDataJSON()).toEqual({ componentId: 'component-b', source: 'System', isInScope: true });
        await new Promise<void>(resolve => { finishAdd = resolve; });
        items.push(added);
        return route.fulfill({ json: added });
      }
      return route.fulfill({ json: { items, totalCount: items.length, page: 1, pageSize: 25 } });
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/boundary-a/component-candidates{,?*}', route => route.fulfill({ json: {
      items: items.length === 1 ? [{ id: 'component-b', name: 'mission-storage', componentType: 'Thing', subType: 'Storage account',
        source: 'System', description: 'Mission storage resource', isAssigned: false }] : [],
    } }));
    await context.route('**/api/dashboard/boundary-definitions/boundary-a/components', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/boundary-a/lock', route => route.fulfill({ json: {
      locked: false, lockedBy: null, lockedAt: null, expiresAt: null,
    } }));
    // Act / Assert: only the mock's component table is rendered on the main page.
    await page.goto(`${root}/boundaries`);
    await expect(page.getByRole('cell', { name: 'Production boundary', exact: true })).toBeVisible();
    await expect(page.getByRole('status', { name: 'Boundary record status' })).toContainText('1 boundary defined');
    await expect(page.getByRole('cell', { name: 'mission-api', exact: true })).toHaveCount(0);
    await expect(page.getByRole('table')).toHaveCount(1);
    await expect(page.getByText('Manage boundaries', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('textbox', { name: 'Search boundaries' })).toHaveCount(0);
    await expect(page.getByRole('columnheader', { name: 'Boundary', exact: true })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Recorded boundary', exact: true })).toHaveCount(0);
    await expect(page.getByText('Source: System', { exact: true })).toHaveCount(0);
    const support = page.getByRole('complementary', { name: 'Document contribution and next tasks' });
    await expect(support).toHaveCount(0);
    await expect(support.getByText('Do cloud-native systems need an inventory?')).toHaveCount(0);
    // Compare actual typography and spacing against the checked-in design, not a guessed proxy.
    const reference = await page.context().browser()!.newPage({ viewport: { width, height: 1050 } });
    try {
      await reference.goto(`${pathToFileURL(resolve('../../docs/design/system-overview-mock/pages.html')).href}#Inventory%20%26%20boundary`);
      const pairs = [
        ['.boundary-workspace h1', '#title', ['fontSize', 'fontWeight', 'lineHeight', 'letterSpacing']],
        ['.boundary-inventory-card', '.panel', ['paddingTop', 'paddingLeft', 'borderRadius']],
        ['.boundary-inventory-card h2', '.panel h2', ['fontSize', 'fontWeight', 'letterSpacing', 'marginBottom']],
        ['.boundary-inventory-card th', '.panel th', ['fontSize', 'fontWeight', 'lineHeight', 'paddingTop', 'paddingLeft']],
        ['.boundary-inventory-card td button', '.panel td button', ['fontSize', 'lineHeight', 'paddingTop', 'paddingLeft']],
      ] as const;
      for (const [liveSelector, mockSelector, properties] of pairs) {
        const readStyles = (element: Element, keys: readonly string[]) => {
          const styles = getComputedStyle(element);
          return Object.fromEntries(keys.map(key => [key, styles[key as keyof CSSStyleDeclaration]]));
        };
        expect(await page.locator(liveSelector).first().evaluate(readStyles, properties), liveSelector)
          .toEqual(await reference.locator(mockSelector).first().evaluate(readStyles, properties));
      }
      await page.locator('.boundary-workspace').screenshot({ path: info.outputPath(`boundary-page-${width}.png`) });
      await reference.locator('main').screenshot({ path: info.outputPath(`boundary-reference-${width}.png`) });
    } finally {
      await reference.close();
    }
    const open = page.getByRole('button', { name: 'Open boundary Production boundary', exact: true });
    await open.click();
    const drawer = page.getByRole('dialog', { name: 'Production boundary — Details', exact: true });
    await expect(drawer).toBeVisible();
    const box = (await drawer.boundingBox())!;
    expect(box.y).toBe(0);
    expect(Math.abs(box.x + box.width - width)).toBeLessThan(2);
    await drawer.getByRole('region', { name: 'Placement mission-api', exact: true })
      .getByText('Component details', { exact: true }).click();
    await expect(drawer.getByText('component-a', { exact: true })).toBeVisible();
    const resourceId = drawer.getByText(first.azureResourceId, { exact: true });
    expect(await resourceId.evaluate(element => {
      const range = document.createRange();
      range.selectNodeContents(element);
      const bounds = element.closest('dialog')!.getBoundingClientRect();
      return [...range.getClientRects()].every(rect => rect.left >= bounds.left && rect.right <= bounds.right);
    })).toBe(true);
    await expect(drawer.getByRole('button', { name: 'Edit boundary', exact: true })).toBeVisible();
    expect(writes).toEqual([]);
    // Act / Assert: add targets the opened boundary and closing is blocked during the write.
    await drawer.getByRole('button', { name: 'Add components to boundary', exact: true }).click();
    await drawer.getByRole('button', { name: 'Add', exact: true }).click();
    await expect(drawer).toHaveAttribute('aria-busy', 'true');
    await page.keyboard.press('Escape');
    await expect(drawer).toBeVisible();
    await expect.poll(() => !!finishAdd).toBe(true);
    finishAdd!();
    await expect(drawer).toHaveAttribute('aria-busy', 'false');
    await expect(drawer.getByRole('region', { name: 'Placement mission-storage', exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath(`boundary-management-drawer-${width}.png`) });
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(open).toBeFocused();
    await expect(page.getByRole('table')).toHaveCount(1);
    await expect(page.getByRole('cell', { name: 'Production boundary', exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'mission-storage', exact: true })).toHaveCount(0);
    await open.click();
    await expect(drawer.getByRole('region', { name: 'Placement mission-storage', exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    expect(writes).toEqual([{ method: 'POST', path: assignmentPath, body: { componentId: 'component-b', source: 'System', isInScope: true } }]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`single-boundary-table-${width}.png`) });
  });
}
