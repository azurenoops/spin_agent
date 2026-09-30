import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`saved Users contribution reaches fresh readable SSP at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic responses use the actual profile and preview components.
    await page.setViewportSize({ width, height: 1050 });
    await installWorkspaceFixture(context, baseURL!);
    let description = 'Original saved population.';
    let reads = 0;
    const category = () => ({ id: 'category-a', categoryName: 'Mission staff', description,
      approximateCount: 12, accessMethod: 'CAC/PIV', dataSensitivityLevel: 'CUI', sortOrder: 0,
      governanceStatus: 'Draft', revision: 1, pendingDeletion: false });
    const profile = () => ({ id: 'profile-a', sectionType: 'UsersAndAccess', canEditProfile: true,
      governanceStatus: 'Draft', reviewScope: 'AccessContext', draftContent: '{"accessOverview":"Saved access context."}',
      userCategories: [category()], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] });
    await context.route('**/api/dashboard/systems/system-a/profile/UsersAndAccess', route => {
      if (route.request().method() === 'PUT') description = route.request().postDataJSON().childItems[0].description;
      return route.fulfill({ json: profile() });
    });
    await context.route('**/api/dashboard/systems/system-a/documents/ssp/preview', route => {
      reads++;
      return route.fulfill({ json: {
        systemId: 'system-a', format: 'json', contentType: 'application/json',
        content: JSON.stringify({ 'system-security-plan': {
          metadata: { title: 'Generated Mission SSP' }, 'system-characteristics': {
            'system-name': 'Synthetic Mission System', description: 'Registration description remains separate.',
            props: [{ name: 'working-profile', ns: 'https://ato-copilot.io/ns/profile', value: JSON.stringify({
              sectionType: 'UsersAndAccess', sourceState: 'CurrentWorkingData', governanceStatus: 'Draft', reviewScope: 'AccessContext',
              content: { scalarContent: profile().draftContent, userCategories: [category()] },
            }) }],
          },
        } }),
        contentHash: `generated-${reads}`, generatedAt: '2026-09-27T12:00:00Z',
        sourceGaps: [], isPreview: true, sourceState: 'CurrentWorkingData', canGenerate: false,
        previewId: route.request().method() === 'POST' ? 'retained-working-preview' : null,
        sourceManifest: { scope: 'WorkingProfilePreview', previewOnly: true, profiles: [],
          providerSources: [], narratives: [], otherSources: 'Saved working sources' },
      } });
    });
    // Act: change and save a population, then follow the actual contribution action.
    await page.goto(`${root}/profile/UsersAndAccess`);
    await page.getByRole('button', { name: 'Open user category Mission staff' }).click();
    await page.getByRole('button', { name: 'Edit category', exact: true }).click();
    await page.getByLabel('Description', { exact: true }).fill('Updated population saved before preview.');
    await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Users working data saved. This does not approve any category.', { exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Preview contribution', exact: true }).click();
    // Assert: inspect generated content, not a separately assembled browser export.
    await expect(page).toHaveURL(`${baseURL}${root}/documents/preview?contribution=UsersAndAccess`);
    await expect(page.getByRole('heading', { name: 'Users & access', exact: true })).toBeVisible();
    await expect(page.getByText('Updated population saved before preview.', { exact: true })).toBeVisible();
    await expect(page.getByText('Saved access context.', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Review source mapping' })).toHaveAttribute('href', `${root}/profile/UsersAndAccess`);
    // Act / Assert: another saved revision is fetched by Refresh; old text disappears.
    description = 'New saved revision fetched by refresh.';
    await page.getByRole('button', { name: 'Refresh preview', exact: true }).click();
    await expect(page.getByText(description, { exact: true })).toBeVisible();
    await expect(page.getByText('Updated population saved before preview.', { exact: true })).toHaveCount(0);
    await page.screenshot({ path: info.outputPath(`users-ssp-contribution-${width}.png`), fullPage: true });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    // Act / Assert: retaining a working preview cannot authorize final export.
    await page.getByRole('button', { name: 'Retain generated preview', exact: true }).click();
    await page.getByRole('checkbox', { name: 'I reviewed this exact retained preview and its source diagnostics.' }).check();
    await expect(page.getByRole('button', { name: 'Export retained OSCAL', exact: true })).toBeDisabled();
    await expect(page.getByText(/Working previews are review-only/)).toBeVisible();
  });
}
