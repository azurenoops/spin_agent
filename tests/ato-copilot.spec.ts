import { test, expect } from '@playwright/test';

const BASE = 'https://ca-ato-copilot-dashboard-v2.blackwater-9393aa1a.centralus.azurecontainerapps.io';

test.describe('ATO Copilot public routes', () => {
  test('landing page returns index and has root div', async ({ page }) => {
    await page.goto(BASE + '/');
    await expect(page).toHaveTitle(/ATO Copilot/i);
    const root = await page.locator('#root');
    await expect(root).toBeVisible();
  });

  test('health endpoint returns healthy JSON', async ({ request }) => {
    const r = await request.get(BASE + '/api/health');
    expect(r.status()).toBe(200);
    const body = await r.json();
    expect(body.status).toBe('healthy');
    expect(body.service).toContain('ATO Copilot');
  });

  test('static assets reachable (js/css)', async ({ request }) => {
    const js = await request.get(BASE + '/assets/index-qJReiUyJ.js');
    expect(js.status()).toBe(200);
    const css = await request.get(BASE + '/assets/index-ltZLVpHo.css');
    expect(css.status()).toBe(200);
  });

  test('favicon returns 404 (expected)', async ({ request }) => {
    const r = await request.get(BASE + '/favicon.ico');
    // The deployment currently returns 404 for favicon
    expect(r.status()).toBe(404);
  });

  test('robots.txt reachable (serves index html)', async ({ request }) => {
    const r = await request.get(BASE + '/robots.txt');
    expect(r.status()).toBe(200);
    const text = await r.text();
    expect(text).toContain('<div id="root"');
  });

  test('SPA fallback returns index for unknown paths', async ({ request }) => {
    const r = await request.get(BASE + '/this-path-should-404-during-server-but-serve-spa');
    expect(r.status()).toBe(200);
    const text = await r.text();
    expect(text).toContain('<div id="root"');
  });

  test.skip('auth-gated: dashboard requires auth (SKIPPED(REQUIRES_AUTH))', async ({ page }) => {
    // This test is intentionally skipped because no auth is available in the test run
    await page.goto(BASE + '/dashboard');
  });
});
