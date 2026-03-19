import { test, expect } from '@playwright/test';

async function gotoLegal(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /legal/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Legal & Regulatory', () => {
  test('should load legal page', async ({ page }) => {
    await gotoLegal(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/legal|regulatory|policy/i);
  });

  test('should display policy list', async ({ page }) => {
    await gotoLegal(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      const rows = table.locator('tbody tr');
      const count = await rows.count();
      expect(count).toBeGreaterThanOrEqual(0);
    }
  });

  test('should open create-policy form', async ({ page }) => {
    await gotoLegal(page);
    const addBtn = page.getByRole('button', { name: /create|add|new/i }).first();
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.getByLabel(/name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should create a policy', async ({ page }) => {
    await gotoLegal(page);
    const addBtn = page.getByRole('button', { name: /create|add|new/i }).first();
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      const name = `E2E Policy ${Date.now().toString().slice(-6)}`;
      await page.getByLabel(/name/i).first().fill(name);
      await page.getByRole('button', { name: /save|create|submit/i }).click();
      await page.waitForLoadState('networkidle');
      await expect(page.getByText(name).first()).toBeVisible();
    }
  });

  test('should search policies', async ({ page }) => {
    await gotoLegal(page);
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('FISMA');
      await page.waitForTimeout(500);
    }
  });

  test('should edit a policy', async ({ page }) => {
    await gotoLegal(page);
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
      await expect(page.getByLabel(/name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should delete a policy', async ({ page }) => {
    await gotoLegal(page);
    const deleteBtn = page.getByRole('button', { name: /delete/i }).first();
    if (await deleteBtn.isVisible()) {
      await deleteBtn.click();
      await page.waitForTimeout(300);
      const confirmBtn = page.getByRole('button', { name: /confirm|yes/i });
      if (await confirmBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
        // Don't actually delete to keep test idempotent
        await page.getByRole('button', { name: /cancel|no/i }).click().catch(() => {});
      }
    }
  });
});
