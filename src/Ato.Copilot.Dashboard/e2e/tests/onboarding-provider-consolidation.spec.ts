import { expect, test, type BrowserContext } from '@playwright/test';
import { createHash } from 'node:crypto';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { setupResult, setupState } from '../../src/__tests__/provider-authorizations/testData';
import { packageStatus } from '../../src/__tests__/package-imports/fixtures';
import type { ProviderScreen, SetupState, UploadIntent, UploadIntentInput } from '../../src/features/csp-onboarding/providerSetupApi';

const provider = '11111111-1111-4111-8111-111111111111';
const draftId = '22222222-2222-4222-8222-222222222222';
const actor = '33333333-3333-4333-8333-333333333333';
const intentId = '44444444-4444-4444-8444-444444444444';
const source = Buffer.from('Synthetic provider source. No real authorization or sensitive content.');
const headings: Record<ProviderScreen, string> = {
  'p-details': 'Identify your provider', 'p-access': 'Confirm provider access',
  'p-offering': 'Add your first service offering', 'p-sources': 'Add source material',
  'p-uncertain': 'Check the package receipt', 'p-review': 'Review provider setup',
  'p-ready': 'Your provider workspace is ready',
};
function fixtureState(screen: ProviderScreen): SetupState {
  const state = setupState(screen);
  state.providerId = provider;
  state.profile.cspProfileId = provider;
  state.draft!.draftId = draftId;
  state.access.actor = { directoryTenantId: provider, objectId: actor, displayName: 'Synthetic provider administrator' };
  state.handling.environmentLabel = 'Synthetic local test environment';
  if (screen === 'p-ready') {
    state.profile.onboardingState = 'Active'; state.profile.currentStep = 'Complete';
    state.draft!.completion = { completedAt: '2026-09-30T12:00:00Z', profileRevision: 1, draftRevision: 1 };
  }
  if (screen === 'p-uncertain') {
    state.uploadIntents = [pendingIntent()];
    state.draft!.fields.sources = { choice: 'Intents', intentIds: [intentId] };
  }
  state.facts = [{ actionId: `provider:${provider}:setup:sources`, label: 'Add provider source material', state: 'Deferred',
    ownerRole: 'CSP.Admin', reasonCode: 'EXPLICIT_DEFERRAL', reason: 'Sources will be reviewed later',
    destination: { path: '/workspaces/csp/authorizations/import', label: 'Review in portal' },
    contribution: 'Retain source provenance for reviewed service documentation.' }];
  return state;
}
function pendingIntent(input?: UploadIntentInput): UploadIntent {
  return { intentId: input?.intentId ?? intentId, intentHash: 'A'.repeat(64), revision: 1, savedAt: '2026-09-30T12:00:00Z',
    input: input ?? { intentId, schemaVersion: 1, packageName: 'Synthetic source package', entryPoint: 'Onboarding',
      associationMode: 'Unassociated', offeringHintId: null, context: null,
      files: [{ ordinal: 0, fileName: 'synthetic-provider.txt', mediaType: 'text/plain', byteLength: source.length,
        sha256: createHash('sha256').update(source).digest('hex').toUpperCase() }],
      handlingPolicyVersion: 'test-1',
      declaredContent: { classification: 'Unclassified', markings: [], containsOnlySyntheticData: true } },
    receipt: null, reconciliation: { outcome: 'NotObserved', observedAt: '2026-09-30T12:00:00Z', nextAction: 'ReselectSameFiles' } };
}

async function installProvider(context: BrowserContext, baseURL: string, initial: ProviderScreen) {
  const workspaceRequests = await installWorkspaceFixture(context, baseURL, { providerOnly: true });
  let current = fixtureState(initial);
  let deny = false;
  let failSave = false;
  let uploaded = false;
  const writes: { path: string; body: unknown; key?: string }[] = [];
  const receipt = packageStatus({ packageId: '55555555-5555-4555-8555-555555555555',
    operationId: '55555555-5555-4555-8555-555555555555', name: 'Synthetic source package', processingState: 'Received', revision: 1,
    coverage: { total: 1, pending: 1, processed: 0, unsupported: 0, unreadable: 0, failed: 0, excluded: 0 } });
  await context.route('**/api/csp/onboarding/state', route => route.fulfill({ json: { status: 'success', data: current.profile } }));
  await context.route('**/api/csp/organization-onboarding/drafts?*', route => route.fulfill({
    json: { status: 'success', data: { items: [], total: 0, page: 1, pageSize: 25 } },
  }));
  await context.route('**/api/csp/offerings?*', route => route.fulfill({
    json: { status: 'success', data: { items: [], total: 0, page: 1, pageSize: 25 } },
  }));
  await context.route(/\/api\/csp\/(?:onboarding\/setup(?:\/.*)?|package-imports\/(?:upload-intents|receipt-reconciliation)|onboarding\/atos\/upload)$/, async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const success = (data: unknown, status = 200) => route.fulfill({ status, json: { status: 'success', data, metadata: { timestamp: '2026-09-30T12:00:00Z' } } });
    if (deny) return route.fulfill({ status: 403, json: { status: 'error', error: { errorCode: 'PROVIDER_ACCESS_DENIED', message: 'Current provider access denied.' } } });
    if (request.method() === 'GET') return success(current);
    const body = path.endsWith('/atos/upload') ? null : request.postDataJSON();
    writes.push({ path, body, key: request.headers()['idempotency-key'] });
    if (path.endsWith('/draft')) {
      if (failSave) return route.fulfill({ status: 503, json: { status: 'error', error: { errorCode: 'SETUP_UNAVAILABLE', message: 'Synthetic save unavailable; changes remain unsaved.' } } });
      current.draft!.fields = structuredClone(body.draft);
      current.draft!.currentScreen = body.draft.currentScreen;
      current.draft!.revision++;
      return success(setupResult(current));
    }
    if (path.endsWith('/upload-intents')) {
      expect(body.intent.associationMode).toBe('Unassociated');
      expect(body.intent.context).toBeNull();
      const intent = pendingIntent(body.intent);
      current.uploadIntents = [intent];
      current.draft!.fields.sources = { choice: 'Intents', intentIds: [intent.intentId] };
      current.draft!.fields.currentScreen = 'p-uncertain'; current.draft!.currentScreen = 'p-uncertain';
      current.draft!.revision++;
      return success(intent, 201);
    }
    if (path.endsWith('/atos/upload')) {
      expect(request.headers().prefer).toBe('respond-async');
      expect(request.headers()['x-workspace-kind']).toBe('csp');
      expect(request.headers()['x-workspace-mode']).toBe('ordinary');
      expect(request.headers()['x-workspace-tenant-id']).toBeUndefined();
      expect(request.headers()['x-provider-upload-intent']).toBe(current.uploadIntents[0]!.intentId);
      expect(request.headers()['idempotency-key'].length).toBeLessThanOrEqual(100);
      uploaded = true;
      return route.abort('connectionfailed');
    }
    if (path.endsWith('/receipt-reconciliation')) {
      if (uploaded) {
        current.uploadIntents[0]!.receipt = receipt;
        return success({ outcome: 'Confirmed', observedAt: '2026-09-30T12:01:00Z', receipt, nextAction: 'OpenReceipt' });
      }
      return success({ outcome: 'NotObserved', observedAt: '2026-09-30T12:01:00Z', receipt: null, nextAction: 'ReselectSameFiles' });
    }
    return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unconfigured provider fixture ${path}` } } });
  });
  return { writes, workspaceRequests, state: () => current, screen: (value: ProviderScreen) => { current = fixtureState(value); },
    deny: () => { deny = true; }, failSave: () => { failSave = true; } };
}

for (const width of [1440, 390]) {
  test(`all seven provider mock states retain hierarchy and responsive layout at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    const fixture = await installProvider(context, baseURL!, 'p-details');
    await page.setViewportSize({ width, height: 1000 });
    for (const [id, heading] of Object.entries(headings)) {
      fixture.screen(id as ProviderScreen);
      // Act
      await page.goto('/workspaces/csp/onboarding/csp?reentry=resume');
      // Assert
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Save & finish later' })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: info.outputPath(`${id}-${width}.png`), fullPage: true });
      const primary = id === 'p-ready' ? 'Open provider review queue' : id === 'p-review' ? 'Finish provider setup'
        : id === 'p-uncertain' ? 'Check existing receipt' : id === 'p-sources' ? 'Continue to setup review' : 'Save & continue';
      await page.getByRole('button', { name: primary, exact: true }).scrollIntoViewIfNeeded();
      await expect(page.getByRole('button', { name: primary, exact: true })).toBeVisible();
      await page.screenshot({ path: info.outputPath(`${id}-${width}-footer.png`), fullPage: true });
    }
    expect(fixture.writes).toEqual([]);
  });

  test(`provider explicit partial save survives reload without activating at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    const fixture = await installProvider(context, baseURL!, 'p-details');
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/workspaces/csp/onboarding/csp?reentry=resume');
    await page.getByLabel('Provider display name', { exact: true }).fill('Synthetic partial save');
    // Act
    await page.getByRole('button', { name: 'Save & finish later' }).click();
    await expect(page).toHaveURL(/\/setup\/resume$/);
    await page.goto('/workspaces/csp/onboarding/csp?reentry=resume');
    await page.reload();
    // Assert
    await expect(page.getByLabel('Provider display name', { exact: true })).toHaveValue('Synthetic partial save');
    expect(fixture.state().profile.onboardingState).toBe('InWizard');
    expect(fixture.writes.map(write => write.path)).toEqual(['/api/csp/onboarding/setup/draft']);
    await page.screenshot({ path: info.outputPath(`provider-saved-reloaded-${width}.png`), fullPage: true });
  });
}

test('unknown receipt is reconciled after real SPA reload without duplicate upload or fabricated boundary', async ({ page, context, baseURL }, info) => {
  // Arrange
  const fixture = await installProvider(context, baseURL!, 'p-sources');
  await page.goto('/workspaces/csp/onboarding/csp?reentry=resume');
  await page.getByLabel('Package name', { exact: true }).fill('Synthetic source package');
  await page.getByLabel('Select source files', { exact: true }).setInputFiles({ name: 'synthetic-provider.txt', mimeType: 'text/plain', buffer: source });
  await page.getByLabel('Declared source classification').selectOption('Unclassified');
  await page.getByLabel('These files contain only synthetic data.').check();
  // Act
  await page.getByRole('button', { name: 'Upload package', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Check the package receipt', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Check existing receipt', exact: true }).click();
  // Assert
  await expect(page.getByText('Receipt confirmed · Revision 1')).toBeVisible();
  expect(fixture.writes.filter(write => write.path.endsWith('/atos/upload'))).toHaveLength(1);
  expect(fixture.writes.some(write => /boundary-revisions|approval|publish/.test(write.path))).toBe(false);
  await page.screenshot({ path: info.outputPath('provider-receipt-recovered.png'), fullPage: true });
});

test('failed save and denied setup do not show fabricated success or provider-private fields', async ({ page, context, baseURL }, info) => {
  // Arrange
  const fixture = await installProvider(context, baseURL!, 'p-details');
  fixture.failSave();
  await page.goto('/workspaces/csp/onboarding/csp?reentry=resume');
  await page.getByLabel('Provider display name', { exact: true }).fill('Synthetic unsaved edit');
  // Act
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  // Assert
  await expect(page.getByRole('alert')).toContainText('Synthetic save unavailable');
  await expect(page.getByLabel('Provider display name', { exact: true })).toHaveValue('Synthetic unsaved edit');
  await expect(page).toHaveURL(/\/onboarding\/csp\?reentry=resume$/);
  await page.screenshot({ path: info.outputPath('provider-save-unavailable.png'), fullPage: true });
  // Act
  fixture.deny();
  await page.reload();
  // Assert
  await expect(page.getByRole('alert')).toContainText('Current provider access denied');
  await expect(page.getByLabel('Provider display name', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Save & finish later' })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('provider-forbidden.png'), fullPage: true });
});

test('unknown handling policy permits draft work and deferral but disables source upload', async ({ page, context, baseURL }, info) => {
  // Arrange
  const fixture = await installProvider(context, baseURL!, 'p-details');
  fixture.state().handling = { ...fixture.state().handling, state: 'Unknown', uploadsPermitted: false,
    analysisPermitted: false, allowedClassifications: [], reasonCode: 'HANDLING_POLICY_UNKNOWN' };
  await page.goto('/workspaces/csp/onboarding/csp?reentry=resume');
  // Act
  await expect(page.getByText('Deployment handling policy is unknown or expired. Uploads are unavailable.')).toBeVisible();
  await page.getByRole('button', { name: '4 Source package · optional', exact: true }).click();
  // Assert
  await expect(page.getByLabel('Select source files', { exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Upload package', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Skip sources for now', exact: true })).toBeEnabled();
  expect(fixture.writes).toEqual([]);
  await page.screenshot({ path: info.outputPath('provider-unknown-handling.png'), fullPage: true });
});
