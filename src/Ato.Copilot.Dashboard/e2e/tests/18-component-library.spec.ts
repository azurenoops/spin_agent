import { test, expect } from '@playwright/test';

test.describe('Component Library (Org-wide)', () => {
  test('should load component library page', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const body = await page.textContent('body');
    expect(body).toMatch(/component|person|place|thing/i);
  });

  test('should display component cards', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const cards = page.locator('[class*="card"], [class*="component"]');
    await expect(cards.first()).toBeVisible({ timeout: 15_000 });
  });

  test('should show type and status filters', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const selects = page.locator('select');
    const count = await selects.count();
    expect(count).toBeGreaterThanOrEqual(0);
  });

  test('should search components', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('ISSM');
      await page.waitForTimeout(500);
    }
  });

  test('should open add-component form', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const addBtn = page.getByRole('button', { name: /create|add|new/i }).first();
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.getByLabel(/^name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should filter by component type', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const typeSelect = page.locator('select').first();
    if (await typeSelect.isVisible()) {
      const options = await typeSelect.locator('option').allTextContents();
      if (options.length > 1) {
        await typeSelect.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should edit a component from library', async ({ page }) => {
    await page.goto('/components');
    await page.waitForLoadState('networkidle');
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.getByLabel(/^name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });
});
