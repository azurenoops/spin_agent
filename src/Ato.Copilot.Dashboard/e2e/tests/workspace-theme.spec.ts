import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

for (const provider of [false, true]) {
  for (const width of [1440, 390]) {
    test(`${provider ? 'provider' : 'organization'} library light/dark selection persists at ${width}px`, async ({ page, context, baseURL }) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await page.emulateMedia({ colorScheme: 'dark' });
      await installWorkspaceFixture(context, baseURL!, { providerOnly: provider });
      await context.route('**/api/workspaces/organizations/*/capabilities?*', route =>
        route.fulfill({ json: { data: { items: [], total: 0, page: 1, pageSize: 25 } } }));
      await context.route('**/api/csp/catalog?*', route =>
        route.fulfill({ json: { data: { items: [], total: 0, page: 1, pageSize: 25 } } }));
      await context.route('**/api/csp/catalog/overview?*', route =>
        route.fulfill({ json: { data: { providerName: 'Synthetic Provider', sourceArtifacts: { items: [], page: 1, pageSize: 25, total: 0 }, authorizationRecord: null } } }));
      const path = provider ? '/workspaces/csp/security-capabilities'
        : '/workspaces/organizations/org-a/security-capabilities';
      await page.goto(path);
      const heading = page.getByRole('main').getByRole('heading', { name: provider ? 'Capabilities you provide' : 'Security Capabilities', exact: true });
      await expect(heading).toBeVisible();
      await expect(page.getByRole('main')).toHaveCSS('background-color', 'rgb(255, 255, 255)');

      // Act
      await page.getByRole('button', { name: 'Settings', exact: true }).click();
      await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
      const theme = page.getByRole('combobox', { name: 'Theme', exact: true });
      await theme.selectOption('light');

      // Assert
      await expect(page.locator('html')).not.toHaveClass(/dark/);
      await expect(page.locator('html')).toHaveCSS('color-scheme', 'light');
      await expect(page.getByRole('main')).toHaveCSS('background-color', 'rgb(255, 255, 255)');
      await expect(theme).toHaveCSS('background-color', 'rgb(255, 255, 255)');
      await expect(page.getByRole('region', { name: 'Active workspace' })).toHaveCSS('background-color', 'rgb(238, 242, 255)');
      await expect.poll(() => page.evaluate(() =>
        JSON.parse(localStorage.getItem('ato-dashboard-settings') ?? '{}').theme)).toBe('light');
      await page.reload();
      await expect(heading).toBeVisible();
      await expect(page.getByRole('main')).toHaveCSS('background-color', 'rgb(255, 255, 255)');
      await page.getByRole('button', { name: 'Settings', exact: true }).click();
      await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
      await expect(theme).toHaveValue('light');
      await page.emulateMedia({ colorScheme: 'light' });
      await theme.selectOption('dark');
      await expect(page.locator('html')).toHaveClass(/dark/);
      await expect(page.locator('html')).toHaveCSS('color-scheme', 'dark');
      await expect(page.getByRole('main')).toHaveCSS('background-color', 'rgb(3, 7, 18)');
      await expect(theme).toHaveCSS('background-color', 'rgb(31, 41, 55)');
      await expect(theme).toHaveCSS('color', 'rgb(243, 244, 246)');
      await expect(page.getByRole('region', { name: 'Active workspace' })).toHaveCSS('background-color', 'rgb(30, 27, 75)');
      await expect.poll(() => page.evaluate(() =>
        JSON.parse(localStorage.getItem('ato-dashboard-settings') ?? '{}').theme)).toBe('dark');
      await page.reload();
      await expect(heading).toBeVisible();
      await expect(page.getByRole('main')).toHaveCSS('background-color', 'rgb(3, 7, 18)');
      await page.getByRole('button', { name: 'Settings', exact: true }).click();
      await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
      await theme.selectOption('system');
      await expect(page.locator('html')).not.toHaveClass(/dark/);
      await page.emulateMedia({ colorScheme: 'dark' });
      await expect(page.locator('html')).toHaveClass(/dark/);
    });
  }
}
