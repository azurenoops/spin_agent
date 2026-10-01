import { defineConfig } from '@playwright/test';
import isolated from './playwright.assessment-readiness.config';

export default defineConfig({
  ...isolated,
  testDir: './e2e/tests',
  testMatch: 'control-narratives.spec.ts',
  outputDir: './test-results/requirement-coverage',
});
