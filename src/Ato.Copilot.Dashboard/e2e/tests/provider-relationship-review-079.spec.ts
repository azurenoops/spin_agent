import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { allocationResponse } from '../../src/__tests__/provider-relationships/fixtures';
const root = '/workspaces/organizations/org-a/systems/system-a';

for (const width of [1440, 390]) {
  test(`copying description is not a scope review; recorded review updates the table at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange: synthetic service exercises actual UI; no demo relationship decisions.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!);
    let revision = 1, reviewed = false;
    const writes: { path: string; body: Record<string, unknown> }[] = [];
    const row = () => ({ ...allocationResponse, relationshipId: 'relationship-a', revision, assignmentRevision: 3,
      state: reviewed ? 'SeparateBoundaryConsumer' : 'Undetermined', reviewRequired: !reviewed, canAssociate: false,
      canReviewRelationship: true, canReviewCoveredScope: false,
      reviewedAt: reviewed ? '2026-09-28T13:00:00Z' : null, reviewedBy: reviewed ? 'reviewer-a' : null });
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => route.fulfill({ json: {
      id: 'profile-a', sectionType: 'EnvironmentAndDeployment', canEditProfile: true, governanceStatus: 'Draft',
      draftContent: '{"hostingModel":"CSP-hosted","cloudProvider":"[\\"Azure Government\\"]","additionalDetails":"Original description"}',
      userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/provider-relationships{,/**,?*}', route => {
      const path = new URL(route.request().url()).pathname;
      if (route.request().method() === 'GET') return route.fulfill({ json: { status: 'success', data: { items: [row()], page: 1, pageSize: 25, total: 1 } } });
      const body = route.request().postDataJSON();
      writes.push({ path, body });
      if (path.endsWith('/previews')) {
        expect(body).toMatchObject({ expectedRevision: revision, expectedAssignmentRevision: 3, relationshipState: 'SeparateBoundaryConsumer', evidence: [] });
        revision++;
        return route.fulfill({ json: { status: 'success', data: { previewId: 'preview-a', previewHash: 'hash-a', revision,
          contextSnapshotHash: 'context-a', blockers: [], canReview: true } } });
      }
      expect(path).toBe('/api/dashboard/systems/system-a/provider-relationships/relationship-a/review');
      expect(body).toMatchObject({ expectedRevision: revision, previewId: 'preview-a', previewHash: 'hash-a' });
      revision++; reviewed = true;
      return route.fulfill({ json: { status: 'success', data: row() } });
    });
    await context.route('**/api/workspaces/organizations/org-a/systems/system-a/security-capabilities?*', route => route.fulfill({ json: { data: {
      items: [], page: 1, pageSize: 10, total: 0, scope: 'applied', grouping: 'capability', boundaries: [],
      permissions: { canRead: true, canManage: false, canReviewResponsibilities: false, canManageEvidence: false, canAuthorNarratives: false, canReviewNarratives: false },
    } } }));
    // Act / Assert: prefill changes draft only; neither checkbox nor copy claims a review.
    await page.goto(`${root}/profile/EnvironmentAndDeployment`);
    const table = page.getByRole('table', { name: 'Associated provider scope' });
    await expect(table).toContainText('Not reviewed');
    await table.getByRole('button', { name: 'Open', exact: true }).click();
    await page.getByRole('button', { name: 'Copy hosting description to draft', exact: true }).click();
    await expect(page.getByRole('dialog')).toContainText('does not record a provider-relationship review');
    await page.getByRole('checkbox').check();
    await page.getByRole('button', { name: 'Use in draft', exact: true }).click();
    await expect(table).toContainText('Review required');
    expect(writes).toEqual([]);
    // Act / Assert: explicit determination, preview and confirmation write only the relationship.
    await table.getByRole('button', { name: 'Open', exact: true }).click();
    await page.getByRole('button', { name: 'Review provider relationship', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Review provider relationship', exact: true });
    await dialog.getByLabel('Relationship determination', { exact: true }).selectOption('SeparateBoundaryConsumer');
    await dialog.getByLabel('Review rationale', { exact: true }).fill('The mission maintains its own authorization boundary.');
    await dialog.getByRole('button', { name: 'Prepare review', exact: true }).click();
    await expect(dialog.getByRole('heading', { name: 'Confirm relationship review', exact: true })).toBeVisible();
    expect(writes).toHaveLength(1);
    await expect(dialog.getByRole('button', { name: 'Record relationship review', exact: true })).toBeDisabled();
    await dialog.getByRole('checkbox').check();
    await dialog.getByRole('button', { name: 'Record relationship review', exact: true }).click();
    await expect(table).toContainText('Separate boundary consumer');
    await expect(table.getByText('Reviewed', { exact: true })).toBeVisible();
    await expect(table).not.toContainText('Review required');
    expect(writes).toHaveLength(2);
    await page.reload();
    await expect(table.getByText('Reviewed', { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
