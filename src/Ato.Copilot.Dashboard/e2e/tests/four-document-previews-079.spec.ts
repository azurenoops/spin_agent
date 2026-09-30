import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a/documents/preview';
const documents = {
  ssp: { 'system-security-plan': { uuid: 'ssp-a', metadata: { title: 'Recorded SSP' }, 'system-characteristics': { 'system-name': 'Mission', description: 'Recorded SSP description' } } },
  sap: { 'security-assessment-plan': { id: 'sap-a', title: 'Recorded SAP', status: 'Draft', scopeNotes: 'Recorded assessment scope',
    assessmentApproach: 'Examine, interview and test.', controlEntries: [{ controlId: 'AC-2', objectives: ['Review accounts'] }], teamMembers: [{ name: 'Assessment team member' }] } },
  sar: { 'security-assessment-report': { id: 'sar-a', title: 'Recorded SAR', status: 'Draft', satisfiedCount: 0,
    sections: [{ title: 'Assessment summary', content: 'Recorded assessment findings and conclusions.' }] } },
  poam: { 'poam-register': { systemId: 'system-a', systemName: 'Mission', itemCount: 1,
    items: [{ id: 'poam-a', weaknessName: 'Recorded weakness', status: 'Open', milestones: [{ description: 'Implement correction', completedAt: null }] }] } },
};
for (const width of [1440, 390]) {
  test(`SSP SAP SAR and POAM are distinct read-only previews at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: populated fixtures and a genuine missing-report state; no fabricated lifecycle operations.
    await page.setViewportSize({ width, height: 1050 });
    await installWorkspaceFixture(context, baseURL!);
    let missingSar = true;
    const writes: string[] = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/') && request.method() !== 'GET') writes.push(request.method());
    });
    await context.route('**/api/dashboard/systems/system-a/documents/*/preview', route => {
      const documentType = new URL(route.request().url()).pathname.split('/').at(-2) as keyof typeof documents;
      if (documentType === 'sar' && missingSar) return route.fulfill({ json: {
        systemId: 'system-a', systemName: 'Mission', documentType, available: false,
        reasonCode: 'SAR_NOT_FOUND', message: 'No saved security assessment report exists.',
      } });
      return route.fulfill({ json: {
        systemId: 'system-a', systemName: 'Mission', documentType, available: true,
        format: 'json', contentType: 'application/json', content: JSON.stringify(documents[documentType]),
        contentHash: `hash-${documentType}`, generatedAt: '2026-09-27T12:00:00Z', sourceGaps: [],
        isPreview: true, sourceState: 'CurrentWorkingData', canGenerate: false,
        documentStatus: documentType === 'poam' ? 'CurrentRegister' : 'Draft',
        sourceRecords: [{ kind: documentType, recordId: `${documentType}-a`, versionId: null, contentHash: `source-${documentType}` }],
      } });
    });
    await page.goto(root);
    await expect(page.getByRole('tablist', { name: 'Document types' }).getByRole('tab')).toHaveText(['SSP', 'SAP', 'SAR', 'POA&M']);
    for (const [documentType, label] of [['ssp', 'SSP'], ['sap', 'SAP'], ['sar', 'SAR'], ['poam', 'POA&M']] as const) {
      // Act
      const tab = page.getByRole('tab', { name: label, exact: true });
      await tab.click();
      // Assert
      await expect(tab).toHaveAttribute('aria-selected', 'true');
      await expect(tab).toBeFocused();
      await expect(page).toHaveURL(`${baseURL}${root}${documentType === 'ssp' ? '' : `?document=${documentType}`}`);
      if (documentType === 'sar') {
        await expect(page.getByText('No saved security assessment report exists.', { exact: true })).toBeVisible();
        await expect(page.getByRole('region', { name: 'SAR cover page', exact: true })).toHaveCount(0);
        missingSar = false;
        await page.getByRole('button', { name: 'Refresh availability', exact: true }).click();
      }
      const cover = page.getByRole('region', { name: `${label} cover page`, exact: true });
      await expect(cover).toBeVisible();
      await expect(page.getByRole('navigation', { name: `${label} document sections`, exact: true })).toBeVisible();
      const values = await page.locator('[data-document-value-path]').evaluateAll(nodes =>
        Object.fromEntries(nodes.map(node => [node.getAttribute('data-document-value-path')!, node.textContent])));
      let count = 0;
      const verify = (value: unknown, path: string) => {
        if (Array.isArray(value)) return value.forEach((item, index) => verify(item, `${path}/${index}`));
        if (value && typeof value === 'object') return Object.entries(value).forEach(([key, item]) =>
          verify(item, `${path}/${key.replace(/~/g, '~0').replace(/\//g, '~1')}`));
        count++;
        expect(values[path], path).toBe(value === null || value === '' ? 'Not recorded' : String(value));
      };
      verify(documents[documentType], '');
      expect(Object.keys(values)).toHaveLength(count);
      if (documentType !== 'ssp') {
        await expect(page.getByRole('button', { name: 'Retain generated preview', exact: true })).toHaveCount(0);
        await page.getByRole('button', { name: 'JSON source', exact: true }).click();
        expect(JSON.parse(await page.getByLabel('Document JSON').innerText())).toEqual(documents[documentType]);
        await page.getByRole('button', { name: `${label} document`, exact: true }).click();
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await cover.scrollIntoViewIfNeeded();
      await page.screenshot({ path: info.outputPath(`${documentType}-preview-${width}.png`) });
    }
    // Act / Assert: browser history and keyboard navigation remain scoped to this system.
    await page.goBack();
    await expect(page.getByRole('tab', { name: 'SAR', exact: true })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('tab', { name: 'SSP', exact: true }).click();
    await page.getByRole('tab', { name: 'SSP', exact: true }).press('ArrowRight');
    await expect(page.getByRole('tab', { name: 'SAP', exact: true })).toBeFocused();
    await page.getByRole('tab', { name: 'SAP', exact: true }).press('Enter');
    await expect(page.getByRole('tab', { name: 'SAP', exact: true })).toHaveAttribute('aria-selected', 'true');
    expect(writes).toEqual([]);
  });
}
