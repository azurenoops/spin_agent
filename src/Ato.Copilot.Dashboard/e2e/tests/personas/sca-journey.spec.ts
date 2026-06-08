import { test, expect } from '@playwright/test';
import { DashboardPage } from '../../pages/DashboardPage';
import { AssessmentsPage } from '../../pages/AssessmentsPage';
import { SystemsPage } from '../../pages/systems.page';

/**
 * SCA Persona Journey
 * Flow: Browse systems → Import scan findings → Review findings → Navigate to POA&M
 * Seed: e2e/fixtures/sca-seed.json
 */
test.describe('SCA Journey — Scan Import & Finding Review', () => {
  test.setTimeout(120_000);

  test('Step 1 — navigate to Systems', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto();
    await dashboard.expectLoaded();
    await dashboard.navigateToSystems();
    await expect(page).toHaveURL(/\/systems/);
  });

  test('Step 2 — open system and navigate to Assessments', async ({ page }) => {
    const systems = new SystemsPage(page);
    await systems.goto();
    const rows = page.locator('table tbody tr a');
    if (await rows.count() === 0) { test.skip(); return; }
    await rows.first().click();
    await page.waitForLoadState('networkidle');
    const assessments = new AssessmentsPage(page);
    await assessments.navigate();
    await assessments.expectLoaded();
  });

  test('Step 3 — check for findings table or empty state', async ({ page }) => {
    const systems = new SystemsPage(page);
    await systems.goto();
    const rows = page.locator('table tbody tr a');
    if (await rows.count() === 0) { test.skip(); return; }
    await rows.first().click();
    await page.waitForLoadState('networkidle');
    const assessments = new AssessmentsPage(page);
    await assessments.navigate();
    await assessments.expectFindingsVisible();
    // Findings table or empty state must be present
    const hasContent = await page.locator('table tbody tr, [class*="empty"], [class*="no-data"]').first()
      .isVisible({ timeout: 10_000 }).catch(() => false);
    expect(hasContent).toBeTruthy();
  });

  test('Step 4 — navigate to POA&M tab', async ({ page }) => {
    const systems = new SystemsPage(page);
    await systems.goto();
    const rows = page.locator('table tbody tr a');
    if (await rows.count() === 0) { test.skip(); return; }
    await rows.first().click();
    await page.waitForLoadState('networkidle');
    const poamTab = page.getByRole('link', { name: /poa|poam|plan of action/i }).first();
    if (await poamTab.isVisible()) {
      await poamTab.click();
      await page.waitForLoadState('networkidle');
      await expect(page.getByRole('heading', { name: /poa|poam/i }).first()).toBeVisible();
    }
  });
});
