import { expect, test, type BrowserContext } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { SystemSetupDraft, SystemSetupView } from '../../src/features/onboarding/systemSetupApi';

async function systemSetupFixture(context: BrowserContext, uncertain = false) {
  let saved: SystemSetupView | undefined;
  let loseResponse = uncertain;
  let forbidden = false;
  const writes: { key: string | undefined; data: unknown; path: string }[] = [];
  const contacts = [{ personId: 'contact-a', displayName: 'Synthetic preparation lead' }];
  await context.route('**/api/workspaces/organizations/org-a/systems/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.endsWith('/setup-context')) return route.fulfill({ json: { data: {
      organizationName: 'Synthetic Organization A', canCreate: !forbidden, contacts,
    } } });
    if (url.pathname.endsWith('/setup-access')) return route.fulfill({ json: { data: { canCreateSystem: !forbidden } } });
    if (url.pathname.endsWith('/setup-drafts') && request.method() === 'GET')
      return route.fulfill({ json: { data: { items: saved ? [saved] : [], nextCursor: null } } });
    if (!/\/setup(?:\/confirm)?$|\/setup-drafts$/.test(url.pathname)) return route.fallback();
    if (forbidden) return route.fulfill({ status: 403, json: { error: { code: 'WORKSPACE_OPERATION_NOT_AUTHORIZED', message: 'Your setup permission was revoked.' } } });
    if (request.method() === 'GET') return route.fulfill({ json: { data: saved } });
    const data = request.postDataJSON();
    writes.push({ key: request.headers()['idempotency-key'], data, path: url.pathname });
    if (url.pathname.endsWith('/confirm')) {
      saved = { ...saved!, setupState: 'confirmed', completedAt: '2026-09-30T12:00:00Z' };
    } else {
      const draft = data as SystemSetupDraft;
      saved = {
        systemId: 'system-a', tenantId: 'org-a', displayName: draft.name,
        organizationName: 'Synthetic Organization A', revision: (saved?.revision ?? 0) + 1,
        identityRevision: 'identity-v1', savedAt: '2026-09-30T12:00:00Z', setupState: 'draft', completedAt: null,
        draft, contacts, effectiveTeam: [{ role: 'Issm', displayName: 'Synthetic reviewer', source: 'OrgFallback' }],
        sources: [], tasks: [{ id: 'setup:documents', label: 'Review system documentation', detail: 'Review required',
          state: 'reviewRequired', contribution: 'Prepare reviewed SSP documentation', link: '/systems/system-a/documents',
          ownerRole: 'ISSM', canAct: true }],
        canManage: true,
        monitoring: { configuration: 'present', access: 'notChecked', scopeReview: 'unsupported', collection: 'unknown', evaluation: 'unknown' },
        links: { documents: '/systems/system-a/documents', monitoring: '/systems/system-a/conmon' },
      };
    }
    if (loseResponse) { loseResponse = false; return route.abort('failed'); }
    return route.fulfill({ status: url.pathname.endsWith('/setup-drafts') ? 201 : 200, json: { data: saved } });
  });
  return { writes, revoke: () => { forbidden = true; }, read: () => saved };
}

for (const width of [1440, 390]) {
  test(`seven system setup states retain server choices at ${width}px (synthetic API)`, async ({ page, context, baseURL }) => {
    // Arrange
    await installWorkspaceFixture(context, baseURL!);
    const fixture = await systemSetupFixture(context);
    await page.setViewportSize({ width, height: 1000 });
    const capture = async (screen: string) => {
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: test.info().outputPath(`${screen}.png`), fullPage: true });
    };
    // Act
    await page.goto('/workspaces/organizations/org-a/systems/new');
    // Assert
    await expect(page.getByRole('heading', { name: 'Start your system workspace', level: 1 })).toBeVisible();
    await expect(page.getByText('Register, manage, and monitor all information systems in your portfolio.', { exact: true })).toHaveCount(0);
    await capture('s-details');
    // Act
    await page.getByLabel('System name', { exact: true }).fill('Synthetic cargo system');
    await page.getByLabel('Mission purpose').fill('Support synthetic cargo preparation.');
    await page.getByRole('button', { name: 'Save & continue' }).click();
    // Assert
    await expect(page.getByRole('heading', { name: 'Identify your system team', level: 1 })).toBeFocused();
    await capture('s-team');
    // Act
    await page.getByRole('combobox', { name: 'System contact', exact: true }).selectOption('contact-a');
    await page.getByRole('button', { name: 'Save & continue' }).click();
    await expect(page.getByRole('heading', { name: 'Bring existing documentation', level: 1 })).toBeVisible();
    await capture('s-sources');
    await page.getByRole('button', { name: 'Save choice & continue' }).click();
    await expect(page.getByRole('heading', { name: 'Identify how the system is hosted', level: 1 })).toBeVisible();
    await page.getByLabel('Use organization-managed hosting').check();
    await capture('s-hosting');
    await page.getByRole('button', { name: 'Save choice & continue' }).click();
    await expect(page.getByRole('heading', { name: 'Plan cloud monitoring', level: 1 })).toBeVisible();
    await capture('s-connect');
    await page.getByRole('button', { name: 'Save choice & continue' }).click();
    await expect(page.getByRole('heading', { name: 'Review system setup', level: 1 })).toBeVisible();
    await capture('s-review');
    await page.getByLabel('Confirm the saved identity and setup choices shown.').check();
    await page.getByRole('button', { name: 'Confirm system setup' }).click();
    await expect(page.getByRole('heading', { name: 'Your system workspace is ready', level: 1 })).toBeVisible();
    await capture('s-ready');
    // Assert
    expect(fixture.writes.filter(write => write.path.endsWith('/setup-drafts'))).toHaveLength(1);
    expect(fixture.read()?.draft.hostingChoice).toBe('organizationManaged');
    await expect(page.getByText('No document is generated or approved by setup.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Document previews' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/documents');
  });
}

test('save-exit reload resumes the same named system and rechecks permission (synthetic API)', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!);
  const fixture = await systemSetupFixture(context);
  await page.goto('/workspaces/organizations/org-a/systems/new');
  await page.getByLabel('System name', { exact: true }).fill('Saved cargo draft');
  // Act
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  await page.goto('/workspaces/organizations/org-a/systems/system-a/setup');
  await page.reload();
  // Assert
  await expect(page.getByLabel('System name', { exact: true })).toHaveValue('Saved cargo draft');
  expect(fixture.writes).toHaveLength(1);
  // Act
  fixture.revoke();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  // Assert
  await expect(page.getByRole('alert')).toContainText('Your setup permission was revoked.');
  await expect(page.getByRole('heading', { name: 'Start your system workspace', level: 1 })).toBeVisible();
  await page.screenshot({ path: test.info().outputPath('s-forbidden.png'), fullPage: true });
});

test('lost response retries the original draft intent and key (synthetic API)', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!);
  const fixture = await systemSetupFixture(context, true);
  await page.goto('/workspaces/organizations/org-a/systems/new');
  await page.getByLabel('System name', { exact: true }).fill('Uncertain cargo draft');
  // Act
  await page.getByRole('button', { name: 'Save & continue' }).click();
  // Assert
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByLabel('System name', { exact: true })).toBeDisabled();
  await page.screenshot({ path: test.info().outputPath('s-uncertain.png'), fullPage: true });
  // Act
  await page.getByRole('button', { name: 'Save & continue' }).click();
  // Assert
  await expect(page.getByRole('heading', { name: 'Identify your system team', level: 1 })).toBeVisible();
  expect(fixture.writes).toHaveLength(2);
  expect(fixture.writes[1]).toEqual(fixture.writes[0]);
});

test('source receipt has an explicit separate review before document handoff (synthetic API)', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!);
  const fixture = await systemSetupFixture(context);
  await page.setViewportSize({ width: 390, height: 1000 });
  const writes: { path: string; data?: unknown }[] = [];
  let source = { sessionId: 'source-a', kind: 'emass' as const, systemId: 'system-a', fileName: 'synthetic.xlsx',
    sha256: 'a'.repeat(64), sourceRevision: 1, receiptState: 'confirmed', analysisState: 'parsed',
    reviewState: 'pending', state: 'reviewRequired', fields: [
      { field: 'name', sourceField: 'system_name', proposedValue: 'Source display name', supported: true },
      { field: 'acronym', sourceField: 'system_identifier', proposedValue: 'SRC', supported: true },
    ], error: null };
  await context.route('**/api/workspaces/organizations/org-a/systems/system-a/source-imports/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'GET') return route.fulfill({ json: { data: source } });
    writes.push({ path, data: path.endsWith('/apply') ? route.request().postDataJSON() : undefined });
    if (path.endsWith('/review-previews')) return route.fulfill({ json: { data: {
      sessionId: source.sessionId, systemId: source.systemId, sourceRevision: 1,
      sourceHash: source.sha256, identityRevision: 'identity-v1', previewHash: 'review-v1',
      fields: [{ field: 'name', currentValue: 'Retained identity', proposedValue: 'Source display name', supported: true },
        { field: 'acronym', currentValue: null, proposedValue: 'SRC', supported: true }],
    } } });
    if (path.endsWith('/apply')) source = { ...source, state: 'applied', reviewState: 'applied' };
    fixture.read()!.sources = [source];
    return route.fulfill({ status: path.endsWith('/emass') ? 201 : 200, json: { data: source } });
  });
  // Act
  await page.goto('/workspaces/organizations/org-a/systems/new');
  await page.getByLabel('System name', { exact: true }).fill('Retained identity');
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByRole('combobox', { name: 'System contact', exact: true }).selectOption('contact-a');
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByLabel('Review a supported eMASS export').check();
  await page.getByLabel('Source file', { exact: true }).setInputFiles({
    name: 'synthetic.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: Buffer.from('synthetic browser transport fixture, not a parsed document'),
  });
  await page.getByRole('button', { name: 'Retain source for review' }).click();
  // Assert
  await expect(page.getByText(source.sha256, { exact: true })).toBeVisible();
  expect(writes.some(write => write.path.endsWith('/apply'))).toBe(false);
  await page.screenshot({ path: test.info().outputPath('s-sources-confirmed.png'), fullPage: true });
  // Act
  await page.getByRole('link', { name: 'Review source fields' }).click();
  await page.getByRole('button', { name: 'Review proposed fields' }).click();
  await page.getByRole('combobox', { name: 'Decision for acronym', exact: true }).selectOption('applyProposed');
  await page.getByLabel('I reviewed this source and the exact target fields.').check();
  await page.getByRole('button', { name: 'Apply reviewed decisions' }).click();
  // Assert
  await expect(page.getByRole('status').filter({ hasText: 'Source decisions recorded' })).toBeVisible();
  const applied = writes.find(write => write.path.endsWith('/apply'))!.data as { decisions: unknown[] };
  expect(applied.decisions).toEqual([{ field: 'name', decision: 'keepCurrent' }, { field: 'acronym', decision: 'applyProposed' }]);
  await expect(page.getByRole('button', { name: 'Open document previews' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('source-review-applied.png'), fullPage: true });
});
