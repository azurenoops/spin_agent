import { expect, test } from '@playwright/test';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { fixtureBoundary, fixtureCandidates, fixtureCapabilities, fixtureEntries, fixtureEvidence, fixtureFinding,
  fixturePublishedCapability, fixtureHosting, fixtureOffering, fixtureOverview, paged } from '../fixtures/provider-presentation-data';

const apiRoot = `/api/csp/offerings/${fixtureOffering.offeringId}`;
const root = `/workspaces/csp/authorizations/offerings/${fixtureOffering.offeringId}`;
const sections = [
  ['', 'Overview', 'offering'], ['packages', 'Authorizations & sources', 'sources'],
  ['inherited-coverage', 'Services & scope', 'scope'],
  ['inherited-coverage?task=capabilities', 'Capabilities & responsibilities', 'capabilities'],
  ['findings', 'Evidence & findings', 'evidence'],
] as const;
const responses = new Map<string, unknown>([
  [apiRoot, fixtureOffering], ['/api/csp/offerings', paged([fixtureOffering])],
  [`${apiRoot}/overview`, fixtureOverview], [`${apiRoot}/boundary-overview`, fixtureCapabilities],
  [`${apiRoot}/boundary-revisions/${fixtureBoundary.boundaryRevisionId}`, fixtureBoundary],
  [`${apiRoot}/hosting-scope-revisions/${fixtureHosting.snapshot.revisionId}`, fixtureHosting],
  [`${apiRoot}/hosting-scope-revisions`, paged([fixtureHosting])],
  [`${apiRoot}/authorization-records`, paged([])], [`${apiRoot}/package-versions`, paged([])],
  [`${apiRoot}/findings`, paged([fixtureFinding])], [`${apiRoot}/findings/${fixtureFinding.findingId}`, fixtureFinding],
  [`${apiRoot}/findings/${fixtureFinding.findingId}/evidence`, paged([fixtureEvidence])],
  ['/api/csp/package-imports/package-1/candidates', fixtureCandidates],
  ['/api/csp/package-imports/package-1/entries', fixtureEntries],
  ['/api/csp/catalog/capabilities/capability-fixture', fixturePublishedCapability],
]);

for (const width of [1440, 390]) {
  test(`all five offering tabs follow the supplied mock at ${width}px`, async ({ browser, page, context, baseURL }, info) => {
    // Arrange: real route components, synthetic API records, exact checked-in design reference.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await context.route('**/api/csp/**', route => {
      const path = new URL(route.request().url()).pathname;
      return responses.has(path) ? route.fulfill({ json: { status: 'success', data: responses.get(path) } }) : route.fallback();
    });
    const mutations: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/csp/') && request.method() !== 'GET') mutations.push(request.method() + ' ' + request.url());
    });
    const referenceContext = await browser.newContext({ viewport: { width, height: 1000 } });
    try {
      const reference = await referenceContext.newPage();
      await reference.goto(pathToFileURL(resolve('../../docs/design/provider-workspace-mock/index.html')).href + '#offering');
      const expected = await reference.locator('#tabs').evaluate(nav => {
        const tab = nav.querySelector('button')!;
        return { gap: getComputedStyle(nav).columnGap, fontSize: getComputedStyle(tab).fontSize,
          padding: getComputedStyle(tab).padding, underline: getComputedStyle(tab).borderBottomWidth };
      });
      for (const [path, label, screen] of sections) {
        // Act
        await page.goto(root + (path ? '/' + path : ''));
        await expect(page.getByRole('heading', { level: 1 })).toHaveText(path ? label : fixtureOffering.name);
        const nav = page.getByRole('navigation', { name: 'Offering sections', exact: true });
        await page.waitForLoadState('networkidle');
        // Assert
        await expect(nav.getByRole('link')).toHaveText(sections.map(([, name]) => name));
        await expect(nav.locator('[aria-current="page"]')).toHaveText(label);
        await expect(nav.locator('[aria-current="page"]')).toBeInViewport();
        const actual = await nav.evaluate(element => {
          const tab = element.querySelector('[aria-current="page"]')!;
          return { gap: getComputedStyle(element).columnGap, fontSize: getComputedStyle(tab).fontSize,
            padding: getComputedStyle(tab).padding, underline: getComputedStyle(tab).borderBottomWidth };
        });
        expect(actual).toEqual(expected);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        if (width === 390) expect(await nav.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
        if (screen === 'offering') await expect(page.getByRole('region', { name: 'Complete the next release' })).toBeVisible();
        if (screen === 'sources') {
          const table = page.getByRole('table', { name: 'Source packages and documents' });
          await expect(table).toContainText('responsibility-matrix.json');
          if (width === 390) expect(await table.evaluate(element => element.getBoundingClientRect().width)).toBeGreaterThanOrEqual(680);
        }
        if (screen === 'scope') await expect(page.getByRole('table', { name: 'Technical hosting scope' })).toBeVisible();
        if (screen === 'capabilities') await expect(page.getByRole('table', { name: 'Service implementations' })).toBeVisible();
        if (screen === 'evidence') {
          await expect(page.getByRole('table', { name: 'Evidence library' })).toBeVisible();
          await expect(page.getByRole('table', { name: 'Service findings' })).toBeVisible();
        }
        await page.screenshot({ path: info.outputPath(`${screen}-${width}.png`), fullPage: true });
      }
      expect(mutations).toEqual([]);
    } finally {
      await referenceContext.close();
    }
  });
}
