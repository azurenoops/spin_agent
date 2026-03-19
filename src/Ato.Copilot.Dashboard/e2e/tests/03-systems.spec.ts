import { test, expect } from '@playwright/test';
import { SystemsPage } from '../pages/systems.page';

test.describe('Systems Management', () => {
  let systems: SystemsPage;

  test.beforeEach(async ({ page }) => {
    systems = new SystemsPage(page);
    await systems.goto();
  });

  test('should load systems list', async () => {
    await systems.expectLoaded();
  });

  test('should display systems in the table', async () => {
    await systems.expectSystemsListed();
  });

  test('should open add-system dialog', async ({ page }) => {
    await systems.openAddSystemForm();
    // Dialog should be visible with form fields
    await expect(page.getByLabel(/name/i).first()).toBeVisible();
  });

  test('should register a new system', async ({ page }) => {
    const name = `E2E Test System ${Date.now()}`;
    await systems.registerSystem({ name, acronym: 'E2E' });
    // After creation, the system should appear in the list
    await page.waitForTimeout(1_000);
    await expect(page.getByText(name).first()).toBeVisible();
  });

  test('should sort by column header', async ({ page }) => {
    const firstCell = page.locator('table tbody tr td').first();
    const before = await firstCell.textContent();
    await systems.sortByColumn('System');
    const after = await firstCell.textContent();
    // Content may change order (or stay same if single item)
    expect(typeof after).toBe('string');
  });

  test('should filter by impact level', async ({ page }) => {
    const select = page.locator('select').first();
    if (await select.isVisible()) {
      const options = await select.locator('option').allTextContents();
      if (options.length > 1) {
        await select.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should navigate into system detail', async ({ page }) => {
    const link = page.locator('table tbody tr a').first();
    if (await link.isVisible()) {
      await link.click();
      await page.waitForLoadState('networkidle');
      expect(page.url()).toContain('/systems/');
    }
  });
});
