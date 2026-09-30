import { expect, test, type BrowserContext } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import type { PackageReadinessCheck, PackageReadinessRun, PackageReadinessWorkspace, PackagePurpose } from '../../src/api/packageReadiness';

const root = '/workspaces/organizations/org-a/systems/system-a';
const apiRoot = '/api/dashboard/systems/system-a/package-readiness';
async function setup(context: BrowserContext, baseURL: string) {
  await installSystemCapabilityFixture(context, baseURL);
  const stamp = '2026-09-29T14:00:00Z';
  let revision = 0, runCounter = 0, failedRead = false, unavailable = false;
  let delay: (() => void) | undefined, hold = false;
  const runs: { purpose: PackagePurpose; run: PackageReadinessRun; checks: PackageReadinessCheck[] }[] = [];
  const checks = (): PackageReadinessCheck[] => [
    { id: 'inventory', ruleId: 'inventory-v1', title: 'Complete system inventory', outcome: revision ? 'Passed' : 'Blocking',
      category: 'inventory', required: true, applicability: 'Applicable', why: 'The package needs traceable hardware and software records.',
      missingSource: revision ? null : 'No inventory records', sources: revision ? [{ kind: 'Inventory', recordId: 'item-a', revision: '1', contentHash: null, label: 'Mission inventory' }] : [],
      nextSteps: ['Record applicable components.', 'Verify boundary placement.', 'Return and recheck readiness.'],
      expectedRole: 'SystemOwner', recordedOwner: null,
      action: { canView: true, canEdit: true, path: 'security-capabilities/inventory', label: 'Open', reason: null } },
    { id: 'privacy', ruleId: 'privacy-v1', title: 'Privacy impact assessment', outcome: 'NotApplicable',
      category: 'privacy', required: false, applicability: 'NotApplicable', why: 'The recorded PTA determines no PIA is required.',
      missingSource: null, sources: [], nextSteps: [], expectedRole: null, recordedOwner: null,
      action: { canView: true, canEdit: false, path: 'legal', label: 'Open', reason: 'View the recorded determination.' } },
    { id: 'evidence', ruleId: 'evidence-v1', title: 'Review supporting evidence', outcome: unavailable ? 'Unavailable' : 'FollowUp',
      category: 'evidence', required: unavailable, applicability: 'Applicable', why: 'Evidence coverage supports assessment.',
      missingSource: unavailable ? 'Evidence source is unavailable' : null, sources: [], nextSteps: ['Review supporting records.'],
      expectedRole: 'ISSO', recordedOwner: null,
      action: { canView: true, canEdit: false, path: 'evidence', label: 'Open', reason: 'Your access is read-only.' } },
  ];
  const fingerprint = () => (revision === 0 ? 'a' : revision === 1 ? 'c' : 'd').repeat(64);
  const scope = (purpose: PackagePurpose) => ({ systemId: 'system-a', purpose, retainedContext: null, selectionHash: 'b'.repeat(64) });
  const decorate = (value: PackageReadinessRun): PackageReadinessRun => ({
    ...value, freshness: { state: value.sourceHash === fingerprint() ? 'Current' : 'Stale', checkedAt: stamp,
      currentSourceHash: fingerprint(), reason: value.sourceHash === fingerprint() ? null : 'Inventory records changed after this run.' },
  });
  const writes: { path: string; body: unknown }[] = [];
  const exports: { readinessRunId: string; expectedSourceHash: string }[] = [];
  await context.route(`**${apiRoot}{,/**,?*}`, async route => {
    const url = new URL(route.request().url());
    const purpose = (url.searchParams.get('purpose') ?? route.request().postDataJSON()?.purpose ?? 'Legacy') as PackagePurpose;
    if (failedRead) return route.fulfill({ status: 503, json: { error: 'Readiness store unavailable' } });
    if (route.request().method() === 'POST') {
      writes.push({ path: url.pathname, body: route.request().postDataJSON() });
      if (hold) await new Promise<void>(resolve => { delay = resolve; });
      const items = checks();
      const newRun: PackageReadinessRun = {
        id: `run-${++runCounter}`, outcome: items.some(item => item.outcome === 'Blocking' || item.required && item.outcome === 'Unavailable') ? 'Blocked' : 'Ready',
        startedAt: stamp, evaluatedAt: stamp, evaluatedBy: 'Fixture ISSM', sourceHash: fingerprint(), sourceHashAfter: fingerprint(), ruleVersion: 'v1',
        counts: { total: items.length, blocking: items.filter(c => c.outcome === 'Blocking').length,
          passed: items.filter(c => c.outcome === 'Passed').length, followUp: items.filter(c => c.outcome === 'FollowUp').length,
          notApplicable: 1, unavailable: unavailable ? 1 : 0, requiredUnavailable: unavailable ? 1 : 0 },
        recommendedCheckId: revision ? 'evidence' : 'inventory', failure: null,
        freshness: { state: 'Current', checkedAt: stamp, currentSourceHash: fingerprint(), reason: null },
      };
      runs.push({ purpose, run: newRun, checks: items });
      return route.fulfill({ json: { ...scope(purpose), run: newRun, checks: { items, totalCount: items.length, limit: 50, offset: 0 } } });
    }
    const eligible = runs.filter(item => item.purpose === purpose);
    const latest = eligible.at(-1);
    const match = url.pathname.match(/\/runs\/([^/]+)(?:\/checks\/([^/]+))?$/);
    if (match) {
      const selected = eligible.find(item => item.run.id === match[1]);
      if (!selected) return route.fulfill({ status: 404, json: { error: 'Wrong system or purpose run' } });
      return route.fulfill({ json: match[2] ? { ...scope(purpose), runId: selected.run.id, check: selected.checks.find(c => c.id === match[2]) }
        : { ...scope(purpose), run: decorate(selected.run), checks: { items: selected.checks, totalCount: selected.checks.length, limit: 50, offset: 0 } } });
    }
    if (url.pathname.endsWith('/runs')) return route.fulfill({ json: { ...scope(purpose),
      items: eligible.map(item => decorate(item.run)).reverse(), totalCount: eligible.length, limit: 20, offset: 0 } });
    const action = { canView: true, canEdit: false, path: 'documents', label: 'Open' as const, reason: 'View retained records.' };
    const workspace: PackageReadinessWorkspace = { ...scope(purpose),
      source: { state: 'Available', hash: fingerprint(), ruleVersion: 'v1', reason: null },
      latestRun: latest ? decorate(latest.run) : null,
      permissions: { canValidate: true, validateReason: null, canGenerate: !!latest && latest.run.outcome === 'Ready' && latest.run.sourceHash === fingerprint(),
        generateReason: latest?.run.outcome === 'Ready' && latest.run.sourceHash === fingerprint() ? null : 'A current valid run is required.' },
      rmf: { phase: 'Prepare', transitions: [], totalCount: 0 },
      progress: (['prepare', 'validate', 'export', 'emass', 'decision'] as const).map(id => ({
        id, state: id === 'export' ? 'Recorded' : id === 'emass' || id === 'decision' ? 'NotRecorded' : 'NotChecked',
        description: id === 'export' ? 'A historical export exists.' : id === 'emass' ? 'No eMASS receipt recorded; no live submission connector.'
          : id === 'decision' ? 'No AO decision recorded.' : 'Read the selected validation result.',
        totalCount: id === 'export' ? 1 : 0, records: [], action,
      })),
      documents: [{ kind: 'ssp', title: 'SSP', presence: 'Present', status: 'Draft', reviewState: 'Review pending',
        sourceState: 'Current working data', validationOutcome: null, recordCount: 1, records: [], action }],
    };
    return route.fulfill({ json: workspace });
  });
  await context.route('**/api/v1/systems/system-a/packages', route => {
    const body = route.request().postDataJSON(); exports.push(body);
    if (body.expectedSourceHash !== fingerprint()) return route.fulfill({ status: 409, json: { error: 'Sources changed. Recheck readiness.' } });
    return route.fulfill({ json: { systemId: 'system-a', purpose: body.purpose, packageId: 'package-a', status: 'Pending',
      message: 'Queued', readinessRunId: body.readinessRunId, sourceHash: body.expectedSourceHash } });
  });
  return { writes, exports, changeSource: () => { revision++; }, fail: (value: boolean) => { failedRead = value; },
    evidenceUnavailable: () => { unavailable = true; }, hold: () => { hold = true; }, release: () => { hold = false; delay?.(); } };
}

for (const width of [1440, 390]) {
  test(`purpose-bound readiness, source drawer and stale revalidation at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    const state = await setup(context, baseURL!);
    await page.goto(`${root}/documents`);
    // Assert: a document/export does not imply a run, submission or decision.
    await expect(page.getByText('Readiness not checked', { exact: true })).toBeVisible();
    await expect(page.getByText('Existing authorization package (legacy validation)', { exact: true })).toBeVisible();
    await expect(page.getByText('A historical export exists.', { exact: true })).toBeVisible();
    await expect(page.getByText(/No eMASS receipt recorded/)).toBeVisible();
    await expect(page.getByText('No AO decision recorded.', { exact: true })).toBeVisible();
    expect(state.writes).toEqual([]);
    // Act: purpose change is explicit and confirmed.
    await page.getByRole('button', { name: 'Change package purpose', exact: true }).click();
    await page.getByRole('combobox', { name: 'Package purpose', exact: true }).selectOption('InitialSubmission');
    await expect(page).not.toHaveURL(/purpose=InitialSubmission/);
    await page.getByRole('button', { name: 'Use selected purpose', exact: true }).click();
    await expect(page).toHaveURL(/purpose=InitialSubmission/);
    state.hold();
    await page.getByRole('button', { name: 'Check readiness', exact: true }).click();
    await expect(page.getByText('Checking package readiness…', { exact: true })).toBeVisible();
    await expect.poll(() => state.writes.length).toBe(1);
    state.release();
    await expect(page.getByText('Package needs work', { exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Blocking checks (1)', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Follow-up (1)', exact: true })).toBeVisible();
    await page.getByRole('tab', { name: 'All checks (3)', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Privacy impact assessment', exact: true })).toBeVisible();
    await expect(page.getByText(/Not applicable · The recorded PTA/)).toBeVisible();
    // Act: direct-link drawer, actual source action with retained return context.
    await page.getByRole('button', { name: 'Complete system inventory', exact: true }).click();
    const drawer = page.getByRole('dialog', { name: 'Complete system inventory', exact: true });
    await expect(drawer).toBeVisible();
    await expect(drawer.getByText('Responsible person: Not recorded', { exact: true })).toBeVisible();
    const bounds = (await drawer.boundingBox())!;
    expect(Math.abs(bounds.x + bounds.width - width)).toBeLessThan(2);
    const detailUrl = page.url();
    await page.screenshot({ path: info.outputPath(`readiness-drawer-${width}.png`) });
    const source = drawer.getByRole('link', { name: 'Open source workflow', exact: true }).first();
    await expect(source).toHaveAttribute('href', /readinessReturn=/);
    await page.goBack();
    await expect(drawer).toHaveCount(0);
    await page.goForward();
    await expect(drawer).toBeVisible();
    await page.goto(detailUrl);
    await expect(drawer).toBeVisible();
    await source.click();
    await expect(page).toHaveURL(/security-capabilities\/inventory\?readinessReturn=/);
    await expect(page.getByRole('link', { name: 'Return to package readiness', exact: true })).toBeVisible();
    // Simulate the independently tested source workflow updating its saved record.
    state.changeSource();
    await page.getByRole('link', { name: 'Return to package readiness', exact: true }).click();
    await expect(drawer).toBeVisible();
    await drawer.getByRole('button', { name: 'Close', exact: true }).click();
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.getByText('Readiness result is out of date', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Prepare validated export', exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Recheck readiness', exact: true }).click();
    await expect(page.getByText('Package ready for export', { exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Blocking checks (0)', exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Stale validation' })).toHaveCount(0);
    await expect(page.getByText('No AO decision recorded.', { exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath(`readiness-current-${width}.png`) });
    await page.getByRole('button', { name: 'Validation history', exact: true }).click();
    const history = page.getByRole('dialog', { name: 'Validation history', exact: true });
    await expect(history.getByRole('button', { name: /Blocked · Stale/ })).toBeVisible();
    await expect(history.getByRole('button', { name: /Ready · Current/ })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(history).toHaveCount(0);
    expect(state.writes).toHaveLength(2);
    expect(state.writes.every(write => (write.body as { purpose: string }).purpose === 'InitialSubmission')).toBe(true);
    expect(state.exports).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
  test(`failed and unavailable checks never appear ready at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    const state = await setup(context, baseURL!);
    state.fail(true);
    await page.goto(`${root}/documents?purpose=InitialSubmission`);
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByText('Package ready for export', { exact: true })).toHaveCount(0);
    // Act
    state.fail(false); state.evidenceUnavailable();
    await page.getByRole('button', { name: 'Retry readiness data', exact: true }).click();
    await page.getByRole('button', { name: 'Check readiness', exact: true }).click();
    // Assert
    await expect(page.getByRole('tab', { name: 'Blocking checks (2)', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Follow-up (0)', exact: true })).toBeVisible();
    await expect(page.getByText(/Unable to verify · Evidence source is unavailable/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Prepare validated export', exact: true })).toBeDisabled();
  });
  test(`export generation is bound to the selected run and rejects a source race at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    const state = await setup(context, baseURL!);
    state.changeSource();
    await page.goto(`${root}/documents?purpose=InitialSubmission`);
    await page.getByRole('button', { name: 'Check readiness', exact: true }).click();
    await expect(page.getByText('Package ready for export', { exact: true })).toBeVisible();
    // Act: open the explicit export confirmation, then change data before submission.
    await page.getByRole('button', { name: 'Prepare validated export', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Prepare validated export', exact: true });
    // The race is modeled by changing the evaluated state to a different fingerprint.
    state.changeSource();
    await dialog.getByRole('button', { name: 'Generate package', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    expect(state.exports).toHaveLength(1);
    expect(state.exports[0]).toMatchObject({ readinessRunId: 'run-1', expectedSourceHash: 'c'.repeat(64) });
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByText('Package ready for export', { exact: true })).toHaveCount(0);
    await expect(page).toHaveURL(/documents\?purpose=InitialSubmission/);
  });
}
