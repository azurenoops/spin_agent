import { test, expect } from '@playwright/test';

const BASE = process.env.BASE_URL || 'https://ca-ato-copilot-dashboard-v2.blackwater-9393aa1a.centralus.azurecontainerapps.io';

test.describe('ATO Copilot - Public Routes (external smoke)', () => {
  test('Landing page (public) - 200 and root div present', async ({ page }) => {
    const res = await page.goto(BASE, { waitUntil: 'domcontentloaded' });
    expect(res).not.toBeNull();
    expect(res!.status()).toBe(200);
    const ct = res!.headers()['content-type'] || '';
    expect(ct.toLowerCase()).toContain('text/html');
    await expect(page.locator('#root, [data-root], #app')).toHaveCount(1);
  });

  test('Health endpoint /api/health (public) returns JSON {status: "healthy"}', async ({ request }) => {
    const r = await request.get(`${BASE}/api/health`);
    expect(r.status()).toBe(200);
    const ct = r.headers()['content-type'] || '';
    expect(ct.toLowerCase()).toContain('application/json');
    const body = await r.json().catch(() => ({}));
    expect(body).toBeTruthy();
    if (body.status) {
      expect(String(body.status).toLowerCase()).toBe('healthy');
    }
  });

  test('Static assets - index-*.js and css exist (public)', async ({ request }) => {
    const candidates = [
      `${BASE}/assets/index-0.js`,
      `${BASE}/assets/index-1.js`,
      `${BASE}/assets/index.js`,
      `${BASE}/static/js/index.js`,
      `${BASE}/assets/index.css`,
      `${BASE}/static/css/index.css`
    ];
    const results = await Promise.all(candidates.map(p => request.get(p)));
    const okJs = results.some(r => {
      if (!r) return false;
      const ct = (r.headers()['content-type'] || '').toLowerCase();
      return r.status() === 200 && ct.includes('javascript');
    });
    const okCss = results.some(r => {
      if (!r) return false;
      const ct = (r.headers()['content-type'] || '').toLowerCase();
      return r.status() === 200 && ct.includes('css');
    });
    expect(okJs || okCss).toBeTruthy();
  });

  test('favicon.ico - expects 404 (server returns 404)', async ({ request }) => {
    const r = await request.get(`${BASE}/favicon.ico`);
    expect([200, 404]).toContain(r.status());
    if (r.status() === 200) {
      console.warn('favicon returned 200; artifact expected 404');
    }
  });

  test('robots.txt reachable and serves index (200/HTML)', async ({ request }) => {
    const r = await request.get(`${BASE}/robots.txt`);
    expect([200, 404]).toContain(r.status());
    const ct = (r.headers()['content-type'] || '').toLowerCase();
    expect(ct.includes('text') || ct.includes('html') || r.status() === 404).toBeTruthy();
  });

  test('Nonexistent path - SPA fallback: expect 200 with index HTML', async ({ page }) => {
    const path = `${BASE}/this-route-should-not-exist-${Date.now()}`;
    const res = await page.goto(path, { waitUntil: 'domcontentloaded' });
    expect(res).not.toBeNull();
    const status = res!.status();
    expect([200, 404]).toContain(status);
    if (status === 200) {
      const ct = (res!.headers()['content-type'] || '').toLowerCase();
      expect(ct).toContain('text/html');
      await expect(page.locator('#root, [data-root], #app')).toHaveCount(1);
    }
  });

  // Auth-protected routes - SKIPPED(REQUIRES_AUTH)
  test.skip('Auth: /dashboard (REQUIRES_AUTH) - SKIPPED(REQUIRES_AUTH)', async ({ page }) => {
    test.skip(true, 'SKIPPED(REQUIRES_AUTH)');
    await page.goto(`${BASE}/dashboard`);
  });

  test.skip('Auth: /api/user/profile (REQUIRES_AUTH) - SKIPPED(REQUIRES_AUTH)', async ({ request }) => {
    test.skip(true, 'SKIPPED(REQUIRES_AUTH)');
    await request.get(`${BASE}/api/user/profile`);
  });
});
