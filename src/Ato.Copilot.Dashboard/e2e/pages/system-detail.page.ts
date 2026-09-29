import { type Page, expect } from '@playwright/test';

export class SystemDetailPage {
  readonly page: Page;

  constructor(page: Page) {
    this.page = page;
  }

  async expectLoaded() {
    await expect(this.page.url()).toContain('/systems/');
  }

  /** Navigate to a tab within the system */
  async goToTab(tabName: string) {
    await this.page.getByRole('link', { name: new RegExp(tabName, 'i') }).click();
    await this.page.waitForLoadState('networkidle');
  }

  // ── Overview ──────────────────────────────────────────────────────────────

  async expectReadinessTasks() {
    await expect(this.page.getByRole('tab', { name: 'Readiness', exact: true })).toBeVisible();
    await expect(this.page.getByRole('tab', { name: 'Monitoring & follow-up', exact: true })).toBeVisible();
    await expect(this.page.getByText('System diagnostics & RMF phase management', { exact: true })).toHaveCount(0);
  }
}
