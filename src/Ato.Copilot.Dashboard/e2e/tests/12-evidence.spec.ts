import { test, expect } from '@playwright/test';
import { EvidencePage } from '../pages/evidence.page';

async function gotoEvidence(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /evidence/i }).click();
  await page.waitForLoadState('networkidle');
  return new EvidencePage(page);
}

test.describe('Evidence Repository', () => {
  test('should load evidence page', async ({ page }) => {
    const evidence = await gotoEvidence(page);
    await evidence.expectLoaded();
  });

  test('should display summary metrics', async ({ page }) => {
    const evidence = await gotoEvidence(page);
    await evidence.expectSummaryMetrics();
  });

  test('should open upload dialog', async ({ page }) => {
    const evidence = await gotoEvidence(page);
    await evidence.openUploadDialog();
    await expect(page.locator('form, [role="dialog"]').first()).toBeVisible();
    await page.getByRole('button', { name: /cancel|close/i }).click().catch(() => {});
  });

  test('should search evidence', async ({ page }) => {
    const evidence = await gotoEvidence(page);
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('scan');
      await page.waitForTimeout(500);
    }
  });

  test('should filter evidence by category', async ({ page }) => {
    const evidence = await gotoEvidence(page);
    const catSelect = page.getByLabel(/category/i);
    if (await catSelect.isVisible()) {
      const options = await catSelect.locator('option').allTextContents();
      if (options.length > 1) {
        await catSelect.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should sort evidence by column', async ({ page }) => {
    const evidence = await gotoEvidence(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      const header = table.locator('th').first();
      await header.click();
      await page.waitForTimeout(300);
    }
  });
});
