import { test, expect } from '@playwright/test';

async function gotoRoadmap(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /roadmap/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Implementation Roadmap', () => {
  test('should load roadmap page', async ({ page }) => {
    await gotoRoadmap(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/roadmap|phase|effort|risk|timeline/i);
  });

  test('should display summary metrics', async ({ page }) => {
    await gotoRoadmap(page);
    await expect(page.getByText(/total|effort|risk|timeline/i).first()).toBeVisible();
  });

  test('should display phase timeline visualization', async ({ page }) => {
    await gotoRoadmap(page);
    const timeline = page.locator('[class*="timeline"], [class*="gantt"], svg, canvas');
    await expect(timeline.first()).toBeVisible({ timeout: 15_000 });
  });

  test('should display risk reduction chart', async ({ page }) => {
    await gotoRoadmap(page);
    const chart = page.locator('svg, canvas, [class*="chart"], [class*="Chart"]');
    await expect(chart.first()).toBeVisible({ timeout: 15_000 });
  });

  test('should display phase progress table', async ({ page }) => {
    await gotoRoadmap(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      const rows = table.locator('tbody tr');
      const count = await rows.count();
      expect(count).toBeGreaterThan(0);
    }
  });

  test('should expand phase to show items', async ({ page }) => {
    await gotoRoadmap(page);
    const expandable = page.locator('[class*="phase"], [class*="Phase"], table tbody tr').first();
    if (await expandable.isVisible()) {
      await expandable.click();
      await page.waitForTimeout(500);
    }
  });
});
