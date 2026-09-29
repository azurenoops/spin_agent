import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { fixtureOffering, paged } from '../fixtures/provider-presentation-data';

const packageId = '11111111-1111-1111-1111-111111111111';
const artifactId = '22222222-2222-2222-2222-222222222222';
const base = '/workspaces/csp/security-capabilities/capability-a';
const customerDuty = 'Mission team enables and reviews application events. '.repeat(6).trim();
for (const width of [1440, 390]) {
  test(`implementation and responsibilities are distinct direct-link views at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: real SPA with explicit synthetic API contracts.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    let working = { capabilityId: 'capability-a', revision: 3, snapshotHash: 'snapshot-a', approvedRevision: 3,
      updatedAt: '2026-09-27', contributors: ['monitor'], controlDuties: { 'AU-2': 'Shared' },
      classification: 'Unclassified', serviceCategory: 'Audit', approvalState: 'Approved',
      approvedPreviewId: 'preview-a', approvedPreviewHash: 'preview-hash', approvedAt: '2026-09-27', approvedBy: 'Provider reviewer' };
    const writes: unknown[] = [];
    await context.route('**/api/csp/**', route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      const ok = (data: unknown) => route.fulfill({ json: { status: 'success', data } });
      if (path === '/api/csp/catalog/capabilities/capability-a') return ok({
        capability: { source: 'Provider', capabilityId: 'capability-a', componentId: 'source-parent', name: 'Audit collection',
          description: 'Collect platform audit events within the enrolled scope.', componentName: 'Source group', componentType: 'Service',
          lifecycle: 'Published', reviewState: 'Reviewed', sourceFormat: 'Package', sourceReference: `package:${packageId}/artifact:${artifactId}`,
          distinctAdoptionCount: 2, workingRevision: working.revision, releasedRevision: 3 },
        supportingComponents: [{ id: 'monitor', name: 'Azure Monitor Logs', description: 'Recorded Azure service component.', source: 'provider', componentType: 'Service' }],
        unresolvedContributorIds: [], mappedControlIds: ['AU-2'], implementationNarrative: null, sourceEvidenceReferences: null,
        sourceArtifacts: [{ componentId: 'source-parent', componentName: 'Source group', sourceFormat: 'Package', sourceFileName: 'service-scope.json',
          sourceReference: `package:${packageId}/artifact:${artifactId}` }],
      });
      if (path === '/api/csp/catalog/capabilities/capability-a/working-revision') {
        if (request.method() === 'PUT') { const body = request.postDataJSON(); writes.push(body); working = { ...working, ...body, revision: 4, approvalState: 'NotApproved' }; }
        return ok(working);
      }
      if (path === '/api/csp/catalog/capabilities/capability-a/subscribers') return ok(paged([]));
      if (path === `/api/csp/package-imports/${packageId}`) return ok({ packageId,
        association: { offeringId: fixtureOffering.offeringId, boundaryRevisionId: 'boundary-a', packageVersionId: 'source-version-a' } });
      if (path === `/api/csp/package-imports/${packageId}/candidates`) return ok(paged([
        { candidateId: 'duty-a', type: 'Responsibility', contributorIds: ['capability-a'], name: 'Audit collection — Customer duties',
          description: customerDuty, reviewState: 'Reviewed', revision: 2 },
        { candidateId: 'duty-unrelated', type: 'Responsibility', contributorIds: ['capability-b'], name: 'Unrelated duty',
          description: 'Do not show unrelated capability responsibility.', reviewState: 'Reviewed', revision: 1 },
      ]));
      if (path === `/api/csp/offerings/${fixtureOffering.offeringId}`) return ok(fixtureOffering);
      return route.fallback();
    });

    // Act: load each user-reported URL independently, not just client-side tab changes.
    await page.goto(base + '?tab=implementation');
    const implementation = page.getByRole('tabpanel', { name: 'Implementation', exact: true });
    await expect(implementation.getByRole('heading', { name: 'What this capability provides' })).toBeVisible();
    await expect(implementation.getByText('Azure Monitor Logs', { exact: true })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Offering sections' }).locator('[aria-current="page"]')).toHaveText('Capabilities & responsibilities');
    await expect(page.getByRole('table', { name: 'Control responsibility allocation' })).toHaveCount(0);
    await expect(page.getByLabel('Classification', { exact: true })).toHaveCount(0);
    await page.screenshot({ path: info.outputPath(`implementation-${width}.png`), fullPage: true });
    await page.goto(base + '?tab=responsibilities');
    const responsibilities = page.getByRole('tabpanel', { name: 'Coverage & duties', exact: true });

    // Assert
    await expect(responsibilities.getByRole('table', { name: 'Control responsibility allocation' })).toContainText('AU-2');
    const duty = responsibilities.getByText(customerDuty, { exact: true });
    await expect(duty).toBeVisible();
    expect(await duty.evaluate(element => element.getBoundingClientRect().right <= element.closest('section')!.getBoundingClientRect().right)).toBe(true);
    await expect(page.getByText('Do not show unrelated capability responsibility.')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Components that deliver this capability' })).toHaveCount(0);
    await expect(page.getByRole('tab', { name: 'Coverage & duties', exact: true })).toHaveAttribute('aria-selected', 'true');
    await page.screenshot({ path: info.outputPath(`responsibilities-${width}.png`), fullPage: true });

    // Act: focused editing retains exact concurrency data.
    await page.getByRole('button', { name: 'Edit working revision', exact: true }).click();
    const editor = page.getByRole('dialog', { name: 'Edit working revision', exact: true });
    await editor.getByLabel('Classification', { exact: true }).fill('CUI');
    await editor.getByRole('button', { name: 'Save working revision' }).click();
    await expect(editor).toHaveCount(0);
    expect(writes).toEqual([expect.objectContaining({ expectedRevision: 3, classification: 'CUI', controlDuties: { 'AU-2': 'Shared' } })]);
    await page.getByRole('tab', { name: 'Implementation', exact: true }).click();
    await expect(implementation).toBeVisible();
    await expect(responsibilities).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.goBack();
    await expect(responsibilities).toBeVisible();
    await expect(page.getByRole('link', { name: 'Back to offering capabilities' })).toHaveAttribute('href',
      `/workspaces/csp/authorizations/offerings/${fixtureOffering.offeringId}/inherited-coverage?task=capabilities`);
  });
}
