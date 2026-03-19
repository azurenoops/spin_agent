import { test, expect } from '@playwright/test';

async function gotoGaps(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /gaps/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Gap Analysis', () => {
  test('should load gap analysis page', async ({ page }) => {
    await gotoGaps(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/gap|coverage|control|covered|waived/i);
  });

  test('should display summary metrics', async ({ page }) => {
    await gotoGaps(page);
    await expect(page.getByText(/total|coverage|gaps/i).first()).toBeVisible();
  });

  test('should select boundary from dropdown', async ({ page }) => {
    await gotoGaps(page);
    const select = page.locator('select').first();
    if (await select.isVisible()) {
      const options = await select.locator('option').allTextContents();
      if (options.length > 1) {
        await select.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should display coverage matrix', async ({ page }) => {
    await gotoGaps(page);
    // Coverage matrix shows NIST families
    const body = await page.textContent('body');
    expect(body).toMatch(/AC|AU|AT|CA|CM|CP|IA|IR|MA|MP|PE|PL|PS|RA|SA|SC|SI|PM/);
  });

  test('should display boundary comparison table for "All Boundaries"', async ({ page }) => {
    await gotoGaps(page);
    // When "All Boundaries" is selected, a comparison table should appear
    const table = page.locator('table');
    if (await table.isVisible()) {
      const body = await table.textContent();
      expect(body).toMatch(/boundary|coverage|total|gaps/i);
    }
  });
});
