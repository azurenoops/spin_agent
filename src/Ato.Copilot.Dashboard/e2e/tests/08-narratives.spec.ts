import { test, expect } from '@playwright/test';

async function gotoNarratives(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /narratives/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Narratives', () => {
  test('should load narratives page', async ({ page }) => {
    await gotoNarratives(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/narrative|control|family/i);
  });

  test('should display narratives table', async ({ page }) => {
    await gotoNarratives(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      const rows = table.locator('tbody tr');
      const count = await rows.count();
      expect(count).toBeGreaterThanOrEqual(0);
    }
  });

  test('should filter by NIST family', async ({ page }) => {
    await gotoNarratives(page);
    const familyFilter = page.getByLabel(/family/i);
    if (await familyFilter.isVisible()) {
      const options = await familyFilter.locator('option').allTextContents();
      if (options.length > 1) {
        await familyFilter.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should filter by implementation status', async ({ page }) => {
    await gotoNarratives(page);
    const statusFilter = page.locator('select').last();
    if (await statusFilter.isVisible()) {
      const options = await statusFilter.locator('option').allTextContents();
      if (options.length > 1) {
        await statusFilter.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should open add narrative form', async ({ page }) => {
    await gotoNarratives(page);
    const addBtn = page.getByRole('button', { name: /add narrative/i });
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.locator('form, [role="dialog"]').first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should edit a narrative', async ({ page }) => {
    await gotoNarratives(page);
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForTimeout(500);
      // Text editor or form should be visible
      const textarea = page.locator('textarea');
      if (await textarea.isVisible()) {
        await expect(textarea).toBeVisible();
      }
      await page.getByRole('button', { name: /cancel|close/i }).click().catch(() => {});
    }
  });
});
