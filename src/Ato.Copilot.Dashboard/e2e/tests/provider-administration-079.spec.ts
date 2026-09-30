import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { fixtureOffering, fixtureHosting, fixtureBoundary, fixtureCapabilities, paged } from '../fixtures/provider-presentation-data';

const root = `/api/csp/offerings/${fixtureOffering.offeringId}`;
for (const width of [1440, 390]) {
  test(`provider administration actions use actual identity and offering context at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic directory responses exercise the actual UI, never role grants.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await context.route('**/api/audit?*', route => route.fulfill({ json: { status: 'success', data: {
      items: [{ id: 'audit-a', timestamp: '2026-09-27T12:00:00Z', action: 'SourceReviewed', actorDisplayName: 'Provider reviewer',
        actorUserId: 'reviewer-a', tenantId: null, entityType: 'Source', entityId: 'source-a', detail: 'Retained audit fixture',
        impersonatedTenantName: null, ipAddress: null, surface: null, correlationId: null }],
      page: 1, pageSize: 50, totalCount: 1,
    } } }));
    const writes: string[] = [];
    const searches: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method() + ' ' + request.url());
    });
    await context.route('**/api/csp/**', route => {
      const url = new URL(route.request().url());
      const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (url.pathname === '/api/csp/directory/connections') return ok([
        { id: 'entra-a', name: 'Provider directory', directoryTenantId: 'directory-a', cloud: 'AzureUSGovernment', configured: true },
      ]);
      if (url.pathname === '/api/csp/directory/users') {
        searches.push(url.search);
        return ok({ users: [{ directoryTenantId: 'directory-a', objectId: 'person-a', displayName: 'Alex Reviewer',
          email: 'alex@example.invalid', userPrincipalName: 'alex@example.invalid' }], hasMore: false });
      }
      if (url.pathname === '/api/csp/onboarding/state') return ok({
        cspProfileId: 'provider-a', onboardingState: 'Active', currentStep: 'Complete',
        identity: { displayName: 'Synthetic Provider', legalEntityName: 'Fixture Provider LLC' },
        supportContact: { primarySupportEmail: 'support@example.invalid' },
        classification: { defaultClassificationFloor: 'Unclassified' },
      });
      if (url.pathname === '/api/csp/organizations') return ok(paged([]));
      if (url.pathname === '/api/csp/offerings') return ok(paged([fixtureOffering]));
      if (url.pathname === root) return ok(fixtureOffering);
      if (url.pathname === `${root}/hosting-scope-revisions/${fixtureHosting.snapshot.revisionId}`) return ok(fixtureHosting);
      if (url.pathname === `${root}/hosting-scope-revisions`) return ok(paged([fixtureHosting]));
      if (url.pathname === `${root}/boundary-revisions/${fixtureBoundary.boundaryRevisionId}`) return ok(fixtureBoundary);
      if (url.pathname === `${root}/boundary-revisions`) return ok(paged([fixtureBoundary]));
      if (url.pathname === `${root}/boundary-overview`) return ok(fixtureCapabilities);
      if (url.pathname === `${root}/authorization-records`) return ok(paged([]));
      return route.fallback();
    });
    // Act / Assert: provider role view is not organization role enrollment.
    await page.goto('/workspaces/csp/provider-administration');
    await expect(page.getByRole('heading', { name: 'Provider administration', exact: true })).toBeVisible();
    await expect(page.getByRole('table', { name: 'Provider team' })).toContainText('Synthetic Owner');
    await expect(page.getByRole('region', { name: 'Connections', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'View role', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Provider role details' })).toContainText('CSP.Admin');
    await expect(page.getByRole('dialog')).toContainText('does not grant or revoke roles');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button', { name: 'View role' })).toBeFocused();

    // Act: search and select a directory user without granting any access.
    await page.getByRole('button', { name: 'Find user in Entra', exact: true }).click();
    const directory = page.getByRole('dialog', { name: 'Find user in Microsoft Entra' });
    await directory.getByLabel('Find a person', { exact: true }).fill('Alex');
    await directory.getByRole('button', { name: 'Search Entra', exact: true }).click();
    await directory.getByRole('button', { name: 'Select Alex Reviewer (alex@example.invalid)' }).click();
    await expect(directory).toContainText('No membership or role has been granted');
    expect(searches).toHaveLength(1);
    expect(new URLSearchParams(searches[0]).get('connectionId')).toBe('entra-a');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('table', { name: 'Provider team' }).getByText('Alex Reviewer')).toHaveCount(0);

    // Act: inspect actual recorded Azure scope, then the actual provider profile.
    await page.getByRole('button', { name: 'Review scope configuration', exact: true }).click();
    const scope = page.getByRole('dialog', { name: 'Review Azure scope configuration' });
    await scope.getByLabel('Service offering', { exact: true }).selectOption(fixtureOffering.offeringId);
    await expect(scope).toContainText('synthetic-shared');
    await expect(scope).toContainText('not a credential test');
    await expect(scope.getByRole('link', { name: 'Open service scope' })).toHaveAttribute('href',
      `/workspaces/csp/authorizations/offerings/${fixtureOffering.offeringId}/inherited-coverage`);
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Review provider profile', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Provider profile' })).toContainText('Fixture Provider LLC');
    await page.keyboard.press('Escape');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`administration-${width}.png`), fullPage: true });
    await page.getByRole('link', { name: 'Review audit history →', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Platform Audit Log' })).toBeVisible();
    await expect(page.getByText('Retained audit fixture', { exact: true })).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
    await page.goto('/workspaces/csp/provider-administration');

    // Act: configure reference opens the selected offering's real existing editor.
    await page.getByRole('button', { name: 'Configure reference', exact: true }).click();
    const reference = page.getByRole('dialog', { name: 'Configure service reference' });
    await expect(reference.getByRole('link', { name: 'Open reference configuration' })).toHaveCount(0);
    await reference.getByLabel('Service offering', { exact: true }).selectOption(fixtureOffering.offeringId);
    await reference.getByRole('link', { name: 'Open reference configuration' }).click();
    await expect(page).toHaveURL(new RegExp(`/offerings/${fixtureOffering.offeringId}/inherited-coverage\\?task=references$`));
    await expect(page.getByRole('dialog', { name: 'Add or review upstream provider references' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save reference draft', exact: true })).toBeVisible();
    expect(writes).toEqual([]);
  });
}
