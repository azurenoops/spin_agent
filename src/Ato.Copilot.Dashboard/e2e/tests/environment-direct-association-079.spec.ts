import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { allocationResponse } from '../../src/__tests__/provider-relationships/fixtures';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`associates a CSP scope from Environment with explicit confirmation at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic service records exercise the real page without modifying the demo.
    await page.setViewportSize({ width, height: 1050 });
    await installWorkspaceFixture(context, baseURL!);
    const rows = [
      { ...allocationResponse, assignmentId: 'allocation-a', offeringName: 'Azure shared services', providerName: 'Provider A', hostingScopeName: 'Production scope', canAssociate: true },
      { ...allocationResponse, assignmentId: 'allocation-b', offeringName: 'Restricted allocation', providerName: 'Provider B', canAssociate: false },
    ];
    let associated = false;
    let fail = true;
    let releaseRequest: (() => void) | undefined;
    const writes: { path: string; method: string; key?: string; body: unknown }[] = [];
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/') && request.method() !== 'GET') {
        writes.push({ path, method: request.method(), key: request.headers()['idempotency-key'], body: request.postDataJSON() });
      }
    });
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => route.fulfill({ json: {
      id: 'environment-a', sectionType: 'EnvironmentAndDeployment', canEditProfile: true, governanceStatus: 'Draft',
      draftContent: '{"hostingModel":"CSP-hosted","cloudProvider":"[\\"Azure Government\\"]","additionalDetails":"Saved description"}',
      userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/provider-relationships{,?*}', async route => {
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON();
        expect(body).toEqual({ assignmentId: 'allocation-a', expectedAssignmentRevision: allocationResponse.assignmentRevision });
        if (fail) { fail = false; return route.fulfill({ status: 503, json: { error: { message: 'Temporary connection failure' } } }); }
        await new Promise<void>(resolve => { releaseRequest = resolve; });
        associated = true;
        return route.fulfill({ json: { status: 'success', data: {
          relationshipId: 'relationship-a', assignmentId: 'allocation-a', revision: 1, state: 'Undetermined',
        } } });
      }
      return route.fulfill({ json: { status: 'success', data: { items: rows.map(row => row.assignmentId === 'allocation-a' && associated
        ? { ...row, relationshipId: 'relationship-a', revision: 1 } : row), page: 1, pageSize: 25, total: rows.length } } });
    });
    await context.route('**/api/workspaces/organizations/org-a/systems/system-a/security-capabilities?*', route => route.fulfill({ json: { data: {
      items: [], page: 1, pageSize: 10, total: 0, scope: 'applied', grouping: 'capability', boundaries: [],
      permissions: { canRead: true, canManage: false, canReviewResponsibilities: false, canManageEvidence: false, canAuthorNarratives: false, canReviewNarratives: false },
    } } }));
    await page.goto(`${root}/profile/EnvironmentAndDeployment`);
    const choose = page.getByRole('button', { name: 'Choose provider hosting', exact: true });
    await expect(page.getByText('Hosting association · Not associated', { exact: true })).toBeVisible();
    await expect(page.getByText('No provider scope associated', { exact: true })).toBeVisible();
    await expect(page.getByText('Azure shared services', { exact: false })).toHaveCount(0);
    const hosting = page.getByRole('region', { name: 'Provider hosting', exact: true });
    const network = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'Network zones & deployment locations' }) });
    const recovery = page.locator('details').filter({ has: page.locator('summary').filter({ hasText: 'Recovery, availability & operating details' }) });
    expect((await hosting.boundingBox())!.y).toBeLessThan((await network.boundingBox())!.y);
    expect((await network.boundingBox())!.y).toBeLessThan((await recovery.boundingBox())!.y);
    expect(await hosting.evaluate(node => node.nextElementSibling?.tagName)).toBe('DETAILS');
    await expect(page.getByRole('region', { name: 'Documentation & review', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Preview contribution', exact: true })).toBeVisible();
    await page.getByRole('textbox', { name: 'Deployment description', exact: true }).fill('Unsaved mission description');
    await choose.click();
    const available = page.getByRole('dialog', { name: 'Choose provider hosting', exact: true });
    const drawerBox = (await available.boundingBox())!;
    expect(Math.abs(drawerBox.x + drawerBox.width - width)).toBeLessThan(2);
    expect(drawerBox.y).toBe(0);
    await expect(available.getByRole('button', { name: 'Associate CSP scope', exact: true })).toHaveCount(1);
    await expect(available.getByText('Azure shared services', { exact: false })).toBeVisible();
    await expect(available.getByText('allocation-a', { exact: true })).not.toBeVisible();
    await available.getByText('Details', { exact: true }).first().click();
    await expect(available.getByText('allocation-a', { exact: true })).toBeVisible();
    const associate = available.getByRole('button', { name: 'Associate CSP scope', exact: true });
    // Act / Assert: opening/cancelling does not associate.
    await associate.click();
    let dialog = page.getByRole('dialog', { name: 'Associate CSP scope', exact: true });
    await expect(dialog.getByRole('button', { name: 'Confirm association', exact: true })).toBeDisabled();
    expect(writes).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(choose).toBeFocused();
    await choose.click();
    await associate.click();
    dialog = page.getByRole('dialog', { name: 'Associate CSP scope', exact: true });
    await dialog.getByRole('checkbox').check();
    await dialog.getByRole('button', { name: 'Confirm association', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText('Temporary connection failure');
    await dialog.getByRole('button', { name: 'Confirm association', exact: true }).click();
    await expect(dialog).toHaveAttribute('aria-busy', 'true');
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();
    await expect.poll(() => !!releaseRequest).toBe(true);
    releaseRequest!();
    // Assert: selected scope moves to associated table; no profile or capability write.
    await expect(page.getByText('Provider scope associated with this system.', { exact: false })).toBeVisible();
    await expect(page.getByRole('table', { name: 'Associated provider scope', exact: true })).toContainText('Azure shared services');
    await expect(page.getByText('Hosting association · 1 pending review', { exact: true })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Deployment description', exact: true })).toHaveValue('Unsaved mission description');
    expect(writes).toHaveLength(2);
    expect(writes.every(write => write.method === 'POST' && write.path === '/api/dashboard/systems/system-a/provider-relationships')).toBe(true);
    expect(writes[0]!.key).toBeTruthy();
    expect(writes[0]!.key).toBe(writes[1]!.key);
    await choose.click();
    await page.getByText('When and who associates a scope?', { exact: true }).click();
    await expect(page.getByText('After registration and the CSP allocation,', { exact: false })).toBeVisible();
    await page.keyboard.press('Escape');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`direct-scope-association-${width}.png`), fullPage: true });
  });
}
