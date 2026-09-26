import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { candidate, packageStatus, page as results } from '../../src/__tests__/package-imports/fixtures';
import { boundary, offering, receipt as associatedReceipt } from '../../src/__tests__/provider-authorizations/testData';

for (const width of [1440, 390]) for (const scoped of [false, true]) test(`file-first authorization import at ${width}px (${scoped ? 'selected offering' : 'global'})`, async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  await page.setViewportSize({ width, height: 1000 });
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  let received = false;
  let mutations = 0;
  let associated = false;
  const receipt = { ...packageStatus(), association: null };
  await context.route('**/api/csp/inherited-components/import', async route => {
    expect(route.request().headers()['idempotency-key']).toBeTruthy();
    expect(route.request().postDataBuffer()?.toString()).toContain('scope.txt');
    received = true;
    await route.fulfill({ status: 202, json: { status: 'success', data: receipt } });
  });
  await context.route('**/api/csp/package-imports/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/association')) {
      expect(route.request().method()).toBe('POST');
      expect(route.request().postDataJSON()).toEqual({
        offeringId: offering.offeringId, boundaryRevisionId: boundary.boundaryRevisionId,
        expectedOfferingRevision: offering.revision, expectedPackageRevision: receipt.revision,
      });
      associated = true;
      return route.fulfill({ json: { status: 'success', data: associatedReceipt } });
    }
    return route.fulfill({ json: { status: 'success', data: path.endsWith('/candidates') ? results([candidate({
      type: 'BoundaryClaim', name: 'Sample Azure scope', claim: { boundary: {
        subject: 'Sample Azure offering', scope: 'Provider-managed test subscription only.', relationship: 'Included',
        environment: null, resourceIds: [], responsibilities: [], decisionReference: null,
      }, authorizationDecision: null, assessmentFinding: null, poamItem: null, fieldSources: [], relationships: [], sourceAliases: [], qualifications: [] },
    })]) : associated ? { ...receipt, association: associatedReceipt.package.association } : receipt } });
  });
  await context.route('**/api/csp/offerings**', route => {
    if (route.request().method() !== 'GET') mutations++;
    const path = new URL(route.request().url()).pathname;
    return route.fulfill({ json: { status: 'success', data: path.endsWith('/boundary-revisions')
      ? results([boundary]) : path.endsWith(`/${offering.offeringId}`) ? offering : results([offering]) } });
  });
  const importPath = scoped ? `/workspaces/csp/authorizations/offerings/${offering.offeringId}/import` : '/workspaces/csp/authorizations/import';
  // Act
  await page.goto('/workspaces/csp/authorizations');
  await page.getByRole('link', { name: scoped ? `Add package to this offering: ${offering.name}` : 'Import authorization package', exact: true }).click();
  // Assert
  await expect(page).toHaveURL(importPath);
  await expect(page.getByRole('heading', { name: 'Start with your authorization package' })).toBeVisible();
  const uploadBounds = await page.getByRole('region', { name: 'Import authorization package', exact: true }).boundingBox();
  expect(uploadBounds).not.toBeNull();
  expect(uploadBounds!.width).toBeGreaterThanOrEqual(Math.min(width - 64, 600));
  expect(uploadBounds!.x).toBeGreaterThanOrEqual(0);
  expect(uploadBounds!.x + uploadBounds!.width).toBeLessThanOrEqual(width);
  if (scoped) {
    await expect(page.getByRole('navigation', { name: 'Offering sections' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Back to offering' })).toHaveAttribute('href', `/workspaces/csp/authorizations/offerings/${offering.offeringId}`);
  }
  await expect(page.getByLabel('Offering', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Boundary revision', { exact: true })).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath(`import-${width}.png`), fullPage: true });
  // Act
  await page.getByLabel('Select source files').setInputFiles({ name: 'scope.txt', mimeType: 'text/plain', buffer: Buffer.from('Synthetic scope for testing.') });
  await page.getByRole('button', { name: 'Upload package', exact: true }).click();
  // Assert
  await expect(page).toHaveURL(`${importPath}?packageId=package-1`);
  await expect(page.getByRole('heading', { name: 'Review extracted scope' })).toBeVisible();
  expect(received).toBe(true);
  expect(associated).toBe(false);
  // Act
  await page.reload();
  await page.getByRole('button', { name: 'Use this scope as a starting point' }).click();
  const confirmationBounds = await page.getByRole('region', { name: 'Authorization package preparation', exact: true }).boundingBox();
  expect(confirmationBounds).not.toBeNull();
  expect(confirmationBounds!.width).toBeGreaterThanOrEqual(Math.min(width - 64, 600));
  expect(confirmationBounds!.x).toBeGreaterThanOrEqual(0);
  expect(confirmationBounds!.x + confirmationBounds!.width).toBeLessThanOrEqual(width);
  // Assert
  if (scoped) {
    await expect(page.getByLabel('Offering', { exact: true })).toHaveCount(0);
    await expect(page.getByLabel('Offering name', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('heading', { name: offering.name, level: 2 })).toBeVisible();
    await expect(page.getByLabel('Boundary revision')).toHaveValue('');
    await page.getByText('Prepare an explicit boundary', { exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Explicit scope statement', exact: true })).toHaveValue('Provider-managed test subscription only.');
  } else {
    await expect(page.getByRole('combobox', { name: 'Offering', exact: true })).toHaveValue('');
    await expect(page.getByLabel('Offering name', { exact: true })).toHaveValue('Sample Azure offering');
    await expect(page.getByLabel('Azure Government', { exact: true })).not.toBeChecked();
  }
  expect(mutations).toBe(0);
  expect(associated).toBe(false);
  await page.screenshot({ path: testInfo.outputPath(`confirm-${width}.png`), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  if (scoped) {
    // Act
    await page.getByLabel('Boundary revision').selectOption(boundary.boundaryRevisionId);
    await page.getByRole('button', { name: 'Associate retained package', exact: true }).click();
    // Assert
    await expect(page.getByRole('link', { name: 'Continue review in Authorizations' })).toHaveAttribute('href',
      `/workspaces/csp/authorizations/offerings/${offering.offeringId}/packages/package-1`);
    expect(associated).toBe(true);
    expect(mutations).toBe(0);
  }
});
