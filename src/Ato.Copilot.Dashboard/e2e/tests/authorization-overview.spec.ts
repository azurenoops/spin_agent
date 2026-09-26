import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering, boundary } from '../../src/__tests__/provider-authorizations/testData';
import { offeringOverview } from '../../src/__tests__/provider-authorizations/overviewFixtures';
import { candidate, page as paged, reviewState } from '../../src/__tests__/package-imports/fixtures';

for (const width of [1440, 390]) {
  test(`offering overview explains retained sources and opens guided tasks at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const data = offeringOverview();
    const errors: string[] = [];
    const writes: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await context.route('**/api/csp/offerings/**', async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const ok = (value: unknown) => route.fulfill({ json: { status: 'success', data: value } });
      if (route.request().method() !== 'GET') { writes.push(route.request().method() + ' ' + path); return route.abort(); }
      if (path.endsWith('/overview')) return ok(data);
      if (path.endsWith(`/offerings/${offering.offeringId}`)) return ok(offering);
      if (path.endsWith('/authorization-records')) return ok(paged([]));
      if (path.endsWith('/hosting-scope-revisions')) return ok(paged([]));
      if (path.endsWith('/boundary-revisions')) return ok(paged([boundary]));
      if (path.endsWith(`/boundary-revisions/${boundary.boundaryRevisionId}`)) return ok(boundary);
      if (path.endsWith('/boundary-overview')) return ok({
        offeringId: offering.offeringId, offeringRevision: offering.revision,
        capabilities: { ...paged([]), published: 0, awaitingReview: 0 }, missionSystems: paged([]),
      });
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unexpected fixture route ${path}` } } });
    });
    await context.route('**/api/csp/package-imports/**', async route => {
      const url = new URL(route.request().url());
      const ok = (value: unknown) => route.fulfill({ json: { status: 'success', data: value } });
      if (route.request().method() !== 'GET') { writes.push(route.request().method() + ' ' + url.pathname); return route.abort(); }
      if (url.pathname.endsWith('/package-1')) return ok({
        ...data.packages.items[0].package, processingState: 'ReadyForReview',
        association: { offeringId: offering.offeringId, packageVersionId: 'version-1', boundaryRevisionId: boundary.boundaryRevisionId },
      });
      if (url.pathname.endsWith('/review-state')) return ok(reviewState());
      if (url.pathname.endsWith('/entries')) return ok(paged([]));
      if (url.pathname.endsWith('/candidates')) {
        expect(url.searchParams.get('type')).toBe('AuthorizationDecisionClaim');
        return ok(paged([candidate({ type: 'AuthorizationDecisionClaim', name: 'Extracted authorization letter',
          claim: { authorizationDecision: { subjectKind: 'Provider', subject: 'Test service', reference: 'Test decision',
            authority: 'Example authority', decisionType: 'ATO', statusAsStated: 'Authorized', decisionDate: '2026-01-01',
            expirationDate: '2027-01-01', scope: 'Named provider services', conditions: [], exclusions: [] },
          boundary: null, assessmentFinding: null, poamItem: null, fieldSources: [], relationships: [], sourceAliases: [], qualifications: [] } })]));
      }
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unexpected fixture route ${url.pathname}` } } });
    });
    const home = `/workspaces/csp/authorizations/offerings/${offering.offeringId}`;
    // Act
    await page.goto(home);
    // Assert
    for (const name of ['Offering overview', 'Authorization', 'Package analysis', 'Security capabilities', 'Hosting and mission systems'])
      await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Authorization', exact: true })).toContainText('Not recorded');
    await expect(page.getByText('415 of 428 source segments analyzed')).toBeVisible();
    await expect(page.locator('[data-metric="Proposed"]')).toHaveText('86');
    await expect(page.getByRole('region', { name: 'Next action' }).getByRole('link')).toHaveCount(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`overview-${width}.png`), fullPage: true });
    // Act
    const trigger = page.getByRole('button', { name: 'Record authorization manually', exact: true });
    await trigger.click();
    const dialog = page.getByRole('dialog', { name: 'Record an existing authorization', exact: true });
    // Assert
    await expect(dialog).toBeVisible();
    expect(await dialog.evaluate(element => element.matches(':modal'))).toBe(true);
    await expect(dialog).toContainText('does not issue a new ATO');
    await expect(dialog.getByLabel('Reference', { exact: true })).toBeVisible();
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(trigger).toBeFocused();
    // Act
    await page.getByRole('region', { name: 'Security capabilities', exact: true }).getByRole('link', { name: 'Review capabilities' }).click();
    // Assert
    await expect(page.getByRole('dialog', { name: 'Review offering capabilities' })).toBeVisible();
    // Act
    await page.goto(home);
    await page.getByRole('link', { name: 'Review extracted authorization details' }).click();
    // Assert
    await expect(page).toHaveURL(`${baseURL}${home}/packages/package-1?type=AuthorizationDecisionClaim`);
    await expect(page.getByRole('combobox', { name: 'Candidate type' })).toHaveValue('AuthorizationDecisionClaim');
    await page.getByRole('button', { name: 'Review Extracted authorization letter' }).click();
    await expect(page.getByRole('region', { name: 'Source claim review' })).toContainText('Example authority');
    expect(writes).toEqual([]);
    expect(errors).toEqual([]);
  });
}
