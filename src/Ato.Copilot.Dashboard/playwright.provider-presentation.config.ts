import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e/isolated',
  testMatch: 'provider-presentation.local.ts',
  workers: 1,
  retries: 0,
  reporter: [['list']],
  outputDir: './test-results/provider-presentation-run',
  use: { baseURL: 'http://localhost:4187', serviceWorkers: 'block', trace: 'retain-on-failure' },
  projects: [{ name: 'provider-components', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1100 } } }],
  webServer: {
    command: 'npm run dev -- --host localhost --port 4187 --strictPort',
    url: 'http://localhost:4187',
    reuseExistingServer: false,
    env: { VITE_API_PROXY_TARGET: 'http://127.0.0.1:1' },
  },
});
