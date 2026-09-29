import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture, systemCapabilityRoot } from '../fixtures/system-capabilities';

const groups = ['Overview', 'System definition', 'Controls & evidence', 'Assessment & risk',
  'ATO package & eMASS', 'Continuous monitoring', 'Team & permissions', 'Activity & history'];
const topLinks = ['Portfolio', 'Systems', 'Security Capabilities', 'Knowledge Base'];
const root = '/workspaces/organizations/org-a/systems/system-a';

for (const width of [1440, 1000, 700, 390]) {
  test(`organization top links and eight icon sections match the system navigation at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    const writes: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.url());
    });
    // Act
    await page.goto(systemCapabilityRoot);
    await expect(page.getByRole('heading', { name: 'Security Capabilities', exact: true })).toBeVisible();

    // Assert
    expect(await page.locator('.organization-topbar > div:first-child img').evaluate(element => element.getBoundingClientRect().height)).toBe(42);
    const sidebar = page.locator('.system-sidebar');
    if (width > 650) {
      const top = page.getByRole('navigation', { name: 'Organization navigation' });
      await expect(top.getByRole('link')).toHaveText(topLinks);
      await expect(top.getByRole('link', { name: 'Systems', exact: true })).toHaveAttribute('aria-current', 'page');
      const navigationBounds = await top.boundingBox();
      const toolsBounds = await page.getByRole('button', { name: 'Notifications', exact: true }).boundingBox();
      expect(navigationBounds && toolsBounds && (navigationBounds.x + navigationBounds.width <= toolsBounds.x
        || navigationBounds.y + navigationBounds.height <= toolsBounds.y)).toBeTruthy();
      await expect(sidebar.getByText('Synthetic Mission System', { exact: true })).toBeVisible();
      await expect(sidebar.getByRole('link')).toHaveText(groups);
      await expect(sidebar.locator('svg[aria-hidden=true]')).toHaveCount(8);
      expect(await sidebar.evaluate(element => element.getBoundingClientRect().width)).toBe(width === 1440 ? 220 : 180);
      await expect(sidebar.getByRole('link', { name: 'Controls & evidence', exact: true })).toHaveAttribute('aria-current', 'page');
      await sidebar.getByRole('link', { name: 'System definition', exact: true }).focus();
      await page.keyboard.press('Enter');
    } else {
      await expect(sidebar).toBeHidden();
      const selector = page.getByRole('combobox', { name: 'Navigate system pages' });
      await expect(selector).toBeVisible();
      await expect(selector.locator('option')).toHaveCount(30);
      expect(await selector.locator('optgroup').evaluateAll(items => items.map(item => item.label))).toEqual(groups);
      await page.getByLabel('Open organization navigation').click();
      await expect(page.getByRole('navigation', { name: 'Mobile navigation' }).getByRole('link')).toHaveText(topLinks);
      await page.getByLabel('Open organization navigation').click();
      await selector.selectOption('profile/MissionAndPurpose');
    }
    await expect(page).toHaveURL(`${baseURL}${root}/profile/MissionAndPurpose`);
    await expect(page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Mission', exact: true })).toHaveAttribute('aria-current', 'page');
    if (width > 650) await expect(sidebar.getByRole('link', { name: 'System definition', exact: true })).toHaveAttribute('aria-current', 'page');
    else await expect(page.getByRole('combobox', { name: 'Navigate system pages' })).toHaveValue('profile/MissionAndPurpose');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`system-navigation-${width}.png`), fullPage: true });
    expect(writes).toEqual([]);
  });
}
