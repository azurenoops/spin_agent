import { test, expect } from '@playwright/test';
import { DashboardPage } from '../../pages/DashboardPage';
import { ControlsPage } from '../../pages/ControlsPage';
import { AssessmentsPage } from '../../pages/AssessmentsPage';
import { AuthorizationPage } from '../../pages/AuthorizationPage';
import { SystemsPage } from '../../pages/systems.page';

/**
 * ISSO Persona Journey
 * RMF Steps: Categorize → Select → Implement → Assess → Authorize
 * Seed: e2e/fixtures/isso-seed.json
 *
 * NOTE: This is a structural journey spec. Individual steps are
 * smoke-tested for navigation and page load — full data-driven
 * assertions require a seeded API environment.
 */
test.describe('ISSO Journey — Full RMF Flow', () => {
  test.setTimeout(120_000);

  test('Step 1 — navigate to Systems and load dashboard', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await dashboard.goto();
    await dashboard.expectLoaded();
    await dashboard.navigateToSystems();
    await expect(page).toHaveURL(/\/systems/);
  });

  test('Step 2 — open a system and verify tabs', async ({ page }) => {
    const systems = new SystemsPage(page);
    await systems.goto();
    const rows = page.locator('table tbody tr a');
    const count = await rows.count();
    if (count === 0) {
      test.skip();
      return;
    }
    await rows.first().click();
    await page.waitForLoadState('networkidle');
    // System detail should be visible
    await expect(page.locator('nav, [role="tablist"]').first()).toBeVisible();
  });

  test('Step 3 — navigate to Controls tab', async ({ page }) => {
    const systems = new SystemsPage(page);
    await systems.goto();
    const rows = page.locator('table tbody tr a');
    if (await rows.count() === 0) { test.skip(); return; }
    await rows.first().click();
    await page.waitForLoadState('networkidle');
    const controls = new ControlsPage(page);
    await controls.navigate();
    await controls.expectLoaded();
  });

  test('Step 4 — navigate to Assessments tab', async ({ page }) => {
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

  test('Step 5 — navigate to Authorization tab', async ({ page }) => {
    const systems = new SystemsPage(page);
    await systems.goto();
    const rows = page.locator('table tbody tr a');
    if (await rows.count() === 0) { test.skip(); return; }
    await rows.first().click();
    await page.waitForLoadState('networkidle');
    const auth = new AuthorizationPage(page);
    await auth.navigate();
    await auth.expectLoaded();
  });
});
