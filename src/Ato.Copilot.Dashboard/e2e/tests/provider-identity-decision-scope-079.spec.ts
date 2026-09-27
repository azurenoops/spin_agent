import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering } from '../../src/__tests__/provider-authorizations/testData';
import { recordedAuthorization } from '../../src/__tests__/provider-authorizations/overviewFixtures';

for (const width of [1440, 390]) {
  test(`explicit provider identity, decision and scope proposal at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    const scope = {
      offeringId: offering.offeringId, offeringRevision: offering.revision,
      snapshot: { revisionId: 'scope-1', revision: 2, snapshotHash: 'synthetic-prior-hash' },
      predecessorRevisionId: null, impactReviewId: null, name: 'Synthetic shared-service scope',
      purpose: 'Synthetic shared production resources', changeRationale: 'Prior reviewed rationale',
      permittedScopes: [{
        cloud: 'AzureUSGovernment', directoryTenantId: '11111111-1111-4111-8111-111111111111',
        subscriptionId: '22222222-2222-4222-8222-222222222222',
        resourceId: '/subscriptions/22222222-2222-4222-8222-222222222222',
      }], exclusions: [], citations: [],
    };
    await context.route('**/api/csp/offerings**', async route => {
      const path = new URL(route.request().url()).pathname;
      const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (path === `/api/csp/offerings/${offering.offeringId}`) return ok({ ...offering, currentHostingScopeRevisionId: 'scope-1' });
      if (path.endsWith('/authorization-records/decision-1')) return ok(recordedAuthorization);
      if (path.endsWith('/hosting-scope-revisions/scope-1')) return ok(scope);
      if (path.endsWith('/hosting-scope-revisions')) return ok({ items: [scope], page: 1, pageSize: 25, total: 1 });
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Outside fixture: ${path}` } } });
    });
    // Act
    await page.goto('/workspaces/csp/authorizations/create');
    // Assert
    await expect(page.getByRole('heading', { name: 'Create a service offering', exact: true })).toBeVisible();
    for (const label of ['Service model', 'Management arrangement'])
      await expect(page.getByRole('combobox', { name: label, exact: true })).toBeVisible();
    for (const label of ['Service owner', 'Security contact'])
      await expect(page.getByRole('textbox', { name: label, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`provider-create-${width}.png`), fullPage: true });
    // Act
    await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/decisions/decision-1`);
    // Assert
    await expect(page.getByRole('region', { name: 'Selected decision' })).toBeVisible();
    await expect(page.getByText(recordedAuthorization.snapshotHash, { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Revise draft', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`provider-decision-${width}.png`), fullPage: true });
    // Act
    await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/inherited-coverage/propose`);
    // Assert
    await expect(page.getByRole('heading', { name: 'Propose a scope update', exact: true })).toBeVisible();
    await expect(page.getByLabel('Hosting scope name', { exact: true })).toHaveValue(scope.name);
    await expect(page.getByLabel('Purpose', { exact: true })).toHaveValue(scope.purpose);
    await expect(page.getByLabel('Change rationale', { exact: true })).toHaveValue('');
    await expect(page.getByRole('button', { name: 'Save hosting scope revision', exact: true })).toBeDisabled();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`provider-scope-proposal-${width}.png`), fullPage: true });
    expect(errors).toEqual([]);
  });
}
