import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering, boundary } from '../../src/__tests__/provider-authorizations/testData';
import { offeringOverview } from '../../src/__tests__/provider-authorizations/overviewFixtures';

const path = `/workspaces/csp/authorizations/offerings/${offering.offeringId}`;
const envelope = (data: unknown) => ({ status: 'success', data });

for (const width of [1440, 390]) {
  for (const state of ['source', 'missing-duties', 'restricted-source'] as const) {
    test(`implementation drawer parity and exact source context ${state} at ${width}`, async ({ page, context, baseURL }, info) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
      const name = 'Independent provider service implementation with a deliberately long readable protection name';
      const description = `Complete retained implementation paragraph.\n\n${'Long source statement remains readable without clipping. '.repeat(18)}`;
      const writes: string[] = [];
      const errors: string[] = [];
      let restricted = state === 'restricted-source';
      page.on('pageerror', error => errors.push(error.message));
      page.on('request', request => {
        if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method());
      });
      await context.route('**/api/csp/offerings/**', route => {
        const url = new URL(route.request().url());
        if (!url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: envelope(offering) });
        return route.fulfill({ json: envelope({
          offeringId: offering.offeringId, offeringRevision: offering.revision,
          capabilities: { items: [{ capabilityId: 'canonical-protection', candidateId: 'retained-proposal', packageId: 'retained-package',
            name, publicationState: 'Published', reviewState: 'Reviewed', releaseId: 'pinned-release', releaseRevision: 3,
            boundaryRevisionId: offering.currentBoundaryRevisionId }], total: 1, page: 1, pageSize: 1, published: 1, awaitingReview: 0 },
          missionSystems: { items: [], total: 0, page: 1, pageSize: 1 },
        }) });
      });
      await context.route('**/api/csp/package-imports/retained-package/candidates**', route => {
        if (restricted) return route.fulfill({ status: 403, json: { status: 'error',
          error: { message: 'Retained source is restricted', errorCode: 'FORBIDDEN' } } });
        return route.fulfill({ json: envelope({
          items: [{ candidateId: 'retained-proposal', description, revision: 7, reviewState: 'Published',
            controlDuties: state === 'missing-duties' ? {} : { 'CP-9': 'Shared', 'CP-10': 'Shared', 'SC-12': 'Customer manages the recorded keys.' },
            citations: [{ artifactId: 'retained-artifact', archivePath: 'retained-protection.pdf', locator: 'Section 7',
              quote: 'Exact retained source evidence, not a fabricated mission approval.' }] }],
          total: 1, page: 1, pageSize: 25,
        }) });
      });
      // Act
      await page.goto(`${path}/inherited-coverage?task=capabilities`);
      const opener = page.getByRole('button', { name, exact: true });
      await opener.focus();
      await page.keyboard.press('Enter');
      const drawer = page.getByRole('dialog', { name, exact: true });
      // Assert
      await expect(drawer).toHaveClass(/offering-implementation-drawer/);
      await expect(drawer.getByRole('heading', { name: 'Service implementation', exact: true })).toBeVisible();
      await expect(drawer.getByText(offering.name, { exact: true })).toBeVisible();
      await expect(drawer.getByRole('textbox')).toHaveCount(0);
      await expect(drawer.getByRole('link', { name: 'Open capability workflow' })).toHaveAttribute('href', '/workspaces/csp/security-capabilities/canonical-protection');
      await expect(drawer.getByRole('link', { name: 'Review retained source' })).toHaveAttribute('href',
        `${path}/packages/retained-package/candidates/retained-proposal`);
      if (restricted) {
        await expect(drawer.getByRole('alert')).toContainText('Retained source is restricted');
        await expect(drawer).toContainText('Implementation summary unavailable.');
        restricted = false;
        await drawer.getByRole('button', { name: 'Retry implementation details' }).click();
        await expect(drawer.getByRole('alert')).toHaveCount(0);
      }
      await expect(drawer.locator('.offering-implementation-statement')).toHaveText(description);
      await expect(drawer).toContainText('Retained source proposal revision 7');
      await expect(drawer).toContainText('not an immutable release payload');
      if (state === 'missing-duties') await expect(drawer).toContainText('Pinned published duties are not supplied');
      else await expect(drawer.getByRole('group', { name: 'Shared', exact: true })).toContainText('CP-9CP-10');
      const bounds = await drawer.boundingBox();
      expect(Math.round(bounds!.width)).toBe(width === 1440 ? 520 : 366);
      expect(await drawer.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      await drawer.getByText('Retained source citations', { exact: true }).click();
      await expect(drawer).toContainText('Exact retained source evidence');
      await drawer.getByText('Source and release identities', { exact: true }).click();
      await expect(drawer).toContainText('pinned-release');
      await expect(drawer).toContainText(`Draft · revision ${offering.revision}`);
      await page.addScriptTag({ content: axe.source });
      const accessibility = await page.evaluate(async () => (window as Window & { axe: typeof axe }).axe.run('.offering-implementation-drawer',
        { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
      expect(accessibility.violations).toEqual([]);
      await drawer.getByRole('button', { name: 'Close details' }).focus();
      await page.keyboard.press('Tab');
      await expect(drawer.getByRole('button', { name: 'Close dialog' })).toBeFocused();
      await page.keyboard.press('Shift+Tab');
      await expect(drawer.getByRole('button', { name: 'Close details' })).toBeFocused();
      await drawer.screenshot({ path: info.outputPath(`implementation-${state}-${width}.png`) });
      await page.keyboard.press('Escape');
      await expect(drawer).toHaveCount(0);
      await expect(opener).toBeFocused();
      expect(writes).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
}

for (const state of ['no-boundary', 'missing-duties', 'many-services', 'restricted-scope', 'stale-context', 'partial-linked'] as const) {
  test(`scope displays ${state} without inventing coverage at mobile width`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width: 390, height: 1100 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const services = Array.from({ length: state === 'many-services' ? 37 : 2 }, (_, index) => `Documented service ${index + 1}`);
    const writes: string[] = [];
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => {
      if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method());
    });
    await context.route('**/api/csp/offerings/**', route => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/boundary-overview')) {
        if (state === 'partial-linked') return route.fulfill({ status: 403, json: { status: 'error',
          error: { message: 'Linked records are restricted', errorCode: 'FORBIDDEN' } } });
        return route.fulfill({ json: envelope({
          offeringId: offering.offeringId, offeringRevision: offering.revision + (state === 'stale-context' ? 1 : 0),
          capabilities: { items: [], total: 0, page: 1, pageSize: 10, published: 0, awaitingReview: 0 },
          missionSystems: { items: [], total: 0, page: 1, pageSize: 10 },
        }) });
      }
      if (url.pathname.includes('/boundary-revisions/')) {
        if (state === 'restricted-scope') return route.fulfill({ status: 403, json: { status: 'error',
          error: { message: 'Recorded scope is restricted', errorCode: 'FORBIDDEN' } } });
        return route.fulfill({ json: envelope({ ...boundary, services }) });
      }
      return route.fulfill({ json: envelope({ ...offering, currentBoundaryRevisionId: state === 'no-boundary' ? null : boundary.boundaryRevisionId }) });
    });
    // Act
    await page.goto(`${path}/boundary`);
    const scope = page.getByRole('region', { name: 'Service scope' });
    // Assert
    if (state === 'no-boundary') {
      await expect(scope).toContainText('No boundary recorded.');
      await expect(scope.getByRole('button', { name: 'Edit boundary' })).toBeEnabled();
    } else if (state === 'restricted-scope') {
      await expect(scope).toContainText('Recorded scope is restricted');
      await expect(scope.getByRole('button', { name: 'Edit boundary' })).toBeDisabled();
      await expect(scope).not.toContainText('No boundary recorded.');
      await expect(page.getByRole('region', { name: 'Provider duties' })).toContainText('Responsibilities are unavailable');
    } else {
      await expect(scope.getByRole('listitem')).toHaveCount(services.length);
      await expect(page.getByRole('region', { name: 'Provider duties' })).toContainText('No CSP responsibilities recorded.');
      await expect(page.getByRole('region', { name: 'Customer duties' })).toContainText('No Mission Owner responsibilities recorded.');
      if (state === 'stale-context') {
        await expect(scope).toContainText('Offering context changed since this page loaded.');
        await expect(scope.getByRole('button', { name: 'Edit boundary' })).toBeDisabled();
      }
      if (state === 'partial-linked') await expect(page.getByText('Linked records are restricted', { exact: true })).toBeVisible();
      await scope.getByText('Hosting identity & source provenance', { exact: true }).click();
      await expect(scope).toContainText('No supporting source citations recorded.');
      await expect(scope).toContainText('This is not universal resource coverage.');
    }
    await expect(page.getByText(/Recorded boundary duties are working context/)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(writes).toEqual([]);
    expect(errors).toEqual([]);
  });
}

test('isolated read-only scope rejection keeps entered duties and offers cancel without faking a revision', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const submitted: unknown[] = [];
  await context.route('**/api/csp/offerings/**', route => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'POST') {
      submitted.push(request.postDataJSON());
      return route.fulfill({ status: 403, json: { status: 'error', error: {
        message: 'This recorded scope is read-only', errorCode: 'FORBIDDEN',
      } } });
    }
    if (url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: envelope({
      offeringId: offering.offeringId, offeringRevision: offering.revision,
      capabilities: { items: [], total: 0, page: 1, pageSize: 10, published: 0, awaitingReview: 0 },
      missionSystems: { items: [], total: 0, page: 1, pageSize: 10 },
    }) });
    return route.fulfill({ json: envelope(url.pathname.includes('/boundary-revisions/') ? boundary : offering) });
  });
  // Act
  await page.goto(`${path}/boundary`);
  await page.getByRole('button', { name: 'Edit boundary' }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit boundary' });
  await dialog.getByRole('textbox', { name: 'Provider responsibilities (one per line)', exact: true }).fill('User-entered duty stays in the rejected editor');
  await dialog.getByRole('button', { name: 'Save boundary revision' }).click();
  // Assert
  await expect(dialog).toContainText('This recorded scope is read-only');
  await expect(dialog.getByRole('textbox', { name: 'Provider responsibilities (one per line)', exact: true }))
    .toHaveValue('User-entered duty stays in the rejected editor');
  expect(submitted).toEqual([expect.objectContaining({ providerResponsibilities: ['User-entered duty stays in the rejected editor'] })]);
  await dialog.getByRole('button', { name: 'Cancel editing' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Service scope' })).toContainText('Recorded boundary version 1');
  await expect(page.getByRole('region', { name: 'Provider duties' })).not.toContainText('User-entered duty stays in the rejected editor');
});

for (const width of [1440, 390]) {
  test(`scope mock structure, source values, keyboard cancel and isolated versioned save at ${width}`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    let currentOffering = { ...offering };
    let currentBoundary = { ...boundary,
      services: Array.from({ length: 8 }, (_, index) => `Recorded independent service ${index + 1}`),
      providerResponsibilities: ['Operate the recorded services only.'],
      customerResponsibilities: ['Review mission-specific applicability.'],
      exclusions: [{ scope: null, description: 'Mission application code', rationale: 'Outside provider operations' }],
    };
    const errors: string[] = [];
    const submitted: Record<string, unknown>[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await context.route('**/api/csp/offerings/**', async route => {
      const request = route.request();
      const url = new URL(request.url());
      if (request.method() === 'POST' && url.pathname.endsWith('/boundary-revisions')) {
        const input = request.postDataJSON();
        submitted.push(input);
        currentOffering = { ...currentOffering, revision: currentOffering.revision + 1, currentBoundaryRevisionId: 'boundary-2' };
        currentBoundary = { ...currentBoundary, name: input.name, scopeStatement: input.scopeStatement,
          version: currentBoundary.version + 1, boundaryRevisionId: 'boundary-2', offeringRevision: currentOffering.revision };
        return route.fulfill({ json: envelope(currentBoundary) });
      }
      if (url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: envelope({
        offeringId: offering.offeringId, offeringRevision: currentOffering.revision,
        capabilities: { items: [], total: 0, page: 1, pageSize: 10, published: 0, awaitingReview: 0 },
        missionSystems: { items: [], total: 0, page: 1, pageSize: 10 },
      }) });
      if (url.pathname.includes('/boundary-revisions/')) return route.fulfill({ json: envelope(currentBoundary) });
      return route.fulfill({ json: envelope(currentOffering) });
    });
    // Act
    await page.goto(`${path}/boundary`);
    const scope = page.getByRole('region', { name: 'Service scope' });
    await expect(scope.getByRole('listitem')).toHaveCount(8);
    // Assert
    await expect(scope).toContainText('Recorded boundary version 1 · Working recorded scope');
    await expect(page.getByRole('region', { name: 'Provider duties' })).toContainText(currentBoundary.providerResponsibilities[0]!);
    await expect(page.getByRole('region', { name: 'Customer duties' })).toContainText(currentBoundary.customerResponsibilities[0]!);
    expect(await scope.locator('.offering-scope-services').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length))
      .toBe(width === 1440 ? 2 : 1);
    expect(await page.locator('.offering-scope-duties').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length))
      .toBe(width === 1440 ? 2 : 1);
    const provenance = scope.getByText('Hosting identity & source provenance', { exact: true });
    await provenance.focus();
    await page.keyboard.press('Enter');
    await expect(scope.getByText(boundary.snapshotHash, { exact: true })).toBeVisible();
    await page.keyboard.press('Enter');
    await expect(scope.getByText(boundary.snapshotHash, { exact: true })).not.toBeVisible();
    const edit = scope.getByRole('button', { name: 'Edit boundary' });
    await edit.focus();
    // Act
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Edit boundary' });
    // Assert
    await expect(dialog.getByRole('textbox', { name: 'Explicit scope statement', exact: true })).toHaveValue(boundary.scopeStatement);
    await expect(dialog.getByRole('textbox', { name: 'Services (one per line)', exact: true })).toHaveValue(currentBoundary.services.join('\n'));
    await dialog.getByRole('button', { name: 'Cancel editing' }).focus();
    await page.keyboard.press('Tab');
    await expect(dialog.getByRole('button', { name: 'Close dialog' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(edit).toBeFocused();
    expect(submitted).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.addScriptTag({ content: axe.source });
    const accessibility = await page.evaluate(async () => (window as Window & { axe: typeof axe }).axe.run('main',
      { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
    expect(accessibility.violations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`scope-fixture-${width}.png`), fullPage: true });
    // Act: the POST is intercepted in this isolated fixture, never sent live.
    await edit.click();
    await dialog.getByRole('textbox', { name: 'Boundary name', exact: true }).fill('User-recorded revised scope');
    await dialog.getByRole('button', { name: 'Save boundary revision' }).click();
    // Assert
    await expect(dialog).toHaveCount(0);
    await expect(scope).toContainText('Recorded boundary version 2 · Working recorded scope');
    expect(submitted).toEqual([expect.objectContaining({
      name: 'User-recorded revised scope', expectedOfferingRevision: offering.revision,
      predecessorRevisionId: boundary.boundaryRevisionId, scopeStatement: boundary.scopeStatement,
      services: currentBoundary.services, providerResponsibilities: currentBoundary.providerResponsibilities,
      customerResponsibilities: currentBoundary.customerResponsibilities,
    })]);
    await edit.click();
    await expect(dialog.getByRole('textbox', { name: 'Boundary name', exact: true })).toHaveValue('User-recorded revised scope');
    await page.keyboard.press('Escape');
    expect(errors).toEqual([]);
  });
}

for (const width of [1440, 390]) {
  test(`focused offering task navigation, search, pagination and keyboard drawer at ${width}`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const errors: string[] = [];
    const writes: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => {
      if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method());
    });
    const capabilities = Array.from({ length: 12 }, (_, index) => ({
      capabilityId: null, candidateId: `candidate-${index}`, packageId: 'synthetic-package',
      name: `Protection ${String(index).padStart(2, '0')}`, reviewState: 'Reviewed',
      publicationState: 'Published', releaseId: `release-${index}`, releaseRevision: 3,
      boundaryRevisionId: index === 0 ? 'older-boundary' : boundary.boundaryRevisionId,
    }));
    await context.route('**/api/csp/offerings/**', async route => {
      const url = new URL(route.request().url());
      const next = Number(url.searchParams.get('capabilityPage') || 1);
      if (url.pathname.endsWith('/overview')) return route.fulfill({ json: envelope({
        ...offeringOverview(), openFindingCount: 1, capabilities: { ...offeringOverview().capabilities,
          published: 12, proposed: 0, awaitingReview: 0, awaitingApproval: 0, publishedReleaseRevisions: [3] },
      }) });
      if (url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: envelope({
        offeringId: offering.offeringId, offeringRevision: offering.revision,
        capabilities: { items: capabilities.slice((next - 1) * 10, next * 10), total: 12, page: next, pageSize: 10, published: 12, awaitingReview: 0 },
        missionSystems: { items: [{ assignmentId: 'assignment-1', systemId: 'mission-1', systemName: 'Synthetic mission',
          relationshipState: 'ReviewRequired', associated: true, adoptedCapabilityCount: 0, assignedScopes: [] }],
          total: 1, page: 1, pageSize: 10 },
      }) });
      return route.fulfill({ json: envelope(offering) });
    });
    await context.route('**/api/csp/package-imports/synthetic-package/candidates**', route => route.fulfill({ json: envelope({
      items: capabilities.map(item => ({ candidateId: item.candidateId, description: `Retained source ${item.name}`,
        controlDuties: { 'AU-2': 'Shared' }, revision: 7, reviewState: 'Reviewed',
        citations: [{ artifactId: 'source-artifact', archivePath: 'source.pdf', locator: 'Section 2', quote: 'Synthetic cited scope' }] })),
      page: 1, pageSize: 25, total: 12,
    }) }));
    // Act
    await page.goto(path);
    // Assert
    await expect(page.getByRole('region', { name: 'Next action' })).toContainText('Review the open provider finding');
    await expect(page.getByText(`Offering identity: ${offering.lifecycle} · revision ${offering.revision}`, { exact: true })).toBeVisible();
    const nav = page.getByRole('navigation', { name: 'Offering sections' });
    await expect(nav.getByRole('link')).toHaveCount(6);
    await nav.getByRole('link', { name: 'Overview', exact: true }).focus();
    await page.keyboard.press('ArrowRight');
    await expect(nav.getByRole('link', { name: 'Capabilities', exact: true })).toBeFocused();
    await page.keyboard.press('End');
    await expect(nav.getByRole('link', { name: 'Mission use', exact: true })).toBeFocused();
    await page.keyboard.press('Home');
    await expect(nav.getByRole('link', { name: 'Overview', exact: true })).toBeFocused();
    // Act
    await nav.getByRole('link', { name: 'Capabilities', exact: true }).click();
    const table = page.getByRole('table', { name: 'Service implementations' });
    // Assert
    await expect(table.getByRole('button')).toHaveCount(5);
    await page.getByRole('combobox', { name: 'Sort by name' }).selectOption('desc');
    await expect(table.getByRole('row').nth(1)).toContainText('Protection 11');
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(table.getByRole('row').nth(1)).toContainText('Protection 06');
    await page.getByRole('textbox', { name: 'Search capabilities' }).fill('AU-2');
    await page.getByRole('combobox', { name: 'Release state' }).selectOption('pending');
    await expect(table).toContainText('No capabilities match this search.');
    await page.getByRole('combobox', { name: 'Release state' }).selectOption('published');
    await page.getByRole('textbox', { name: 'Search capabilities' }).fill('Protection 00');
    const opener = page.getByRole('button', { name: 'Protection 00', exact: true });
    await opener.focus();
    await page.keyboard.press('Enter');
    const drawer = page.getByRole('dialog', { name: 'Protection 00' });
    await expect(drawer).toContainText('Different boundary version');
    await expect(drawer).toContainText('Retained source proposal revision 7');
    await expect(drawer).toContainText('not an immutable release payload');
    await drawer.getByText('Retained source citations', { exact: true }).click();
    await expect(drawer).toContainText('Synthetic cited scope');
    const last = drawer.getByRole('button', { name: 'Close details' });
    await last.focus();
    await page.keyboard.press('Tab');
    await expect(drawer.getByRole('button', { name: 'Close dialog' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(drawer).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.addScriptTag({ content: axe.source });
    const accessibility = await page.evaluate(async () => (window as Window & { axe: typeof axe }).axe.run('main',
      { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
    expect(accessibility.violations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`offering-capabilities-${width}.png`), fullPage: true });
    await nav.getByRole('link', { name: 'Mission use' }).click();
    await expect(page.getByText('Associated', { exact: true })).toBeVisible();
    await expect(page.getByText('0 · no adopted release recorded')).toBeVisible();
    await page.getByRole('button', { name: /View handoff for/ }).click();
    await expect(page.getByRole('link', { name: 'Inspect service relationship' })).toHaveAttribute('href', `${path}/missions/assignment-1`);
    await page.getByRole('button', { name: 'Close handoff', exact: true }).click();
    await nav.getByRole('link', { name: 'Release & changes' }).click();
    await expect(page.getByText('Published revision 3', { exact: true })).toBeVisible();
    await expect(page.getByText(/No pending capability proposals/)).toBeVisible();
    expect(writes).toEqual([]);
    expect(errors).toEqual([]);
  });
}

test('synthetic identity compare, close guard and rejected stale save preserve entered values', async ({ context, page, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  const submitted: unknown[] = [];
  await context.route('**/api/csp/offerings/**', async route => {
    if (route.request().method() === 'PATCH') {
      submitted.push(route.request().postDataJSON());
      return route.fulfill({ status: 409, json: { status: 'error', error: { message: 'Synthetic revision changed', errorCode: 'AUTHORIZATION_CONTEXT_STALE' } } });
    }
    return route.fulfill({ json: envelope(route.request().url().includes('/overview') ? { ...offeringOverview(), openFindingCount: 0 } : offering) });
  });
  // Act
  await page.goto(path);
  await page.getByRole('link', { name: 'Edit service details' }).click();
  const editor = page.getByRole('dialog', { name: 'Edit service identity' });
  await editor.getByLabel('Service owner', { exact: true }).fill('Retained synthetic edit');
  await editor.getByRole('button', { name: 'Compare identity edits' }).click();
  // Assert
  await expect(editor.getByRole('table', { name: 'Identity changes' })).toContainText('Retained synthetic edit');
  // Act
  await editor.getByRole('button', { name: 'Close dialog' }).click();
  const confirm = page.getByRole('dialog', { name: 'Discard unsaved identity edits?' });
  await confirm.getByRole('button', { name: 'Keep editing' }).click();
  await expect(editor.getByRole('button', { name: 'Close dialog' })).toBeFocused();
  await editor.getByRole('button', { name: 'Save service identity' }).click();
  // Assert
  await expect(editor.getByRole('alert')).toContainText('Synthetic revision changed');
  await expect(editor.getByLabel('Service owner', { exact: true })).toHaveValue('Retained synthetic edit');
  expect(submitted).toEqual([expect.objectContaining({ expectedRevision: offering.revision, serviceOwner: 'Retained synthetic edit' })]);
  // Act
  await page.keyboard.press('Escape');
  await page.getByRole('dialog', { name: 'Discard unsaved identity edits?' }).getByRole('button', { name: 'Discard edits' }).click();
  // Assert
  await expect(editor).toHaveCount(0);
});

  for (const state of ['zero', 'single', 'denied', 'foreign', 'stale-pages', 'partial-source'] as const) {
    test(`capability ${state} state preserves context without writes`, async ({ context, page, baseURL }) => {
      // Arrange
      await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
      await page.setViewportSize({ width: 390, height: 1000 });
      const writes: string[] = [];
      page.on('request', request => {
        if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method());
      });
      await context.route('**/api/csp/offerings/**', async route => {
        const url = new URL(route.request().url());
        if (!url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: envelope(offering) });
        if (state === 'denied') return route.fulfill({ status: 403, json: {
          status: 'error', error: { message: 'Provider administrator workspace required', errorCode: 'PROVIDER_ACCESS_DENIED' },
        } });
        const next = Number(url.searchParams.get('capabilityPage') || 1);
        const total = state === 'zero' ? 0 : state === 'stale-pages' ? 11 : 1;
        return route.fulfill({ json: envelope({
          offeringId: state === 'foreign' ? 'foreign-offering' : offering.offeringId,
          offeringRevision: offering.revision + (state === 'stale-pages' ? next - 1 : 0),
          capabilities: { items: Array.from({ length: Math.min(10, Math.max(0, total - (next - 1) * 10)) }, (_, index) => ({
            capabilityId: null, candidateId: 'source-1', packageId: 'synthetic-package', name: `Synthetic protection ${index}`,
            reviewState: 'Reviewed', publicationState: 'Published', releaseId: 'exact-retained-release', releaseRevision: 3,
            boundaryRevisionId: null,
          })), total, page: next, pageSize: 10, published: total, awaitingReview: 0 },
          missionSystems: { items: [], total: 0, page: 1, pageSize: 10 },
        }) });
      });
      await context.route('**/api/csp/package-imports/synthetic-package/candidates**', route => state === 'partial-source'
        ? route.fulfill({ status: 503, json: { status: 'error', error: { message: 'Retained source unavailable' } } })
        : route.fulfill({ json: envelope({ items: [{ candidateId: 'source-1', revision: 5, reviewState: 'Reviewed',
          description: 'Retained synthetic context', controlDuties: { 'SC-7': 'Shared' }, citations: [] }], page: 1, pageSize: 25, total: 1 }) }));
      // Act
      await page.goto(`${path}/inherited-coverage?task=capabilities`);
      // Assert
      if (state === 'zero') {
        await expect(page.getByText('No published release', { exact: true })).toBeVisible();
        await expect(page.getByText(/No capability records linked/)).toBeVisible();
        await expect(page.getByRole('table')).toHaveCount(0);
      } else if (state === 'denied' || state === 'foreign' || state === 'stale-pages') {
        await expect(page.getByRole('alert')).toContainText(state === 'denied' ? 'Provider administrator workspace required'
          : state === 'foreign' ? 'requested offering' : 'Offering changed while capability pages were loading');
        await expect(page.getByRole('table')).toHaveCount(0);
      } else {
        const table = page.getByRole('table', { name: 'Service implementations' });
        await expect(table.getByRole('button', { name: 'Synthetic protection 0' })).toBeVisible();
        if (state === 'partial-source') {
          await page.getByRole('textbox', { name: 'Search capabilities' }).fill('SC-7');
          await expect(page.getByText(/Search is incomplete/)).toBeVisible();
          await expect(table).toContainText('No matches in the available metadata.');
          await expect(page.getByText('1 capabilities available · 0 awaiting review')).toBeVisible();
        } else {
          await table.getByRole('button', { name: 'Synthetic protection 0' }).click();
          await expect(page.getByRole('dialog')).toContainText('Boundary context');
          await expect(page.getByRole('dialog')).toContainText('Not recorded');
          await expect(page.getByRole('dialog').getByRole('link', { name: 'Review retained source' })).toHaveAttribute('href',
            `${path}/packages/synthetic-package/candidates/source-1`);
          await page.keyboard.press('Escape');
        }
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect(writes).toEqual([]);
    });
  }

  test('empty release context explains exact missing scope prerequisite with a working offering-scoped handoff', async ({ context, page, baseURL }) => {
    // Arrange
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    const empty = { ...offering, currentBoundaryRevisionId: null, currentHostingScopeRevisionId: null };
    await context.route('**/api/csp/offerings/**', route => route.fulfill({ json: envelope(route.request().url().includes('/overview')
      ? { ...offeringOverview(), capabilities: { proposed: 0, published: 0, awaitingReview: 0, awaitingApproval: 0, archived: 0, publishedReleaseRevisions: [] } }
      : empty) }));
    // Act
    await page.goto(`${path}/release`);
    // Assert
    await expect(page.getByText('No published capabilities', { exact: true })).toBeVisible();
    await page.getByText('Retained source & scope comparisons', { exact: true }).click();
    await expect(page.getByText('Boundary comparison unavailable: no boundary revision recorded.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Stage a scope update' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Record hosting prerequisites' })).toHaveAttribute('href', `${path}/inherited-coverage?task=hosting`);
  });

test('synthetic successful identity save refreshes actual returned revision without rewriting published context', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
  await page.setViewportSize({ width: 390, height: 1000 });
  let current = { ...offering };
  const writes: unknown[] = [];
  await context.route('**/api/csp/offerings/**', async route => {
    if (route.request().method() === 'PATCH') {
      const input = route.request().postDataJSON();
      writes.push(input);
      current = { ...current, serviceOwner: input.serviceOwner, revision: current.revision + 1 };
      return route.fulfill({ json: envelope(current) });
    }
    return route.fulfill({ json: envelope(route.request().url().includes('/overview') ? {
      ...offeringOverview(), offeringRevision: current.revision, openFindingCount: 0,
      capabilities: { ...offeringOverview().capabilities, publishedReleaseRevisions: [3] },
    } : current) });
  });
  // Act
  await page.goto(path);
  await page.getByRole('link', { name: 'Edit service details' }).click();
  const editor = page.getByRole('dialog', { name: 'Edit service identity' });
  await editor.getByLabel('Service owner', { exact: true }).fill('Explicit synthetic owner update');
  await editor.getByRole('button', { name: 'Compare identity edits' }).click();
  // Assert
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  // Act
  await editor.getByRole('button', { name: 'Save service identity' }).click();
  // Assert
  await expect(editor).toHaveCount(0);
  await expect(page.getByText(`Offering identity: ${offering.lifecycle} · revision ${offering.revision + 1}`, { exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Your service team' })).toContainText('Explicit synthetic owner update');
  await expect(page.getByText('Published revision 3 is available to customers', { exact: true })).toBeVisible();
  expect(writes).toEqual([expect.objectContaining({ expectedRevision: offering.revision, serviceOwner: 'Explicit synthetic owner update' })]);
  await page.getByRole('link', { name: 'Edit service details' }).click();
  await expect(editor.getByLabel('Service owner', { exact: true })).toHaveValue('Explicit synthetic owner update');
  await editor.getByRole('button', { name: 'Close dialog' }).click();
  await expect(editor).toHaveCount(0);
});
