import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const capability = {
  source: 'provider', componentId: 'component-a', capabilityId: 'capability-a',
  name: 'Security monitoring (SIEM)', description: 'Detect and investigate suspicious activity.',
  componentName: 'Monitoring service', componentType: 'Service', lifecycle: 'Published', reviewState: 'NeedsReview',
  sourceFormat: 'OscalJson', sourceReference: 'Provider SSP v3', distinctAdoptionCount: 3, distinctOrganizationCount: 2,
  workingRevision: 4, releasedRevision: 3, workingApprovalState: 'NotApproved',
  supportingComponents: [
    { id: 'component-a', name: 'Monitoring service', componentType: 'Service', source: 'provider' },
    { id: 'component-b', name: 'Operations platform', componentType: 'Platform', source: 'provider' },
  ],
};
const working = {
  capabilityId: 'capability-a', revision: 4, snapshotHash: 'hash-v4', approvedRevision: null,
  updatedAt: '2026-09-23T00:00:00Z', contributors: ['component-b'], controlDuties: { 'AU-6': 'Shared' },
  classification: 'CUI', serviceCategory: 'Monitoring', approvalState: 'NotApproved',
  approvedPreviewId: null, approvedPreviewHash: null, approvedAt: null, approvedBy: null,
};
const preview = {
  previewId: 'preview-a', capabilityId: 'capability-a', revision: 4, workingSnapshotHash: 'hash-v4',
  previewHash: 'preview-hash-v4', generatedAt: '2026-09-23T00:00:00Z', expiresAt: '2099-09-23T00:00:00Z', isStale: false,
  contributorChanges: [{ value: 'component-b', changeKind: 'Added' }],
  dutyChanges: [{ key: 'AU-6', before: 'Provider', after: 'Shared', changeKind: 'Changed' }],
  referenceChanges: [{ value: 'Provider SSP v4', changeKind: 'Added' }],
  affectedOrganizations: ['org-a'], affectedSystems: [{ organizationId: 'org-a', systemId: 'system-a' }],
  delivery: { impactWrites: 1, distinctOrganizations: 1, distinctSystems: 1 },
  notifications: { recipientCount: 2, distinctOrganizations: 1 },
};

async function capture(page: Page, info: TestInfo, name: string) {
  await expect(page.locator('html')).not.toHaveClass(/dark/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath(`${name}.png`), fullPage: true });
}

test('a new capability saves its first revision from the authoring screen', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  await context.route('**/api/csp/catalog/capabilities/new-capability', route => route.fulfill({ json: { data: {
    capability: { ...capability, capabilityId: 'new-capability', workingRevision: null, releasedRevision: null, workingApprovalState: null },
    supportingComponents: [], unresolvedContributorIds: [], sourceArtifacts: [],
    mappedControlIds: [], sourceEvidenceReferences: null, implementationNarrative: null,
  } } }));
  await context.route('**/api/csp/catalog/capabilities/new-capability/subscribers?*', route => route.fulfill({ json: { data: {
    items: [], page: 1, pageSize: 25, total: 0,
  } } }));
  let saves = 0;
  await context.route('**/api/csp/catalog/capabilities/new-capability/working-revision', route => {
    if (route.request().method() === 'GET') {
      return route.fulfill({ status: 404, json: { error: { code: 'WORKING_REVISION_NOT_FOUND', message: 'Working revision was not found.' } } });
    }
    saves++;
    expect(route.request().postDataJSON()).toEqual({
      expectedRevision: 1, classification: 'CUI', serviceCategory: 'Monitoring', contributors: [], controlDuties: {},
    });
    return route.fulfill({ json: { data: { ...working, capabilityId: 'new-capability', revision: 1, contributors: [], controlDuties: {} } } });
  });
  // Act
  await page.goto('/workspaces/csp/security-capabilities/new-capability');
  await expect(page.getByLabel('Classification', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save working revision' })).toBeDisabled();
  await page.getByLabel('Classification', { exact: true }).fill('CUI');
  await page.getByLabel('Service category', { exact: true }).fill('Monitoring');
  await page.getByRole('button', { name: 'Save working revision' }).click();
  // Assert
  await expect(page.getByRole('button', { name: 'Review publication impact' })).toBeEnabled();
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect(saves).toBe(1);
});

for (const width of [1440, 390]) {
  test(`provider catalog, authoring and publication match the Light mocks at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await context.route('**/api/csp/catalog/overview?*', route => route.fulfill({ json: { data: {
      providerName: 'Synthetic Provider', sourceArtifacts: {
        items: [{ componentId: 'component-a', componentName: 'Monitoring service', sourceFileName: 'Provider source package', sourceReference: 'Baseline package v3', sourceFormat: 'OscalJson' }],
        page: 1, pageSize: 25, total: 1, aggregateState: 'Available',
      },
      authorizationRecord: null,
    } } }));
    await context.route('**/api/csp/catalog?*', route => {
      const component = new URL(route.request().url()).searchParams.get('grouping') === 'component';
      return route.fulfill({ json: { data: {
        items: component ? [{ ...capability, capabilityId: null, name: 'Monitoring service' }] : [
          capability, { ...capability, capabilityId: 'capability-b', name: 'Identity and access management', releasedRevision: null, workingRevision: 1, distinctOrganizationCount: 0, distinctAdoptionCount: 0 },
        ], page: 1, pageSize: 25, total: component ? 1 : 2, aggregateState: 'Available',
      } } });
    });
    await context.route('**/api/csp/catalog/capabilities/capability-a', route => route.fulfill({ json: { data: {
      capability, supportingComponents: capability.supportingComponents, unresolvedContributorIds: [],
      sourceArtifacts: [{ componentId: 'component-a', componentName: 'Monitoring service', sourceFileName: 'Provider SSP', sourceReference: 'Provider SSP v3', sourceFormat: 'OscalJson' }],
      mappedControlIds: ['AU-6'], sourceEvidenceReferences: null, implementationNarrative: null,
    } } }));
    await context.route('**/api/csp/catalog/capabilities/capability-a/working-revision', route => route.fulfill({ json: { data: working } }));
    await context.route('**/api/csp/catalog/capabilities/capability-a/subscribers?*', route => route.fulfill({ json: { data: {
      items: [{ organizationId: 'org-a', organizationName: 'Coastal Watch', systemId: 'system-a', systemName: 'Vanguard',
        subscriptionId: 'subscription-a', sourceRevision: '3', reviewState: 'Pending' }], page: 1, pageSize: 25, total: 1,
    } } }));
    await context.route('**/api/csp/catalog/capabilities/capability-a/publication-previews', route => route.fulfill({ json: { data: preview } }));
    let approvals = 0;
    let publications = 0;
    await context.route('**/api/csp/catalog/capabilities/capability-a/working-revision/approve', route => {
      approvals++;
      expect(route.request().postDataJSON()).toEqual({ revision: 4, previewId: 'preview-a', previewHash: 'preview-hash-v4' });
      return route.fulfill({ json: { data: { ...working, approvedRevision: 4, approvalState: 'Approved', approvedPreviewId: 'preview-a', approvedPreviewHash: 'preview-hash-v4' } } });
    });
    await context.route('**/api/csp/catalog/capabilities/capability-a/publish', route => {
      publications++;
      expect(route.request().postDataJSON()).toMatchObject({ revision: 4, approvedRevision: 4, previewId: 'preview-a', previewHash: 'preview-hash-v4' });
      return route.fulfill({ json: { data: { releaseId: 'release-a', capabilityId: 'capability-a', revision: 4, snapshotHash: 'hash-v4', publishedAt: '2026-09-23T00:00:00Z', impactCount: 1, existing: false } } });
    });

    // Act
    await page.goto('/workspaces/csp/security-capabilities');
    await expect(page.getByRole('heading', { name: 'Capabilities you provide' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'By capability' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByText('2 organizations / 3 systems')).toBeVisible();
    await expect(page.getByText('Not published', { exact: true })).toBeVisible();
    await capture(page, info, 'provider-catalog');
    await page.getByRole('button', { name: 'View source package' }).click();
    await expect(page.getByText('Baseline package v3')).toBeVisible();
    await page.getByRole('button', { name: 'By component' }).click();
    await page.getByRole('link', { name: 'Monitoring service', exact: true }).click();
    await expect(page).toHaveURL(/componentId=component-a/);
    await page.getByRole('link', { name: 'Security monitoring (SIEM)', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Components that deliver this capability' })).toBeVisible();
    await expect(page.getByText('Operations platform', { exact: true })).toBeVisible();
    await expect(page.getByText('No provider implementation narrative recorded.')).toBeVisible();
    if (width === 1440) {
      const delivery = await page.getByRole('heading', { name: 'Components that deliver this capability' }).boundingBox();
      const readiness = await page.getByRole('heading', { name: 'Publication readiness' }).boundingBox();
      expect(readiness!.x).toBeGreaterThan(delivery!.x + delivery!.width);
    }
    await capture(page, info, 'provider-authoring');
    await page.getByRole('button', { name: 'Review publication impact' }).click();
    await page.getByRole('button', { name: 'Generate publication preview' }).click();
    await expect(page.getByText('AU-6: Provider → Shared', { exact: true })).toBeVisible();
    await expect(page.getByText('Coastal Watch', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    await capture(page, info, 'provider-publication');
    await page.getByRole('checkbox', { name: 'Source evidence and coverage reviewed' }).check();
    await page.getByRole('checkbox', { name: 'Provider and customer duties reviewed' }).check();
    await page.getByRole('button', { name: 'Approve exact preview' }).click();
    await page.getByRole('button', { name: 'Publish release' }).click();

    // Assert
    await expect(page.getByText('Release 4 published with 1 durable customer impacts.')).toBeVisible();
    expect(approvals).toBe(1);
    expect(publications).toBe(1);
    await expect(page.locator('html')).not.toHaveClass(/dark/);
  });
}
