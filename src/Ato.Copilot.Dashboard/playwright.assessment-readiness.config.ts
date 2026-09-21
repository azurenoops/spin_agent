import { defineConfig, devices } from '@playwright/test';

// Deliberately excludes global-setup and every live/CRUD suite in playwright.config.ts.
const origin = 'http://localhost:4179';

export default defineConfig({
  testDir: './e2e/isolated',
  // The .local.ts suffix also prevents discovery by the existing live suite.
  testMatch: 'assessment-readiness.local.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  reporter: [['list']],
  outputDir: './test-results/assessment-readiness',
  use: {
    baseURL: origin,
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'local-chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run dev -- --host localhost --port 4179 --strictPort',
    url: origin,
    reuseExistingServer: false,
    env: {
      VITE_API_BASE_URL: '/api/dashboard',
      VITE_API_BASE_URL_ORGS: '/api/orgs',
      VITE_API_BASE_URL_ONBOARDING: '/api/onboarding',
      VITE_API_BASE_URL_ONBOARDING_TENANT: '/api/onboarding/tenant',
      // A missed browser API mock must never reach an existing local backend.
      VITE_API_PROXY_TARGET: 'http://127.0.0.1:1',
    },
  },
});
