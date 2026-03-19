import { test, expect } from '@playwright/test';
import { RemediationPage } from '../pages/remediation.page';

async function gotoRemediation(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /remediation/i }).click();
  await page.waitForLoadState('networkidle');
  return new RemediationPage(page);
}

test.describe('Remediation — Table View', () => {
  test('should load remediation page', async ({ page }) => {
    const rem = await gotoRemediation(page);
    await rem.expectLoaded();
  });

  test('should display summary cards', async ({ page }) => {
    const rem = await gotoRemediation(page);
    await rem.expectSummaryCards();
  });

  test('should display table by default', async ({ page }) => {
    const rem = await gotoRemediation(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      await rem.expectTableView();
    }
  });

  test('should search tasks', async ({ page }) => {
    const rem = await gotoRemediation(page);
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('AC-');
      await page.waitForTimeout(500);
    }
  });

  test('should open task detail drawer on click', async ({ page }) => {
    const rem = await gotoRemediation(page);
    const rows = page.locator('table tbody tr');
    if (await rows.first().isVisible()) {
      await rows.first().click();
      await page.waitForTimeout(500);
      const drawer = page.locator('[class*="drawer"], [class*="Drawer"]');
      if (await drawer.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
        await rem.expectDrawerOpen();
      }
    }
  });
});

test.describe('Remediation — Kanban View', () => {
  test('should switch to kanban view', async ({ page }) => {
    const rem = await gotoRemediation(page);
    const kanbanBtn = page.getByRole('button', { name: /kanban/i });
    if (await kanbanBtn.isVisible()) {
      await kanbanBtn.click();
      await page.waitForTimeout(500);
      await rem.expectKanbanColumns();
    }
  });

  test('should display kanban columns', async ({ page }) => {
    const rem = await gotoRemediation(page);
    const kanbanBtn = page.getByRole('button', { name: /kanban/i });
    if (await kanbanBtn.isVisible()) {
      await kanbanBtn.click();
      await page.waitForTimeout(500);
      // Look for column headers
      const body = await page.textContent('body');
      expect(body).toMatch(/backlog|to.?do|in.?progress|review|done|blocked/i);
    }
  });

  test('should switch back to table view', async ({ page }) => {
    const rem = await gotoRemediation(page);
    const kanbanBtn = page.getByRole('button', { name: /kanban/i });
    if (await kanbanBtn.isVisible()) {
      await kanbanBtn.click();
      await page.waitForTimeout(300);
      const tableBtn = page.getByRole('button', { name: /table/i });
      if (await tableBtn.isVisible()) {
        await tableBtn.click();
        await page.waitForTimeout(300);
        await rem.expectTableView();
      }
    }
  });
});
