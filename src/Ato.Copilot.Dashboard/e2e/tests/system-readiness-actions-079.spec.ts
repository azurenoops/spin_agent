import { expect, test } from '@playwright/test';
import { installSystemOverviewFixture, systemOverviewRoot } from '../fixtures/system-overview';

for (const width of [1440, 390]) {
  test(`Overview source actions retain preparation and return context at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const state = await installSystemOverviewFixture(context, baseURL!);
    const system = systemOverviewRoot;
    // Act
    await page.goto(`${system}?phase=Implement&owner=all&expanded=ac1`);
    // Assert
    await expect(page.getByText('5 returned findings', { exact: true })).toBeVisible();
    await expect(page.getByText('Viewing phase: Implement', { exact: true })).toBeVisible();
    const work = page.getByRole('link', { name: 'Open Review AC-1 requirement responses' });
    await expect(work).toHaveAttribute('href', /\/narratives\?control=AC-1&statement=policy&readinessReturn=/);
    const source = new URL((await work.getAttribute('href'))!, baseURL!);
    expect(source.searchParams.get('readinessReturn')).toBe('/systems/system-a?phase=Implement&owner=all&expanded=ac1');
    await expect(page.getByRole('link', { name: 'Open package workspace' })).toHaveAttribute('href', `${system}/documents?purpose=InitialSubmission`);
    // Act
    await page.getByRole('tab', { name: 'Monitoring & follow-up' }).click();
    await page.getByRole('tab', { name: 'Package preparation' }).click();
    // Assert
    await expect(page.getByText('Recorded requirement gap 0', { exact: true })).toBeVisible();
    await expect(page.getByText('Viewing phase: Implement', { exact: true })).toBeVisible();
    expect(state.writes).toHaveLength(0);
  });
}
