import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { boundary, offering } from '../../src/__tests__/provider-authorizations/testData';

for (const width of [1440, 390]) {
  test(`hosting setup presents tasks and preserves a stale reference draft at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    let revision = offering.revision;
    let attempts = 0;
    const writes: Record<string, unknown>[] = [];
    const scope = { offeringId: offering.offeringId, offeringRevision: revision,
      snapshot: { revisionId: 'hosting-current', revision: 1, snapshotHash: 'hidden-hosting-hash' },
      name: 'DoD platform hosting', predecessorRevisionId: null, impactReviewId: null,
      permittedScopes: [{ cloud: 'AzureUSGovernment', directoryTenantId: '11111111-1111-1111-1111-111111111111',
        subscriptionId: '22222222-2222-2222-2222-222222222222',
        resourceId: '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/platform' }],
      exclusions: [], citations: [] };
    await context.route('**/api/csp/offerings/**', async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const success = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      const paged = (items: unknown[]) => ({ items, page: 1, pageSize: 25, total: items.length });
      if (route.request().method() === 'POST' && path.endsWith('/authorization-records')) {
        attempts++;
        const input = route.request().postDataJSON();
        if (attempts === 1) {
          revision++;
          return route.fulfill({ status: 409, json: { status: 'error', error: { message: 'Offering revision changed' } } });
        }
        expect(input.expectedOfferingRevision).toBe(revision);
        expect(input.recordKind).toBe('InheritedMicrosoftReference');
        writes.push(input);
        return success({ ...input, recordId: 'reference-1', revisionId: 'reference-version-1', revision: 1,
          snapshotHash: 'reference-hash', metadataReviewState: 'Unconfirmed' });
      }
      if (path.endsWith(`/offerings/${offering.offeringId}`)) return success({ ...offering, revision, currentHostingScopeRevisionId: scope.snapshot.revisionId });
      if (path.endsWith('/hosting-scope-revisions/hosting-current')) return success(scope);
      if (path.endsWith('/hosting-scope-revisions')) return success(paged([scope]));
      if (path.endsWith('/authorization-records')) {
        expect(url.searchParams.get('recordKind')).toBe('InheritedMicrosoftReference');
        return success(paged([]));
      }
      if (path.endsWith(`/boundary-revisions/${boundary.boundaryRevisionId}`)) return success(boundary);
      if (path.endsWith('/boundary-revisions')) return success(paged([boundary]));
      if (path.endsWith('/boundary-overview')) return success({
        offeringId: offering.offeringId, offeringRevision: revision,
        capabilities: { ...paged([]), awaitingReview: 0, published: 0 }, missionSystems: paged([]),
      });
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unexpected test route ${path}` } } });
    });
    // Act
    await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/inherited-coverage`);
    const hosting = page.getByRole('region', { name: 'Azure hosting', exact: true });
    await expect(hosting).toContainText('DoD platform hosting');
    // Assert
    for (const name of ['Azure hosting', 'Microsoft authorization references', 'Security capabilities', 'Mission systems', 'Shared responsibilities'])
      await expect(page.getByRole('heading', { name, exact: true, level: 2 })).toBeVisible();
    await expect(hosting.getByRole('textbox')).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Microsoft authorization references', exact: true }).getByRole('textbox')).toHaveCount(0);
    await expect(page.getByText('hidden-hosting-hash', { exact: true })).toBeHidden();
    await expect(page.getByRole('region', { name: 'Suggested next step' }).getByRole('button')).toHaveCount(1);
    if (width < 640) {
      const bounds = await hosting.boundingBox();
      expect(bounds!.width).toBeGreaterThan(width - 80);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    }
    await page.screenshot({ path: info.outputPath(`hosting-${width}.png`), fullPage: true });
    // Act
    await hosting.getByRole('button', { name: 'Configure hosting', exact: true }).click();
    const hostingDialog = page.getByRole('dialog', { name: 'Configure Azure hosting', exact: true });
    await expect(hostingDialog).toBeVisible();
    expect(await hostingDialog.evaluate(element => element.matches(':modal'))).toBe(true);
    await expect(hostingDialog.getByLabel('Hosting scope name', { exact: true })).toHaveValue(scope.name);
    await expect(page.getByLabel('Customer tenant ID', { exact: true })).toHaveCount(0);
    const bounds = await hostingDialog.boundingBox();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    expect(await hostingDialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await hostingDialog.screenshot({ path: info.outputPath(`hosting-dialog-${width}.png`) });
    await page.keyboard.press('Escape');
    await expect(hostingDialog).toHaveCount(0);
    await expect(hosting.getByRole('button', { name: 'Configure hosting', exact: true })).toBeFocused();
    for (const [region, action, title] of [
      ['Security capabilities', 'Review capabilities', 'Review offering capabilities'],
      ['Mission systems', 'View associations', 'Mission system associations'],
      ['Shared responsibilities', 'Review responsibilities', 'Review responsibilities'],
    ]) {
      const trigger = page.getByRole('region', { name: region, exact: true }).getByRole('button', { name: action, exact: true });
      await trigger.click();
      const dialog = page.getByRole('dialog', { name: title, exact: true });
      await expect(dialog).toBeVisible();
      await dialog.getByRole('button', { name: 'Close dialog' }).click();
      await expect(dialog).toHaveCount(0);
      await expect(trigger).toBeFocused();
    }
    await page.getByRole('region', { name: 'Microsoft authorization references', exact: true }).getByRole('button', { name: 'Add reference', exact: true }).click();
    const referenceDialog = page.getByRole('dialog', { name: 'Add or review Microsoft references', exact: true });
    await expect(referenceDialog).toBeVisible();
    await page.getByRole('combobox', { name: 'Boundary revision', exact: true }).selectOption(boundary.boundaryRevisionId);
    await page.getByLabel('Reference', { exact: true }).fill('Synthetic Microsoft authorization letter');
    await page.getByLabel('Scope statement', { exact: true }).fill('Named platform services only.');
    await page.getByRole('button', { name: 'Save reference draft', exact: true }).click();
    await expect(page.getByText(/Stale revision. Inputs retained/)).toBeVisible();
    await page.getByRole('button', { name: 'Refresh current records', exact: true }).click();
    await page.getByRole('button', { name: 'Use refreshed revision with retained inputs', exact: true }).click();
    // Assert
    await expect(referenceDialog).toBeVisible();
    await expect(page.getByLabel('Reference', { exact: true })).toHaveValue('Synthetic Microsoft authorization letter');
    await page.getByRole('button', { name: 'Save reference draft', exact: true }).click();
    await expect.poll(() => writes.length).toBe(1);
    expect(writes[0].scopeStatement).toBe('Named platform services only.');
  });
}

test('a failed hosting service is unavailable, not an empty usable form', async ({ context, page, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  await context.route('**/api/csp/offerings/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith(`/offerings/${offering.offeringId}`))
      return route.fulfill({ json: { status: 'success', data: { ...offering, currentHostingScopeRevisionId: null } } });
    return route.fulfill({ status: 404, json: { status: 'error', error: { message: 'Required route unavailable' } } });
  });
  // Act
  await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/inherited-coverage`);
  // Assert
  const hosting = page.getByRole('region', { name: 'Azure hosting', exact: true });
  await expect(hosting.getByRole('alert')).toContainText('Azure hosting unavailable');
  await expect(hosting.getByRole('button', { name: 'Configure hosting', exact: true })).toBeDisabled();
  await expect(hosting.getByText('Not configured', { exact: true })).toHaveCount(0);
  await expect(hosting.getByRole('textbox')).toHaveCount(0);
});
