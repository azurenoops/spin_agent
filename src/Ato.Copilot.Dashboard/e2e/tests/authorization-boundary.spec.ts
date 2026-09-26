import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { boundary, offering } from '../../src/__tests__/provider-authorizations/testData';
import type { OfferingBoundaryOverview } from '../../src/features/provider-authorizations/types';

for (const width of [1440, 390]) {
  test(`boundary explains scope and next steps before immutable version history at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    let currentOffering = { ...offering, name: 'Azure IL5' };
    let currentBoundary = { ...boundary, name: 'Microsoft 365 DoD Tenant (Synthetic)',
      scopeStatement: 'Tenant identity and federation; Exchange Online DoD mailboxes; Teams DoD collaboration.',
      services: ['Exchange Online DoD', 'Teams DoD'], providerResponsibilities: ['Provide tenant audit collection.'],
      customerResponsibilities: ['Configure mission access policies.'],
    };
    const originalBoundary = structuredClone(currentBoundary);
    const writes: unknown[] = [];
    const overview: OfferingBoundaryOverview = {
      offeringId: offering.offeringId, offeringRevision: offering.revision,
      capabilities: { page: 1, pageSize: 10, total: 2, awaitingReview: 1, published: 1, items: [
        { capabilityId: null, candidateId: 'proposal-1', packageId: 'package-1', name: 'Source-stated identity protection',
          reviewState: 'NeedsReview', publicationState: 'Unpublished', releaseId: null, boundaryRevisionId: boundary.boundaryRevisionId },
        { capabilityId: 'capability-1', candidateId: null, packageId: null, name: 'Published audit collection',
          reviewState: 'Reviewed', publicationState: 'Published', releaseId: 'release-1', boundaryRevisionId: boundary.boundaryRevisionId },
      ] },
      missionSystems: { page: 1, pageSize: 10, total: 1, items: [{
        assignmentId: 'assignment-1', systemId: 'system-1', systemName: null, relationshipState: 'Undetermined',
        associated: false, adoptedCapabilityCount: 0, assignedScopes: [],
      }] },
    };
    await context.route('**/api/csp/offerings/**', async route => {
      const path = new URL(route.request().url()).pathname;
      const success = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (route.request().method() === 'POST' && path.endsWith('/boundary-revisions')) {
        const data = route.request().postDataJSON();
        expect(data.expectedOfferingRevision).toBe(currentOffering.revision);
        expect(data.predecessorRevisionId).toBe(currentBoundary.boundaryRevisionId);
        writes.push(data);
        currentOffering = { ...currentOffering, revision: currentOffering.revision + 1, currentBoundaryRevisionId: 'boundary-2' };
        currentBoundary = { ...currentBoundary, ...data, boundaryRevisionId: 'boundary-2',
          version: 2, snapshotHash: 'new-snapshot-hash', offeringRevision: currentOffering.revision };
        return success(currentBoundary);
      }
      if (route.request().method() === 'GET') {
        if (path.endsWith(`/offerings/${offering.offeringId}`)) return success(currentOffering);
        if (path.endsWith('/boundary-overview')) return success({ ...overview, offeringRevision: currentOffering.revision });
        if (path.endsWith('/boundary-revisions')) return success({
          items: writes.length ? [currentBoundary, originalBoundary] : [originalBoundary], page: 1, pageSize: 25, total: writes.length ? 2 : 1,
        });
        if (path.endsWith(`/boundary-revisions/${currentBoundary.boundaryRevisionId}`)) return success(currentBoundary);
      }
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unconfigured synthetic boundary route ${path}` } } });
    });
    // Act
    await page.goto(`/workspaces/csp/authorizations/offerings/${offering.offeringId}/boundary`);
    // Assert
    const summary = page.getByRole('region', { name: 'Authorization boundary', exact: true });
    await expect(summary).toContainText('Microsoft 365 DoD Tenant (Synthetic)');
    await expect(page.getByRole('list', { name: 'Offering workflow' }).getByRole('listitem')).toHaveCount(4);
    await expect(page.getByRole('list', { name: 'Recorded scope statements' }).getByRole('listitem')).toHaveCount(3);
    await expect(page.getByRole('heading', { name: 'Included services and resources', exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Security capabilities' })).toContainText('Published audit collection');
    await expect(page.getByRole('region', { name: 'Mission systems' })).toContainText('Mission Owner association pending');
    await expect(page.getByText(originalBoundary.snapshotHash, { exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Edit boundary', exact: true })).toHaveCount(1);
    await expect(page.getByText('Prepare boundary revision', { exact: true })).toHaveCount(0);
    expect(writes).toHaveLength(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    if (width < 640) {
      const card = await summary.boundingBox();
      expect(card).not.toBeNull();
      expect(card!.width).toBeGreaterThan(width - 80);
      expect(card!.x).toBeGreaterThanOrEqual(0);
      expect(card!.x + card!.width).toBeLessThanOrEqual(width);
    }
    await page.screenshot({ path: info.outputPath(`boundary-${width}.png`), fullPage: true });
    // Act
    await page.getByRole('button', { name: 'Edit boundary', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Edit boundary', exact: true });
    await expect(dialog).toBeVisible();
    expect(await dialog.evaluate(element => element.matches(':modal'))).toBe(true);
    await dialog.getByRole('button', { name: 'Close dialog' }).focus();
    await page.keyboard.press('Shift+Tab');
    await expect(dialog.locator(':focus')).toHaveCount(1);
    const dialogBounds = await dialog.boundingBox();
    expect(dialogBounds!.x).toBeGreaterThanOrEqual(0);
    expect(dialogBounds!.x + dialogBounds!.width).toBeLessThanOrEqual(width);
    expect(dialogBounds!.height).toBeLessThanOrEqual(900);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await dialog.screenshot({ path: info.outputPath(`boundary-dialog-${width}.png`) });
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Edit boundary', exact: true })).toBeFocused();
    expect(writes).toHaveLength(0);
    await page.getByRole('button', { name: 'Edit boundary', exact: true }).click();
    await expect(page.getByLabel('Boundary name', { exact: true })).toHaveValue(originalBoundary.name);
    await expect(page.getByLabel(/^Explicit scope statement/)).toHaveValue(originalBoundary.scopeStatement);
    await page.getByLabel('Boundary name', { exact: true }).fill('Confirmed synthetic service boundary');
    await page.getByRole('button', { name: 'Save boundary revision', exact: true }).click();
    // Assert
    await expect(dialog).toHaveCount(0);
    await expect(summary).toContainText('Confirmed synthetic service boundary');
    expect(writes).toHaveLength(1);
    // Act
    await page.getByText('Version history', { exact: true }).click();
    // Assert
    await expect(page.getByText(originalBoundary.snapshotHash, { exact: true })).toBeVisible();
    await expect(page.getByText('new-snapshot-hash', { exact: true })).toBeVisible();
    await expect(page.getByText(/Version 1 .*Microsoft 365 DoD Tenant/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit boundary', exact: true })).toHaveCount(1);
    expect(originalBoundary.name).toBe('Microsoft 365 DoD Tenant (Synthetic)');
  });
}
