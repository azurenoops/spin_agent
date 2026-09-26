import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { candidate, entry, packageStatus, page as responsePage, preview } from '../../src/__tests__/package-imports/fixtures';
import type { PackageCandidate, PackagePreview, PackagePublication } from '../../src/features/package-imports/types';
import type { BrowserContext } from '@playwright/test';

const componentId = 'bbbbbbbb-1000-4000-8000-000000000002';
const capabilityId = 'bbbbbbbb-1000-4000-8000-000000000004';

async function installPackageReview(context: BrowserContext, baseURL: string, losePublicationResponse = false) {
  await installWorkspaceFixture(context, baseURL, { providerOnly: true });
  let status = packageStatus();
  await context.route('**/api/csp/offerings/**', route => route.fulfill({ json: { status: 'success', data: new URL(route.request().url()).pathname.endsWith('/offering-1') ? { offeringId: 'offering-1', name: 'Azure IL5', environments: ['AzureUSGovernment'], revision: 1, lifecycle: 'Draft' } : responsePage([]) } }));
  let records: PackageCandidate[] = [
    candidate({ candidateId: componentId, name: 'Synthetic audit service', controlDuties: {} }),
    candidate({ candidateId: capabilityId, type: 'Capability', name: 'Synthetic event logging', contributorIds: [componentId] }),
  ];
  let decision: PackagePreview | null = null;
  let outcome: PackagePublication | null = null;
  let approvals = 0;
  let publications = 0;
  const publishKeys: string[] = [];
  await context.route('**/api/csp/package-imports**', route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const success = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
    if (request.method() === 'GET') {
      if (path.endsWith('/package-imports')) return success(responsePage([status]));
      if (path.endsWith('/package-1')) return success({ ...status, association: { offeringId: 'offering-1', packageVersionId: 'version-1', boundaryRevisionId: 'boundary-1' } });
      if (path.endsWith('/review-state')) return success({
        packageId: status.packageId, revision: status.revision, preview: decision,
        previewIsStale: decision !== null && decision.state !== 'Published' && decision.revision !== status.revision,
        publication: outcome,
      });
      if (path.endsWith('/entries')) return success(responsePage([entry({ candidateCount: 2 })]));
      if (path.endsWith('/candidates')) {
        const type = url.searchParams.get('type');
        const reviewState = url.searchParams.get('reviewState');
        return success(responsePage(records.filter(record => (!type || record.type === type) && (!reviewState || record.reviewState === reviewState))));
      }
    }
    if (request.method() === 'PATCH' && path.includes('/candidates/')) {
      const body = request.postDataJSON();
      const id = path.split('/').at(-1);
      records = records.map(record => record.candidateId === id
        ? { ...record, ...body, reviewState: body.reviewAction, revision: record.revision + 1 } : record);
      status = { ...status, revision: status.revision + 1 };
      decision = null;
      return success(records.find(record => record.candidateId === id));
    }
    if (path.endsWith('/approval-previews')) {
      const body = request.postDataJSON();
      decision = preview({ revision: status.revision, candidates: body.candidates, newComponents: 1, newCapabilities: 1 });
      return success(decision);
    }
    if (path.endsWith('/approve')) {
      approvals++;
      expect(publications).toBe(0);
      decision = { ...decision!, state: 'Approved' };
      return success(decision);
    }
    if (path.endsWith('/publish')) {
      publishKeys.push(request.headers()['idempotency-key']);
      if (outcome) return success({ ...outcome, existing: true });
      expect(decision?.state).toBe('Approved');
      publications++;
      outcome = { packageId: status.packageId, publicationState: 'Published', existing: publications > 1,
        records: records.map(record => ({ candidateId: record.candidateId, recordId: record.candidateId, type: record.type,
          releaseId: record.type === 'Capability' ? 'bbbbbbbb-1000-4000-8000-000000000099' : null })) };
      status = { ...status, publicationState: 'Published' };
      records = records.map(record => ({ ...record, publishedRecordId: record.candidateId, reviewState: 'Published' }));
      decision = { ...decision!, state: 'Published' };
      if (losePublicationResponse) return route.abort('connectionfailed');
      return success(outcome);
    }
    return route.fulfill({ status: 404, json: { status: 'error', error: { errorCode: 'FIXTURE_ROUTE', message: `Unconfigured synthetic route ${path}` } } });
  });
  return { counts: () => ({ approvals, publications, publishKeys }), outcome: () => outcome };
}

for (const width of [1440, 390]) {
  test(`onboarding recovers upload receipt and continues without record review at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    let received = false;
    let completed = false;
    const uploadKeys: string[] = [];
    const packageMethods: string[] = [];
    const receipt = packageStatus({ processingState: 'Received', revision: 1,
      coverage: { total: 1, pending: 1, processed: 0, unsupported: 0, unreadable: 0, failed: 0, excluded: 0 } });
    await context.route('**/api/csp/onboarding/state', route => route.fulfill({ json: { status: 'success', data: {
      cspProfileId: 'synthetic-provider', onboardingState: completed ? 'Active' : 'InWizard', currentStep: 'Review',
      identity: { legalEntityName: 'Synthetic provider', displayName: 'Synthetic provider' },
      supportContact: { primarySupportEmail: 'support@example.invalid' }, classification: { defaultClassificationFloor: 'Unclassified' },
    } } }));
    await context.route('**/api/csp/onboarding/atos/upload', route => {
      const headers = route.request().headers();
      uploadKeys.push(headers['idempotency-key']);
      expect(headers.prefer).toBe('respond-async');
      expect(headers['x-workspace-kind']).toBe('csp');
      expect(headers['x-workspace-mode']).toBe('ordinary');
      expect(headers['x-workspace-tenant-id']).toBeUndefined();
      received = true;
      if (uploadKeys.length === 1) return route.abort('connectionfailed');
      return route.fulfill({ status: 202, json: { status: 'success', data: receipt } });
    });
    await context.route('**/api/csp/package-imports**', route => {
      packageMethods.push(route.request().method());
      expect(route.request().headers()['x-workspace-kind']).toBe('csp');
      return route.fulfill({ json: { status: 'success', data: responsePage(received ? [receipt] : []) } });
    });
    await context.route('**/api/csp/onboarding/submit', route => {
      completed = true;
      return route.fulfill({ json: { status: 'success', data: {
        cspProfileId: 'synthetic-provider', onboardingState: 'Active', onboardingCompletedAt: '2026-09-23T12:00:00Z',
      } } });
    });
    await page.goto('/onboarding/csp');
    await page.getByRole('button', { name: 'Back', exact: true }).click();

    // Act
    await page.getByLabel('Select source files').setInputFiles({
      name: 'synthetic-package.json', mimeType: 'application/json', buffer: Buffer.from('{"fixture":"synthetic"}'),
    });
    await expect(page.getByRole('button', { name: 'Continue', exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Upload package', exact: true }).click();
    await page.getByRole('button', { name: 'Retry same upload', exact: true }).click();

    // Assert
    await expect(page.getByText('Receipt confirmed · Revision 1')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Continue', exact: true })).toBeEnabled();
    expect(uploadKeys).toHaveLength(2);
    expect(uploadKeys[0]).toBeTruthy();
    expect(uploadKeys[1]).toBe(uploadKeys[0]);
    await expect(page.getByRole('region', { name: 'Candidate records' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Mark reviewed' })).toHaveCount(0);
    expect(packageMethods.every(method => method === 'GET')).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`onboarding-receipt-${width}.png`), fullPage: true });
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByRole('button', { name: 'Submit & finalize onboarding' }).click();
    await expect.poll(() => completed).toBe(true);
  });

  test(`source review precedes exact approval and publication at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const state = await installPackageReview(context, baseURL!, true);
    await page.goto('/workspaces/csp/security-capabilities/imports/package-1');
    await expect(page.getByRole('heading', { name: 'Review extracted records', exact: true })).toBeVisible();
    await page.screenshot({ path: `../../docs/design/package-review-${width}.png`, fullPage: true });
    await page.getByText('Publication controls', { exact: true }).click();
    await expect(page.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
    await page.screenshot({ path: info.outputPath(`review-initial-${width}.png`), fullPage: true });

    // Act
    for (const name of ['Synthetic audit service', 'Synthetic event logging']) {
      await page.getByRole('button', { name: `Review ${name}`, exact: true }).click();
      await expect(page.getByRole('region', { name: 'Source citations' })).toBeVisible();
      await expect(page.getByText('Confidence 99% is advisory, never approval.')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Mark reviewed' })).toBeDisabled();
      await page.getByRole('checkbox', { name: /reviewed the source/i }).check();
      await page.getByRole('button', { name: 'Mark reviewed' }).click();
      await expect(page.getByRole('heading', { name: 'Review candidate' })).toHaveCount(0);
    }
    await page.getByRole('checkbox', { name: /Select Synthetic audit service revision/ }).check();
    await page.getByRole('checkbox', { name: /Select Synthetic event logging revision/ }).check();
    await page.getByRole('button', { name: 'Preview selected revisions' }).click();
    await expect(page.getByRole('heading', { name: 'Approval preview' })).toBeVisible();
    expect(state.counts().publications).toBe(0);
    await page.getByRole('button', { name: 'Approve exact preview' }).click();
    await expect(page.getByRole('button', { name: 'Publish approved set' })).toBeEnabled();
    expect(state.counts()).toMatchObject({ approvals: 1, publications: 0 });
    await page.screenshot({ path: info.outputPath(`approved-${width}.png`), fullPage: true });
    await page.reload();
    await expect(page.getByRole('button', { name: 'Publish approved set' })).toBeEnabled();
    expect(state.counts()).toMatchObject({ approvals: 1, publications: 0 });
    await page.getByRole('button', { name: 'Publish approved set' }).click();
    await expect.poll(() => state.counts().publications).toBe(1);
    await page.reload();

    // Assert
    await expect(page.getByRole('region', { name: 'Persisted publication outcome' })).toBeVisible();
    expect(state.outcome()?.records).toHaveLength(2);
    expect(state.counts().publications).toBe(1);
    expect(state.counts().publishKeys[0]).toBeTruthy();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await page.screenshot({ path: info.outputPath(`published-${width}.png`), fullPage: true });
  });

  test(`offering links reviewed source references without claiming authorization at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const status = packageStatus({ processingState: 'NeedsAttention',
      coverage: { total: 1, processed: 0, pending: 0, failed: 0, unreadable: 0, excluded: 0, unsupported: 1 } });
    const authorizationReference = { reference: 'Synthetic test-only letter', issuer: 'Synthetic test authority',
      issuedAt: '2026-01-01T00:00:00Z', expiresAt: '2027-01-01T00:00:00Z' };
    const reference = candidate({
      candidateId: 'reference-1', type: 'AuthorizationReference', name: 'Synthetic source reference',
      description: 'A source-stated reference, not a verified authorization.',
      reviewState: 'Reviewed', controlDuties: {}, contributorIds: [],
      authorizationReference,
      citations: [{ artifactId: 'artifact-1', archivePath: 'synthetic-reference.json',
        locator: '/authorizationReferences/0', quote: JSON.stringify(authorizationReference) }],
    });
    const mutations: string[] = [];
    await context.route('**/api/csp/catalog?*', route => route.fulfill({ json: { status: 'success', data: {
      items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available',
    } } }));
    await context.route('**/api/csp/catalog/overview?*', route => route.fulfill({ json: { status: 'success', data: {
      providerName: 'Synthetic Provider', authorizationRecord: null, sourceArtifacts: responsePage([]),
    } } }));
    await context.route('**/api/csp/package-imports**', route => {
      const request = route.request();
      const url = new URL(request.url());
      const success = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      expect(request.headers()['x-workspace-kind']).toBe('csp');
      if (request.method() !== 'GET') mutations.push(request.method());
      if (url.pathname.endsWith('/package-imports')) return success(responsePage([status]));
      if (url.pathname.endsWith('/package-1')) return success(status);
      if (url.pathname.endsWith('/review-state')) return success({
        packageId: status.packageId, revision: status.revision, preview: null, previewIsStale: false, publication: null,
      });
      if (url.pathname.endsWith('/entries')) return success(responsePage([entry({ status: 'Unsupported', reason: 'Surrounding prose requires analysis.' })]));
      if (url.pathname.endsWith('/candidates')) {
        expect(url.searchParams.get('type')).toBe('AuthorizationReference');
        expect(url.searchParams.get('reviewState')).toBe('Reviewed');
        return success(responsePage([reference]));
      }
      if (url.pathname.endsWith('/content')) return route.fulfill({
        contentType: 'application/json', body: JSON.stringify({
          notice: 'Synthetic source only. Not an authorization.', authorizationReferences: [authorizationReference],
        }),
        headers: { 'Content-Disposition': 'attachment; filename="synthetic-reference.json"' },
      });
      return route.fulfill({ status: 404, json: { status: 'error', error: { errorCode: 'FIXTURE_ROUTE', message: url.pathname } } });
    });

    // Act
    await page.goto('/workspaces/csp/security-capabilities');
    await page.getByRole('button', { name: 'View source package' }).click();
    await page.getByRole('button', { name: 'Show reviewed references for Synthetic package' }).click();
    const references = page.getByRole('region', { name: 'Reviewed authorization references' });
    await expect(references.getByText('Synthetic test-only letter', { exact: true })).toBeVisible();
    await references.getByRole('link', { name: 'Review source authorization reference' }).click();

    // Assert
    await expect(page.getByRole('textbox', { name: 'Authorization reference', exact: true })).toHaveValue('Synthetic test-only letter');
    await expect(page.getByText(/not verified authorization decisions/)).toBeVisible();
    await expect(page.getByRole('checkbox', { name: /Select Synthetic source reference/ })).toBeDisabled();
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download cited source' }).click();
    expect((await download).suggestedFilename()).toBe('synthetic-reference.json');
    expect(mutations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`reference-review-${width}.png`), fullPage: true });
  });

  test(`provider import entry and access denial remain explicit at ${width}px`, async ({ context, page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await context.route('**/api/csp/catalog?*', route => route.fulfill({ json: { status: 'success', data: {
      items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available',
    } } }));
    await context.route('**/api/csp/catalog/overview?*', route => route.fulfill({ json: { status: 'success', data: {
      providerName: 'Synthetic Provider', authorizationRecord: null,
      sourceArtifacts: { items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available' },
    } } }));
    const packageRequests: string[] = [];
    await context.route('**/api/csp/package-imports**', route => {
      packageRequests.push(route.request().method());
      return route.fulfill({ status: 403, json: {
        status: 'error',
        error: { errorCode: 'PACKAGE_ACCESS_DENIED', message: 'Package review is not permitted.' },
      } });
    });

    // Act
    await page.goto('/workspaces/csp/security-capabilities');
    const reviewLink = page.getByRole('link', { name: 'Review imports', exact: true });
    await expect(reviewLink).toHaveAttribute('href', '/workspaces/csp/security-capabilities/imports');
    await reviewLink.focus();
    await page.keyboard.press('Enter');

    // Assert
    await expect(page).toHaveURL(/\/workspaces\/csp\/security-capabilities\/imports$/);
    await expect(page.getByText('Package review is not permitted.', { exact: false })).toBeVisible();
    expect(packageRequests.length).toBeGreaterThan(0);
    expect(packageRequests.every(method => method === 'GET')).toBe(true);
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
