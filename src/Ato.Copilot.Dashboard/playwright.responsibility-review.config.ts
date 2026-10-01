import { defineConfig } from '@playwright/test';
import isolated from './playwright.assessment-readiness.config';

export default defineConfig({
  ...isolated,
  testDir: './e2e/tests',
  testMatch: 'capability-responsibility-review.spec.ts',
  outputDir: './test-results/responsibility-review',
});
