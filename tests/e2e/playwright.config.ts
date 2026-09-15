import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 120000,
  use: {
    baseURL: process.env.MCP_BASE_URL || 'http://localhost:5100',
    headless: true,
  },
});
