import { expect, test } from '@playwright/test';
import { installSystemOverviewFixture, systemOverviewRoot } from '../fixtures/system-overview';

for (const width of [1440, 390]) {
  test(`Overview personal work uses explicit assignments, not simulated roles at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const state = await installSystemOverviewFixture(context, baseURL!);
    // Act
    await page.goto(`${systemOverviewRoot}?owner=mine`);
    // Assert
    await expect(page.getByText('1 work group', { exact: true })).toBeVisible();
    await expect(page.getByText('Review AC-1 requirement responses', { exact: true })).toBeVisible();
    await expect(page.getByText('Review system design', { exact: true })).toHaveCount(0);
    // Act
    await page.evaluate(() => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' })));
    await page.reload();
    // Assert
    await expect(page.getByText('1 work group', { exact: true })).toBeVisible();
    await expect(page.getByText('Review system design', { exact: true })).toHaveCount(0);
    await expect(page.getByText('5 returned findings', { exact: true })).toBeVisible();
    // Act
    await page.getByRole('tab', { name: 'All system work' }).click();
    // Assert
    await expect(page.getByText('2 work groups', { exact: true })).toBeVisible();
    await expect(page.getByText('Review system design', { exact: true })).toBeVisible();
    expect(state.writes).toHaveLength(0);
  });
}
