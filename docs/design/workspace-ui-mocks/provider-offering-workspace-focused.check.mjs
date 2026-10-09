import { chromium } from '../../../src/Ato.Copilot.Dashboard/node_modules/playwright-core/index.mjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Run from the repository root. No server or application session is required.
const mock = new URL('./provider-offering-workspace-focused.html', import.meta.url);
const html = readFileSync(mock, 'utf8');
assert.match(html, /<dialog/);
assert.match(html, /Prototype/);
assert.match(html, /connect-src 'none'/);
assert.doesNotMatch(html, /fetch\(|XMLHttpRequest|https:\/\/.*\.js/);

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
const page = await context.newPage();
const errors = [], requests = [];
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => requests.push({ url: request.url(), method: request.method() }));
let assertions = 4;
const check = (value, message) => { assert.ok(value, message); assertions++; };

try {
  // Arrange
  await page.goto(mock.href);
  const sections = ['Overview', 'Capabilities 8', 'Scope & duties', 'Sources & findings', 'Release & changes', 'Mission use'];

  // Act
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    for (const name of sections) {
      await page.getByRole('tab', { name, exact: true }).click();
      // Assert
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px overflow on ${name}`);
      check(await page.getByRole('tabpanel').filter({ visible: true }).count() === 1, 'One visible panel');
    }
    await page.getByRole('tab', { name: 'Capabilities 8', exact: true }).click();
    await page.getByRole('button', { name: 'Audit collection', exact: true }).click();
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px drawer overflow`);
    await page.keyboard.press('Escape');
  }

  // Arrange
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole('tab', { name: 'Capabilities 8', exact: true }).click();
  // Act
  await page.locator('#search').fill('AU-2');
  // Assert
  check(await page.locator('#results').innerText() === '1–1 of 1 capabilities · page 1 of 1', 'Control search');
  await page.getByRole('button', { name: 'Audit collection', exact: true }).click();
  check(await page.locator('#detail').isVisible(), 'Detail opens');
  await page.keyboard.press('Escape');
  check(!await page.locator('#detail').isVisible(), 'Escape closes detail');
  check(await page.getByRole('button', { name: 'Audit collection', exact: true }).evaluate(el => el === document.activeElement), 'Focus returns');
  await page.locator('#search').fill('');
  await page.locator('#filter').selectOption('pending');
  check(await page.locator('#results').innerText() === '0 capabilities', 'No invented pending proposals');
  await page.getByRole('button', { name: 'Reset search & filters' }).click();
  await page.locator('#sort').selectOption('desc');
  check((await page.locator('[data-capability]').first().innerText()).startsWith('Vulnerability management'), 'Descending sort');
  await page.locator('#next').click();
  check((await page.locator('#results').innerText()).includes('page 2 of 2'), 'Pagination next');
  await page.locator('#previous').click();
  await page.getByRole('tab', { name: 'Capabilities 8', exact: true }).focus();
  await page.keyboard.press('ArrowRight');
  check(await page.getByRole('tab', { name: 'Scope & duties', exact: true }).getAttribute('aria-selected') === 'true', 'Keyboard tabs');

  // Arrange
  await page.getByRole('button', { name: 'Edit service details', exact: true }).click();
  // Act
  await page.locator('#owner').fill('Prototype review team');
  await page.keyboard.press('Escape');
  // Assert
  check(await page.locator('#leave-warning').isVisible(), 'Dirty-close warning');
  check(await page.locator('#editor').isVisible(), 'Dirty form stays open');
  await page.getByRole('button', { name: 'Save simulated draft', exact: true }).click();
  check(await page.locator('#draft-badge').isVisible(), 'Local saved-draft badge');
  check((await page.locator('#changes').innerText()).includes('Prototype review team'), 'Local diff');
  check((await page.locator('#changes').innerText()).includes('Captured: Northstar'), 'Before/after provenance');
  await page.getByRole('button', { name: 'Discard simulated draft', exact: true }).click();
  check(!await page.locator('#draft-badge').isVisible(), 'Draft discard');
  await page.getByRole('button', { name: 'Edit service details', exact: true }).click();
  await page.locator('#owner').fill('Unsaved demo');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('button', { name: 'Discard unsaved edits', exact: true }).click();
  check(!await page.locator('#editor').isVisible(), 'Discard unsaved closes editor');
  await page.getByRole('button', { name: 'Edit service details', exact: true }).click();
  check(await page.locator('#owner').inputValue() === 'Northstar Shared Services Demo Team', 'Original restored');
  await page.locator('#owner').fill('   ');
  await page.getByRole('button', { name: 'Save simulated draft', exact: true }).click();
  check(await page.locator('#editor').isVisible(), 'Reject blank simulated owner');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('button', { name: 'Discard unsaved edits', exact: true }).click();
  await page.getByRole('button', { name: 'What is simulated?', exact: true }).click();
  check(await page.locator('#info').isVisible(), 'Prototype explanation');
  await page.keyboard.press('Escape');
  await page.reload();
  check(!await page.locator('#draft-badge').isVisible(), 'Reload discards state');

  // Arrange
  const axe = readFileSync(new URL('../../../src/Ato.Copilot.Dashboard/node_modules/axe-core/axe.min.js', import.meta.url), 'utf8');
  await page.addScriptTag({ content: axe });
  // Act
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    for (const name of sections) {
      await page.getByRole('tab', { name, exact: true }).click();
      const result = await page.evaluate(async () => await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
      // Assert
      check(result.violations.length === 0, `${width}px ${name} accessibility: ${JSON.stringify(result.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) })))}`);
    }
  }
  check(errors.length === 0, `JavaScript errors: ${errors.join(', ')}`);
  check(requests.every(request => request.method === 'GET' && request.url === mock.href), 'No outbound API requests or writes');
  console.log(`PASS: ${assertions} assertions; 1440px/390px; six tabs; search/filter/sort/pagination; dialogs/focus/Escape; dirty edits/save/discard; axe WCAG checks; no JS errors or API requests/writes.`);
} finally {
  await browser.close();
}
