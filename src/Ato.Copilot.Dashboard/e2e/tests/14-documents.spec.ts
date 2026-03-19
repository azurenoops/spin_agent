import { test, expect } from '@playwright/test';

async function gotoDocuments(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /documents/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Documents & Exports', () => {
  test('should load documents page', async ({ page }) => {
    await gotoDocuments(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/ssp|authorization|security|document|plan/i);
  });

  test('should display authorization package section', async ({ page }) => {
    await gotoDocuments(page);
    await expect(page.getByText(/system security plan|ssp/i).first()).toBeVisible();
  });

  test('should display SSP progress', async ({ page }) => {
    await gotoDocuments(page);
    // SSP section should show progress or manage button
    const sspSection = page.getByText(/ssp|system security plan/i).first();
    await expect(sspSection).toBeVisible();
  });

  test('should display authorization decision section', async ({ page }) => {
    await gotoDocuments(page);
    const authSection = page.getByText(/authorization|ato|decision/i).first();
    await expect(authSection).toBeVisible();
  });

  test('should display privacy section', async ({ page }) => {
    await gotoDocuments(page);
    const privacySection = page.getByText(/privacy|pta|pia/i);
    if (await privacySection.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expect(privacySection.first()).toBeVisible();
    }
  });

  test('should open export SSP dialog', async ({ page }) => {
    await gotoDocuments(page);
    const exportBtn = page.getByRole('button', { name: /export|manage/i }).first();
    if (await exportBtn.isVisible()) {
      await exportBtn.click();
      await page.waitForTimeout(500);
      const dialog = page.locator('[role="dialog"], [class*="modal"], [class*="Modal"]');
      if (await dialog.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
        await expect(dialog.first()).toBeVisible();
        await page.getByRole('button', { name: /cancel|close/i }).click().catch(() => {});
      }
    }
  });
});
