import { expect, test, type BrowserContext } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { allocationResponse, capability } from '../../src/__tests__/provider-relationships/fixtures';
import type { SystemProviderScope } from '../../src/api/systemEnvironments';

const root = '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment';
const scope = (count: number): SystemProviderScope => ({
  assignmentId: 'assignment-a', assignmentVersion: 3, relationshipId: 'relationship-a',
  offeringId: 'offering-a', offeringName: 'Recorded offering', providerName: 'Recorded operator',
  hostingScopeRevisionId: 'hosting-pinned', hostingScopeRevision: 7, hostingScopeName: 'Recorded service scope',
  state: 'Active', relationshipState: 'SeparateBoundaryConsumer', reviewRequired: false,
  assignedScopes: [{ kind: 'Service', environment: 'ManualService', serviceId: 'service-a', serviceName: 'Recorded service', tenantReference: null }],
  exclusions: [], selectionVersion: 2,
  responsibilityReview: { state: 'Reviewed', canReview: true, canConfirm: false, reason: 'Canonical review recorded.' },
  publishedDuties: { state: 'Available', reason: null, totalCapabilities: count, capabilities: Array.from({ length: count }, (_, index) => ({
    capabilityId: `cap-${index}`, capabilityName: `Recorded capability ${index}`, description: `Full recorded description ${index}`,
    releaseId: `release-${index}`, releaseRevision: 4, releaseSnapshotHash: `hash-${index}`, contentHash: `content-${index}`,
    applicabilityContextId: `context-${index}`, providerControlIds: index % 3 === 0 ? ['SC-7'] : [],
    sharedControlIds: index % 3 === 1 ? ['SI-4'] : [], customerControlIds: index % 3 === 2 ? ['AC-2'] : [],
  })) },
});
async function install(context: BrowserContext, baseURL: string, item: SystemProviderScope) {
  await installWorkspaceFixture(context, baseURL);
  await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => route.fulfill({ json: {
    id: 'section', sectionType: 'EnvironmentAndDeployment', governanceStatus: 'Draft', canEditProfile: true,
    draftContent: '{"hostingModel":"Hybrid"}', userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
  } }));
  await context.route('**/api/dashboard/systems/system-a/environments', route => {
    if (route.request().method() !== 'GET') throw new Error('Unexpected live-shaped feature write in synthetic drawer inspection');
    return route.fulfill({ json: { systemId: 'system-a', version: 2,
      permissions: { canManageEnvironments: true, canCheckAccess: false, canRunAssessments: false,
        canManageMonitoring: false, canRegisterSubscriptions: false },
      attachments: [], legacyReferences: [], hostingLinks: [], providerScopes: [item] } });
  });
  await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [{ ...allocationResponse, relationshipId: item.relationshipId,
      assignmentId: item.assignmentId, offeringId: item.offeringId, assignmentRevision: 3, revision: 2,
      state: item.relationshipState, reviewRequired: item.reviewRequired, canReviewRelationship: true,
      reviewedAt: '2026-10-06T12:00:00Z', reviewedBy: 'Synthetic reviewer' }], page: 1, pageSize: 25, total: 1 },
  } }));
  const entries = item.publishedDuties!.capabilities.map(source => ({ ...capability,
    capabilityId: source.capabilityId, capabilityName: source.capabilityName, releaseId: source.releaseId,
    releaseSnapshotHash: source.releaseSnapshotHash, releaseRevision: source.releaseRevision,
    relationshipReviewRequired: false, outstandingDecisions: [], authorizationRelationship: 'SeparateBoundaryConsumer',
  }));
  await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => {
    const page = Number(new URL(route.request().url()).searchParams.get('page'));
    return route.fulfill({ json: { status: 'success', data: {
      items: entries.slice((page - 1) * 25, page * 25), page, pageSize: 25, total: entries.length,
    } } });
  });
}

for (const width of [1440, 390]) {
  test(`Canceled workflow navigation preserves the independent profile draft at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await install(context, baseURL!, scope(1));
    await page.goto(root);
    await page.getByRole('textbox', { name: 'Deployment description', exact: true }).fill('Unsaved independent profile draft');
    await page.getByRole('button', { name: 'Review Recorded offering', exact: true }).click();
    const drawer = page.getByRole('dialog', { name: 'Review provider offering' });
    await drawer.getByText("What's included · 1 capability", { exact: true }).click();
    // Act
    await drawer.getByRole('link', { name: 'Review applicability and capability adoption', exact: true }).click();
    // Assert
    await expect(page.getByRole('dialog', { name: 'Unsaved System definition changes' })).toBeVisible();
    // Act
    await page.getByRole('dialog', { name: 'Unsaved System definition changes' }).getByRole('button', { name: 'Keep editing' }).click();
    // Assert
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await expect(drawer).toBeVisible();
    await drawer.getByRole('button', { name: 'Close dialog' }).click();
    await expect(page.getByRole('textbox', { name: 'Deployment description', exact: true })).toHaveValue('Unsaved independent profile draft');
  });
  test(`Canceled same-tab navigation restores drawer focus at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await install(context, baseURL!, scope(1));
    await page.goto(root);
    await page.getByRole('button', { name: 'Review Recorded offering', exact: true }).click();
    const drawer = page.getByRole('dialog');
    await drawer.getByText("What's included · 1 capability", { exact: true }).click();
    await drawer.locator('summary').filter({ hasText: /^Remove provider relationship$/ }).click();
    await drawer.getByRole('textbox', { name: 'Removal rationale' }).fill('Retain local navigation input');
    const link = drawer.getByRole('link', { name: 'Review applicability and capability adoption', exact: true });
    // Act
    await link.click();
    await drawer.getByRole('button', { name: 'Keep editing', exact: true }).click();
    // Assert
    await expect(link).toBeFocused();
    await expect(drawer.getByRole('textbox', { name: 'Removal rationale' })).toHaveValue('Retain local navigation input');
    // Act
    await page.keyboard.press('Escape');
    // Assert
    await expect(drawer.getByRole('button', { name: 'Keep editing', exact: true })).toBeVisible();
    // Act
    await drawer.getByRole('button', { name: 'Discard and close', exact: true }).click();
  });
  for (const count of [0, 1, 31]) {
    test(`Focused offering ${count} capabilities preserves recorded outcomes and keyboard focus at ${width}px`, async ({ page, context, baseURL }) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await install(context, baseURL!, scope(count));
      // Act
      await page.goto(root);
      const opener = page.getByRole('button', { name: 'Review Recorded offering', exact: true });
      await opener.click();
      const drawer = page.getByRole('dialog', { name: 'Review provider offering' });
      // Assert
      await expect(drawer.getByRole('button', { name: 'Inspect recorded review', exact: true })).toBeEnabled();
      await expect(drawer.getByRole('heading', { name: 'Recorded offering' })).toBeVisible();
      await expect(drawer.getByText('Review recorded', { exact: true }).first()).toBeVisible();
      expect(await drawer.locator('details[open]').count()).toBe(0);
      await expect(drawer.getByRole('textbox')).toHaveCount(0);
      await expect(drawer.locator('a[target="_blank"]')).toHaveCount(0);
      expect(await drawer.locator('div.min-h-0.overflow-y-auto').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
      // Act
      await drawer.getByText(`What's included · ${count} ${count === 1 ? 'capability' : 'capabilities'}`, { exact: true }).click();
      // Assert
      if (count === 0) await expect(drawer.getByText('No capabilities are bound to this published scope.')).toBeVisible();
      else {
        await drawer.locator('summary').filter({ hasText: /^Recorded capability 0$/ }).click();
        await expect(drawer.getByText('Full recorded description 0')).toBeVisible();
        await expect(drawer.getByText('Provider control responsibilities:').first()).toBeVisible();
        if (count > 10) {
          await drawer.getByRole('button', { name: 'Next capabilities' }).click();
          await expect(drawer.locator('summary').filter({ hasText: /^Recorded capability 10$/ })).toBeVisible();
          await expect(drawer.locator('summary').filter({ hasText: /^Recorded capability 0$/ })).toHaveCount(0);
        }
      }
      await drawer.getByRole('button', { name: 'Close dialog' }).focus();
      // Act / Assert
      for (let index = 0; index < 14; index++) {
        await page.keyboard.press(index % 2 ? 'Shift+Tab' : 'Tab');
        expect(await drawer.evaluate(el => el.contains(document.activeElement))).toBe(true);
      }
      await page.keyboard.press('Escape');
      await expect(opener).toBeFocused();
    });
  }
  test(`Failed review save preserves rationale, guarded close and original source at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const item = { ...scope(1), reviewRequired: true, relationshipState: 'Undetermined',
      responsibilityReview: { state: 'NotAdopted' as const, canReview: true, canConfirm: false, reason: 'No adoption recorded.' } };
    await install(context, baseURL!, item);
    let writes = 0;
    await context.route('**/provider-relationships/relationship-a/previews', route => {
      writes++;
      return route.fulfill({ json: { status: 'success', data: { previewId: 'preview', previewHash: 'preview-hash',
        revision: 3, contextSnapshotHash: 'captured-context', blockers: [], canReview: true } } });
    });
    await context.route('**/provider-relationships/relationship-a/review', route => {
      writes++;
      return route.fulfill({ status: 409, json: { status: 'error', message: 'Source changed while saving.' } });
    });
    // Act
    await page.goto(root);
    await page.getByRole('button', { name: 'Review Recorded offering', exact: true }).click();
    const drawer = page.getByRole('dialog');
    await drawer.getByRole('button', { name: 'Review relationship', exact: true }).click();
    await drawer.getByRole('combobox', { name: 'Relationship determination' }).selectOption('SeparateBoundaryConsumer');
    await drawer.getByRole('textbox', { name: 'Review rationale' }).fill('Preserved synthetic rationale');
    await drawer.getByRole('button', { name: 'Prepare review', exact: true }).click();
    await drawer.getByRole('checkbox', { name: /confirm this determination/ }).check();
    await drawer.getByRole('button', { name: 'Record relationship review', exact: true }).click();
    // Assert
    await expect(drawer.getByRole('alert')).toContainText('Source changed while saving.');
    await expect(drawer.getByRole('textbox', { name: 'Review rationale' })).toHaveValue('Preserved synthetic rationale');
    expect(writes).toBe(2);
    // Act
    await page.keyboard.press('Escape');
    await drawer.getByRole('button', { name: 'Keep editing', exact: true }).click();
    await expect(drawer.getByRole('textbox', { name: 'Review rationale' })).toHaveValue('Preserved synthetic rationale');
    await drawer.getByRole('button', { name: 'Cancel', exact: true }).click();
    await drawer.getByRole('button', { name: 'Review relationship', exact: true }).click();
    await expect(drawer.getByRole('textbox', { name: 'Review rationale' })).toHaveValue('Preserved synthetic rationale');
    await page.keyboard.press('Escape');
    await drawer.getByRole('button', { name: 'Discard and close', exact: true }).click();
  });
  test(`Unavailable reads and changed source remain explicit and read-only at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const item = scope(2);
    await install(context, baseURL!, item);
    let reads = 0;
    await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => {
      if (++reads === 1) return route.fulfill({ status: 503, json: { status: 'error', message: 'Published source unavailable.' } });
      return route.fulfill({ json: { status: 'success', data: { page: 1, pageSize: 25, total: 2,
        items: item.publishedDuties!.capabilities.map(source => ({ ...capability,
          capabilityId: source.capabilityId, capabilityName: source.capabilityName, releaseId: `new-${source.releaseId}`,
          releaseRevision: 5, releaseSnapshotHash: `new-${source.releaseSnapshotHash}`,
          reasonCodes: ['RELEASE_SUPERSEDED'], applicabilityState: 'ReviewRequired', canProposeAdoption: false })),
      } } });
    });
    // Act
    await page.goto(root);
    await page.getByRole('button', { name: 'Review Recorded offering', exact: true }).click();
    const drawer = page.getByRole('dialog');
    // Assert
    await expect(drawer.getByRole('alert')).toContainText('Published source unavailable.');
    await expect(drawer.getByRole('button', { name: 'Retry review records', exact: true })).toBeEnabled();
    // Act
    await drawer.getByRole('button', { name: 'Retry review records', exact: true }).click();
    await drawer.getByRole('button', { name: 'Review changed source', exact: true }).click();
    // Assert
    const group = drawer.getByRole('region', { name: 'Shared source prerequisites' });
    await expect(group.getByText('A newer capability release is available')).toHaveCount(1);
    await expect(group).toContainText('Recorded capability 0, Recorded capability 1');
    await expect(drawer.getByRole('alert')).toContainText('selected releases have not been replaced');
    await expect(drawer.locator('summary').filter({ hasText: /^Raw applicability diagnostics$/ }).locator('..')).not.toHaveAttribute('open');
    // Act
    await drawer.getByText("What's included · 2 capabilities", { exact: true }).click();
    await drawer.locator('summary').filter({ hasText: /^Recorded capability 0$/ }).click();
    // Assert
    await expect(drawer.getByText('Published capability release: 4').first()).toBeVisible();
    await expect(drawer.getByText('Current published release 5 differs from this captured release.').first()).toBeVisible();
    await expect(drawer.getByRole('button', { name: 'Confirm responsibilities', exact: true })).toHaveCount(0);
    await page.keyboard.press('Escape');
  });
}
