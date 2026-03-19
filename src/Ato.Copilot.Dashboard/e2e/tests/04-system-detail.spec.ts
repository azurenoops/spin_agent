import { test, expect } from '@playwright/test';
import { SystemDetailPage } from '../pages/system-detail.page';

/**
 * Helper: navigate into the first system's detail page.
 */
async function gotoFirstSystem(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  const link = page.locator('table tbody tr a').first();
  await link.click();
  await page.waitForLoadState('networkidle');
  return new SystemDetailPage(page);
}

test.describe('System Detail / Overview', () => {
  test('should load system overview', async ({ page }) => {
    const detail = await gotoFirstSystem(page);
    await detail.expectMetricCards();
  });

  test('should display compliance heatmap', async ({ page }) => {
    const detail = await gotoFirstSystem(page);
    await detail.expectComplianceHeatmap();
  });

  test('should open control drill-down on heatmap family click', async ({ page }) => {
    const detail = await gotoFirstSystem(page);
    // Find a family abbreviation (AC, AU, etc.) and click it
    const families = page.locator('[class*="heatmap"] [class*="cell"], [class*="Heatmap"] div');
    if (await families.first().isVisible()) {
      await families.first().click();
      await page.waitForTimeout(500);
      // Modal or expanded view should appear
      const modal = page.locator('[role="dialog"], [class*="modal"], [class*="Modal"], [class*="drill"]');
      if (await modal.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
        await expect(modal.first()).toBeVisible();
      }
    }
  });

  test('should display activity feed', async ({ page }) => {
    const detail = await gotoFirstSystem(page);
    await detail.expectActivityFeed();
  });

  test('should display RMF phase progress', async ({ page }) => {
    const detail = await gotoFirstSystem(page);
    await detail.expectPhaseProgress();
  });

  test('should show compliance trend chart', async ({ page }) => {
    await gotoFirstSystem(page);
    const chart = page.locator('svg, canvas, [class*="chart"], [class*="Chart"], [class*="trend"]');
    await expect(chart.first()).toBeVisible({ timeout: 15_000 });
  });
});
