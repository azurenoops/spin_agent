import { defineConfig, devices } from '@playwright/test';
import isolated from './playwright.assessment-readiness.config';

export default defineConfig({
  ...isolated,
  testMatch: 'narratives.local.ts',
  outputDir: './test-results/narratives',
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1000 } } },
    { name: 'mobile', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } } },
  ],
});