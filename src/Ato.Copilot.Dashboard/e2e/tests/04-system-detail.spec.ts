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
    await page.getByRole('tab', { name: 'Readiness', exact: true }).click();
    // Assert
    await expect(page.getByRole('link', { name: 'Continue preparation', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Next actions for this system', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'View package readiness', exact: true })).toBeVisible();
  });

  test('should retain monitoring follow-up navigation', async ({ page }) => {
    // Arrange
    await gotoFirstSystem(page);
    // Act
    await page.getByRole('tab', { name: 'Monitoring & follow-up', exact: true }).click();
    // Assert
    await expect(page.getByRole('heading', { name: 'Maintain the reviewed baseline', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Review monitoring', exact: true })).toBeVisible();
  });
});
