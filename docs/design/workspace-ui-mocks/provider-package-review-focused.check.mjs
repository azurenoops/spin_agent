import { chromium } from '../../../src/Ato.Copilot.Dashboard/node_modules/playwright-core/index.mjs';
import axe from '../../../src/Ato.Copilot.Dashboard/node_modules/axe-core/axe.js';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';

const htmlPath = fileURLToPath(new URL('./provider-package-review-focused.html', import.meta.url));
const html = readFileSync(htmlPath, 'utf8');
const captured = JSON.parse(html.match(/<script id="snapshot" type="application\/json">([\s\S]*?)<\/script>/)[1]);
const url = process.env.PACKAGE_MOCK_URL || pathToFileURL(htmlPath).href;
const live = `http://127.0.0.1:4196/workspaces/csp/authorizations/offerings/${captured.offeringId}/packages/${captured.status.packageId}`;
let assertions = 0;
const check = (condition, text) => { assert.ok(condition, text); assertions++; };
const browser = await chromium.launch();
try {
  check(captured.records.length === 69, 'All inspected candidate pages retained');
  check(captured.entries.length === captured.status.coverage.total, 'All source entries retained');
  check(captured.records.filter(record => record.publishedRecordId).length === captured.review.publication.records.length,
    'Publication count reconciles with captured records');
  for (const width of [1440, 390]) {
    const page = await browser.newPage({ viewport: { width, height: 1000 } });
    const errors = [], apiRequests = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (request.url().includes('/api/')) apiRequests.push(request.url()); });
    await page.goto(url);
    check(await page.getByRole('heading', { name: 'Review source package', exact: true }).isVisible(), 'Clear title');
    check(await page.locator('#file-count').innerText() === String(captured.entries.length), 'Exact source count');
    check(await page.locator('#record-count').innerText() === String(captured.records.length), 'Exact record count');
    check(await page.locator('#published-count').innerText() === String(captured.review.publication.records.length), 'Exact publication count');
    check(await page.locator('#review-count').innerText() === String(captured.records.filter(r => r.reviewState === 'Reviewed').length), 'Actual supporting review count');
    check(await page.locator('#next-title').innerText() === 'Inspect the published set', 'Next action reflects published outcome');
    check(await page.locator('#live-page').getAttribute('href') === live, 'Exact offering/package link');
    await page.getByRole('tab', { name: 'Overview', exact: true }).focus();
    await page.keyboard.press('ArrowRight');
    check(await page.getByRole('tab', { name: 'Extracted records', exact: true }).getAttribute('aria-selected') === 'true', 'Keyboard tab selection');
    check(await page.getByRole('table', { name: 'Extracted package records' }).isVisible(), 'Searchable records view');
    check(await page.locator('#record-rows tr').count() === 8, 'Compact first page');
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    check((await page.locator('#record-range').innerText()).startsWith('9–16'), 'Real pagination');
    const capability = captured.records.find(record => record.type === 'Capability' && record.publishedRecordId);
    await page.getByLabel('Record type', { exact: true }).selectOption('Capability');
    await page.getByLabel('Review status', { exact: true }).selectOption('Published');
    await page.getByLabel('Search records', { exact: true }).fill(capability.name);
    check(await page.locator('#record-rows tr').count() === 1, 'Combined name/type/status filters');
    const opener = page.getByRole('button', { name: `Inspect ${capability.name}`, exact: true });
    await opener.focus();
    await page.keyboard.press('Enter');
    const drawer = page.getByRole('dialog', { name: capability.name, exact: true });
    check(await drawer.isVisible(), 'Focused detail drawer');
    check((await drawer.innerText()).includes(capability.description), 'Captured description unchanged');
    check((await drawer.innerText()).includes(capability.citations[0].archivePath), 'Actual citation path');
    check(await drawer.getByRole('link', { name: 'Continue with this record in SPIN (new tab)' }).getAttribute('href') ===
      `${live}?candidate=${encodeURIComponent(capability.candidateId)}&page=${capability.sourcePage}`, 'Record handoff retains original source page');
    await drawer.getByLabel('Try a local review note').fill('Local-only review observation');
    await page.keyboard.press('Escape');
    check(await page.getByRole('dialog', { name: 'Unsaved local note' }).isVisible(), 'Unsaved close guard');
    await page.getByRole('button', { name: 'Keep editing', exact: true }).click();
    check(await drawer.getByLabel('Try a local review note').inputValue() === 'Local-only review observation', 'Retained note');
    await drawer.getByRole('button', { name: 'Save local note' }).click();
    check((await drawer.innerText()).includes('Review status unchanged'), 'No false review success');
    check(await page.locator('#snapshot').textContent() === JSON.stringify(captured).replace(/</g, '\\u003c'), 'Captured source immutable');
    await drawer.getByRole('button', { name: 'Close', exact: true }).focus();
    await page.keyboard.press('Tab');
    check(await drawer.getByRole('button', { name: 'Close record', exact: true }).evaluate(element => element === document.activeElement), 'Drawer focus wrap');
    await page.keyboard.press('Escape');
    check(await page.getByRole('dialog').count() === 0, 'Escape dismisses clean drawer');
    check(await page.getByRole('button', { name: `Inspect ${capability.name}`, exact: true }).evaluate(element => element === document.activeElement),
      'Focus restored after background list rerender');
    check((await page.locator('#record-rows').innerText()).includes('Local note saved · no review change'), 'Local note distinguished');
    await page.getByLabel('Search records', { exact: true }).fill('no-such-captured-record-xyz');
    check(await page.locator('#record-empty').isVisible(), 'Honest filtered empty state');
    await page.getByRole('tab', { name: 'Source files', exact: true }).click();
    check(await page.locator('#source-rows tr').count() === captured.entries.length, 'Every archive entry listed');
    const entry = captured.entries[0];
    await page.getByRole('button', { name: `Inspect file ${entry.fileName}`, exact: true }).click();
    const info = page.getByRole('dialog', { name: entry.fileName, exact: true });
    check((await info.innerText()).includes(`${entry.byteLength.toLocaleString()} bytes`), 'Actual file size');
    await info.getByText('Archive location & integrity', { exact: true }).click();
    check((await info.innerText()).includes(entry.sha256), 'Retained integrity metadata');
    await page.keyboard.press('Escape');
    await page.getByRole('tab', { name: 'Publication', exact: true }).click();
    check((await page.locator('#outcome-count').innerText()).includes(String(captured.review.publication.records.length)), 'Exact historical set');
    check(await page.getByRole('button', { name: /^(Approve|Publish)(\s|$)/ }).count() === 0, 'No fake approval/publication action');
    await page.getByRole('button', { name: 'Reset captured view', exact: true }).click();
    check(await page.getByRole('tab', { name: 'Overview', exact: true }).getAttribute('aria-selected') === 'true', 'Reset without live refresh');
    await page.getByRole('button', { name: 'Discard local notes', exact: true }).click();
    check(await page.locator('#discard-notes').isHidden(), 'Local note reset');
    await page.addScriptTag({ content: axe.source });
    for (const tab of ['Overview', 'Extracted records', 'Source files', 'Publication']) {
      await page.getByRole('tab', { name: tab, exact: true }).click();
      const accessibility = await page.evaluate(async () => window.axe.run('main',
        { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
      check(accessibility.violations.length === 0, `${width}px ${tab} WCAG checks: ${JSON.stringify(accessibility.violations.map(v => v.id))}`);
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px ${tab} no page overflow`);
    }
    check(errors.length === 0, `No runtime errors: ${errors.join('; ')}`);
    check(apiRequests.length === 0, 'No API requests from prototype');
    await page.close();
  }
  console.log(`Passed ${assertions} package-prototype assertions at 1440px and 390px; zero API requests.`);
} finally {
  await browser.close();
}
