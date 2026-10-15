import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering } from '../../src/__tests__/provider-authorizations/testData';

const envelope = (data: unknown) => ({ status: 'success', data });
const path = `/workspaces/csp/authorizations/offerings/${offering.offeringId}/inherited-coverage?task=capabilities`;

test('provider explicitly proposes, reviews, and publishes an offering association', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  let association: Record<string, unknown> | null = null;
  const writes: { method: string; path: string; body: unknown }[] = [];
  await context.route('**/api/csp/package-imports/source-package/candidates**', route => route.fulfill({ json: envelope({
    items: [{
      candidateId: 'source-candidate', description: 'Source-backed account management service.', revision: 7,
      reviewState: 'Published', controlDuties: { 'AC-2': 'Shared' },
      citations: [{ artifactId: 'source-artifact', archivePath: 'source.json', locator: 'control:AC-2',
        quote: 'Provider operates the account-management service.' }],
    }],
    total: 1, page: 1, pageSize: 25,
  }) }));
  await context.route('**/api/csp/offerings/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: envelope({
      offeringId: offering.offeringId, offeringRevision: offering.revision,
      capabilities: {
        items: [{
          componentId: 'component-1', capabilityId: 'capability-1', candidateId: 'source-candidate',
          packageId: 'source-package', name: 'Account management service', publicationState: 'Published',
          reviewState: 'Reviewed', releaseId: 'release-1', releaseRevision: 3,
          boundaryRevisionId: offering.currentBoundaryRevisionId,
        }],
        total: 1, page: 1, pageSize: 1, published: 1, awaitingReview: 0,
      },
      missionSystems: { items: [], total: 0, page: 1, pageSize: 1 },
    }) });
    if (url.pathname.endsWith('/associations') && request.method() === 'GET')
      return route.fulfill({ json: envelope(association ? [association] : []) });
    if (url.pathname.endsWith('/associations') && request.method() === 'POST') {
      const body = request.postDataJSON();
      writes.push({ method: request.method(), path: url.pathname, body });
      association = {
        ...body, associationId: 'association-1', revisionId: 'association-revision-1',
        offeringId: offering.offeringId, capabilityReleaseRevision: 3, revision: 1, status: 'Draft',
        author: 'provider-admin', reviewer: null, createdAt: '2026-10-14T00:00:00Z',
        reviewedAt: null, reviewRationale: null, contentHash: 'association-hash',
      };
      return route.fulfill({ status: 201, json: envelope(association) });
    }
    if (url.pathname.endsWith('/review') && request.method() === 'POST') {
      const body = request.postDataJSON();
      writes.push({ method: request.method(), path: url.pathname, body });
      association = { ...association!, revision: 2, status: 'Reviewed', reviewer: 'provider-reviewer',
        reviewedAt: '2026-10-14T00:01:00Z', reviewRationale: body.rationale };
      return route.fulfill({ json: envelope(association) });
    }
    if (url.pathname.endsWith('/publish') && request.method() === 'POST') {
      writes.push({ method: request.method(), path: url.pathname, body: Object.fromEntries(url.searchParams) });
      association = { ...association!, revision: 3, status: 'Published' };
      return route.fulfill({ json: envelope(association) });
    }
    return route.fulfill({ json: envelope(offering) });
  });

  // Act
  await page.goto(path);
  await page.getByRole('button', { name: 'Account management service' }).click();
  const drawer = page.getByRole('dialog', { name: 'Account management service' });
  await drawer.getByLabel('Remaining customer obligations').fill('Mission approves tenant account assignments.');
  await drawer.getByRole('button', { name: 'Create association proposal' }).click();

  // Assert
  await expect(page.getByRole('table', { name: 'Offering association proposals' })).toContainText('Draft');
  expect(writes[0]).toMatchObject({
    method: 'POST',
    body: {
      componentId: 'component-1', capabilityId: 'capability-1', capabilityReleaseId: 'release-1',
      offeringRevision: offering.revision, providerCoverage: ['AC-2'],
      customerObligations: ['Mission approves tenant account assignments.'], contributionRole: 'Primary',
    },
  });
  expect((writes[0]!.body as { sourceProvenance: unknown[] }).sourceProvenance).toEqual([
    expect.objectContaining({ packageId: 'source-package', artifactId: 'source-artifact' }),
  ]);
  await page.getByRole('button', { name: 'Approve review' }).click();
  await expect(page.getByRole('table', { name: 'Offering association proposals' })).toContainText('Reviewed');
  await page.getByRole('button', { name: 'Publish association' }).click();
  await expect(page.getByRole('table', { name: 'Offering association proposals' })).toContainText('Published');
  expect(writes.map(write => write.path)).toEqual([
    `/api/csp/offerings/${offering.offeringId}/associations`,
    `/api/csp/offerings/${offering.offeringId}/associations/association-1/review`,
    `/api/csp/offerings/${offering.offeringId}/associations/association-1/publish`,
  ]);
  expect(writes[1]!.body).toMatchObject({ expectedRevision: 1, approve: true });
  expect(writes[2]!.body).toEqual({ expectedRevision: '2' });
  await expect(page.getByText(/Catalog publication alone does not/)).toBeVisible();
});
