import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { fixtureOffering, fixtureHosting, fixtureCapabilities, fixtureBoundary, paged } from '../fixtures/provider-presentation-data';

const root = `/api/csp/offerings/${fixtureOffering.offeringId}`;
const uiRoot = `/workspaces/csp/authorizations/offerings/${fixtureOffering.offeringId}`;
const customer = { systemId: 'mission-a', name: 'Harbor Logistics', tenantId: 'tenant-a', orgDisplayName: 'Maritime Operations' };
const relationship = {
  assignmentId: 'assignment-a', systemId: customer.systemId, systemName: customer.name,
  targetTenantId: customer.tenantId, targetTenantName: customer.orgDisplayName,
  relationshipState: 'SeparateBoundaryConsumer', associated: true, adoptedCapabilityCount: 1,
  assignedScopes: fixtureHosting.permittedScopes,
  adoptedReleases: [{ capabilityId: 'capability-a', capabilityName: 'Azure protection', releaseId: 'release-2',
    revision: 2, currentReleaseRevision: 3, updateAvailable: true }],
};
for (const width of [1440, 390]) {
  test(`provider mission actions preserve selected customer and match their labels at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: real route components with explicit synthetic server contracts.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    page.on('pageerror', error => console.error('Mission page error:', error.message));
    const writes: { path: string; body: unknown; key?: string }[] = [];
    const capturedRequests: string[] = [];
    page.on('request', request => capturedRequests.push(request.url()));
    await context.route('**/api/csp/**', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      const respond = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (path === '/api/csp/offerings') return respond(paged([fixtureOffering]));
      if (path === '/api/csp/dashboard/systems') return respond({ items: [customer], page: 1, pageSize: 100, totalCount: 1 });
      if (path === root) return respond(fixtureOffering);
      if (path === `${root}/boundary-overview`) return respond({ ...fixtureCapabilities,
        capabilities: { ...paged([]), published: 0, awaitingReview: 0 }, missionSystems: paged([relationship]) });
      if (path === `${root}/hosting-scope-revisions/${fixtureHosting.snapshot.revisionId}`) return respond(fixtureHosting);
      if (path === `${root}/hosting-scope-revisions`) return respond(paged([fixtureHosting]));
      if (path === `${root}/boundary-revisions/${fixtureBoundary.boundaryRevisionId}`) return respond(fixtureBoundary);
      if (path === `${root}/authorization-records`) return respond(paged([]));
      if (path === `${root}/hosting-assignments/assignment-a`) return respond({
        ...relationship, revision: 1, offeringId: fixtureOffering.offeringId, hostingScope: fixtureHosting.snapshot,
      });
      if (path === `${root}/hosting-assignments` && request.method() === 'POST') {
        const body = request.postDataJSON();
        writes.push({ path, body, key: request.headers()['idempotency-key'] });
        return respond({ assignmentId: 'new-assignment', revision: 1, offeringId: fixtureOffering.offeringId,
          systemId: customer.systemId, hostingScope: fixtureHosting.snapshot, assignedScopes: body.assignedScopes,
          relationshipState: 'Undetermined' });
      }
      return route.fallback();
    });

    // Act: assignment is a named, confirmed task rather than a raw-ID inventory screen.
    await page.goto('/workspaces/csp/systems');
    await expect(page.getByRole('table', { name: 'Customer service relationships' })).toContainText(customer.name);
    await page.getByRole('button', { name: 'Assign service scope', exact: true }).click();
    const assign = page.getByRole('dialog', { name: 'Assign service scope', exact: true });
    await expect(assign).toBeVisible();
    await assign.getByLabel('Customer organization', { exact: true }).selectOption(customer.tenantId);
    await assign.getByLabel('Mission system', { exact: true }).selectOption(customer.systemId);
    await assign.getByRole('checkbox', { name: 'synthetic-shared', exact: true }).check();
    await expect(assign.getByRole('button', { name: 'Save service assignment' })).toBeDisabled();
    await assign.getByLabel('I confirm this allocation grants no permissions or authorization coverage.').check();
    await page.screenshot({ path: info.outputPath(`assignment-${width}.png`), fullPage: true });
    await assign.getByRole('button', { name: 'Save service assignment' }).click();

    // Assert
    await expect(assign).toHaveCount(0);
    expect(writes).toEqual([{ path: `${root}/hosting-assignments`, body: {
      targetTenantId: customer.tenantId, systemId: customer.systemId,
      hostingScopeRevisionId: fixtureHosting.snapshot.revisionId, assignedScopes: fixtureHosting.permittedScopes, references: [],
    }, key: expect.any(String) }]);
    await page.getByRole('searchbox', { name: 'Search mission system or organization' }).fill('Maritime');
    await expect(page.getByRole('table', { name: 'Customer service relationships' })).toContainText('Azure protection · Revision 2');
    await page.getByRole('link', { name: 'View relationship for Harbor Logistics' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Harbor Logistics' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Offering sections' })).toHaveCount(0);
    const providerNav = width === 1440 ? 'Provider workspace' : 'Provider mobile navigation';
    if (width === 390) await page.getByText('Provider navigation', { exact: true }).click();
    await expect(page.getByRole('navigation', { name: providerNav, exact: true }).getByRole('link', { name: 'Mission systems', exact: true })).toHaveAttribute('aria-current', 'page');
    if (width === 390) await page.getByText('Provider navigation', { exact: true }).click();
    await expect(page.getByRole('link', { name: 'Review provider impact for Azure protection' })).toHaveAttribute('href', `${uiRoot}/impact?capabilityId=capability-a`);
    await page.getByRole('button', { name: 'Preview Apply published capabilities' }).click();
    const preview = page.getByRole('dialog', { name: 'Preview Mission Owner handoff' });
    await expect(preview).toContainText('Azure protection · Revision 2');
    await expect(preview).toContainText('read-only');
    await preview.getByRole('button', { name: 'Package contribution', exact: true }).click();
    await expect(preview).toContainText('this preview generates no package');
    await page.keyboard.press('Escape');
    await expect(preview).toHaveCount(0);
    await page.screenshot({ path: info.outputPath(`relationship-${width}.png`), fullPage: true });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole('link', { name: 'Back to mission systems' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Mission systems' })).toBeVisible();
    expect(capturedRequests.some(url => url.includes('/impersonate') || url.includes('/api/dashboard/systems/'))).toBe(false);
    expect(writes).toHaveLength(1);

    // Act: direct offering task navigation can close and reopen without stale URL state.
    await page.goto(`${uiRoot}/inherited-coverage?task=allocations`);
    await expect(page.getByRole('dialog', { name: 'Provider hosting allocation' })).toBeVisible();
    await expect(page.getByLabel('Customer organization', { exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page).toHaveURL(`${baseURL}${uiRoot}/inherited-coverage`);
    await page.goto(`${uiRoot}/inherited-coverage?task=allocations`);
    await expect(page.getByRole('dialog', { name: 'Provider hosting allocation' })).toBeVisible();
    expect(writes).toHaveLength(1);
  });
}
