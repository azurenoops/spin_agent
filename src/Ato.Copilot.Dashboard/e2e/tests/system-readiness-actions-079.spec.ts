import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import { workspaceFixtureActor } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`Readiness layout and actions preserve the initial submission at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: real components, explicitly synthetic service records.
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    const checks: string[] = [];
    const nonGetRequests: { method: string; path: string }[] = [];
    page.on('request', request => {
      const url = new URL(request.url());
      if (url.pathname.startsWith('/api/') && request.method() !== 'GET') {
        nonGetRequests.push({ method: request.method(), path: `${url.pathname}${url.search}` });
      }
    });
    await context.route('**/api/dashboard/notifications/capabilities', route => route.fulfill({ json: {
      recipientId: workspaceFixtureActor, rest: { available: true, reasonCode: null },
      realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false, reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: [] },
      fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
    } }));
    await context.route('**/api/v1/systems/system-a/packages/validate*', route => {
      const purpose = new URL(route.request().url()).searchParams.get('purpose');
      checks.push(purpose ?? 'Legacy');
      return route.fulfill({ json: { purpose, isValid: false, errorCount: 1, warningCount: 0, validatedAt: '2026-09-27T12:00:00Z',
        findings: [{ severity: 'error', category: 'boundary', artifactType: null,
          description: 'Confirm the recorded boundary.', remediation: 'Review the included mission resources.' }] } });
    });
    await context.route('**/api/dashboard/systems/system-a/next-actions', route => route.fulfill({ json: {
      systemId: 'system-a', checkedAt: '2026-09-28T18:00:00Z', effectiveRoles: ['MissionOwner'],
      items: [{ id: 'boundary', title: 'Confirm the recorded boundary.', description: 'Review the included mission resources.',
        path: 'boundaries', actionLabel: 'Open', responsibleRole: 'MissionOwner' }], waitingOnOtherRoles: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/documents', route => route.fulfill({ json: {
      systemId: 'system-a', systemName: 'Synthetic Mission System', currentPhase: 'Prepare',
      ssp: { totalNarratives: 0, completedNarratives: 0, narrativeCompletionPct: 0 },
      sap: null, sar: null, authorization: null, poamCount: 0, poamOverdueCount: 0, hasBaseline: false, baselineControlCount: 0,
      pta: null, pia: null, interconnections: [], conMon: null, sspSections: [], activeWaiverCount: 0,
      narrativeGovernance: null, importHistory: [], inventoryItemCount: 0,
    } }));
    await context.route('**/api/dashboard/systems/system-a/exports?*', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/v1/systems/system-a/packages?*', route => route.fulfill({ json: { items: [], totalCount: 0, limit: 10, offset: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/documents/ssp/preview', route => route.fulfill({ json: {
      systemId: 'system-a', format: 'json', contentType: 'application/json', content: '{"system-security-plan":{"metadata":{"title":"Synthetic preview"}}}',
      contentHash: 'a'.repeat(64), generatedAt: '2026-09-27T12:00:00Z', sourceGaps: [], isPreview: true, sourceState: 'CurrentWorkingData',
    } }));

    // Act / Assert: the mock order, no duplicate root navigation, no fabricated count.
    await page.goto(root);
    const header = page.getByRole('heading', { name: 'A clear path to your ATO package', exact: true });
    await expect(header).toBeVisible();
    const tabs = page.getByRole('tablist', { name: 'System overview tasks' });
    const status = page.getByRole('region', { name: 'Initial submission readiness status' });
    await expect(page.getByRole('navigation', { name: 'System task views' })).toHaveCount(0);
    await expect(status).toContainText('Initial submission · Not checked');
    const headerBox = await header.boundingBox(), tabsBox = await tabs.boundingBox(), statusBox = await status.boundingBox();
    expect(headerBox!.y).toBeLessThan(tabsBox!.y);
    expect(tabsBox!.y).toBeLessThan(statusBox!.y);
    expect(checks).toEqual([]);
    await expect(page.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute('href', `${root}/documents/preview`);
    await page.screenshot({ path: info.outputPath(`readiness-initial-${width}.png`), fullPage: true });
    await page.getByRole('link', { name: 'Preview contribution' }).click();
    await expect(page).toHaveURL(`${baseURL}${root}/documents/preview`);
    await expect(page.getByRole('heading', { name: 'Preview the generated documents' })).toBeVisible();
    await page.goBack();
    await expect(status).toContainText('Initial submission · Not checked');
    await page.getByRole('button', { name: 'Check readiness' }).click();
    await expect(status).toContainText('1 blocking requirement remains');
    await expect(page.getByRole('link', { name: 'Continue preparation' })).toHaveAttribute('href', `${root}/boundaries`);
    await expect(page.getByRole('link', { name: 'Open: Confirm the recorded boundary.' })).toHaveAttribute('href', `${root}/boundaries`);
    expect(checks).toEqual(['InitialSubmission']);

    // Act / Assert: monitoring is a different URL state and history restores Readiness.
    await page.getByRole('tab', { name: 'Monitoring & follow-up' }).click();
    await expect(page).toHaveURL(`${baseURL}${root}?overview=monitoring`);
    await expect(page.getByRole('link', { name: 'Review monitoring' })).toHaveAttribute('href', `${root}/conmon`);
    await expect(page.getByRole('button', { name: 'Check readiness' })).toHaveCount(0);
    await page.goBack();
    await expect(status).toContainText('Initial submission · Not checked');
    await expect(page.getByRole('link', { name: 'Continue preparation' })).toHaveAttribute('href', `${root}/boundaries`);
    await page.getByRole('link', { name: 'View package readiness' }).click();
    await expect(page).toHaveURL(`${baseURL}${root}/documents?purpose=InitialSubmission`);
    await expect(page.getByRole('combobox', { name: 'Package purpose' })).toHaveValue('InitialSubmission');
    await page.getByRole('link', { name: 'Generate & export a package', exact: true }).click();
    await expect(page).toHaveURL(`${baseURL}${root}/documents?tab=exports&purpose=InitialSubmission`);
    await page.getByRole('button', { name: 'Generate Package', exact: true }).click();
    await expect(page.getByRole('dialog').getByRole('combobox', { name: 'Package purpose' })).toHaveValue('InitialSubmission');
    await expect(page.getByRole('dialog')).toContainText('Confirm the recorded boundary.');
    expect(checks).toEqual(['InitialSubmission', 'InitialSubmission']);
    expect(checks.every(purpose => purpose === 'InitialSubmission')).toBe(true);
    expect(nonGetRequests).toEqual(checks.map(() => ({
      method: 'POST', path: '/api/v1/systems/system-a/packages/validate?purpose=InitialSubmission',
    })));
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });

  test(`completed requirements give way to current tasks after save, return and focus at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: isolated server state changes, not browser completion flags.
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    let stage = 0;
    let unavailable = false;
    let savedBoundary = false;
    const actions = [
      { id: 'boundary', title: 'Define the mission boundary.', description: 'Record system scope.', path: 'boundaries' },
      { id: 'mission', title: 'Submit mission profile for review.', description: 'Submit your saved draft.', path: 'profile/MissionAndPurpose' },
      { id: 'data', title: 'Complete data profile.', description: 'Record mission information.', path: 'profile/DataTypes' },
      { id: 'ports', title: 'Complete network profile.', description: 'Record permitted communication.', path: 'profile/PortsProtocolsAndServices' },
    ];
    await context.route('**/api/dashboard/systems/system-a/next-actions', route => {
      if (unavailable) return route.fulfill({ status: 503, json: { error: 'Next actions temporarily unavailable' } });
      return route.fulfill({ json: { systemId: 'system-a', checkedAt: '2026-09-28T18:00:00Z', effectiveRoles: ['MissionOwner'],
        items: actions.slice(stage).map(item => ({ ...item, actionLabel: 'Open', responsibleRole: 'MissionOwner' })),
        waitingOnOtherRoles: [] } });
    });
    const boundary = { id: 'boundary-a', registeredSystemId: 'system-a', name: 'Mission boundary', boundaryType: 'Logical',
      description: 'Documented scope', isPrimary: true, componentCount: 0, resourceCount: 0, coveragePercent: 0 };
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => {
      if (route.request().method() === 'POST') {
        expect(route.request().postDataJSON().name).toBe('Mission boundary');
        savedBoundary = true; stage = 1;
        return route.fulfill({ json: boundary });
      }
      return route.fulfill({ json: { items: savedBoundary ? [boundary] : [], totalCount: savedBoundary ? 1 : 0 } });
    });
    // Act / Assert: server returns only the three highest-priority current destinations.
    await page.goto(root);
    await expect(page.getByRole('heading', { name: 'Define the mission boundary.', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Complete network profile.', exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Open: Submit mission profile for review.', exact: true }))
      .toHaveAttribute('href', `${root}/profile/MissionAndPurpose`);
    // Act: complete a requirement through its normal form.
    await page.getByRole('link', { name: 'Open: Define the mission boundary.', exact: true }).click();
    await page.getByRole('button', { name: 'Create boundary', exact: true }).click();
    const create = page.getByRole('dialog', { name: 'Create Boundary', exact: true });
    await create.getByRole('textbox', { name: 'Name *', exact: true }).fill('Mission boundary');
    await create.getByRole('button', { name: 'Create Boundary', exact: true }).click();
    await expect(page.getByRole('cell', { name: 'Mission boundary', exact: true })).toBeVisible();
    await page.goBack();
    // Assert: returning fetches persisted progress; the finished boundary action is not repeated.
    await expect(page.getByRole('heading', { name: 'Complete network profile.', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Define the mission boundary.', exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Continue preparation', exact: true }))
      .toHaveAttribute('href', `${root}/profile/MissionAndPurpose`);
    // Act: the owner's submission completes elsewhere while the overview is open.
    stage = 2;
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    // Assert
    await expect(page.getByRole('heading', { name: 'Submit mission profile for review.', exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Continue preparation', exact: true })).toHaveAttribute('href', `${root}/profile/DataTypes`);
    await page.screenshot({ path: info.outputPath(`current-next-actions-${width}.png`) });
    // Act: a failed refresh must not display old starter tasks or announce completion.
    unavailable = true;
    await page.getByRole('button', { name: 'Refresh my tasks', exact: true }).click();
    // Assert
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Complete data profile.', exact: true })).toHaveCount(0);
    await expect(page.getByText('No actions currently require your system roles.', { exact: true })).toHaveCount(0);
    // Act / Assert: the successful, empty server result advances to the checklist, not an ATO claim.
    unavailable = false; stage = 4;
    await page.getByRole('button', { name: 'Refresh my tasks', exact: true }).click();
    await expect(page.getByText('No actions currently require your system roles.', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Continue preparation', exact: true })).toHaveAttribute('href', `${root}/documents?purpose=InitialSubmission`);
  });
}
