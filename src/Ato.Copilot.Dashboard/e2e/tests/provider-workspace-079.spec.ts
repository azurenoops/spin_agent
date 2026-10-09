import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering } from '../../src/__tests__/provider-authorizations/testData';
import { offeringOverview } from '../../src/__tests__/provider-authorizations/overviewFixtures';

for (const width of [1440, 390]) {
  test(`provider task navigation and retained overview at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const errors: string[] = [];
    page.on('pageerror', error => { errors.push(error.message); console.error('Provider browser error:', error.message); });
    page.on('requestfailed', request => console.error('Provider request failed:', request.url(), request.failure()?.errorText));
    await context.route('**/api/csp/offerings**', async route => {
      const path = new URL(route.request().url()).pathname;
      const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (path === '/api/csp/offerings') return ok({ items: [offering], total: 1, page: 1, pageSize: 25 });
      if (path.endsWith('/overview')) return ok({ ...offeringOverview(), customerActionCount: 3 });
      if (path.endsWith('/impact-reviews')) return ok({ items: [], total: 0, page: 1, pageSize: 25 });
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Outside fixture: ${path}` } } });
    });
    // Act
    await page.goto('/workspaces/csp');
    // Assert
    await expect(page.getByRole('heading', { name: 'Your provider workspace', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Focus for today' })).toBeVisible();
    await expect(page.getByLabel('Published capabilities', { exact: true })).toHaveText(String(offeringOverview().capabilities.published));
    await expect(page.getByLabel('Customer actions', { exact: true })).toHaveText('3');
    await expect(page.getByRole('combobox', { name: 'Service offering' })).toHaveCount(0);
    await expect(page.getByRole('heading', { name: `Offering overview: ${offering.name}`, exact: true })).toBeVisible();
    await expect(page.getByText('DESIGN PREVIEW')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`provider-overview-${width}.png`), fullPage: true });
    // Act
    if (width === 390) await page.getByText('Provider navigation', { exact: true }).click();
    const nav = page.getByRole('navigation', { name: width === 390 ? 'Provider mobile navigation' : 'Provider workspace', exact: true });
    await nav.getByRole('link', { name: 'Administration', exact: true }).click();
    // Assert
    await expect(page).toHaveURL(`${baseURL}/workspaces/csp/provider-administration`);
    await expect(page.getByRole('heading', { name: 'Provider administration' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Manage organizations' })).toHaveAttribute('href', '/workspaces/csp/organizations');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(errors).toEqual([]);
  });

  test(`provider Overview register selects named metrics without writes at ${width}px`, async ({ context, page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const second = { ...offering, offeringId: 'second-service', name: 'Second synthetic service' };
    const writes: string[] = [];
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/') && request.method() !== 'GET') writes.push(`${request.method()} ${request.url()}`);
    });
    await context.route('**/api/csp/offerings**', async route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/csp/offerings') return route.fulfill({ json: { status: 'success',
        data: { items: [offering, second], total: 2, page: 1, pageSize: 25 } } });
      if (path.endsWith('/overview')) {
        const id = path.split('/').at(-2)!;
        return route.fulfill({ json: { status: 'success', data: { ...offeringOverview(), offeringId: id,
          capabilities: { ...offeringOverview().capabilities, published: id === second.offeringId ? 9 : 4 } } } });
      }
      return route.fulfill({ status: 404, json: { error: 'Outside the synthetic Overview contract' } });
    });
    // Act
    await page.goto('/workspaces/csp');
    // Assert
    await expect(page.getByRole('heading', { name: 'Service offerings', exact: true })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Service offering' })).toHaveCount(0);
    await expect(page.getByLabel('Published capabilities', { exact: true })).toHaveCount(0);
    for (const item of [offering, second]) await expect(page.getByRole('link', { name: `Open offering ${item.name}`, exact: true })).toBeVisible();
    // Act
    const selection = page.getByRole('button', { name: `Show overview for ${second.name}`, exact: true });
    await selection.focus();
    await page.keyboard.press('Enter');
    // Assert
    await expect(page.getByRole('heading', { name: `Offering overview: ${second.name}`, exact: true })).toBeVisible();
    await expect(page.getByLabel('Published capabilities', { exact: true })).toHaveText('9');
    // Act
    await page.keyboard.press('Tab');
    // Assert
    await expect(page.getByRole('link', { name: `Open offering ${second.name}`, exact: true })).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    // Act
    await page.goto(`/workspaces/csp?offeringId=${second.offeringId}`);
    // Assert
    await expect(page.getByRole('heading', { name: `Offering overview: ${second.name}`, exact: true })).toBeVisible();
    await expect(page.getByLabel('Published capabilities', { exact: true })).toHaveText('9');
    expect(writes).toEqual([]);
    expect(errors).toEqual([]);
  });
}
