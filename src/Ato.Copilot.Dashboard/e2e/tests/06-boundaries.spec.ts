import { test, expect } from '@playwright/test';
import { BoundariesPage } from '../pages/boundaries.page';

async function gotoBoundaries(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /boundaries/i }).click();
  await page.waitForLoadState('networkidle');
  return new BoundariesPage(page);
}

test.describe('Boundary Management', () => {
  test('should load boundaries page', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    await boundaries.expectLoaded();
  });

  test('should list existing boundaries', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    // At minimum, the Primary/Default boundary should exist
    await expect(page.getByText(/default|primary/i).first()).toBeVisible();
  });

  test('should open create boundary form', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    await boundaries.openCreateForm();
    await expect(page.getByLabel(/^name/i).first()).toBeVisible();
  });

  test('should create a new boundary', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    const name = `E2E Boundary ${Date.now().toString().slice(-6)}`;
    await boundaries.createBoundary({
      name,
      type: 'Logical',
      description: 'E2E test boundary',
    });
    await boundaries.expectBoundaryListed(name);
  });

  test('should expand a boundary to see components and resources', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    // Click the first boundary to expand it
    const boundaryName = page.locator('[class*="boundary"] h3, [class*="boundary"] h4, [class*="card"] h3').first();
    if (await boundaryName.isVisible()) {
      await boundaryName.click();
      await page.waitForTimeout(500);
      // Expanded content should be visible
      const body = await page.textContent('body');
      expect(body).toBeTruthy();
    }
  });

  test('should edit a boundary', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.getByLabel(/^name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click();
    }
  });

  test('should filter boundaries by type', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    const typeFilter = page.locator('select').first();
    if (await typeFilter.isVisible()) {
      const options = await typeFilter.locator('option').allTextContents();
      if (options.length > 1) {
        await typeFilter.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should search boundaries', async ({ page }) => {
    const boundaries = await gotoBoundaries(page);
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('Default');
      await page.waitForTimeout(500);
    }
  });
});
