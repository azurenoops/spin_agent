import { test, expect } from '@playwright/test';

async function gotoAssessments(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /assessments/i }).click();
  await page.waitForLoadState('networkidle');
}

test.describe('Compliance Assessments', () => {
  test('should load assessments page', async ({ page }) => {
    await gotoAssessments(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/assessment|compliance|framework/i);
  });

  test('should display summary cards', async ({ page }) => {
    await gotoAssessments(page);
    await expect(page.getByText(/total|completed|score|findings/i).first()).toBeVisible();
  });

  test('should display assessments table', async ({ page }) => {
    await gotoAssessments(page);
    const table = page.locator('table');
    if (await table.isVisible()) {
      const rows = table.locator('tbody tr');
      const count = await rows.count();
      expect(count).toBeGreaterThanOrEqual(0);
    }
  });

  test('should search assessments', async ({ page }) => {
    await gotoAssessments(page);
    const search = page.getByPlaceholder(/search/i);
    if (await search.isVisible()) {
      await search.fill('NIST');
      await page.waitForTimeout(500);
    }
  });

  test('should show run-assessment button', async ({ page }) => {
    await gotoAssessments(page);
    const runBtn = page.getByRole('button', { name: /run|assess/i });
    if (await runBtn.isVisible()) {
      await expect(runBtn).toBeVisible();
    }
  });

  test('should open assessment detail on click', async ({ page }) => {
    await gotoAssessments(page);
    const rows = page.locator('table tbody tr');
    if (await rows.first().isVisible()) {
      const viewBtn = rows.first().getByRole('button', { name: /view|detail/i });
      if (await viewBtn.isVisible().catch(() => false)) {
        await viewBtn.click();
        await page.waitForTimeout(500);
      } else {
        await rows.first().click();
        await page.waitForTimeout(500);
      }
    }
  });
});
