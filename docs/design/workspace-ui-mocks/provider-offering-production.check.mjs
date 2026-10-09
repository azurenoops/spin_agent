import { chromium } from '../../../src/Ato.Copilot.Dashboard/node_modules/playwright-core/index.mjs';
import axe from '../../../src/Ato.Copilot.Dashboard/node_modules/axe-core/axe.js';
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';

const base = process.env.PROVIDER_PREVIEW_URL || 'http://127.0.0.1:4196';
const id = process.env.OFFERING_ID;
assert.match(id || '', /^[a-f0-9-]{36}$/i, 'Set OFFERING_ID to the offering to inspect');
const root = `/api/csp/offerings/${encodeURIComponent(id)}`;
const route = `/workspaces/csp/authorizations/offerings/${encodeURIComponent(id)}`;
const output = 'src/Ato.Copilot.Dashboard/focused-offering-live-results';
mkdirSync(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ baseURL: base });
let assertions = 0;
const check = (value, message) => { assert.ok(value, message); assertions++; };
const errors = [], writes = [], failures = [];
try {
  // Arrange: use the development sign-in contract, never a guessed principal.
  const config = await context.request.get('/api/auth/login-config');
  assert.equal(config.status(), 200);
  const descriptor = (await config.json()).data.simulation;
  const admin = descriptor?.identities.find(identity => identity.id === 'dev-cspadmin' && identity.roles.includes('CSP.Admin'));
  assert.ok(admin, 'Verified ordinary developer CSP.Admin identity is required');
  const login = await context.request.post(`/api/auth/simulate?identityId=${encodeURIComponent(admin.id)}`);
  assert.equal(login.status(), 204, 'Development sign-in must succeed');
  const read = async suffix => {
    const response = await context.request.get(root + suffix, { headers: { 'X-Workspace-Kind': 'csp', 'X-Workspace-Mode': 'ordinary' } });
    assert.equal(response.status(), 200, `Read ${suffix}`);
    return (await response.json()).data;
  };
  const suffixes = ['', '/overview', '/boundary-overview', '/boundary-revisions', '/package-versions', '/findings', '/authorization-records', '/impact-reviews'];
  const before = Object.fromEntries(await Promise.all(suffixes.map(async suffix => [suffix, await read(suffix)])));
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => {
    if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
  });
  page.on('response', response => {
    if (response.url().includes('/api/') && response.status() >= 400) failures.push(`${response.status()} ${new URL(response.url()).pathname}`);
  });
  const nav = () => page.getByRole('navigation', { name: 'Offering sections' });
  // Act / Assert: actual production source and actual API records; no fixture routes.
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(route);
    await page.getByRole('region', { name: 'What customers can use' }).waitFor();
    check(await page.getByText(`Offering identity: ${before[''].lifecycle} · revision ${before[''].revision}`, { exact: true }).isVisible(), 'Exact identity lifecycle/revision');
    check((await page.getByLabel('Offering metrics').innerText()).includes(String(before['/overview'].capabilities.published)), 'Published count from actual API');
    check(await nav().getByRole('link').count() === 6, 'Approved six destinations');
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px overview overflow`);
    await page.screenshot({ path: `${output}/overview-${width}.png`, fullPage: true });
    for (const name of ['Capabilities', 'Scope & duties', 'Sources & findings', 'Release & changes', 'Mission use']) {
      await nav().getByRole('link', { name, exact: true }).click();
      const status = page.getByText('Loading...', { exact: true });
      await status.waitFor({ state: 'hidden' });
      if (name === 'Capabilities') {
        const table = page.getByRole('table', { name: 'Service implementations' });
        await table.waitFor();
        const query = page.getByRole('textbox', { name: 'Search capabilities' });
        await page.getByRole('combobox', { name: 'Sort by name' }).selectOption('desc');
        await page.getByRole('combobox', { name: 'Release state' }).selectOption('pending');
        if (!before['/boundary-overview'].capabilities.awaitingReview)
          check((await table.innerText()).includes('No capabilities match this search.'), 'Actual no pending proposals');
        await page.getByRole('combobox', { name: 'Release state' }).selectOption('all');
        const next = page.getByRole('button', { name: 'Next', exact: true });
        if (await next.isVisible()) {
          await next.click();
          await page.getByRole('button', { name: 'Previous', exact: true }).click();
          assertions++;
        }
        const references = await table.getByRole('row').nth(1).getByRole('cell').nth(1).innerText();
        const control = references.split(',')[0].trim();
        await query.fill(control);
        check(await table.getByRole('button').count() > 0, 'Retained source control search');
        const opener = table.getByRole('button').first();
        const name = await opener.innerText();
        await opener.focus();
        await page.keyboard.press('Enter');
        const drawer = page.getByRole('dialog', { name, exact: true });
        await drawer.waitFor();
        check((await drawer.innerText()).includes('not an immutable release payload'), 'No fabricated immutable payload');
        const last = drawer.getByRole('button', { name: 'Close details', exact: true });
        await last.focus();
        await page.keyboard.press('Tab');
        check(await drawer.getByRole('button', { name: 'Close dialog' }).evaluate(element => element === document.activeElement), 'Drawer focus trap');
        await page.screenshot({ path: `${output}/capability-detail-${width}.png`, fullPage: true });
        await page.keyboard.press('Escape');
        check(await opener.evaluate(element => element === document.activeElement), 'Drawer trigger focus restored');
        await query.fill('');
      } else if (name === 'Scope & duties') {
        const providerDuties = page.getByRole('region', { name: 'Provider duties', exact: true });
        const customerDuties = page.getByRole('region', { name: 'Customer duties', exact: true });
        await providerDuties.waitFor();
        await customerDuties.waitFor();
        check(await providerDuties.isVisible() && await customerDuties.isVisible(), 'Recorded duty separation');
        const boundary = before['/boundary-revisions'].items.find(item => item.boundaryRevisionId === before[''].currentBoundaryRevisionId);
        if (boundary?.providerResponsibilities.length)
          check((await providerDuties.innerText()).includes(boundary.providerResponsibilities[0]), 'Provider duty text from retained boundary');
        await page.getByText('Hosting identity & source provenance', { exact: true }).click();
        if (boundary) check((await page.getByRole('region', { name: 'Service scope', exact: true }).innerText()).includes(boundary.snapshotHash), 'Exact boundary provenance');
      } else if (name === 'Sources & findings') {
        await page.getByRole('table', { name: 'Source packages and documents' }).waitFor();
        check((await page.getByRole('region', { name: 'Source packages & documents' }).innerText()).includes(before['/overview'].packages.items[0].package.name), 'Exact retained source package');
        check((await page.getByRole('link', { name: 'Review provider findings & evidence' }).getAttribute('href')).startsWith(route), 'Offering-scoped finding handoff');
        await page.getByRole('table', { name: 'Source packages and documents' }).getByRole('link', { name: 'Review package', exact: true }).first().click();
        await page.getByRole('group', { name: 'Package review views' }).waitFor();
        check(page.url().startsWith(`${base}${route}/packages/`), 'Actual package review retains offering and workspace');
        check(await page.getByRole('button', { name: 'Source files', exact: true }).isVisible(), 'Native retained document review available');
        await page.getByRole('link', { name: 'All source packages', exact: true }).click();
        await page.getByRole('link', { name: 'Review provider findings & evidence' }).click();
        await page.getByRole('table', { name: 'Service findings' }).waitFor();
        if (before['/findings'].items.length)
          check((await page.getByRole('table', { name: 'Service findings' }).innerText()).includes(before['/findings'].items[0].title), 'Actual retained finding review output');
        await page.screenshot({ path: `${output}/finding-handoff-${width}.png`, fullPage: true });
        await nav().getByRole('link', { name: 'Sources & findings', exact: true }).click();
        await page.getByRole('table', { name: 'Source packages and documents' }).waitFor();
      } else if (name === 'Release & changes') {
        const release = page.getByRole('region', { name: 'Published release & working changes', exact: true });
        await release.waitFor();
        const text = await release.innerText();
        check(before['/overview'].capabilities.publishedReleaseRevisions.every(revision => text.includes(String(revision))), 'Actual canonical releases');
      } else if (name === 'Mission use') {
        const record = before['/boundary-overview'].missionSystems.items[0];
        const handoff = page.getByRole('region', { name: 'Mission use', exact: true }).getByRole('button', { name: /^View handoff for/ }).first();
        await handoff.click();
        const dialog = page.getByRole('dialog');
        check(await dialog.getByText('Adopted capabilities', { exact: true }).locator('..').locator('dd').innerText() ===
          String(record.adoptedCapabilityCount), 'Exact adoption count independent of association');
        check(await dialog.getByRole('link', { name: 'Inspect service relationship' }).getAttribute('href') ===
          `${route}/missions/${encodeURIComponent(record.assignmentId)}`, 'Exact allocation handoff');
        await dialog.getByRole('button', { name: 'Close handoff', exact: true }).click();
      }
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px ${name} overflow`);
      await page.addScriptTag({ content: axe.source });
      const accessibility = await page.evaluate(async () => await window.axe.run('main', { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
      check(accessibility.violations.length === 0, `${width}px ${name} WCAG: ${JSON.stringify(accessibility.violations.map(item => ({ id: item.id, targets: item.nodes.map(node => node.target) })))}`);
      await page.screenshot({ path: `${output}/${name.toLowerCase().replaceAll(/[^a-z]+/g, '-')}-${width}.png`, fullPage: true });
    }
  }
  const after = Object.fromEntries(await Promise.all(suffixes.map(async suffix => [suffix, await read(suffix)])));
  assert.deepEqual(after, before, 'Live identity, releases, boundary, sources, mission, finding, authorization and impact snapshots must remain unchanged');
  check(writes.length === 0, `No live feature writes: ${writes.join(', ')}`);
  check(errors.length === 0, `No JavaScript errors: ${errors.join(', ')}`);
  const outsideScopeReadWarnings = failures.filter(failure => failure === '403 /api/onboarding/organization-context');
  const featureFailures = failures.filter(failure => failure !== '403 /api/onboarding/organization-context');
  check(featureFailures.length === 0, `No failed offering reads: ${featureFailures.join(', ')}`);
  if (outsideScopeReadWarnings.length) console.warn(`Outside offering scope: ${outsideScopeReadWarnings.join(', ')}`);
  console.log(JSON.stringify({ assertions, viewports: [1440, 390], apiSnapshotsUnchanged: suffixes.length, writes, errors, featureFailures, outsideScopeReadWarnings,
    identityRevision: before[''].revision, publishedCapabilities: before['/overview'].capabilities.published,
    canonicalReleaseRevisions: before['/overview'].capabilities.publishedReleaseRevisions,
    sourceDocuments: before['/overview'].packages.sourceDocumentCount, openFindings: before['/overview'].openFindingCount,
    missions: before['/boundary-overview'].missionSystems.items.map(item => ({ associated: item.associated, adopted: item.adoptedCapabilityCount })),
  }, null, 2));
} finally {
  await context.close();
  await browser.close();
}
