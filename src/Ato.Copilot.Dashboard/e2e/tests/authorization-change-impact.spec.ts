import { expect, test, type BrowserContext } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering, receipt } from '../../src/__tests__/provider-authorizations/testData';
import { acceptedImpact, emptyImpactPage, impactContext, impactDetails, impactOptions, impactPreview, pendingImpact } from '../../src/__tests__/provider-authorizations/impactFixtures';
import type { ImpactInput } from '../../src/features/provider-authorizations/types';
import type { ImpactOptionKind } from '../../src/features/provider-authorizations/changeImpactApi';

async function installImpact(context: BrowserContext, unavailable = false) {
  const writes: { path: string; body: Record<string, unknown>; key?: string }[] = [];
  let selectedContext: ImpactInput | null = null;
  let decision = '';
  await context.route('**/api/csp/offerings/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
    if (request.method() === 'GET') {
      if (path.endsWith(`/offerings/${offering.offeringId}`)) return ok(offering);
      if (path.endsWith('/package-versions')) return ok({ ...emptyImpactPage, total: 1, items: [receipt.packageVersion] });
      if (path.endsWith('/impact-reviews')) return ok({ ...emptyImpactPage, total: selectedContext ? 2 : 1,
        items: [{ ...acceptedImpact, stale: true }, ...(selectedContext ? [{
          ...(decision ? acceptedImpact : pendingImpact), reviewId: 'impact-new',
        }] : [])] });
      if (path.endsWith('/details')) {
        if (unavailable) return route.fulfill({ status: 404, json: { status: 'error', error: { message: 'Synthetic detail service unavailable.' } } });
        const isNew = path.includes('/impact-new/');
        return ok({ ...impactDetails, context: isNew ? selectedContext : impactContext,
          review: isNew ? { ...(decision ? acceptedImpact : pendingImpact), reviewId: 'impact-new' } : { ...acceptedImpact, stale: true },
          rationale: decision ? 'Reviewed the affected logging capability and mission responsibilities.' : null });
      }
      if (path.endsWith('/impact-options')) {
        const kind = url.searchParams.get('kind') as ImpactOptionKind;
        const items = impactOptions[kind];
        if (items) return ok({ ...emptyImpactPage, items, total: items.length });
      }
      const option = path.match(/\/impact-options\/([^/]+)\/([^/]+)$/);
      if (option) {
        const items = impactOptions[option[1] as ImpactOptionKind];
        const item = items?.find(value => value.id === decodeURIComponent(option[2]));
        if (item) return ok(item);
      }
    } else {
      const body = request.postDataJSON();
      writes.push({ path, body, key: request.headers()['idempotency-key'] });
      if (path.endsWith('/impact-previews')) {
        selectedContext = body;
        return ok({ ...impactPreview, reviewId: 'impact-new' });
      }
      if (path.endsWith('/impact-reviews/impact-new/review')) {
        expect(body).toEqual({ expectedRevision: 2, previewId: impactPreview.previewId, previewHash: impactPreview.previewHash,
          disposition: 'AcceptForPublication', rationale: 'Reviewed the affected logging capability and mission responsibilities.' });
        decision = body.disposition;
        return ok({ ...acceptedImpact, reviewId: 'impact-new' });
      }
    }
    return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unconfigured synthetic impact route ${path}` } } });
  });
  return writes;
}

for (const width of [1440, 390]) {
  test(`change impact explains saved relationships and requires a new explicit review at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const writes = await installImpact(context);
    // Act
    await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/impact`);
    // Assert
    await expect(page.getByRole('heading', { name: 'Change impact', exact: true })).toBeVisible();
    await expect(page.getByText('Changes detected since this review', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Boundary revision ID', { exact: true })).toHaveCount(0);
    expect(writes).toHaveLength(0);
    // Act
    await page.getByRole('button', { name: 'Review changes', exact: true }).click();
    // Assert
    for (const name of ['Proposed change', 'Affected capabilities', 'Affected mission systems', 'Required action', 'Review outcome'])
      await expect(page.getByRole('region', { name, exact: true })).toBeVisible();
    await expect(page.getByText('Mission Alpha', { exact: true })).toBeVisible();
    await expect(page.getByText(acceptedImpact.contextSnapshotHash, { exact: true })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Save review decision' })).toHaveCount(0);
    expect(writes).toHaveLength(0);
    for (const name of ['Proposed change', 'Affected capabilities', 'Affected mission systems']) {
      const bounds = await page.getByRole('region', { name, exact: true }).boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
      if (width === 390) expect(bounds!.width).toBeGreaterThan(300);
    }
    await page.screenshot({ path: info.outputPath(`change-impact-${width}.png`), fullPage: true });
    // Act
    await page.getByRole('button', { name: 'Update impact review', exact: true }).click();
    await expect(page.getByRole('checkbox', { name: /^Logging coverage ·/ })).toBeChecked();
    await expect(page.getByRole('radio', { name: /^Government services ·/ })).toBeChecked();
    await expect(page.getByRole('checkbox', { name: /^Recorded ATO ·/ })).toBeChecked();
    await expect(page.getByRole('button', { name: 'Assess impact', exact: true })).toBeDisabled();
    expect(writes).toHaveLength(0);
    await page.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }).check();
    await page.getByRole('button', { name: 'Assess impact', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Record review decision' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save review decision' })).toBeDisabled();
    expect(writes).toHaveLength(1);
    expect(writes[0].body).toEqual(impactContext);
    expect(writes[0].key).toBeTruthy();
    await page.getByRole('combobox', { name: 'Review decision', exact: true }).selectOption('AcceptForPublication');
    await page.getByRole('textbox', { name: 'Review rationale', exact: true }).fill('Reviewed the affected logging capability and mission responsibilities.');
    await page.getByRole('button', { name: 'Save review decision', exact: true }).click();
    // Assert
    await expect(page.getByText('Review decision saved. No publication was performed.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(2);
    expect(writes.every(write => !write.path.includes('/publish'))).toBe(true);
  });
}

test('revised-package entry carries its exact package version and boundary without creating a review', async ({ context, page, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const writes = await installImpact(context);
  // Act
  await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/packages`);
  await page.getByRole('link', { name: 'Review changes', exact: true }).click();
  // Assert
  await expect(page).toHaveURL(/impact\?packageVersionId=version-1&packageId=package-1&boundaryRevisionId=boundary-1$/);
  await expect(page.getByRole('checkbox', { name: /^Revised logging package ·/ })).toBeChecked();
  await expect(page.getByRole('radio', { name: /^Government services ·/ })).toBeChecked();
  await expect(page.getByText(/Raw package claims do not by themselves establish affected coverage/)).toBeVisible();
  expect(writes).toHaveLength(0);
});

test('capability entry resolves the named saved change and never auto-approves it', async ({ context, page, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const writes = await installImpact(context);
  // Act
  await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/impact?capabilityId=capability-1`);
  // Assert
  await expect(page.getByRole('checkbox', { name: /^Logging coverage ·/ })).toBeChecked();
  await expect(page.getByRole('button', { name: 'Assess impact', exact: true })).toBeDisabled();
  expect(writes).toHaveLength(0);
});

test('an unavailable analysis is not displayed as no affected systems', async ({ context, page, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const writes = await installImpact(context, true);
  // Act
  await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/impact?reviewId=impact-1`);
  // Assert
  await expect(page.getByRole('alert')).toContainText('Impact analysis unavailable');
  await expect(page.getByText(/No conclusion about affected systems can be made/)).toBeVisible();
  await expect(page.getByRole('region', { name: 'Affected mission systems' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Save review decision' })).toHaveCount(0);
  expect(writes).toHaveLength(0);
});
