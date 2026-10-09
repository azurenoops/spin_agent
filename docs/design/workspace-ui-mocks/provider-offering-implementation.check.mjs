import { chromium } from '../../../src/Ato.Copilot.Dashboard/node_modules/playwright-core/index.mjs';
import axe from '../../../src/Ato.Copilot.Dashboard/node_modules/axe-core/axe.js';
import assert from 'node:assert/strict';
import { mkdirSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const base = process.env.PROVIDER_PREVIEW_URL || 'http://127.0.0.1:4196';
const mockBase = process.env.PROVIDER_MOCK_URL || 'http://127.0.0.1:4198';
const id = process.env.OFFERING_ID;
assert.match(id || '', /^[a-f0-9-]{36}$/i, 'Set OFFERING_ID to the selected offering');
const root = `/api/csp/offerings/${encodeURIComponent(id)}`;
const route = `/workspaces/csp/authorizations/offerings/${encodeURIComponent(id)}/inherited-coverage?task=capabilities`;
const output = 'src/Ato.Copilot.Dashboard/focused-offering-live-results';
mkdirSync(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ baseURL: base });
const errors = [], writes = [], failures = [], layouts = [];
try {
  const config = await context.request.get('/api/auth/login-config');
  assert.equal(config.status(), 200);
  const admin = (await config.json()).data.simulation?.identities.find(identity =>
    identity.id === 'dev-cspadmin' && identity.roles.includes('CSP.Admin'));
  assert.ok(admin, 'Ordinary developer CSP.Admin must be reported by the sign-in contract');
  assert.equal((await context.request.post(`/api/auth/simulate?identityId=${encodeURIComponent(admin.id)}`)).status(), 204);
  const read = async url => {
    const response = await context.request.get(url, { headers: { 'X-Workspace-Kind': 'csp', 'X-Workspace-Mode': 'ordinary' } });
    assert.equal(response.status(), 200, `Read ${url}`);
    return (await response.json()).data;
  };
  const overview = await read(`${root}/boundary-overview`);
  const selected = [...overview.capabilities.items].sort((a, b) => a.name.localeCompare(b.name))[0];
  assert.ok(selected?.packageId && selected?.candidateId, 'This comparison requires a retained-source implementation');
  const candidateUrl = `/api/csp/package-imports/${encodeURIComponent(selected.packageId)}/candidates`;
  const candidates = async () => {
    const records = [];
    let next = 1;
    for (;;) {
      const page = await read(`${candidateUrl}?page=${next++}&pageSize=25`);
      records.push(...page.items);
      if (records.length >= page.total) return records;
      assert.ok(page.items.length, 'Candidate pages must make progress');
    }
  };
  const urls = [root, `${root}/overview`, `${root}/boundary-overview`, `${root}/package-versions`];
  const snapshot = async () => ({
    offeringAndRelease: Object.fromEntries(await Promise.all(urls.map(async url => [url, await read(url)]))),
    retainedCandidates: await candidates(),
  });
  const before = await snapshot();
  const source = before.retainedCandidates.find(candidate => candidate.candidateId === selected.candidateId);
  assert.ok(source, 'The exact retained candidate must exist');
  await context.route('**/api/**', async intercepted => {
    if (intercepted.request().method() !== 'GET') {
      writes.push(`${intercepted.request().method()} ${new URL(intercepted.request().url()).pathname}`);
      await intercepted.abort();
    } else await intercepted.continue();
  });
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => {
    if (response.url().includes('/api/') && response.status() >= 400)
      failures.push(`${response.status()} ${new URL(response.url()).pathname}`);
  });
  const mock = await context.newPage();
  const layout = async drawer => drawer.evaluate(element => ({
    width: element.getBoundingClientRect().width,
    fontSize: getComputedStyle(element).fontSize,
    headerPadding: getComputedStyle(element.querySelector('header, .dialog-head')).padding,
    bodyPadding: getComputedStyle(element.querySelector('.dialog-body') || element.querySelector('.offering-implementation-body').parentElement).padding,
    radius: getComputedStyle(element).borderRadius,
    clipped: element.scrollWidth > element.clientWidth,
  }));
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    await mock.setViewportSize({ width, height: 1000 });
    await page.goto(route);
    const opener = page.getByRole('table', { name: 'Service implementations' }).getByRole('button', { name: selected.name, exact: true });
    await opener.focus();
    await page.keyboard.press('Enter');
    const drawer = page.getByRole('dialog', { name: selected.name, exact: true });
    await drawer.waitFor();
    assert.equal(await drawer.locator('.offering-implementation-statement').textContent(), source.description);
    assert.ok((await drawer.innerText()).includes(`revision ${selected.releaseRevision}`));
    assert.ok((await drawer.innerText()).includes(`Retained source proposal revision ${source.revision}`));
    assert.ok((await drawer.innerText()).includes('not an immutable release payload'));
    assert.equal(await drawer.getByRole('textbox').count(), 0, 'Inspection must not simulate an editor');
    for (const [control, duty] of Object.entries(source.controlDuties)) {
      assert.ok((await drawer.getByRole('group', { name: duty || 'Duty not reported', exact: true }).innerText()).includes(control));
    }
    assert.equal(await drawer.getByRole('link', { name: 'Open capability workflow' }).getAttribute('href'),
      `/workspaces/csp/security-capabilities/${encodeURIComponent(selected.capabilityId)}`);
    assert.equal(await drawer.getByRole('link', { name: 'Review retained source' }).getAttribute('href'),
      `/workspaces/csp/authorizations/offerings/${id}/packages/${selected.packageId}/candidates/${selected.candidateId}`);
    assert.equal(await drawer.locator('details[open]').count(), 0);
    if (source.citations.length) {
      await drawer.getByText('Retained source citations', { exact: true }).click();
      for (const citation of source.citations) {
        const text = await drawer.innerText();
        for (const value of [citation.archivePath, citation.locator, citation.quote, citation.artifactId])
          assert.ok(text.includes(value), 'Complete retained citation remains available');
      }
      await drawer.getByText('Retained source citations', { exact: true }).click();
    }
    await drawer.getByText('Source and release identities', { exact: true }).click();
    assert.ok((await drawer.innerText()).includes(selected.releaseId), 'Exact pinned release identity is retained');
    await drawer.getByText('Source and release identities', { exact: true }).click();
    await page.addScriptTag({ content: axe.source });
    assert.deepEqual(await page.evaluate(async () => (await window.axe.run('.offering-implementation-drawer',
      { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } })).violations), []);
    await mock.goto(`${mockBase}/provider-offering-workspace-focused.html`);
    await mock.getByRole('tab', { name: /^Capabilities/ }).click();
    await mock.getByRole('table').getByRole('button', { name: selected.name, exact: true }).click();
    const mockDrawer = mock.getByRole('dialog', { name: selected.name, exact: true });
    const productionLayout = await layout(drawer);
    const mockLayout = await layout(mockDrawer);
    assert.equal(productionLayout.width, mockLayout.width);
    assert.equal(productionLayout.fontSize, mockLayout.fontSize);
    assert.equal(productionLayout.headerPadding, mockLayout.headerPadding);
    assert.equal(productionLayout.bodyPadding, mockLayout.bodyPadding);
    assert.equal(productionLayout.radius, mockLayout.radius);
    assert.equal(productionLayout.clipped, false);
    layouts.push({ width, productionLayout, mockLayout });
    await page.screenshot({ path: `${output}/implementation-production-${width}.png`, fullPage: true });
    await mock.screenshot({ path: `${output}/implementation-mock-${width}.png`, fullPage: true });
    await drawer.screenshot({ path: `${output}/implementation-production-drawer-${width}.png` });
    await mockDrawer.screenshot({ path: `${output}/implementation-mock-drawer-${width}.png` });
    await drawer.getByRole('button', { name: 'Close details' }).focus();
    await page.keyboard.press('Tab');
    assert.equal(await drawer.getByRole('button', { name: 'Close dialog' }).evaluate(element => element === document.activeElement), true);
    await page.keyboard.press('Shift+Tab');
    assert.equal(await drawer.getByRole('button', { name: 'Close details' }).evaluate(element => element === document.activeElement), true);
    await page.keyboard.press('Escape');
    assert.equal(await drawer.count(), 0);
    assert.equal(await opener.evaluate(element => element === document.activeElement), true);
  }
  const after = await snapshot();
  assert.deepEqual(after, before, 'Selected offering, source candidates and published release references must remain unchanged');
  assert.deepEqual(errors, []);
  assert.deepEqual(writes, []);
  const result = { layouts, featureWrites: writes, javascriptErrors: errors, shellHttpFailures: failures,
    snapshotsUnchanged: true,
    snapshotHashes: { before: createHash('sha256').update(JSON.stringify(before)).digest('hex'),
      after: createHash('sha256').update(JSON.stringify(after)).digest('hex') },
    selected: { name: selected.name, releaseId: selected.releaseId, releaseRevision: selected.releaseRevision,
      candidateId: selected.candidateId, candidateRevision: source.revision },
    limitations: 'Retained candidate context is not an immutable historical release payload. Mock statements are not copied. No mission acceptance or backend authorization assertion.' };
  writeFileSync(`${output}/implementation-verification.json`, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
} finally {
  await browser.close();
}
