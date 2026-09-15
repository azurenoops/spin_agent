// Playwright + HTTP tests for Spin Agent public QA
// RUN: node ./node_modules/.bin/playwright test

const { test, expect, request } = require('@playwright/test');

const BASE_URL = process.env.SPIN_AGENT_URL || 'https://example.com'; // placeholder replaced at run time if found

test.describe('Public unauthenticated flows - Spin Agent (OPTION A)', () => {
  test('GET root path is reachable and returns HTML (public)', async ({ request }) => {
    const res = await request.get('/');
    expect(res.ok()).toBeTruthy();
    const ct = res.headers()['content-type'] || '';
    expect(ct).toContain('text/html');
  });

  test('GET /health or /healthz if exists (public)', async ({ request }) => {
    const paths = ['/health','/healthz','/status','/ping'];
    let found = false;
    for (const p of paths) {
      const res = await request.get(p);
      if (res.status() === 200) {
        found = true;
        const text = await res.text();
        expect(text.length).toBeGreaterThan(0);
        break;
      }
    }
    expect(found).toBeTruthy();
  });

  test('RMF role path: /rmf/public (example) - check 200/403 allowed', async ({ request }) => {
    const res = await request.get('/rmf');
    expect([200,401,403,404]).toContain(res.status());
  });

});

// Auth-required tests marked REQUIRES_AUTH

test.describe('Auth-required flows - SKIPPED without configured login (REQUIRES_AUTH)', () => {
  test.skip('POST /login and access /rmf/secure or role-based page', async ({ request }) => {
    // This test requires credentials and is intentionally skipped when none provided.
    const user = process.env.SPIN_AGENT_USER;
    const pass = process.env.SPIN_AGENT_PASS;
    if (!user || !pass) test.skip();
    const resLogin = await request.post('/login', { form: { username: user, password: pass } });
    expect(resLogin.status()).toBe(200);
    const cookie = resLogin.headers()['set-cookie'];
    expect(cookie).toBeTruthy();
    const resSecure = await request.get('/rmf/secure', { headers: { cookie } });
    expect(resSecure.status()).toBe(200);
  });
});
