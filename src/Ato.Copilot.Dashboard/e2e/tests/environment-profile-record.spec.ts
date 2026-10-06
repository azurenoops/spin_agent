import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { EnvironmentChoice, SystemEnvironmentsResponse } from '../../src/api/systemEnvironments';
import { allocationResponse } from '../../src/__tests__/provider-relationships/fixtures';

const root = '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment';
const permissions = { canManageEnvironments: true, canCheckAccess: true, canRunAssessments: false,
  canManageMonitoring: false, canRegisterSubscriptions: false };
const choice: EnvironmentChoice = {
  choiceId: 'choice-a', source: 'OrganizationOwned',
  registration: { registrationId: 'registration-a', ownerTenantId: 'org-a', subscriptionId: 'subscription-a',
    directoryTenantId: 'directory-a', cloud: 'AzureUSGovernment', displayName: 'Synthetic organization subscription',
    status: 'Selected', lastVerifiedAt: '2026-10-06' },
  allocationId: null, allocationVersion: null, offeringId: null, offeringName: null,
  hostingScopeRevisionId: null, allocationState: null, startsAt: null, expiresAt: null,
  provenance: { source: 'ManualVerified', externalId: null, sourceRevision: null, reconciliationState: 'Verified',
    evidenceReference: null, recordedAt: '2026-10-06' }, eligible: true, ineligibleReason: null,
};
const environment: SystemEnvironmentsResponse = {
  systemId: 'system-a', version: 4, permissions, attachments: [], legacyReferences: [], hostingLinks: [],
  providerScopes: [{
    assignmentId: 'scope-a', assignmentVersion: 3, relationshipId: 'relationship-a', offeringId: 'offering-a',
    offeringName: 'Synthetic provider service', providerName: 'Synthetic Provider', hostingScopeRevisionId: 'release-a',
    providerId: 'provider-a', hostingScopeRevision: 12, hostingScopeName: 'Synthetic released scope',
    state: 'Active', relationshipState: 'Undetermined', reviewRequired: true, selectionVersion: 2,
    assignedScopes: [{ kind: 'Service', serviceId: 'service-a', serviceName: 'Synthetic service',
      environment: 'Microsoft365DoD', tenantReference: null }],
    responsibilityReview: { state: 'ReviewRequired', canReview: true, canConfirm: false, reason: 'Synthetic source changed.' },
  }],
};

for (const width of [1440, 390]) {
  test(`Environment unifies all draft fields and preserves independent connection actions at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!);
    await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [{ ...allocationResponse, relationshipId: 'relationship-a',
        assignmentId: 'scope-a', assignmentRevision: 3, canReviewRelationship: true }], page: 1, pageSize: 25, total: 1 },
    } }));
    await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [], page: 1, pageSize: 25, total: 0 },
    } }));
    let scalar = JSON.stringify({ hostingModel: 'Hybrid', additionalDetails: 'Saved synthetic deployment',
      networkZones: '["DMZ"]', rtoRpo: 'Recorded custom target', customSource: 'retain' });
    const writes: { path: string; body: unknown }[] = [];
    const resourceId = '/subscriptions/subscription-a/resourceGroups/mission/providers/Microsoft.Web/sites/api';
    let current = structuredClone(environment);
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', async route => {
      if (route.request().method() === 'PUT') {
        const input = route.request().postDataJSON();
        scalar = input.content; writes.push({ path: 'profile', body: input });
      }
      return route.fulfill({ json: { id: 'environment-section', sectionType: 'EnvironmentAndDeployment',
        governanceStatus: 'Draft', canEditProfile: true, draftContent: scalar, approvedContent: '{"additionalDetails":"Retained approved"}',
        userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    await context.route('**/api/dashboard/systems/system-a/environments**', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (path.endsWith('/choices')) return route.fulfill({ json: { ...current, choices: [choice], registrationHref: '' } });
      if (path.endsWith('/provider-scope-choices')) return route.fulfill({ json: { systemId: 'system-a', version: 4, canManage: true, choices: [] } });
      if (path.endsWith('/discover')) return route.fulfill({ json: { systemId: 'system-a', discoveryToken: 'synthetic-discovery',
        expiresAt: '2099-01-01', discoveredAt: '2026-10-06', selection: request.postDataJSON().selection,
        resources: [{ resourceId, name: 'Synthetic API', resourceType: 'Microsoft.Web/sites', resourceGroup: 'mission', location: null }] } });
      if (path.endsWith('/apply-batch')) {
        const input = request.postDataJSON();
        writes.push({ path: 'attach', body: input });
        current = { ...current, version: 5, attachments: [{
          attachmentId: 'attachment-a', systemId: 'system-a', version: 1, source: 'OrganizationOwned',
          registration: choice.registration, allocationId: null, allocationVersion: null, offeringId: null, offeringName: null,
          hostingAssignmentId: null, hostingReviewState: 'Undetermined', attachmentState: 'Attached',
          scope: { revisionId: 'scope-v1', version: 1, reviewState: 'PendingReview', resourceIds: [resourceId],
            exclusions: [], sharedDependencyResourceIds: [], discoveredAt: '2026-10-06' },
          assessmentAccess: { state: 'NotChecked', checkedAt: null, reason: null },
          monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
          monitoring: { configured: false, enabled: false, health: 'NotEvaluated', evaluatedAt: null, reason: null },
          readiness: { state: 'NotChecked', checkedAt: null, reason: null }, provenance: choice.provenance, updatedAt: '2026-10-06',
        }] };
        return route.fulfill({ json: current });
      }
      if (request.method() !== 'GET') throw new Error(`Unexpected synthetic mutation: ${request.method()} ${path}`);
      return route.fulfill({ json: current });
    });
    // Act
    await page.goto(root);
    const header = page.locator('header').filter({ has: page.getByRole('heading', { name: 'Environment & hosting', exact: true }) });
    const save = header.getByRole('button', { name: 'Save Draft', exact: true });
    const form = page.locator('form#system-profile-editor');
    const table = page.getByRole('table', { name: 'Provider services & scopes' });
    // Assert
    await expect(table).toBeVisible();
    await expect(table.getByRole('columnheader')).toHaveText(['Offering', 'System relationship', 'Responsibility review', 'Action']);
    await expect(table.getByRole('button')).toHaveCount(1);
    await expect(page.getByRole('region', { name: 'System subscriptions', exact: true }).getByRole('table')).toHaveCount(0);
    if (width === 390) {
      await expect(table.locator('.register-label').first()).toBeVisible();
      expect(await table.locator('tbody tr').evaluate(el => getComputedStyle(el).display)).toBe('grid');
    }
    await expect(save).toBeVisible();
    await expect(page.getByRole('button', { name: 'Submit for Review', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(1);
    await expect(page.locator('form')).toHaveCount(1);
    await expect(form.locator('details, form')).toHaveCount(0);
    await expect(save).toHaveAttribute('form', 'system-profile-editor');
    for (const name of ['Hosting model', 'Cloud environment', 'Network Zones', 'Geographic Locations',
      'Availability Tier', 'Disaster Recovery Strategy', 'RTO / RPO Targets', 'Maintenance Windows', 'Operating Systems']) {
      await expect(form.getByRole('combobox', { name, exact: true })).toBeVisible();
    }
    await expect(form.getByRole('textbox', { name: 'Deployment description', exact: true })).toBeVisible();
    expect(await table.evaluate(el => el.closest('form') === null)).toBe(true);
    expect(await page.getByRole('region', { name: 'System subscriptions', exact: true }).evaluate(el => el.closest('form') === null)).toBe(true);
    const support = page.getByRole('complementary', { name: 'Document contribution and next tasks' });
    const guidance = page.getByText(/Prepare for review: complete the applicable deployment/);
    expect((await support.boundingBox())!.y).toBeGreaterThan((await guidance.boundingBox())!.y + (await guidance.boundingBox())!.height);
    if (width === 1440) {
      const draft = page.getByRole('heading', { name: 'Deployment description', exact: true });
      expect(Math.abs((await support.boundingBox())!.y - (await draft.locator('..').locator('..').boundingBox())!.y)).toBeLessThan(2);
    }
    // Act
    await expect(page.getByLabel('RTO / RPO Targets')).toHaveValue('Recorded custom target');
    await page.getByLabel('Availability Tier', { exact: false }).selectOption('Best Effort');
    await page.getByRole('textbox', { name: 'Deployment description', exact: true }).fill('Unsaved synthetic deployment');
    await table.getByRole('button', { name: 'Review Synthetic provider service', exact: true }).click();
    await page.getByRole('dialog').getByText('Source details and prerequisites', { exact: true }).click();
    await page.getByRole('dialog').getByText('Source and technical metadata', { exact: true }).click();
    // Assert
    await expect(page.getByRole('dialog')).toContainText('Released scope revision: 12');
    await expect(page.getByRole('dialog')).toContainText('Assignment revision: 3 · Selection revision: 2');
    await expect(page.getByRole('dialog')).toContainText('Synthetic source changed.');
    await page.getByRole('button', { name: 'Close dialog' }).focus();
    await page.keyboard.press('Tab');
    expect(await page.getByRole('dialog').evaluate(el => el.contains(document.activeElement))).toBe(true);
    await page.keyboard.press('Shift+Tab');
    await expect(page.getByRole('button', { name: 'Close dialog' })).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    expect(await page.getByRole('dialog').evaluate(el => el.contains(document.activeElement))).toBe(true);
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Close dialog' })).toBeFocused();
    // Act
    await page.keyboard.press('Escape');
    // Assert
    await expect(table.getByRole('button', { name: 'Review Synthetic provider service', exact: true })).toBeFocused();
    await expect(page.getByRole('textbox', { name: 'Deployment description', exact: true })).toHaveValue('Unsaved synthetic deployment');
    expect(writes).toEqual([]);
    // Act
    await table.getByRole('button', { name: 'Review Synthetic provider service', exact: true }).click();
    await expect(page.getByRole('dialog')).toContainText('Optional subscription links');
    await page.getByRole('dialog').getByText('Optional subscription links', { exact: true }).first().click();
    await expect(page.getByRole('dialog')).toContainText('No subscriptions linked (optional).');
    await page.getByRole('dialog').getByRole('button', { name: 'Review relationship', exact: true }).click();
    await page.getByRole('combobox', { name: 'Relationship determination', exact: true }).selectOption('SeparateBoundaryConsumer');
    await page.getByRole('textbox', { name: 'Review rationale', exact: true }).fill('Retained local review draft');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await page.getByRole('button', { name: 'Keep editing', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Review rationale', exact: true })).toHaveValue('Retained local review draft');
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Review relationship', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Review rationale', exact: true })).toHaveValue('Retained local review draft');
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Discard and close', exact: true }).click();
    await page.getByRole('button', { name: 'Add provider scope', exact: true }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Attach subscription', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Attach subscriptions' })).toBeVisible();
    await page.keyboard.press('Escape');
    // Assert
    await expect(page.getByRole('button', { name: 'Attach subscription', exact: true })).toBeFocused();
    expect(writes).toEqual([]);
    // Act
    await save.click();
    await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
    await page.reload();
    // Assert
    await expect(page.getByRole('textbox', { name: 'Deployment description', exact: true })).toHaveValue('Unsaved synthetic deployment');
    expect(JSON.parse(scalar)).toMatchObject({ customSource: 'retain', rtoRpo: 'Recorded custom target', availabilityTier: 'Best Effort' });
    expect(writes.map(write => write.path)).toEqual(['profile']);
    // Act
    await page.getByRole('button', { name: 'Attach subscription', exact: true }).click();
    await page.getByRole('checkbox', { name: /Synthetic organization subscription/ }).check();
    await page.getByRole('button', { name: 'Select system resource scope', exact: true }).click();
    await expect(page.getByRole('checkbox', { name: 'Include Synthetic API' })).not.toBeChecked();
    await page.getByRole('checkbox', { name: 'Include Synthetic API' }).check();
    await page.getByRole('button', { name: 'Review and attach', exact: true }).click();
    await page.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.' }).check();
    await page.getByRole('button', { name: 'Attach selected subscriptions', exact: true }).click();
    // Assert
    const subscriptions = page.getByRole('table', { name: 'System subscriptions' });
    await expect(subscriptions).toContainText('subscription-a');
    await expect(subscriptions.getByRole('columnheader')).toHaveText([
      'Subscription name and identifier', 'System resource scope', 'Assessment-access status', 'Monitoring status', 'Action',
    ]);
    await expect(subscriptions.getByRole('button')).toHaveCount(1);
    await expect(subscriptions).toContainText('Pending Review');
    await expect(subscriptions).toContainText('Not Checked');
    await expect(subscriptions).toContainText('Not enabled');
    expect(writes.map(write => write.path)).toEqual(['profile', 'attach']);
    await page.getByRole('button', { name: 'Manage Synthetic organization subscription', exact: true }).click();
    await page.getByRole('dialog').getByText('Subscription source and technical details', { exact: true }).click();
    await expect(page.getByRole('dialog')).toContainText('ManualVerified');
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Check access', exact: true })).toBeEnabled();
    await expect(page.getByRole('dialog').getByRole('link', { name: 'View monitoring (opens in a new tab)', exact: true })).toHaveAttribute('target', '_blank');
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Manage system scope', exact: true })).toBeEnabled();
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Review pending scope', exact: true })).toBeEnabled();
    await page.keyboard.press('Escape');
    await page.getByRole('region', { name: 'Provider register' }).focus();
    await page.keyboard.press('ArrowRight');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const locked of ['UnderReview', 'Viewer'] as const) {
  test(`Environment preserves ${locked} profile locks and independent server permissions`, async ({ page, context, baseURL }) => {
    // Arrange
    await installWorkspaceFixture(context, baseURL!);
    const writes: string[] = [];
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => {
      if (route.request().method() !== 'GET') writes.push(route.request().method());
      return route.fulfill({ json: { id: 'environment-section', sectionType: 'EnvironmentAndDeployment',
        governanceStatus: locked === 'UnderReview' ? 'UnderReview' : 'Draft', canEditProfile: locked === 'UnderReview',
        draftContent: '{"hostingModel":"On-Premises","additionalDetails":"Retained read-only description"}',
        userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    await context.route('**/api/dashboard/systems/system-a/environments', route => {
      if (route.request().method() !== 'GET') writes.push(route.request().method());
      return route.fulfill({ json: { ...environment, permissions: { ...permissions, canManageEnvironments: locked === 'UnderReview' } } });
    });
    // Act
    await page.goto(root);
    await expect(page.getByRole('table', { name: 'Provider services & scopes' })).toBeVisible();
    // Assert
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Hosting model' })).toBeDisabled();
    await expect(page.getByRole('textbox', { name: 'Deployment description', exact: true })).toBeDisabled();
    const form = page.locator('form#system-profile-editor');
    await expect(form.locator('details, form')).toHaveCount(0);
    for (const name of ['Availability Tier', 'Disaster Recovery Strategy', 'RTO / RPO Targets', 'Maintenance Windows']) {
      await expect(form.getByRole('combobox', { name, exact: true })).toBeVisible();
      await expect(form.getByRole('combobox', { name, exact: true })).toBeDisabled();
    }

    for (const name of ['Cloud environment', 'Network Zones', 'Geographic Locations', 'Operating Systems']) {
      await expect(form.getByRole('combobox', { name, exact: true })).toBeVisible();
      await expect(form.getByRole('combobox', { name, exact: true })).toHaveAttribute('aria-disabled', 'true');
    }
    const attach = page.getByRole('button', { name: 'Attach subscription', exact: true });
    if (locked === 'UnderReview') await expect(attach).toBeEnabled();
    else await expect(attach).toBeDisabled();
    await page.getByRole('button', { name: 'Review Synthetic provider service', exact: true }).click();
    await page.getByRole('dialog').getByText('Source details and prerequisites', { exact: true }).click();
    await page.getByRole('dialog').getByText('Source and technical metadata', { exact: true }).click();
    await expect(page.getByRole('dialog')).toContainText('Released scope revision: 12');
    await page.keyboard.press('Escape');
    expect(writes).toEqual([]);
  });
}

for (const width of [1440, 390]) {
  test(`Focused provider review safely wraps long offering and release metadata at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!);
    const offeringName = `Offering-${'a'.repeat(200)}`;
    const providerName = `Provider-${'p'.repeat(180)}`;
    const sourceName = `Source-${'s'.repeat(260)}`;
    const releaseId = `release-${'r'.repeat(240)}`;
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => route.fulfill({ json: {
      id: 'environment-section', sectionType: 'EnvironmentAndDeployment', governanceStatus: 'Draft', canEditProfile: true,
      draftContent: '{"hostingModel":"Hybrid"}', userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/environments', route => route.fulfill({ json: {
      ...environment, providerScopes: [{ ...environment.providerScopes![0], offeringName, providerName,
        hostingScopeName: sourceName, hostingScopeRevisionId: releaseId }],
    } }));
    await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [], page: 1, pageSize: 25, total: 0 },
    } }));
    await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [], page: 1, pageSize: 25, total: 0 },
    } }));
    // Act
    await page.goto(root);
    const table = page.getByRole('table', { name: 'Provider services & scopes' });
    await expect(table).toBeVisible();
    // Assert
    await expect(table).not.toContainText(sourceName);
    await expect(table).not.toContainText(releaseId);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    // Act
    await table.getByRole('button', { name: `Review ${offeringName}`, exact: true }).click();
    const panel = page.getByRole('dialog', { name: 'Review provider offering' });
    await panel.getByText('Source details and prerequisites', { exact: true }).click();
    await panel.getByText('Source and technical metadata', { exact: true }).click();
    // Assert
    await expect(panel).toContainText(sourceName);
    await expect(panel).toContainText(releaseId);
    expect(await panel.locator('div.min-h-0.overflow-y-auto').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  });
}
