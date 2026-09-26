import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
for (const width of [1440, 1097, 390]) for (const dark of [false, true]) test(`offering screenshot layout and header creation at ${width}px (${dark ? 'dark' : 'light'})`, async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(theme => {
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ theme }));
  }, dark ? 'dark' : 'light');
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const offering = { offeringId: 'sample', providerId: 'provider', name: 'Azure IL5', description: '', environments: ['AzureUSGovernment'], revision: 1, lifecycle: 'Draft', currentBoundaryRevisionId: null, currentHostingScopeRevisionId: null };
  let created = false;
  await context.route('**/api/csp/offerings**', route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'POST') {
      const data = request.postDataJSON();
      expect(data.name).toBe('Sample offering');
      expect(data.environments).toEqual(['AzureUSGovernment']);
      created = true;
      return route.fulfill({ json: { status: 'success', data: { ...offering, name: data.name } } });
    }
    const data = path.endsWith('/offerings') ? { items: [offering], page: 1, pageSize: 25, total: 1 }
      : path.endsWith('/sample') ? { ...offering, name: created ? 'Sample offering' : offering.name }
      : { items: [], page: 1, pageSize: 25, total: 0 };
    return route.fulfill({ json: { status: 'success', data } });
  });
  // Act
  await page.goto('/workspaces/csp/authorizations');
  const card = page.getByRole('article', { name: 'Azure IL5', exact: true });
  await expect(card).toBeVisible();
  await expect(card).toHaveCSS('background-color', dark ? 'rgb(17, 24, 39)' : 'rgb(255, 255, 255)');
  const create = page.getByRole('link', { name: 'Create offering', exact: true });
  // Assert
  await expect(page.getByRole('link', { name: 'Import authorization package', exact: true })).toBeVisible();
  await expect(create).toBeVisible();
  await expect(create).toHaveAttribute('href', '/workspaces/csp/authorizations/create');
  expect(await create.evaluate(element => element.parentElement === [...document.querySelectorAll('a')].find(link => link.textContent === 'Import authorization package')?.parentElement)).toBe(true);
  await expect(page.locator('summary').filter({ hasText: 'Create an offering' })).toHaveCount(0);
  await expect(page.getByLabel('Offering name', { exact: true })).toHaveCount(0);
  await expect(card.getByText('Not recorded')).toHaveCount(2);
  await expect(card.getByRole('link', { name: 'Define boundary for Azure IL5' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/sample/boundary');
  await expect(card.getByRole('link', { name: 'Add package to this offering: Azure IL5' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/sample/import');
  await expect(card.getByRole('link', { name: 'Manage Azure IL5' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/sample');
  const guide = await page.getByRole('complementary', { name: 'Authorization workflow' }).boundingBox();
  const cardBox = await card.boundingBox();
  expect(guide).not.toBeNull();
  expect(cardBox).not.toBeNull();
  if (width >= 1024) expect(guide!.x).toBeGreaterThanOrEqual(cardBox!.x + cardBox!.width);
  else expect(guide!.y).toBeGreaterThan(cardBox!.y + cardBox!.height);
  expect(await page.locator('main').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  const backdrop = await page.locator('main > div').boundingBox();
  const main = await page.locator('main').boundingBox();
  expect(backdrop).not.toBeNull();
  expect(main).not.toBeNull();
  expect(backdrop!.y + backdrop!.height).toBeGreaterThanOrEqual(main!.y + main!.height);
  await page.screenshot({ path: testInfo.outputPath(`landing-${width}-${dark ? 'dark' : 'light'}.png`) });
  if (width < 1024) {
    await page.getByRole('complementary', { name: 'Authorization workflow' }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: testInfo.outputPath(`landing-${width}-${dark ? 'dark' : 'light'}-guide.png`) });
  }
  // Act
  await create.focus();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/authorizations\/create$/);
  await expect(page.getByRole('button', { name: 'Create offering', exact: true })).toBeDisabled();
  await page.getByLabel('Offering name', { exact: true }).fill('Sample offering');
  await page.getByLabel('Azure Government', { exact: true }).check();
  await page.getByRole('button', { name: 'Create offering', exact: true }).click();
  // Assert
  await expect(page).toHaveURL(/\/authorizations\/offerings\/sample$/);
  await expect(page.getByRole('heading', { name: 'Sample offering', exact: true })).toBeVisible();
  expect(created).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('keeps multiple offerings vertically stacked on a wide desktop', async ({ page, context, baseURL }) => {
  // Arrange
  await page.setViewportSize({ width: 1680, height: 1000 });
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  await context.route('**/api/csp/offerings**', route => route.fulfill({ json: { status: 'success', data: {
    items: ['First offering', 'Second offering'].map((name, index) => ({
      offeringId: `offering-${index}`, providerId: 'provider', name, description: '', environments: ['AzureUSGovernment'],
      revision: 1, lifecycle: 'Draft', currentBoundaryRevisionId: null, currentHostingScopeRevisionId: null,
    })), page: 1, pageSize: 25, total: 2,
  } } }));
  // Act
  await page.goto('/workspaces/csp/authorizations');
  await expect(page.getByRole('article', { name: 'Second offering' })).toBeVisible();
  const first = await page.getByRole('article', { name: 'First offering' }).boundingBox();
  const second = await page.getByRole('article', { name: 'Second offering' }).boundingBox();
  // Assert
  expect(first).not.toBeNull();
  expect(second).not.toBeNull();
  expect(second!.y).toBeGreaterThan(first!.y + first!.height);
  expect(second!.x).toBe(first!.x);
  expect(second!.width).toBe(first!.width);
});
