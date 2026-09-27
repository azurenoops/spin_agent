import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { installSystemCapabilityFixture, systemCapabilityRoot } from '../fixtures/system-capabilities';
import { workspaceFixtureActor } from '../fixtures/workspace-shell';

const systemRoot = '/workspaces/organizations/org-a/systems/system-a';

async function capture(page: Page, info: TestInfo, name: string) {
  const geometry = await page.evaluate(() => ({ viewport: innerWidth, document: document.documentElement.scrollWidth }));
  expect(geometry.document).toBeLessThanOrEqual(geometry.viewport);
  await expect(page.locator('main')).toHaveCount(1);
  await page.screenshot({ path: info.outputPath(`${name}.png`), fullPage: true });
}

for (const width of [1440, 390]) {
  test(`Systems task groups and source handoffs at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic API responses exercise production components, not backend persistence.
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    await context.route('**/api/dashboard/notifications/capabilities', route => route.fulfill({ json: {
      recipientId: workspaceFixtureActor, rest: { available: true, reasonCode: null },
      realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
        reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/notifications', '/hubs/package'] },
      fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
    } }));
    await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: { status: 'success', data: {
      items: [{
        relationshipId: 'relationship-a', revision: 4, assignmentId: 'allocation-a', assignmentRevision: 3,
        offeringId: 'offering-a', systemId: 'system-a', state: 'SeparateBoundaryConsumer', reviewRequired: false,
        authorizationRevisionId: 'authorization-revision-2', boundaryRevisionId: 'scope-revision-3', reviewedBy: 'Reviewer',
        reviewedAt: '2026-09-25T12:00:00Z', assignedScopes: [], offeringName: 'Mission hosting',
        providerName: 'Synthetic provider', systemName: 'Synthetic Mission System', hostingScopeName: 'Allocated production scope', canAssociate: false,
      }], page: 1, pageSize: 25, total: 1,
    } } }));
    await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => route.fulfill({ json: {
      id: 'environment-a', sectionType: 'EnvironmentAndDeployment', canEditProfile: true, governanceStatus: 'Draft',
      draftContent: '{}', userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/documents', route => route.fulfill({ json: {
      systemId: 'system-a', systemName: 'Synthetic Mission System', currentPhase: 'Prepare',
      ssp: { totalNarratives: 3, completedNarratives: 1, narrativeCompletionPct: 33 }, sap: null, sar: null,
      authorization: null, poamCount: 0, poamOverdueCount: 0, hasBaseline: true, baselineControlCount: 3,
      pta: null, pia: null, interconnections: [], conMon: null, sspSections: [], activeWaiverCount: 0,
      narrativeGovernance: null, importHistory: [], inventoryItemCount: 0,
    } }));
    await context.route('**/api/v1/systems/system-a/packages/validate*', route => route.fulfill({ json: {
      purpose: new URL(route.request().url()).searchParams.get('purpose') ?? 'Legacy',
      isValid: false, errorCount: 1, warningCount: 0, validatedAt: '2026-09-26T12:00:00Z',
      findings: [{ severity: 'Error', category: 'Sources', artifactType: 'SSP',
        description: 'Reviewed mission source is missing.', remediation: 'Review mission purpose.' }],
    } }));
    await context.route('**/api/v1/systems/system-a/packages?*', route => route.fulfill({ json: { items: [], totalCount: 0, limit: 10, offset: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/exports?*', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    const generatedContent = JSON.stringify({ 'system-security-plan': {
      metadata: { title: 'DEMO generated SSP' },
      'system-characteristics': { 'system-name': 'Synthetic Mission System', description: 'Retained generated mission description.' },
    } });
    await context.route('**/api/dashboard/systems/system-a/documents/ssp/preview', route => route.fulfill({ json: {
      systemId: 'system-a', format: 'json', contentType: 'application/json', content: generatedContent,
      contentHash: 'synthetic-content-hash', generatedAt: '2026-09-26T12:00:00Z',
      previewId: route.request().method() === 'POST' ? 'retained-preview-a' : null,
      sourceGaps: route.request().method() === 'POST' ? [] : [{ code: 'SOURCE_MISSING', message: 'Reviewed provider authority is missing.' }],
      isPreview: true, sourceState: 'CurrentWorkingData',
    } }));
    await context.route('**/api/dashboard/systems/system-a/exports', async route => {
      expect(route.request().postDataJSON()).toEqual({ format: 'json', sourcePreviewId: 'retained-preview-a' });
      expect(route.request().headers()['idempotency-key']).toBeTruthy();
      await route.fulfill({ status: 202, json: { exportId: 'retained-export-a', format: 'json', status: 'Pending',
        generatedBy: 'synthetic', generatedAt: '2026-09-26T12:00:00Z', fileSize: null, controlCount: null, completedAt: null, templateName: null } });
    });
    await context.route('**/api/dashboard/systems/system-a/exports/retained-export-a', route => route.fulfill({ json: {
      exportId: 'retained-export-a', systemId: 'system-a', sourcePreviewId: 'retained-preview-a', format: 'json', status: 'Completed',
      contentHash: 'synthetic-content-hash', generatedBy: 'synthetic', generatedAt: '2026-09-26T12:00:00Z',
      fileSize: 100, controlCount: 1, completedAt: '2026-09-26T12:01:00Z', templateName: null, expiresAt: '2026-12-26T00:00:00Z',
    } }));
    await context.route('**/api/dashboard/systems/system-a/exports/retained-export-a/download', route => {
      expect(route.request().headers()['x-workspace-tenant-id']).toBe('org-a');
      return route.fulfill({ contentType: 'application/json', body: generatedContent,
        headers: { 'Content-Disposition': 'attachment; filename="retained-ssp.json"' } });
    });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));

    // Act / Assert: all eight groups, unavailable tasks, and existing capability content.
    await page.goto(systemCapabilityRoot);
    await expect(page.getByRole('heading', { name: 'Security Capabilities', exact: true })).toBeVisible();
    if (width === 1440) {
      const search = await page.getByRole('searchbox', { name: 'Search this system' }).boundingBox();
      const sort = await page.getByRole('combobox', { name: 'Sort', exact: true }).boundingBox();
      expect(search).not.toBeNull();
      expect(sort).not.toBeNull();
      expect(Math.abs(search!.y - sort!.y)).toBeLessThan(5);
    }
    if (width === 390) await page.getByText('System navigation', { exact: true }).click();
    for (const group of ['Overview', 'System definition', 'Controls & evidence', 'Assessment & risk',
      'ATO package & eMASS', 'Continuous monitoring', 'Team & permissions', 'Activity & history']) {
      await expect(page.getByText(group, { exact: true }).filter({ visible: true }).first()).toBeVisible();
    }
    await expect(page.getByRole('link', { name: 'Document previews', exact: true }).first()).toHaveAttribute('href', `${systemRoot}/documents/preview`);
    if (width === 390) await page.getByText('System navigation', { exact: true }).click();
    await capture(page, info, 'applied-capabilities');
    await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Responsibilities', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Responsibility matrix' })).toBeVisible();
    const allocation = page.getByRole('link', { name: 'Review allocation for AU-2' });
    await allocation.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('region', { name: 'Subscription subscription-a' })).toBeFocused();
    await capture(page, info, 'responsibility-matrix');

    // Act / Assert: real environment form and authoritative provider context are composed together.
    await page.goto(`${systemRoot}/profile/EnvironmentAndDeployment`);
    await expect(page.getByRole('heading', { name: 'Provider hosting' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Provider hosting', exact: true }).getByText('Mission hosting', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Review hosting scope', exact: true })).toHaveAttribute('href', `${systemRoot}/profile/EnvironmentAndDeployment/hosting`);
    await capture(page, info, 'environment-hosting');

    // Act / Assert: validation calls the existing server contract; exports remain a separate task.
    await page.goto(`${systemRoot}/documents`);
    await page.getByRole('button', { name: 'Validate current package' }).click();
    await expect(page.getByText('Reviewed mission source is missing.', { exact: true })).toBeVisible();
    await capture(page, info, 'document-validation');
    await page.getByRole('combobox', { name: 'Package purpose' }).selectOption('InitialSubmission');
    await page.getByRole('button', { name: 'Validate current package' }).click();
    await expect(page.getByText('Reviewed mission source is missing.', { exact: true })).toBeVisible();
    await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Document previews', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'DEMO generated SSP' })).toBeVisible();
    await expect(page.getByText('Reviewed provider authority is missing.')).toBeVisible();
    await expect(page.getByText(/Current working data preview/)).toBeVisible();
    await capture(page, info, 'document-preview');
    await page.getByRole('button', { name: 'OSCAL source' }).click();
    await expect(page.getByLabel('Generated OSCAL JSON')).toHaveText(generatedContent);
    await page.getByRole('button', { name: 'Retain generated preview' }).click();
    await expect(page.getByRole('button', { name: 'Export retained OSCAL' })).toBeDisabled();
    await page.getByRole('checkbox', { name: 'I reviewed this exact retained preview and its source diagnostics.' }).check();
    await page.getByRole('button', { name: 'Export retained OSCAL' }).click();
    const dialog = page.getByRole('dialog', { name: 'Export SSP Document' });
    await expect(dialog).toContainText('retained-preview-a');
    await dialog.getByRole('button', { name: 'Export', exact: true }).click();
    const downloadEvent = page.waitForEvent('download');
    await dialog.getByRole('button', { name: 'Download OSCAL JSON (.json)' }).click();
    const download = await downloadEvent;
    expect(download.suggestedFilename()).toMatch(/\.json$/);
    await dialog.getByRole('button', { name: 'Close', exact: true }).first().click();
    await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Export packages', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Generate & export a package', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Generate Package', exact: true })).toBeVisible();
    await capture(page, info, 'export-packages');
    expect(errors).toEqual([]);
  });
}
