import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { fixtureOffering, fixtureOverview, fixtureCapabilities, fixtureFinding, fixtureEvidence, paged } from '../fixtures/provider-presentation-data';
import { acceptedImpact, impactDetails, pendingImpact } from '../../src/__tests__/provider-authorizations/impactFixtures';

const root = `/api/csp/offerings/${fixtureOffering.offeringId}`;
for (const width of [1440, 390]) {
  test(`changes queue opens exact review, evidence and source records at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: current provider route components with deterministic retained records.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const writes: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/csp/') && request.method() !== 'GET') writes.push(request.url());
    });
    await context.route('**/api/csp/**', async route => {
      const path = new URL(route.request().url()).pathname;
      const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (path === '/api/csp/offerings') return ok(paged([fixtureOffering]));
      if (path === root) return ok(fixtureOffering);
      if (path === `${root}/overview`) return ok({ ...fixtureOverview, customerActionCount: 3 });
      if (path === `${root}/boundary-overview`) return ok(fixtureCapabilities);
      if (path === `${root}/impact-reviews`) return ok(paged([
        { ...pendingImpact, stale: true }, { ...acceptedImpact, reviewId: 'history-a', title: 'Accepted historical review', stale: true },
      ]));
      if (path === `${root}/impact-reviews/${pendingImpact.reviewId}/details`) return ok({ ...impactDetails, review: { ...pendingImpact, stale: true } });
      if (path === `${root}/monitoring`) return ok({
        offeringId: fixtureOffering.offeringId, offeringName: fixtureOffering.name, rules: [], evaluations: [],
        sources: [{ sourceId: 'source-a', name: 'Delayed source record', signal: 'AuthorizationExpiry', collectionHealth: 'Unreviewed',
          sourceRevision: 'source-revision-1', field: 'RemainingDays', value: null, sourceTimestamp: null, snapshotJson: '{}' }],
      });
      if (path === `${root}/findings`) return ok(paged([fixtureFinding]));
      if (path === `${root}/findings/${fixtureFinding.findingId}`) return ok(fixtureFinding);
      if (path === `${root}/findings/${fixtureFinding.findingId}/evidence`) return ok(paged([fixtureEvidence]));
      if (path === `${root}/findings/${fixtureFinding.findingId}/evidence/${fixtureEvidence.evidenceId}`) return ok(fixtureEvidence);
      if (path.includes('/poam') || path.includes('/shares')) return ok(paged([]));
      return route.fallback();
    });

    // Act / Assert: accepted stale history is not counted as a new pending review.
    await page.goto(`/workspaces/csp/provider-changes?offeringId=${fixtureOffering.offeringId}`);
    await expect(page.getByRole('heading', { name: 'Changes requiring attention' })).toBeVisible();
    const queue = page.getByRole('table', { name: 'Change queue' });
    await expect(queue).toContainText(pendingImpact.title!);
    await expect(queue.getByText('Accepted historical review')).toHaveCount(0);
    await expect(page.getByLabel('Provider reviews', { exact: true })).toHaveText('1');
    await expect(page.getByRole('link', { name: `View ${fixtureEvidence.fileName}`, exact: true })).toHaveAttribute('href',
      `/workspaces/csp/authorizations/offerings/${fixtureOffering.offeringId}/evidence/${fixtureEvidence.evidenceId}?findingId=${fixtureFinding.findingId}&returnTo=changes`);
    await page.getByRole('button', { name: 'Inspect Delayed source record' }).click();
    await expect(page.getByRole('dialog', { name: 'Inspect monitoring source' })).toContainText('source-revision-1');
    await expect(page.getByRole('dialog')).toContainText('not live connector health');
    await page.keyboard.press('Escape');
    await page.getByLabel('Queue view').selectOption('history');
    await expect(queue).toContainText('Accepted historical review');
    await expect(queue).toContainText('Impact accepted for publication');
    await page.getByLabel('Queue view').selectOption('attention');
    await page.screenshot({ path: info.outputPath(`changes-${width}.png`), fullPage: true });

    // Act: review the exact selected change, not a generic new assessment.
    await page.getByRole('link', { name: `Review ${pendingImpact.title}`, exact: true }).click();
    await expect(page.getByRole('region', { name: 'What changes', exact: true })).toContainText('Review the saved responsibility changes.');
    await expect(page.getByRole('table', { name: 'Affected mission systems' })).toContainText('Mission Alpha');
    await expect(page.getByRole('navigation', { name: 'Offering sections' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Back to change queue', exact: true })).toHaveAttribute('href',
      `/workspaces/csp/provider-changes?offeringId=${fixtureOffering.offeringId}`);
    if (width === 390) await page.getByText('Provider navigation', { exact: true }).click();
    await expect(page.getByRole('navigation', { name: width === 390 ? 'Provider mobile navigation' : 'Provider workspace', exact: true })
      .getByRole('link', { name: 'Changes', exact: true })).toHaveAttribute('aria-current', 'page');
    if (width === 390) await page.getByText('Provider navigation', { exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`selected-impact-${width}.png`), fullPage: true });
    await page.getByRole('link', { name: 'Back to change queue', exact: true }).click();
    await page.getByRole('link', { name: `View ${fixtureEvidence.fileName}`, exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/evidence/${fixtureEvidence.evidenceId}\\?findingId=${fixtureFinding.findingId}&returnTo=changes$`));
    await expect(page.getByRole('heading', { name: 'Evidence & findings', exact: true })).toBeVisible();
    await expect(page.locator('main')).toContainText(fixtureEvidence.fileName);
    await page.getByRole('link', { name: 'Back to change queue', exact: true }).click();
    await page.getByRole('link', { name: 'Service monitoring', exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`offeringId=${fixtureOffering.offeringId}&tab=monitoring$`));
    await expect(page.getByRole('heading', { name: 'Service monitoring', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Change queue', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Changes requiring attention' })).toBeVisible();
    expect(writes).toEqual([]);
  });
}
