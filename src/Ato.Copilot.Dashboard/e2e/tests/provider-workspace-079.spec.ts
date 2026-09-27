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
}
