import { expect, test, type BrowserContext } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import type {
  EnvironmentHostingLink, PreviewEnvironmentHostingLinkRequest, SystemEnvironmentAttachment,
  SystemEnvironmentsResponse, SystemProviderScope, SystemProviderScopeChoice,
} from '../../src/api/systemEnvironments';

const root = '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment';
const apiRoot = '/api/dashboard/systems/system-a/environments';
const releasedScope: SystemProviderScope = {
  assignmentId: 'service-scope-a', assignmentVersion: 1, relationshipId: 'relationship-a',
  offeringId: 'collaboration-a', offeringName: 'Shared collaboration', providerName: 'Synthetic Service Provider',
  providerId: 'provider-a', hostingScopeRevision: 12,
  hostingScopeRevisionId: 'mail-release-a', hostingScopeName: 'Mail service release', state: 'Active',
  relationshipState: 'Undetermined', reviewRequired: true, selectionVersion: 1,
  responsibilityReview: { state: 'NotAdopted', canReview: true, canConfirm: false, reason: 'Adopt applicable capabilities before confirming duties.' },
  assignedScopes: [{ kind: 'Service', serviceId: 'mail-a', serviceName: 'Shared mail',
    environment: 'Microsoft365DoD', tenantReference: 'synthetic-service-tenant' }],
};
const choice: SystemProviderScopeChoice = {
  offeringId: releasedScope.offeringId, offeringVersion: 7, offeringName: releasedScope.offeringName,
  providerName: releasedScope.providerName, hostingScopeRevisionId: releasedScope.hostingScopeRevisionId,
  providerId: 'provider-a', hostingScopeRevision: 12,
  hostingScopeName: releasedScope.hostingScopeName, permittedScopes: releasedScope.assignedScopes,
  exclusions: [], eligibilitySource: 'Published service offering',
  publishedDuties: { state: 'Available', reason: null, capabilities: [{
    capabilityId: 'mail-protection', capabilityName: 'Published mail protection', description: 'Captured mail filtering responsibilities.',
    releaseId: 'mail-capability-release', releaseRevision: 7, releaseSnapshotHash: 'published-release-hash',
    contentHash: 'published-content-hash', applicabilityContextId: 'published-context',
    providerControlIds: ['SC-7'], sharedControlIds: ['SI-4'], customerControlIds: ['AC-2', 'AU-6'],
  }] },
};
const subscription: SystemEnvironmentAttachment = {
  attachmentId: 'attachment-a', systemId: 'system-a', version: 2, source: 'OrganizationOwned',
  registration: { registrationId: 'registration-a', ownerTenantId: 'org-a', subscriptionId: 'subscription-a',
    directoryTenantId: 'directory-a', cloud: 'AzureUSGovernment', displayName: 'Mission production',
    status: 'Selected', lastVerifiedAt: '2026-09-29T20:00:00Z' },
  allocationId: null, allocationVersion: null, offeringId: null, offeringName: null, hostingAssignmentId: null,
  hostingReviewState: 'NotApplicable', attachmentState: 'Attached',
  scope: { revisionId: 'resources-a', version: 1, reviewState: 'Reviewed', resourceIds: ['/subscriptions/subscription-a/resourceGroups/mission'],
    exclusions: [], sharedDependencyResourceIds: [], discoveredAt: '2026-09-29T20:00:00Z' },
  assessmentAccess: { state: 'NotChecked', checkedAt: null, reason: null },
  monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
  monitoring: { configured: false, enabled: false, health: 'NotEvaluated', evaluatedAt: null, reason: null },
  readiness: { state: 'NotChecked', checkedAt: null, reason: null },
  provenance: { source: 'SyntheticFixture', externalId: null, sourceRevision: null,
    reconciliationState: 'Verified', evidenceReference: null, recordedAt: '2026-09-29T20:00:00Z' },
  updatedAt: '2026-09-29T20:00:00Z',
};

async function install(context: BrowserContext, baseURL: string, withSubscription = false) {
  await installSystemCapabilityFixture(context, baseURL);
  const state: SystemEnvironmentsResponse = {
    systemId: 'system-a', version: 3,
    permissions: { canManageEnvironments: true, canCheckAccess: false, canRunAssessments: false,
      canManageMonitoring: false, canRegisterSubscriptions: false },
    attachments: withSubscription ? [structuredClone(subscription)] : [], legacyReferences: [],
    providerScopes: withSubscription ? [structuredClone(releasedScope)] : [], hostingLinks: [],
  };
  const writes: { path: string; body: unknown }[] = [];
  const profileWrites: unknown[] = [];
  let failedReads = false;
  let linkIntent: PreviewEnvironmentHostingLinkRequest | null = null;
  await context.route(`**${apiRoot}`, route => failedReads
    ? route.fulfill({ status: 503, json: { message: 'Synthetic provider scope read failure' } })
    : route.fulfill({ json: state }));
  await context.route(`**${apiRoot}/provider-scope-choices`, route => route.fulfill({
    json: { systemId: state.systemId, version: state.version, canManage: true, choices: [choice] },
  }));
  await context.route(`**${apiRoot}/provider-scopes`, route => {
    const body = route.request().postDataJSON();
    expect(body).toEqual({ expectedVersion: state.version, offeringId: choice.offeringId,
      expectedOfferingVersion: choice.offeringVersion, hostingScopeRevisionId: choice.hostingScopeRevisionId });
    expect(route.request().headers()['idempotency-key']).toBeTruthy();
    writes.push({ path: 'provider-scopes', body });
    state.providerScopes = [structuredClone(releasedScope)]; state.version++;
    return route.fulfill({ json: state });
  });
  await context.route(`**${apiRoot}/hosting-links/preview`, route => {
    const body: PreviewEnvironmentHostingLinkRequest = route.request().postDataJSON();
    expect(body).toEqual({ expectedVersion: state.version, attachmentId: subscription.attachmentId,
      expectedAttachmentVersion: subscription.version, assignmentId: releasedScope.assignmentId,
      expectedAssignmentVersion: releasedScope.assignmentVersion, action: body.action, rationale: 'Reviewed service applicability' });
    expect(['Link', 'Unlink']).toContain(body.action);
    linkIntent = body;
    writes.push({ path: 'hosting-links/preview', body });
    return route.fulfill({ json: { previewId: `preview-${state.version}`, systemId: state.systemId, allocationId: null,
      expectedVersion: state.version, expiresAt: '2099-01-01T00:00:00Z', systems: [],
      requiresScopeReview: false, warnings: ['Only the optional link changes. Both records and retained history remain.'] } });
  });
  await context.route(`**${apiRoot}/hosting-links/commit`, route => {
    const body = route.request().postDataJSON();
    expect(body).toEqual({ expectedVersion: state.version, previewId: `preview-${state.version}`,
      acknowledgeImpact: true, rationale: 'Reviewed service applicability' });
    expect(linkIntent).not.toBeNull();
    expect(route.request().headers()['idempotency-key']).toBeTruthy();
    const link: EnvironmentHostingLink = {
      linkId: 'link-a', attachmentId: subscription.attachmentId, assignmentId: releasedScope.assignmentId,
      version: state.version, state: linkIntent!.action === 'Link' ? 'Linked' : 'Unlinked',
      source: 'Explicit', updatedAt: '2026-09-29T20:00:00Z',
    };
    writes.push({ path: 'hosting-links/commit', body });
    state.hostingLinks = [link]; state.version++;
    return route.fulfill({ json: state });
  });
  await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => {
    if (route.request().method() !== 'GET') profileWrites.push(route.request().postDataJSON());
    return route.fulfill({ json: { id: 'profile-a', sectionType: 'EnvironmentAndDeployment', governanceStatus: 'Draft',
      canEditProfile: true, draftContent: '{"hostingModel":"Hybrid","additionalDetails":"Saved deployment description"}',
      userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
  });
  return { state, writes, profileWrites, failReads: (value: boolean) => { failedReads = value; } };
}

for (const width of [1440, 390]) {
  test(`provider-only service selection preserves the environment draft without subscriptions at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    const fixture = await install(context, baseURL!);
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(root);
    const provider = page.getByRole('region', { name: 'Provider services & scopes', exact: true });
    await expect(provider.getByText('No provider scopes selected.', { exact: true })).toBeVisible();
    const draft = page.getByLabel('Deployment description', { exact: true });
    await draft.fill('Unsaved provider-independent deployment details');
    // Act
    await provider.getByRole('button', { name: 'Add provider scope', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Add provider scope', exact: true });
    await dialog.getByRole('button', { name: 'Synthetic Service Provider', exact: true }).click();
    await dialog.getByRole('button', { name: 'Shared collaboration — Mail service release', exact: true }).click();
    await expect(dialog.getByRole('heading', { name: 'Applicability & customer duties', exact: true })).toBeVisible();
    await expect(dialog.getByText('Released scope revision: 12', { exact: true })).toBeVisible();
    await expect(dialog.getByRole('heading', { name: 'Published mail protection', exact: true })).toBeVisible();
    await expect(dialog.getByText('Captured mail filtering responsibilities.', { exact: true })).toBeVisible();
    await expect(dialog.getByText('AC-2, AU-6', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Capability release revision: 7', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Customer-duty content and responsibility-review status are unavailable in this picker.', { exact: true })).toHaveCount(0);
    await dialog.getByText('Pinned published source', { exact: true }).click();
    await expect(dialog.getByText('Release snapshot hash: published-release-hash', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Content hash: published-content-hash', { exact: true })).toBeVisible();
    await expect(dialog.getByRole('link', { name: 'Inspect recorded customer duties (opens in a new tab)', exact: true }))
      .toHaveAttribute('target', '_blank');
    const opened = page.waitForEvent('popup');
    await dialog.getByRole('link', { name: 'Inspect recorded customer duties (opens in a new tab)', exact: true }).click();
    const duties = await opened;
    await expect(duties.getByRole('heading', { name: 'Control responsibilities', exact: true })).toBeVisible();
    await duties.close();
    await expect(draft).toHaveValue('Unsaved provider-independent deployment details');
    await expect(dialog.getByRole('button', { name: 'Save relationship', exact: true })).toBeDisabled();
    await dialog.getByRole('checkbox', { name: /reviewed the applicability/ }).check();
    await dialog.getByRole('button', { name: 'Save relationship', exact: true }).click();
    // Assert
    await expect(dialog).not.toBeVisible();
    await expect(provider.getByRole('heading', { name: 'Shared collaboration', exact: true })).toBeVisible();
    await expect(provider.getByText('Scope release revision: 12 · Relationship revision: 1', { exact: true })).toBeVisible();
    await expect(provider.getByText('Responsibility review: Not Adopted', { exact: true })).toBeVisible();
    await expect(provider.getByText('No subscriptions linked (optional).', { exact: true })).toBeVisible();
    await expect(provider.getByRole('alert')).toHaveCount(0);
    await expect(draft).toHaveValue('Unsaved provider-independent deployment details');
    expect(fixture.state.attachments).toEqual([]);
    expect(fixture.state.hostingLinks).toEqual([]);
    expect(fixture.profileWrites).toEqual([]);
    expect(fixture.writes.map(write => write.path)).toEqual(['provider-scopes']);
    await expect(provider.getByRole('link', { name: 'Review responsibilities', exact: true }))
      .toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions');
    await page.screenshot({ path: info.outputPath('provider-only.png'), fullPage: true });
  });

  test(`optional provider links preserve both records and the draft at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    const fixture = await install(context, baseURL!, true);
    const originalAttachments = structuredClone(fixture.state.attachments);
    const originalScopes = structuredClone(fixture.state.providerScopes);
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(root);
    const provider = page.getByRole('region', { name: 'Provider services & scopes', exact: true });
    const draft = page.getByLabel('Deployment description', { exact: true });
    await draft.fill('Unsaved draft survives optional links');
    // Act
    for (const action of ['Link', 'Unlink'] as const) {
      await provider.getByRole('button', { name: 'Manage relationship', exact: true }).click();
      const dialog = page.getByRole('dialog', { name: 'Manage provider relationship', exact: true });
      if (action === 'Link') await dialog.getByRole('combobox', { name: 'Subscription to link', exact: true }).selectOption('attachment-a');
      else await dialog.getByRole('button', { name: 'Remove link to Mission production', exact: true }).click();
      await dialog.getByRole('textbox', { name: 'Link change rationale', exact: true }).fill('Reviewed service applicability');
      await dialog.getByRole('button', { name: 'Preview link change', exact: true }).click();
      await expect(dialog.getByText('Only the optional link changes. Both records and retained history remain.', { exact: true })).toBeVisible();
      await expect(dialog.getByRole('button', { name: 'Confirm link change', exact: true })).toBeDisabled();
      await dialog.getByRole('checkbox', { name: /acknowledge the link impact/ }).check();
      await dialog.getByRole('button', { name: 'Confirm link change', exact: true }).click();
      await expect(dialog).not.toBeVisible();
      // Assert
      await expect(provider.getByRole('button', { name: 'Manage relationship', exact: true })).toBeVisible();
      if (action === 'Link') await expect(provider.getByText('Mission production', { exact: true })).toBeVisible();
      else await expect(provider.getByText('No subscriptions linked (optional).', { exact: true })).toBeVisible();
      await expect(draft).toHaveValue('Unsaved draft survives optional links');
      expect(fixture.state.attachments).toEqual(originalAttachments);
      expect(fixture.state.providerScopes).toEqual(originalScopes);
    }
    expect(fixture.profileWrites).toEqual([]);
    expect(fixture.writes.map(write => write.path)).toEqual([
      'hosting-links/preview', 'hosting-links/commit', 'hosting-links/preview', 'hosting-links/commit',
    ]);
    await page.screenshot({ path: info.outputPath('unlinked-records-retained.png'), fullPage: true });
  });

  test(`failed provider reads are not empty successful states at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    const fixture = await install(context, baseURL!);
    fixture.failReads(true);
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(root);
    const provider = page.getByRole('region', { name: 'Provider services & scopes', exact: true });
    // Act
    await expect(provider.getByRole('alert')).toBeVisible();
    // Assert
    await expect(provider.getByText('No provider scopes selected.', { exact: true })).toHaveCount(0);
    await expect(provider.getByRole('button', { name: 'Add provider scope', exact: true })).toBeDisabled();
    // Act
    fixture.failReads(false);
    await provider.getByRole('button', { name: 'Retry', exact: true }).click();
    // Assert
    await expect(provider.getByText('No provider scopes selected.', { exact: true })).toBeVisible();
    await expect(provider.getByRole('alert')).toHaveCount(0);
    expect(fixture.profileWrites).toEqual([]);
    expect(fixture.writes).toEqual([]);
  });

  test(`invalid hosting mapping has one exact actionable warning at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    const fixture = await install(context, baseURL!);
    fixture.state.providerScopes = [structuredClone(releasedScope)];
    fixture.state.attachments = [{ ...structuredClone(subscription), hostingAssignmentId: 'missing-provider-scope' }];
    const warning = { referenceId: subscription.attachmentId, kind: 'ProviderScopeLink',
      displayName: 'Mission production', reconciliationState: 'ReconciliationRequired',
      reason: 'The recorded provider release mapping needs review.' };
    fixture.state.legacyReferences = [warning, { ...warning }];
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(root);
    const provider = page.getByRole('region', { name: 'Provider services & scopes', exact: true });
    // Act
    await expect(provider.getByRole('alert')).toHaveCount(1);
    // Assert
    await expect(provider.getByRole('heading', { name: 'An existing hosting relationship needs review.', exact: true })).toHaveCount(1);
    await expect(provider.getByRole('button', { name: 'Review relationship', exact: true })).toHaveCount(1);
    // Act
    await provider.getByRole('button', { name: 'Review relationship', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Review relationship', exact: true });
    // Assert
    await expect(dialog.getByText('The recorded provider release mapping needs review.', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Recorded subscription: Mission production', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Retained provider assignment: missing-provider-scope', { exact: true })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Review provider relationship', exact: true })).toHaveCount(0);
    await expect(dialog.getByRole('combobox', { name: 'Subscription to link', exact: true })).toHaveCount(0);
    expect(fixture.state.attachments).toEqual([{ ...subscription, hostingAssignmentId: 'missing-provider-scope' }]);
    expect(fixture.profileWrites).toEqual([]);
    expect(fixture.writes).toEqual([]);
  });

  test(`active capability dependencies block provider removal at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    const fixture = await install(context, baseURL!, true);
    let removalAttempts = 0;
    await context.route(`**${apiRoot}/provider-scopes/${releasedScope.assignmentId}/remove-preview`, route => {
      expect(route.request().postDataJSON()).toEqual({
        expectedVersion: fixture.state.version, expectedAssignmentVersion: releasedScope.assignmentVersion,
        expectedSelectionVersion: releasedScope.selectionVersion, rationale: 'Retiring provider service',
      });
      return route.fulfill({ json: { previewId: 'blocked-removal', systemId: 'system-a', allocationId: null,
        expectedVersion: fixture.state.version, expiresAt: '2099-01-01T00:00:00Z', systems: [],
        requiresScopeReview: false, warnings: [], canCommit: false,
        blockers: ['Published mail protection is still adopted from this scope. Resolve that adoption first.'] } });
    });
    await context.route(`**${apiRoot}/provider-scopes/${releasedScope.assignmentId}/remove`, route => {
      removalAttempts++;
      return route.fulfill({ status: 409, json: { message: 'Current adopted capabilities prevent removal.' } });
    });
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(root);
    // Act
    await page.getByRole('region', { name: 'Provider services & scopes', exact: true })
      .getByRole('button', { name: 'Manage relationship', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Manage provider relationship', exact: true });
    await dialog.getByRole('textbox', { name: 'Removal rationale', exact: true }).fill('Retiring provider service');
    await dialog.getByRole('button', { name: 'Preview removal', exact: true }).click();
    // Assert
    await expect(dialog.getByRole('alert')).toHaveText('Published mail protection is still adopted from this scope. Resolve that adoption first.');
    await expect(dialog.getByRole('checkbox', { name: /acknowledge this impact/ })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Remove provider relationship', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('link', { name: 'Review capability dependencies (opens in a new tab)', exact: true }))
      .toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/security-capabilities');
    expect(removalAttempts).toBe(0);
    expect(fixture.state.providerScopes).toEqual([releasedScope]);
    expect(fixture.state.attachments).toEqual([subscription]);
    expect(fixture.profileWrites).toEqual([]);
  });
}
