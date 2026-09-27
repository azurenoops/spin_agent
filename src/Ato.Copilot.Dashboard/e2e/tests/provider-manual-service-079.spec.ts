import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { HostingScopeInput, HostingScopeRevision } from '../../src/features/provider-authorizations/hostingTypes';

for (const width of [1440, 390]) {
  test(`manual SaaS scope preserves explicit identity without Azure placeholders at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange: all records are synthetic; the real SPA and request adapters are exercised.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    let offering = {
      offeringId: 'service-offering', providerId: 'same-provider', name: 'Synthetic M365 collaboration', description: '',
      revision: 4, lifecycle: 'Draft', environments: ['Microsoft365DoD'], serviceModel: 'SoftwareAsAService',
      managementArrangement: 'SharedOperations', currentBoundaryRevisionId: null, currentHostingScopeRevisionId: 'service-scope-1',
    };
    let scope: HostingScopeRevision = {
      offeringId: offering.offeringId, offeringRevision: 4, name: 'Synthetic service relationship',
      snapshot: { revisionId: 'service-scope-1', revision: 1, snapshotHash: 'synthetic-hash' },
      predecessorRevisionId: null, impactReviewId: null, purpose: 'Synthetic collaboration use',
      permittedScopes: [{ kind: 'Service', serviceId: 'synthetic-m365', serviceName: 'Synthetic collaboration',
        environment: 'Microsoft365DoD', tenantReference: 'Synthetic service tenant' }],
      exclusions: [], citations: [],
    };
    const writes: HostingScopeInput[] = [];
    await context.route('**/api/csp/offerings**', async route => {
      const path = new URL(route.request().url()).pathname;
      const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (path === '/api/csp/offerings/service-offering') return ok(offering);
      if (path.endsWith('/hosting-scope-revisions') && route.request().method() === 'POST') {
        const body = route.request().postDataJSON() as HostingScopeInput;
        writes.push(body);
        scope = { ...scope, ...body, offeringRevision: 5, impactReviewId: 'service-impact',
          snapshot: { revisionId: 'service-scope-2', revision: 2, snapshotHash: 'next-synthetic-hash' } };
        offering = { ...offering, revision: 5, currentHostingScopeRevisionId: 'service-scope-2' };
        return ok(scope);
      }
      if (path.endsWith('/hosting-scope-revisions')) return ok({ items: [scope], page: 1, pageSize: 25, total: 1 });
      if (path.endsWith(`/hosting-scope-revisions/${scope.snapshot.revisionId}`)) return ok(scope);
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: 'Outside the synthetic service fixture' } } });
    });

    // Act
    await page.goto('/workspaces/csp/authorizations/offerings/service-offering/inherited-coverage/propose');
    await expect(page.getByRole('heading', { name: 'Propose a scope update', exact: true })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Service identifier 1', exact: true })).toHaveValue('synthetic-m365');
    await expect(page.getByRole('textbox', { name: 'subscriptionId 1', exact: true })).toHaveCount(0);
    await expect(page.getByRole('textbox', { name: 'directoryTenantId 1', exact: true })).toHaveCount(0);
    await page.getByLabel('Change rationale', { exact: true }).fill('Explicitly reviewed synthetic service relationship');
    await page.getByLabel('I confirm this exact technical scope revision, not authorization coverage.').check();
    await page.getByRole('button', { name: 'Save hosting scope revision', exact: true }).click();

    // Assert
    await expect(page.getByText(/Saved hosting scope revision 2:/)).toBeVisible();
    expect(writes).toHaveLength(1);
    expect(writes[0]?.permittedScopes[0]).toMatchObject({
      kind: 'Service', serviceId: 'synthetic-m365', environment: 'Microsoft365DoD', tenantReference: 'Synthetic service tenant',
    });
    expect(JSON.stringify(writes[0]?.permittedScopes)).not.toMatch(/subscriptionId|directoryTenantId|resourceId/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`manual-service-${width}.png`), fullPage: true });
    expect(errors).toEqual([]);
  });
}
