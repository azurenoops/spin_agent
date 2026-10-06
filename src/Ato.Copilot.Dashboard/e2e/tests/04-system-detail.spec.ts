import { test, expect } from '@playwright/test';
import { SystemDetailPage } from '../pages/system-detail.page';

/**
 * Helper: navigate into the first system's detail page.
 */
async function gotoFirstSystem(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  const link = page.locator('table tbody tr a').first();
  await link.click();
  await page.waitForLoadState('networkidle');
  return new SystemDetailPage(page);
}

test.describe('System Detail / Readiness', () => {
  test('should show readiness tasks without legacy diagnostics', async ({ page }) => {
    // Arrange / Act
    const detail = await gotoFirstSystem(page);
    // Assert
    await detail.expectReadinessTasks();
  });

  test('should retain package preparation links', async ({ page }) => {
    // Arrange
    await gotoFirstSystem(page);
    // Act
    await page.getByRole('tab', { name: 'Package preparation', exact: true }).click();
    // Assert
    await expect(page.getByRole('link', { name: 'Open package workspace', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'What to work on next', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Inspect canonical checks and history', exact: true })).toBeVisible();
  });

  test('should retain monitoring follow-up navigation', async ({ page }) => {
    // Arrange
    await gotoFirstSystem(page);
    // Act
    await page.getByRole('tab', { name: 'Monitoring & follow-up', exact: true }).click();
    // Assert
    await expect(page.getByRole('heading', { name: 'Keep your documented system current', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Review monitoring coverage', exact: true })).toBeVisible();
  });
});
