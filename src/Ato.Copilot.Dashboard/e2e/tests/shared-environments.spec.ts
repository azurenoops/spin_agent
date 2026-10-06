import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import type { EnvironmentChoice, SystemEnvironmentAttachment, SystemEnvironmentsResponse } from '../../src/api/systemEnvironments';

const root = '/workspaces/organizations/org-a/systems/system-a';
const apiRoot = '/api/dashboard/systems/system-a/environments';
for (const width of [1440, 390]) {
  test(`provider and organization sources share one environment without saving documentation at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: canonical server-shaped fixtures, no live Azure or tenant writes.
    await installSystemCapabilityFixture(context, baseURL!);
    await page.setViewportSize({ width, height: 1100 });
    const permissions = { canManageEnvironments: true, canCheckAccess: true, canRunAssessments: false, canManageMonitoring: false, canRegisterSubscriptions: false };
    let version = 0;
    const attachments: SystemEnvironmentAttachment[] = [];
    const profileWrites: unknown[] = [];
    const applies: unknown[] = [];
    let reviewing = '';
    const choices: EnvironmentChoice[] = ['ProviderAllocation', 'OrganizationOwned', 'OrganizationOwned'].map((source, index) => ({
      choiceId: `choice-${index}`, source: source as EnvironmentChoice['source'],
      registration: { registrationId: `registration-${index}`, ownerTenantId: source === 'ProviderAllocation' ? 'provider-a' : 'org-a',
        subscriptionId: `subscription-${index}`, directoryTenantId: 'azure-directory', cloud: 'AzureUSGovernment', displayName: `Mission subscription ${index + 1}`,
        status: 'Selected', lastVerifiedAt: '2026-09-29T20:00:00Z' },
      allocationId: index === 0 ? 'allocation-a' : null, allocationVersion: index === 0 ? 1 : null,
      offeringId: index === 0 ? 'offering-a' : null, offeringName: index === 0 ? 'Azure IL5 Shared Services' : null,
      hostingScopeRevisionId: index === 0 ? 'release-a' : null, allocationState: index === 0 ? 'Active' : null,
      providerName: index === 0 ? 'Synthetic CSP' : null, consumerName: 'Mission organization',
      hostingScopeName: index === 0 ? 'Reviewed mission hosting release' : null,
      startsAt: null, expiresAt: null, provenance: { source: 'Fixture', externalId: null, sourceRevision: null,
        reconciliationState: 'Verified', evidenceReference: null, recordedAt: '2026-09-29T20:00:00Z' },
      eligible: true, ineligibleReason: null,
    }));
    const current = (): SystemEnvironmentsResponse => ({ systemId: 'system-a', version, permissions, attachments, legacyReferences: [] });
    await context.route(`**${apiRoot}`, route => route.fulfill({ json: current() }));
    await context.route(`**${apiRoot}/choices`, route => route.fulfill({ json: { ...current(), registrationHref: '',
      choices: choices.map(item => ({ ...item, eligible: !attachments.some(a => a.registration.registrationId === item.registration.registrationId),
        ineligibleReason: attachments.some(a => a.registration.registrationId === item.registration.registrationId) ? 'Already attached' : null })) } }));
    await context.route(`**${apiRoot}/discover`, route => {
      const body = route.request().postDataJSON();
      return route.fulfill({ json: { systemId: 'system-a', selection: body.selection, discoveryToken: 'token', expiresAt: '2099-01-01T00:00:00Z',
        discoveredAt: '2026-09-29T20:00:00Z', resources: [{ resourceId: `/subscriptions/${body.selection.registrationId}/resourceGroups/rg-app/providers/Microsoft.Web/sites/api`,
          name: 'Mission API', resourceType: 'Microsoft.Web/sites', resourceGroup: 'rg-app', location: 'usgovvirginia' }] } });
    });
    await context.route(`**${apiRoot}/provider-scope-choices`, route => route.fulfill({ json: {
      systemId: 'system-a', version, canManage: true, choices: [],
    } }));
    await context.route(`**${apiRoot}/apply-batch`, route => {
      const batch = route.request().postDataJSON();
      expect(batch.expectedVersion).toBe(version);
      expect(route.request().headers()['idempotency-key']).toBeTruthy();
      for (const body of batch.items) {
      applies.push(body);
      expect(body.resourceIds).toHaveLength(1);
      expect(body.reuseHostingAssignmentId).toBeNull();
      const choice = choices.find(item => item.registration.registrationId === body.selection.registrationId)!;
      attachments.push({ attachmentId: `attachment-${version}`, systemId: 'system-a', version: 1, source: choice.source, registration: choice.registration,
        allocationId: choice.allocationId, allocationVersion: choice.allocationVersion, offeringId: choice.offeringId, offeringName: choice.offeringName,
        providerName: choice.providerName, consumerName: choice.consumerName, hostingScopeName: choice.hostingScopeName,
        hostingScopeRevisionId: choice.hostingScopeRevisionId,
        allocationState: choice.allocationState, allocationStartsAt: choice.startsAt, allocationExpiresAt: choice.expiresAt,
        hostingAssignmentId: null, hostingReviewState: 'NotApplicable',
        attachmentState: 'Attached', scope: { revisionId: `scope-${version}`, version: 1, reviewState: 'PendingReview', resourceIds: body.resourceIds,
          sharedDependencyResourceIds: [], exclusions: [], discoveredAt: '2026-09-29T20:00:00Z' },
        assessmentAccess: { state: 'NotChecked', checkedAt: null, reason: null }, monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
        monitoring: { configured: false, enabled: false, health: 'NotEvaluated', evaluatedAt: null, reason: 'No collection verified' },
        readiness: { state: 'Blocked', checkedAt: null, reason: 'Scope review required' }, provenance: choice.provenance, updatedAt: '2026-09-29T20:00:00Z' });
      version++;
      }
      return route.fulfill({ json: current() });
    });
    await context.route(`**${apiRoot}/*/scope-preview`, route => {
      const body = route.request().postDataJSON();
      expect(body.reviewPendingScope).toBe(true);
      reviewing = new URL(route.request().url()).pathname.split('/').at(-2)!;
      const item = attachments.find(item => item.attachmentId === reviewing)!;
      expect(body.resourceIds).toEqual(item.scope.resourceIds);
      return route.fulfill({ json: { previewId: 'scope-review', systemId: 'system-a', allocationId: item.allocationId,
        expectedVersion: version, expiresAt: '2099-01-01T00:00:00Z', systems: [], requiresScopeReview: true,
        warnings: ['Existing approved boundary remains unchanged.'] } });
    });
    await context.route(`**${apiRoot}/*/scope-commit`, route => {
      const body = route.request().postDataJSON(); expect(body.acknowledgeImpact).toBe(true);
      const item = attachments.find(item => item.attachmentId === reviewing)!;
      item.version++; item.scope = { ...item.scope, version: item.scope.version + 1, reviewState: 'Reviewed',
        reviewedBy: 'Synthetic reviewer', reviewedAt: '2026-09-29T20:00:00Z' }; version++;
      return route.fulfill({ json: current() });
    });
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => {
      if (route.request().method() !== 'GET') profileWrites.push(route.request().postDataJSON());
      return route.fulfill({ json: { id: 'profile-a', sectionType: 'EnvironmentAndDeployment', governanceStatus: 'Draft',
        canEditProfile: true, draftContent: '{"hostingModel":"CSP-hosted","cloudProvider":"[\\"Azure Government\\"]","additionalDetails":"Saved description"}',
        userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    await page.goto(`${root}/profile/EnvironmentAndDeployment`);
    await expect(page.getByText('No subscriptions attached.', { exact: true })).toBeVisible();
    const description = page.getByLabel('Deployment description', { exact: true });
    await description.fill('Unsaved deployment changes must survive');
    // Act: attach all three subscriptions through one source-independent flow.
      await page.getByRole('button', { name: 'Attach subscription', exact: true }).click();
      const dialog = page.getByRole('dialog');
      for (let index = 0; index < 3; index++)
        await dialog.getByRole('checkbox', { name: new RegExp(`Mission subscription ${index + 1}`) }).check();
      await dialog.getByRole('button', { name: 'Select system resource scope', exact: true }).click();
      await expect(dialog.getByRole('checkbox', { name: 'Include Mission API', exact: true })).toHaveCount(3);
      for (const resource of await dialog.getByRole('checkbox', { name: 'Include Mission API', exact: true }).all()) {
        await expect(resource).not.toBeChecked(); await resource.check();
      }
      await dialog.getByRole('button', { name: 'Review and attach', exact: true }).click();
      await dialog.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.', exact: true }).check();
      await dialog.getByRole('button', { name: 'Attach selected subscriptions', exact: true }).click();
      await expect(dialog).toHaveCount(0);
    // Assert: one register, same shared provider identity, independent states and untouched documentation.
    const connected = page.getByRole('region', { name: 'System subscriptions', exact: true });
    await expect(connected.getByRole('table').getByRole('row')).toHaveCount(4);
    await expect(connected.getByText('Not enabled', { exact: true })).toHaveCount(3);
    await expect(description).toHaveValue('Unsaved deployment changes must survive');
    expect(applies).toHaveLength(3); expect(profileWrites).toEqual([]);
    expect(attachments.every(item => item.hostingAssignmentId === null)).toBe(true);
    await connected.getByRole('button', { name: 'Manage Mission subscription 1', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Review pending scope', exact: true }).click();
    const review = page.getByRole('dialog');
    await review.getByRole('button', { name: 'Continue to system scope', exact: true }).click();
    await expect(review.getByRole('checkbox', { name: 'Include Mission API', exact: true })).toBeChecked();
    await expect(review.getByRole('checkbox', { name: 'Include Mission API', exact: true })).toBeDisabled();
    await review.getByLabel('Reason for scope change', { exact: true }).fill('Reviewed exact mission resource inclusion.');
    await review.getByRole('button', { name: 'Review attachment', exact: true }).click();
    await review.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.', exact: true }).check();
    await review.getByRole('button', { name: 'Accept reviewed environment scope', exact: true }).click();
    await expect(review).toHaveCount(0);
    await expect(connected.getByText('Reviewed', { exact: true })).toHaveCount(1);
    expect(profileWrites).toEqual([]);
    await page.screenshot({ path: info.outputPath(`connected-environments-${width}.png`), fullPage: true });
    await page.reload();
    await expect(connected.getByRole('table').getByRole('row')).toHaveCount(4);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
