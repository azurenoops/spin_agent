import { test as base, type Page } from '@playwright/test';

/**
 * Shared helpers available to all tests.
 */
export const test = base.extend<{ dashboardPage: Page }>({
  dashboardPage: async ({ page }, use) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await use(page);
  },
});

export { expect } from '@playwright/test';

// ─── Selector helpers ───────────────────────────────────────────────────────

export function testId(id: string) {
  return `[data-testid="${id}"]`;
}

// ─── Navigation helpers ──────────────────────────────────────────────────────

export async function navigateToSystems(page: Page) {
  await page.getByRole('link', { name: /systems/i }).first().click();
  await page.waitForLoadState('networkidle');
}

export async function navigateToFirstSystem(page: Page) {
  await navigateToSystems(page);
  // Click the first system row link
  await page.locator('table tbody tr a, [class*="system"] a').first().click();
  await page.waitForLoadState('networkidle');
}

export async function navigateToSystemTab(page: Page, tabName: string) {
  await page.getByRole('link', { name: new RegExp(tabName, 'i') }).click();
  await page.waitForLoadState('networkidle');
}

export async function navigateToCapabilities(page: Page) {
  await page.getByRole('link', { name: /capabilities/i }).first().click();
  await page.waitForLoadState('networkidle');
}

export async function navigateToComponents(page: Page) {
  await page.getByRole('link', { name: /components/i }).first().click();
  await page.waitForLoadState('networkidle');
}

// ─── Wait helpers ────────────────────────────────────────────────────────────

export async function waitForApi(page: Page) {
  await page.waitForLoadState('networkidle');
}

export async function waitForModal(page: Page) {
  await page.waitForSelector('[role="dialog"], .modal, [class*="dialog"], [class*="Dialog"]', { state: 'visible' });
}

export async function closeModal(page: Page) {
  const cancelBtn = page.getByRole('button', { name: /cancel/i });
  if (await cancelBtn.isVisible()) {
    await cancelBtn.click();
  }
}
