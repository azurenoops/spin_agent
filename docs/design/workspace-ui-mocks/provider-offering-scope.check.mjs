import { chromium } from '../../../src/Ato.Copilot.Dashboard/node_modules/playwright-core/index.mjs';
import axe from '../../../src/Ato.Copilot.Dashboard/node_modules/axe-core/axe.js';
import assert from 'node:assert/strict';
import { mkdirSync, writeFileSync } from 'node:fs';

const base = process.env.PROVIDER_PREVIEW_URL || 'http://127.0.0.1:4196';
const mockBase = process.env.PROVIDER_MOCK_URL || 'http://127.0.0.1:4198';
const id = process.env.OFFERING_ID;
assert.match(id || '', /^[a-f0-9-]{36}$/i, 'Set OFFERING_ID to the offering to inspect');
const root = `/api/csp/offerings/${encodeURIComponent(id)}`;
const route = `/workspaces/csp/authorizations/offerings/${encodeURIComponent(id)}/boundary`;
const output = 'src/Ato.Copilot.Dashboard/focused-offering-live-results';
mkdirSync(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ baseURL: base });
const errors = [], writes = [], failures = [], layouts = [];
try {
  const config = await context.request.get('/api/auth/login-config');
  assert.equal(config.status(), 200);
  const descriptor = (await config.json()).data.simulation;
  const admin = descriptor?.identities.find(identity => identity.id === 'dev-cspadmin' && identity.roles.includes('CSP.Admin'));
  assert.ok(admin, 'Verified ordinary developer CSP.Admin identity is required');
  const login = await context.request.post(`/api/auth/simulate?identityId=${encodeURIComponent(admin.id)}`);
  assert.equal(login.status(), 204);
  const read = async suffix => {
    const response = await context.request.get(root + suffix, { headers: { 'X-Workspace-Kind': 'csp', 'X-Workspace-Mode': 'ordinary' } });
    assert.equal(response.status(), 200, `Read ${suffix}`);
    return (await response.json()).data;
  };
  const suffixes = ['', '/overview', '/boundary-overview', '/boundary-revisions', '/package-versions', '/findings', '/authorization-records', '/impact-reviews'];
  const snapshot = async () => Object.fromEntries(await Promise.all(suffixes.map(async suffix => [suffix, await read(suffix)])));
  const before = await snapshot();
  const boundaryId = before[''].currentBoundaryRevisionId;
  const boundary = boundaryId ? await read(`/boundary-revisions/${encodeURIComponent(boundaryId)}`) : null;
  // Every feature mutation is blocked as well as counted; only the sign-in above may write.
  await context.route('**/api/**', async intercepted => {
    const request = intercepted.request();
    if (request.method() !== 'GET') {
      writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
      await intercepted.abort();
    } else await intercepted.continue();
  });
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => {
    if (response.url().includes('/api/') && response.status() >= 400) failures.push(`${response.status()} ${new URL(response.url()).pathname}`);
  });
  const mock = await context.newPage();
  const layout = async (target, scope, services, duties) => target.locator(scope).evaluate((element, selectors) => {
    const rect = element.getBoundingClientRect();
    const serviceGrid = element.querySelector(selectors.services);
    const dutyGrid = element.querySelector(selectors.duties);
    return {
      width: rect.width,
      panelRadius: getComputedStyle(element.querySelector('.panel, .provider-panel') || element).borderRadius,
      servicesColumns: serviceGrid ? getComputedStyle(serviceGrid).gridTemplateColumns.split(' ').length : 0,
      dutyColumns: dutyGrid ? getComputedStyle(dutyGrid).gridTemplateColumns.split(' ').length : 0,
      clipped: document.documentElement.scrollWidth > innerWidth,
    };
  }, { services, duties });
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1100 });
    await mock.setViewportSize({ width, height: 1100 });
    await page.goto(route);
    const scope = page.getByRole('region', { name: 'Service scope' });
    await scope.waitFor();
    if (boundary) {
      await page.getByText(`Recorded boundary version ${boundary.version} · Working recorded scope`, { exact: true }).waitFor();
      assert.deepEqual(await scope.getByRole('list', { name: 'Included services' }).getByRole('listitem').allTextContents(), boundary.services);
      for (const duty of boundary.providerResponsibilities) assert.ok((await page.getByRole('region', { name: 'Provider duties' }).innerText()).includes(duty));
      for (const duty of boundary.customerResponsibilities) assert.ok((await page.getByRole('region', { name: 'Customer duties' }).innerText()).includes(duty));
      const provenance = scope.getByText('Hosting identity & source provenance', { exact: true });
      await provenance.focus();
      await page.keyboard.press('Enter');
      assert.ok((await scope.innerText()).includes(boundary.scopeStatement), 'Complete recorded scope text is retained');
      for (const citation of boundary.citations) assert.ok(await scope.getByRole('link', { name: citation.archivePath, exact: true }).count(), 'Recorded citation remains available');
      await page.keyboard.press('Enter');
      const edit = scope.getByRole('button', { name: 'Edit boundary' });
      await edit.focus();
      await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Edit boundary' });
      await dialog.waitFor();
      assert.equal(await dialog.getByRole('textbox', { name: 'Boundary name', exact: true }).inputValue(), boundary.name);
      assert.equal(await dialog.getByRole('textbox', { name: 'Explicit scope statement', exact: true }).inputValue(), boundary.scopeStatement);
      assert.equal(await dialog.getByRole('textbox', { name: 'Services (one per line)', exact: true }).inputValue(), boundary.services.join('\n'));
      await dialog.getByRole('button', { name: 'Cancel editing' }).focus();
      await page.keyboard.press('Tab');
      assert.equal(await dialog.getByRole('button', { name: 'Close dialog' }).evaluate(element => element === document.activeElement), true);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Open editor does not clip');
      await page.keyboard.press('Escape');
      assert.equal(await dialog.count(), 0);
      assert.equal(await edit.evaluate(element => element === document.activeElement), true);
      const supporting = page.getByText('Supporting records, versions & workflow', { exact: true });
      await supporting.click();
      const history = page.getByText('Version history', { exact: true });
      await history.click();
      await page.getByRole('heading', { name: `Version ${boundary.version} · ${boundary.name} (current)`, exact: true }).waitFor();
      await history.click();
      await supporting.click();
    }
    await page.addScriptTag({ content: axe.source });
    const accessibility = await page.evaluate(async () => window.axe.run('main', { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
    assert.deepEqual(accessibility.violations, [], `Scope accessibility at ${width}`);
    await mock.goto(`${mockBase}/provider-offering-workspace-focused.html`);
    await mock.getByRole('tab', { name: 'Scope & duties' }).click();
    // The application scrolls main rather than the document. Fit that scroll
    // container natively so a full-page capture does not omit the duty cards.
    const captureHeight = await page.locator('main').evaluate(element => Math.max(1100, element.scrollHeight + innerHeight - element.clientHeight + 16));
    await page.setViewportSize({ width, height: captureHeight });
    await mock.setViewportSize({ width, height: captureHeight });
    await page.getByRole('heading', { level: 1 }).click();
    assert.equal(await page.locator('main').evaluate(element => element.scrollHeight <= element.clientHeight), true, 'Complete tab fits the native capture');
    await page.screenshot({ path: `${output}/scope-production-${width}.png`, fullPage: true });
    await page.locator('.offering-scope').screenshot({ path: `${output}/scope-production-body-${width}.png` });
    await mock.screenshot({ path: `${output}/scope-mock-${width}.png`, fullPage: true });
    await mock.locator('#scope').screenshot({ path: `${output}/scope-mock-body-${width}.png` });
    const actual = await layout(page, '.offering-scope', '.offering-scope-services', '.offering-scope-duties');
    const approved = await layout(mock, '#scope', '.scope-services', '.columns');
    assert.equal(actual.servicesColumns, approved.servicesColumns, 'Approved service tile columns');
    assert.equal(actual.dutyColumns, approved.dutyColumns, 'Approved provider/customer grouping');
    assert.equal(actual.panelRadius, approved.panelRadius, 'Approved card hierarchy styling');
    assert.equal(actual.clipped, false);
    assert.equal(approved.clipped, false);
    layouts.push({ width, captureHeight, actual, approved });
  }
  assert.deepEqual(await snapshot(), before, 'Relevant live GET snapshots must remain unchanged');
  if (boundary) assert.deepEqual(await read(`/boundary-revisions/${encodeURIComponent(boundaryId)}`), boundary);
  assert.deepEqual(writes, []);
  assert.deepEqual(errors, []);
  const result = { route, offeringRevision: before[''].revision, lifecycle: before[''].lifecycle,
    boundaryVersion: boundary?.version ?? null, recordedServices: boundary?.services.length ?? null,
    publishedCapabilities: before['/overview'].capabilities.published,
    publishedReleaseRevisions: before['/overview'].capabilities.publishedReleaseRevisions,
    layouts, writes, pageErrors: errors, apiWarnings: [...new Set(failures)], snapshotsUnchanged: true };
  writeFileSync(`${output}/scope-verification.json`, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
} finally {
  await browser.close();
}
