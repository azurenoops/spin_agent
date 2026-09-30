import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { fixtureBoundary, fixtureCandidates, fixtureCapabilities, fixtureEntries, fixtureEvidence, fixtureFinding, fixturePublishedCapability,
  fixtureHosting, fixtureOffering, fixtureOverview, paged } from '../fixtures/provider-presentation-data';

const root = `/api/csp/offerings/${fixtureOffering.offeringId}`;
const responses = new Map<string, unknown>([
  ['/api/csp/offerings', paged([fixtureOffering])],
  [`${root}/overview`, fixtureOverview],
  [`${root}/boundary-overview`, fixtureCapabilities],
  [`${root}/boundary-revisions/${fixtureBoundary.boundaryRevisionId}`, fixtureBoundary],
  [`${root}/hosting-scope-revisions/${fixtureHosting.snapshot.revisionId}`, fixtureHosting],
  [`${root}/authorization-records`, paged([])],
  [`${root}/package-versions`, paged([])],
  [`${root}/findings`, paged([fixtureFinding])],
  [`${root}/findings/${fixtureFinding.findingId}/evidence`, paged([fixtureEvidence])],
  ['/api/csp/package-imports/package-1/candidates', fixtureCandidates],
  ['/api/csp/package-imports/package-1/entries', fixtureEntries],
  ['/api/csp/catalog/capabilities/capability-fixture', fixturePublishedCapability],
]);

for (const width of [1440, 390]) {
  test(`focused finding dialogs preserve the record view and keyboard navigation at ${width}px`, async ({ page, context }) => {
    // Arrange
    const unexpected: string[] = [];
    await page.setViewportSize({ width, height: 844 });
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
      if (url.origin !== 'http://localhost:4187') {
        unexpected.push(`External request ${url.href}`); await route.abort(); return;
      }
      if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) {
        if (route.request().method() !== 'GET' || !responses.has(url.pathname)) {
          unexpected.push(`${route.request().method()} ${url.pathname}`); await route.abort(); return;
        }
        await route.fulfill({ json: { status: 'success', data: responses.get(url.pathname) } }); return;
      }
      await route.continue();
    });
    await context.routeWebSocket('**/*', socket => socket.close());
    await page.goto('/e2e/fixtures/provider-presentation.html?screen=evidence');
    await expect(page.getByRole('table', { name: 'Service findings' })).toContainText(fixtureFinding.title);
    const trigger = page.getByRole('button', { name: 'Add finding', exact: true });
    await expect(page.getByLabel('Finding title')).toHaveCount(0);
    // Act
    await trigger.click();
    const dialog = page.getByRole('dialog', { name: 'Record a provider finding' });
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText(fixtureOffering.name);
    await dialog.getByLabel('Finding title').fill('Cancelled local draft');
    const cancel = dialog.getByRole('button', { name: 'Cancel', exact: true });
    const close = dialog.getByRole('button', { name: 'Close dialog' });
    await cancel.focus();
    await page.keyboard.press('Tab');
    // Assert
    await expect(close).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(cancel).toBeFocused();
    expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden');
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    // Act
    await page.keyboard.press('Escape');
    // Assert
    await expect(dialog).toHaveCount(0);
    await expect(trigger).toBeFocused();
    await expect(page.getByRole('table', { name: 'Service findings' })).toContainText(fixtureFinding.title);
    await trigger.click();
    await expect(dialog.getByLabel('Finding title')).toHaveValue('');
    await cancel.click();
    await expect(trigger).toBeFocused();
    expect(unexpected).toEqual([]);
  });
}

for (const screen of ['offering', 'offerings', 'sources', 'scope', 'capabilities', 'evidence']) {
  test(`${screen} matches the provider mock structure with retained-record fixtures`, async ({ page, context }) => {
    // Arrange
    const unexpected: string[] = [];
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
      if (url.origin !== 'http://localhost:4187') {
        unexpected.push(`External request ${url.href}`); await route.abort(); return;
      }
      if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) {
        if (route.request().method() !== 'GET' || !responses.has(url.pathname)) {
          unexpected.push(`${route.request().method()} ${url.pathname}`); await route.abort(); return;
        }
        await route.fulfill({ json: { status: 'success', data: responses.get(url.pathname) } }); return;
      }
      await route.continue();
    });
    await context.routeWebSocket('**/*', socket => socket.close());
    // Act
    await page.goto(`/e2e/fixtures/provider-presentation.html?screen=${screen}`);
    // Assert
    if (screen === 'offering') {
      await expect(page.getByRole('region', { name: 'Complete the next release' })).toBeVisible();
      await expect(page.getByLabel('Open findings')).toContainText('1');
      await expect(page.locator('[aria-label="Offering metrics"] > div')).toHaveCount(4);
      await expect(page.getByRole('region', { name: 'Package analysis' })).not.toBeVisible();
    } else if (screen === 'offerings') {
      await expect(page.getByRole('table', { name: 'Your offerings' })).toContainText('2 mission systems');
      await expect(page.getByRole('columnheader', { name: 'Customer use' })).toBeVisible();
    } else if (screen === 'sources') {
      await expect(page.getByRole('region', { name: 'Recorded authorization' })).toContainText('Synthetic existing ATO');
      await expect(page.getByRole('table', { name: 'Source packages and documents' })).toContainText('Uploaded authorization documents.zip');
    } else if (screen === 'scope') {
      await expect(page.getByRole('table', { name: 'Technical hosting scope' })).toBeVisible();
      await expect(page.getByRole('table', { name: 'Technical hosting scope' })).toContainText('Synthetic mission system');
      await page.getByText('Record details & provenance', { exact: true }).click();
      const hosting = page.getByRole('region', { name: 'Technical hosting scope' });
      await expect(hosting.getByRole('heading', { name: `${fixtureOffering.name} · Scope revision 2` })).toBeVisible();
      const provenance = hosting.getByText('Hosting provenance', { exact: true }).locator('..');
      await expect(provenance.getByText(fixtureHosting.name)).not.toBeVisible();
      await provenance.getByText('Hosting provenance', { exact: true }).click();
      await expect(provenance.getByText(fixtureHosting.name)).toBeVisible();
      await expect(provenance.getByText(fixtureHosting.snapshot.snapshotHash)).toBeVisible();
      await provenance.getByText('Hosting provenance', { exact: true }).click();
      await page.getByText('Record details & provenance', { exact: true }).click();
    } else if (screen === 'capabilities') {
      await expect(page.getByRole('table', { name: 'Service implementations' })).toContainText('AU-6');
      await expect(page.getByRole('dialog')).toHaveCount(0);
    } else {
      await expect(page.getByRole('table', { name: 'Evidence library' })).toContainText(fixtureEvidence.fileName);
      await expect(page.getByRole('table', { name: 'Service findings' })).toContainText(fixtureFinding.title);
    }
    await page.screenshot({ path: `test-results/provider-presentation/${screen}.png`, fullPage: true });
    if (screen === 'offering' || screen === 'capabilities') {
      await page.setViewportSize({ width: 390, height: 844 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: `test-results/provider-presentation/${screen}-mobile.png`, fullPage: true });
    }
    expect(unexpected).toEqual([]);
    const mock = (await readFile(`../../docs/design/provider-workspace-mock/screens/${screen}.png`)).toString('base64');
    const current = (await readFile(`test-results/provider-presentation/${screen}.png`)).toString('base64');
    const comparison = await context.newPage();
    await comparison.setViewportSize({ width: 1500, height: 1100 });
    await comparison.setContent(`<html><body style="margin:0;font:14px sans-serif;color:#202b40">
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:20px;padding:10px">
        <section><h2>Design mock: ${screen}</h2><p>Complete design screen · Synthetic illustrative data</p><img style="width:100%" src="data:image/png;base64,${mock}"></section>
        <section><h2>Current component: ${screen}</h2><p>Isolated API fixture · Actual React component, not deployed runtime</p><img style="width:100%" src="data:image/png;base64,${current}"></section>
      </div></body></html>`);
    await comparison.locator('img').evaluateAll(images => Promise.all(images.map(image => image.decode())));
    await comparison.screenshot({ path: `test-results/provider-presentation/${screen}-comparison.png`, fullPage: true });
    await comparison.close();
  });
}
