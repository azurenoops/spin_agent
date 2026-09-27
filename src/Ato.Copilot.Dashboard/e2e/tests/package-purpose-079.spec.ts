import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import { workspaceFixtureActor } from '../fixtures/workspace-shell';

for (const width of [1440, 390]) {
  test(`explicit archive sources remain visible through package generation at ${width}px`, async ({ context, page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    const hash = 'a'.repeat(64);
    const decisionHash = 'b'.repeat(64);
    let generated = false;
    const selected = { baselinePackageId: 'baseline-a', baselineContentHash: hash, authorizationDecisionId: 'decision-a', expectedDecisionSnapshotHash: decisionHash };
    await context.route('**/api/dashboard/notifications/capabilities', route => route.fulfill({ json: {
      recipientId: workspaceFixtureActor, rest: { available: true, reasonCode: null },
      realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
        reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/package'] },
      fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
    } }));
    await context.route('**/api/dashboard/systems/system-a/documents', route => route.fulfill({ json: {
      systemId: 'system-a', systemName: 'Synthetic Mission System', currentPhase: 'Prepare',
      ssp: { totalNarratives: 1, completedNarratives: 1, narrativeCompletionPct: 100 }, sap: null, sar: null,
      authorization: null, poamCount: 0, poamOverdueCount: 0, hasBaseline: true, baselineControlCount: 1,
      pta: null, pia: null, interconnections: [], conMon: null, sspSections: [], activeWaiverCount: 0,
      narrativeGovernance: null, importHistory: [], inventoryItemCount: 0,
    } }));
    await context.route('**/api/dashboard/systems/system-a/exports?*', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/v1/systems/system-a/packages**', async route => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.pathname.endsWith('/context-options')) return route.fulfill({ json: {
        baselines: [{ id: 'baseline-a', purpose: 'InitialSubmission', generatedAt: '2026-09-01T00:00:00Z', contentHash: hash }],
        decisions: [{ id: 'decision-a', decisionType: 'ATO', decisionDate: '2026-09-02T00:00:00Z', issuer: 'DEMO recorded authority', snapshotHash: decisionHash }],
        previews: [],
      } });
      if (url.pathname.endsWith('/validate')) {
        const purpose = url.searchParams.get('purpose') ?? 'Legacy';
        if (purpose === 'AuthorizedBaselineArchive') expect(request.postDataJSON()).toEqual(selected);
        return route.fulfill({ json: {
          purpose, isValid: purpose === 'AuthorizedBaselineArchive', errorCount: purpose === 'Legacy' ? 1 : 0,
          sourceContextHash: 'c'.repeat(64),
          warningCount: 0, validatedAt: '2026-09-26T00:00:00Z', findings: purpose === 'Legacy'
            ? [{ severity: 'error', category: 'authorization-decision', description: 'Choose the applicable package purpose.', remediation: null }] : [],
        } });
      }
      if (request.method() === 'POST' && url.pathname.endsWith('/packages')) {
        expect(request.postDataJSON()).toMatchObject({ purpose: 'AuthorizedBaselineArchive',
          retainedContext: { ...selected, expectedSourceContextHash: 'c'.repeat(64) } });
        generated = true;
        return route.fulfill({ status: 202, json: { packageId: 'archive-a', status: 'Pending', purpose: 'AuthorizedBaselineArchive', message: 'Queued' } });
      }
      if (url.pathname.endsWith('/archive-a')) return route.fulfill({ json: {
        packageId: 'archive-a', systemId: 'system-a', purpose: 'AuthorizedBaselineArchive', status: 'Completed',
        evidenceMode: 'ManifestOnly', artifacts: [{ type: 'RetainedBaseline' }, { type: 'PackageContext' }],
        validation: { isValid: true, errorCount: 0, warningCount: 0, findings: [] }, fileSize: 100,
        failureReason: null, failedArtifactType: null, generatedBy: 'synthetic',
        generatedAt: '2026-09-26T00:00:00Z', completedAt: '2026-09-26T00:01:00Z', expiresAt: '2026-12-26T00:00:00Z',
      } });
      return route.fulfill({ json: { items: [], totalCount: 0, limit: 10, offset: 0 } });
    });
    // Act
    await page.goto('/workspaces/organizations/org-a/systems/system-a/documents?tab=exports');
    const trigger = page.getByRole('button', { name: 'Generate Package', exact: true });
    await trigger.click();
    const dialog = page.getByRole('dialog', { name: 'Generate Authorization Package' });
    await expect(dialog).toBeVisible();
    expect(await dialog.evaluate(element => element.matches(':modal'))).toBe(true);
    await dialog.getByRole('combobox', { name: 'Package purpose' }).selectOption('AuthorizedBaselineArchive');
    await dialog.getByRole('combobox', { name: 'Retained baseline package' }).selectOption('baseline-a');
    await dialog.getByRole('combobox', { name: 'Recorded authorization decision' }).selectOption('decision-a');
    await dialog.getByRole('button', { name: 'Generate Package', exact: true }).click();
    // Assert
    await expect(dialog.getByText('Package Generated Successfully')).toBeVisible();
    await expect(dialog.getByText('Retained baseline package', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Recorded decision and source manifest', { exact: true })).toBeVisible();
    expect(generated).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    await expect(trigger).toBeFocused();
  });
}
