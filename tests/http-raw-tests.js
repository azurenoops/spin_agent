// tests/http-raw-tests.js
// Usage: node tests/http-raw-tests.js [BASE_URL]
const BASE = process.argv[2] || process.env.BASE_URL || 'https://ca-ato-copilot-dashboard-v2.blackwater-9393aa1a.centralus.azurecontainerapps.io';

const endpoints = [
  { path: '/', desc: 'Landing page (HTML)' },
  { path: '/api/health', desc: 'Health endpoint (JSON)' },
  { path: '/robots.txt', desc: 'robots.txt' },
  { path: '/favicon.ico', desc: 'favicon.ico' },
  { path: `/this-route-should-not-exist-${Date.now()}`, desc: 'Nonexistent path -> SPA fallback' },
];

async function run() {
  let failed = 0;
  for (const e of endpoints) {
    const url = new URL(e.path, BASE).toString();
    try {
      const res = await fetch(url, { method: 'GET' });
      const ct = res.headers.get('content-type') || '';
      const ok = (e.path === '/api/health' && res.status === 200) || (res.status === 200 || res.status === 404);
      console.log(`${e.desc}: ${url} -> ${res.status} (${ct})`);
      if (e.path === '/api/health' && res.status === 200) {
        const body = await res.json().catch(() => null);
        console.log('  body:', body);
        if (!body || (body.status && String(body.status).toLowerCase() !== 'healthy')) {
          console.warn('  WARNING: health payload did not contain expected status=healthy');
        }
      }
      if (!ok) {
        failed++;
      }
    } catch (err) {
      console.error(`ERROR fetching ${url}:`, err);
      failed++;
    }
  }
  if (failed > 0) {
    console.error(`Completed with ${failed} failing checks`);
    process.exit(2);
  }
  console.log('All checks passed (or returned allowed status codes)');
  process.exit(0);
}

run();
