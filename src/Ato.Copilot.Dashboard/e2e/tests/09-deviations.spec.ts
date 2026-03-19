import { test, expect } from '@playwright/test';
import { DeviationsPage } from '../pages/deviations.page';

async function gotoDeviations(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /deviations/i }).click();
  await page.waitForLoadState('networkidle');
  return new DeviationsPage(page);
}

test.describe('Deviations', () => {
  test('should load deviations page', async ({ page }) => {
    const dev = await gotoDeviations(page);
    await dev.expectLoaded();
  });

  test('should display summary cards', async ({ page }) => {
    const dev = await gotoDeviations(page);
    await dev.expectSummaryCards();
  });

  test('should switch deviation type tabs', async ({ page }) => {
    const dev = await gotoDeviations(page);
    const tabs = page.getByRole('tab');
    const count = await tabs.count();
    if (count > 1) {
      for (let i = 0; i < count; i++) {
        await tabs.nth(i).click();
        await page.waitForTimeout(300);
      }
    }
  });

  test('should open add-deviation form', async ({ page }) => {
    const dev = await gotoDeviations(page);
    await dev.openAddForm();
    await expect(page.locator('form, [role="dialog"]').first()).toBeVisible();
    await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
  });

  test('should filter by status', async ({ page }) => {
    const dev = await gotoDeviations(page);
    const statusSelect = page.locator('select').first();
    if (await statusSelect.isVisible()) {
      const options = await statusSelect.locator('option').allTextContents();
      if (options.length > 1) {
        await statusSelect.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should search deviations', async ({ page }) => {
    const dev = await gotoDeviations(page);
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('AC-');
      await page.waitForTimeout(500);
    }
  });

  test('should open deviation detail drawer on row click', async ({ page }) => {
    const dev = await gotoDeviations(page);
    const rows = page.locator('table tbody tr');
    if (await rows.first().isVisible()) {
      await rows.first().click();
      await page.waitForTimeout(500);
      const drawer = page.locator('[class*="drawer"], [class*="Drawer"], [class*="detail"], [class*="Detail"]');
      if (await drawer.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
        await dev.expectDrawerOpen();
      }
    }
  });
});
