import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`formal complete SSP and dialog-only access context at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic API data exercises real components without changing demo records.
    await page.setViewportSize({ width, height: 1050 });
    await installWorkspaceFixture(context, baseURL!);
    const content = '{"accessOverview":"Recorded system-wide context","retainedKey":"preserve"}';
    let rows: Record<string, unknown>[] = [{ id: 'category-a', categoryName: 'Mission staff', description: 'Recorded population',
      approximateCount: 12, accessMethod: 'CAC/PIV', dataSensitivityLevel: 'CUI', sortOrder: 0, revision: 1, governanceStatus: 'Draft' }];
    const writes: { content: string; childItems: Record<string, unknown>[] }[] = [];
    await context.route('**/api/dashboard/systems/system-a/profile/UsersAndAccess', route => {
      if (route.request().method() === 'PUT') {
        const request = route.request().postDataJSON();
        writes.push(request);
        rows = request.childItems.map((row: Record<string, unknown>, index: number) => ({
          ...Object.fromEntries(Object.entries(row).filter(([key]) => key !== '_tempId')),
          id: row.id ?? `category-${index}`, revision: 1, governanceStatus: 'Draft',
        }));
      }
      return route.fulfill({ json: { id: 'profile-users', sectionType: 'UsersAndAccess', canEditProfile: true,
        governanceStatus: 'Draft', draftContent: content, userCategories: rows, dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
    });
    const source = {
      $schema: 'recorded-oscal-schema',
      'system-security-plan': {
        uuid: 'document-a', metadata: { title: 'Mission System Security Plan', version: '2.3' },
        'import-profile': { href: 'recorded-control-baseline' },
        'system-characteristics': { 'system-name': 'Mission System', description: 'Complete system description',
          'authorization-boundary': { description: 'Recorded production boundary' },
          props: [
            { name: 'working-profile', value: JSON.stringify({ sectionType: 'MissionAndPurpose', content: { scalarContent: '{"missionStatement":"Mission purpose is included"}' } }) },
            { name: 'working-profile', value: JSON.stringify({ sectionType: 'UsersAndAccess', content: { scalarContent: content, userCategories: rows } }) },
          ] },
        'system-implementation': { components: [{ uuid: 'component-a', title: 'Recorded Azure component' }],
          'inventory-items': [{ uuid: 'inventory-a', description: 'Recorded inventory resource' }] },
        'control-implementation': { 'implemented-requirements': Array.from({ length: 14 }, (_, index) => ({
          'control-id': `ac-${index + 1}`, description: `Complete implementation statement ${index + 1}`,
        })) },
        'back-matter': { resources: [{ uuid: 'evidence-a', title: 'Recorded evidence source', rlinks: [{ href: 'recorded-evidence-reference' }] }] },
        'future-section': { retained: 'Future contributor value', count: 0, enabled: false, unset: null },
      },
    };
    await context.route('**/api/dashboard/systems/system-a/documents/ssp/preview', route => {
      source['system-security-plan']['system-characteristics'].props[1]!.value = JSON.stringify({
        sectionType: 'UsersAndAccess', content: { scalarContent: content, userCategories: rows },
      });
      return route.fulfill({ json: {
      systemId: 'system-a', format: 'json', contentType: 'application/json', content: JSON.stringify(source),
      contentHash: 'source-hash', generatedAt: '2026-09-27T12:00:00Z', sourceGaps: [], isPreview: true,
      sourceState: 'CurrentWorkingData', canGenerate: false,
      sourceManifest: { scope: 'WorkingProfilePreview', previewOnly: true, profiles: [], providerSources: [], narratives: [], otherSources: 'Recorded sources' },
      } });
    });

    // Act / Assert: context is absent below the table; cancelling its dialog preserves category drafts.
    await page.goto(`${root}/profile/UsersAndAccess`);
    await expect(page.getByRole('heading', { name: 'Users & access', exact: true })).toBeVisible();
    await expect(page.locator('summary').filter({ hasText: /^Access context$/ })).toHaveCount(0);
    await expect(page.getByLabel('Access Overview', { exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: 'Add user category', exact: true }).click();
    await page.getByRole('dialog').getByLabel('Category', { exact: true }).fill('Pending category');
    await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    const manage = page.getByRole('button', { name: 'Manage access context', exact: true });
    await manage.click();
    await expect(page.getByRole('dialog', { name: 'System-wide access context', exact: true })).toBeVisible();
    await page.getByLabel('Access Overview', { exact: true }).fill('Cancel this context edit');
    await page.keyboard.press('Escape');
    await expect(manage).toBeFocused();
    await expect(page.getByRole('table', { name: 'User categories' })).toContainText('Pending category');
    expect(writes).toHaveLength(0);
    await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
    await expect(page.getByText('Users working data saved. This does not approve any category.', { exact: true })).toBeVisible();
    expect(JSON.parse(writes[0]!.content)).toEqual(JSON.parse(content));
    expect(writes[0]!.childItems).toHaveLength(2);

    // Act / Assert: the Users link highlights, but never filters, the complete formal document.
    await page.getByRole('link', { name: 'Preview contribution', exact: true }).click();
    await expect(page.getByRole('region', { name: 'SSP cover page', exact: true })).toContainText('WORKING DRAFT');
    await expect(page.getByRole('table', { name: 'Document control', exact: true })).toContainText('2.3');
    await expect(page.getByRole('heading', { name: 'Table of contents', exact: true })).toBeVisible();
    for (const title of ['Mission & purpose', 'Users & access']) await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
    const rendered = await page.locator('[data-ssp-value-path]').evaluateAll(nodes =>
      Object.fromEntries(nodes.map(node => [node.getAttribute('data-ssp-value-path')!, node.textContent])));
    let count = 0;
    const verify = (value: unknown, path: string) => {
      if (Array.isArray(value)) return value.forEach((item, index) => verify(item, `${path}/${index}`));
      if (value && typeof value === 'object') return Object.entries(value).forEach(([key, item]) =>
        verify(item, `${path}/${key.replace(/~/g, '~0').replace(/\//g, '~1')}`));
      count++;
      expect(rendered[path], path).toBe(value === null || value === '' ? 'Not recorded' : String(value));
    };
    verify(source, '');
    expect(Object.keys(rendered)).toHaveLength(count);
    await expect(page.getByText('Complete implementation statement 14', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Collapse long records', exact: true }).click();
    await expect(page.getByText('Complete implementation statement 14', { exact: true })).not.toBeVisible();
    await page.getByRole('button', { name: 'Expand all records', exact: true }).click();
    await expect(page.getByText('Complete implementation statement 14', { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole('region', { name: 'SSP cover page', exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`formal-complete-ssp-${width}.png`) });
  });
}
