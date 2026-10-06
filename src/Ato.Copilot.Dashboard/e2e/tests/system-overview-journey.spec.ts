import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { installSystemOverviewFixture, systemOverviewRoot } from '../fixtures/system-overview';

for (const width of [1440, 390]) {
  for (const theme of ['light', 'dark']) {
    test(`RMF journey and paged work ${width}px ${theme}`, async ({ page, context, baseURL }, info) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      const state = await installSystemOverviewFixture(context, baseURL!, { large: true });
      await context.addInitScript(value => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ theme: value })), theme);
      // Act
      await page.goto(systemOverviewRoot);
      // Assert
      await expect(page.getByRole('heading', { name: 'A clear path to your ATO package' })).toBeVisible();
      await expect(page.getByText('2408 returned findings', { exact: true })).toBeVisible();
      await expect(page.getByText('Current RMF phase: Not confirmed', { exact: true })).toBeVisible();
      // Act
      await page.getByRole('button', { name: 'View Prepare phase' }).focus();
      await page.keyboard.press('ArrowRight');
      // Assert
      await expect(page.getByRole('button', { name: 'View Categorize phase' })).toBeFocused();
      await expect(page.getByText('Viewing phase: Categorize')).toBeVisible();
      await expect(page.getByText('Current RMF phase: Not confirmed')).toBeVisible();
      expect(state.writes).toHaveLength(0);
      // Act
      await page.getByText('Review AC-1 requirement responses', { exact: true }).click();
      await page.getByRole('button', { name: 'Next findings' }).click();
      // Assert
      await expect(page.getByText('Recorded AC-1 requirement gap 20', { exact: true })).toBeVisible();
      await expect(page.locator('summary').filter({ hasText: 'Technical identifiers' })).toHaveCount(21);
      // Act
      await page.getByRole('tab', { name: 'Assigned to me' }).click();
      // Assert
      await expect(page.getByText('1 work group', { exact: true })).toBeVisible();
      await expect(page.getByText('2408 returned findings', { exact: true })).toBeVisible();
      // Act
      await page.getByRole('tab', { name: 'Monitoring & follow-up' }).click();
      // Assert
      await expect(page.getByText('ScopeUnsupported', { exact: true })).toBeVisible();
      await expect(page.getByText('Current RMF phase: Not confirmed', { exact: true })).toBeVisible();
      await expect(page.getByText('reviewed-baseline-7', { exact: true })).toBeVisible();
      await page.addScriptTag({ content: axe.source });
      const violations = await page.evaluate(async () => {
        const result = await (window as Window & { axe: typeof import('axe-core') }).axe.run(document.querySelector('main')!, {
          runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
        });
        return result.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) }));
      });
      expect(violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.getByRole('tab', { name: 'Package preparation' }).click();
      await expect(page.getByRole('tab', { name: 'Assigned to me' })).toHaveAttribute('aria-selected', 'true');
      await page.screenshot({ path: info.outputPath(`system-overview-${width}-${theme}.png`), fullPage: true });
    });
  }
}
test('failed check retains findings; phase confirmation is explicit and audited', async ({ page, context, baseURL }) => {
  // Arrange
  const state = await installSystemOverviewFixture(context, baseURL!, { failure: true });
  await page.goto(systemOverviewRoot);
  await expect(page.getByText('5 returned findings', { exact: true })).toBeVisible();
  // Act
  await page.getByRole('button', { name: 'Check again' }).click();
  // Assert
  await expect(page.getByText('Readiness refresh unavailable. Previous check remains recorded.', { exact: true })).toBeVisible();
  await expect(page.getByText('5 returned findings', { exact: true })).toBeVisible();
  await expect(page.getByText(/Previous successful check retained/)).toBeVisible();
  // Act
  await page.getByRole('button', { name: 'Confirm recorded phase' }).click();
  const dialog = page.getByRole('dialog', { name: 'Confirm recorded RMF phase' });
  await dialog.getByLabel('Basis for this recorded phase').fill('Explicitly reviewed mission preparation state');
  await dialog.getByRole('button', { name: 'Confirm phase', exact: true }).click();
  // Assert
  await expect(page.getByText('Current RMF phase: Prepare', { exact: true })).toBeVisible();
  expect(state.writes.map(item => item.path)).toEqual([
    '/api/dashboard/systems/system-a/package-readiness/runs',
    '/api/dashboard/systems/system-a/package-readiness/rmf-phase',
  ]);
});
