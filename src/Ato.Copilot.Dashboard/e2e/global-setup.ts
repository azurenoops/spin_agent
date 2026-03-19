import type { FullConfig } from '@playwright/test';

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5173';

/**
 * Global setup: ensure the dashboard and API are reachable before running tests.
 */
export default async function globalSetup(_config: FullConfig) {
  const maxRetries = 30;
  const delay = 2_000;

  for (let i = 0; i < maxRetries; i++) {
    try {
      const res = await fetch(`${BASE_URL}/`);
      if (res.ok) {
        console.log(`✓ Dashboard reachable at ${BASE_URL}`);
        return;
      }
    } catch {
      // not ready yet
    }
    if (i < maxRetries - 1) {
      console.log(`Waiting for dashboard (${i + 1}/${maxRetries})...`);
      await new Promise((r) => setTimeout(r, delay));
    }
  }
  throw new Error(`Dashboard at ${BASE_URL} did not become reachable within ${(maxRetries * delay) / 1000}s`);
}
