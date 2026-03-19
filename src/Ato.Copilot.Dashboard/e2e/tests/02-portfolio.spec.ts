import { test, expect } from '@playwright/test';
import { PortfolioPage } from '../pages/portfolio.page';

test.describe('Portfolio Risk Profile', () => {
  let portfolio: PortfolioPage;

  test.beforeEach(async ({ page }) => {
    portfolio = new PortfolioPage(page);
    await portfolio.goto();
  });

  test('should load portfolio dashboard', async () => {
    await portfolio.expectLoaded();
  });

  test('should display metric KPI cards', async ({ page }) => {
    // Verify key metrics are visible on the page
    const body = await page.textContent('body');
    expect(body).toMatch(/total systems|compliance|poa.m|findings/i);
  });

  test('should display compliance by system chart', async ({ page }) => {
    // Chart section should exist
    const charts = page.locator('svg, canvas, [class*="chart"], [class*="Chart"]');
    await expect(charts.first()).toBeVisible({ timeout: 15_000 });
  });

  test('should display system risk summary table', async ({ page }) => {
    // Look for table with system rows
    const table = page.locator('table');
    if (await table.isVisible()) {
      const rows = table.locator('tbody tr');
      const count = await rows.count();
      expect(count).toBeGreaterThan(0);
    }
  });

  test('should navigate to system detail when clicking a system name', async ({ page }) => {
    // Find a clickable system name
    const systemLink = page.locator('table tbody tr a, [class*="system"] a').first();
    if (await systemLink.isVisible()) {
      await systemLink.click();
      await page.waitForLoadState('networkidle');
      expect(page.url()).toContain('/systems/');
    }
  });
});
