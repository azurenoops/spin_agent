import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering } from '../../src/__tests__/provider-authorizations/testData';
import { offeringOverview } from '../../src/__tests__/provider-authorizations/overviewFixtures';

const root = `/workspaces/csp/authorizations/offerings/${offering.offeringId}`;
for (const width of [1440, 390]) {
  for (const state of ['published', 'mixed', 'empty', 'failed'] as const) {
    test(`release mock layout retains real ${state} context at ${width}`, async ({ page, context, baseURL }, info) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
      const identity = { ...offering, name: 'Independent service with a deliberately long offering name'.repeat(3),
        revision: 27, currentBoundaryRevisionId: state === 'empty' ? null : offering.currentBoundaryRevisionId,
        currentHostingScopeRevisionId: state === 'empty' ? null : offering.currentHostingScopeRevisionId };
      const overview = { ...offeringOverview(), offeringRevision: identity.revision + (state === 'mixed' ? 1 : 0),
        capabilities: { published: state === 'empty' ? 0 : 4, proposed: state === 'mixed' ? 9 : 0,
          awaitingReview: state === 'mixed' ? 6 : 0, awaitingApproval: state === 'mixed' ? 3 : 0, archived: 0,
          publishedReleaseRevisions: state === 'mixed' ? [7, 2] : state === 'empty' ? [] : [7] } };
      let failed = state === 'failed';
      const writes: string[] = [];
      const errors: string[] = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('request', request => {
        if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method());
      });
      await context.route('**/api/csp/offerings/**', route => {
        if (route.request().url().includes('/overview')) return failed
          ? route.fulfill({ status: 503, json: { status: 'error', error: { message: 'Release context unavailable', errorCode: 'UNAVAILABLE' } } })
          : route.fulfill({ json: { status: 'success', data: overview } });
        return route.fulfill({ json: { status: 'success', data: identity } });
      });
      // Act
      await page.goto(`${root}/release`);
      if (failed) {
        await expect(page.getByRole('alert')).toContainText('Release context unavailable');
        await expect(page.getByRole('heading', { name: 'Customer-visible capability snapshots' })).toHaveCount(0);
        failed = false;
        await page.getByRole('button', { name: 'Retry', exact: true }).click();
      }
      const release = page.getByRole('region', { name: 'Published release & working changes', exact: true });
      await expect(release).toBeVisible();
      // Assert
      await expect(release.getByRole('heading', { name: 'Working offering identity', exact: true })).toBeVisible();
      await expect(release).toContainText('27');
      await expect(release).toContainText(state === 'mixed' ? '9 capability records' : '0 capability records');
      await expect(release).toContainText(state === 'empty' ? 'No published capabilities'
        : state === 'mixed' ? 'Published revisions 2, 7' : 'Published revision 7');
      if (state === 'mixed') await expect(page.getByRole('alert')).toContainText('Offering context changed');
      const links = page.getByRole('navigation', { name: 'Provider release workflow orientation' }).getByRole('link');
      await expect(links).toHaveCount(4);
      for (const link of await links.all()) expect(await link.getAttribute('href')).toContain(root);
      const advanced = page.getByText('Retained source & scope comparisons', { exact: true });
      expect(await advanced.locator('..').getAttribute('open')).toBeNull();
      await advanced.focus();
      await page.keyboard.press('Enter');
      if (state === 'empty') {
        await expect(page.getByText('Boundary comparison unavailable: no boundary revision recorded.')).toBeVisible();
        await expect(page.getByRole('link', { name: 'Record hosting prerequisites' })).toHaveAttribute('href', `${root}/inherited-coverage?task=hosting`);
      } else await expect(page.getByRole('link', { name: 'Review boundary change impact' })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Compare identity edits', exact: true })).toHaveAttribute('href', `${root}?action=identity`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      const cards = page.locator('.offering-release-card');
      const first = (await cards.nth(0).boundingBox())!;
      const second = (await cards.nth(1).boundingBox())!;
      const firstFact = (await cards.first().locator('.provider-fact').nth(0).boundingBox())!;
      const secondFact = (await cards.first().locator('.provider-fact').nth(1).boundingBox())!;
      if (width === 1440) {
        expect(Math.abs(first.y - second.y)).toBeLessThan(2);
        expect(second.x - first.x - first.width).toBeCloseTo(20, 0);
        expect(Math.abs(firstFact.y - secondFact.y)).toBeLessThan(2);
      } else {
        expect(second.y).toBeGreaterThan(first.y + first.height);
        expect(Math.abs(second.x - first.x)).toBeLessThan(2);
        expect(secondFact.y).toBeGreaterThan(firstFact.y + firstFact.height);
      }
      if (state === 'published') {
        const mock = await context.newPage();
        await mock.goto(pathToFileURL(resolve('../../docs/design/workspace-ui-mocks/provider-offering-workspace-focused.html')).href);
        await mock.getByRole('tab', { name: 'Release & changes', exact: true }).click();
        const styles = (element: Element) => {
          const value = getComputedStyle(element);
          return { padding: value.paddingTop, radius: value.borderRadius };
        };
        expect(await cards.first().evaluate(styles)).toEqual(await mock.locator('#release .columns>.panel').first().evaluate(styles));
        await mock.locator('#release').screenshot({ path: info.outputPath(`release-mock-${width}.png`) });
        await mock.close();
        await page.addScriptTag({ content: axe.source });
        const accessibility = await page.evaluate(async () => (window as Window & { axe: typeof axe }).axe.run('.offering-release-workspace',
          { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
        expect(accessibility.violations).toEqual([]);
        await page.locator('.offering-release-workspace').screenshot({ path: info.outputPath(`release-production-${width}.png`) });
      }
      expect(writes).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
}
