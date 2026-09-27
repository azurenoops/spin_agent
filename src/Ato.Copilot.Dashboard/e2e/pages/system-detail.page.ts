import { type Page, type Locator, expect } from '@playwright/test';

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

  private async openDiagnostics() {
    const summary = this.page.getByText('System diagnostics & RMF phase management', { exact: true });
    const details = summary.locator('..');
    if (await details.getAttribute('open') === null) await summary.click();
  }

  async expectMetricCards() {
    // Metric cards show compliance %, ATO status, POA&Ms, etc.
    const text = await this.page.textContent('body');
    expect(text).toMatch(/compliance|ato|poa.m|finding/i);
  }

  async expectComplianceHeatmap() {
    await this.openDiagnostics();
    // The heatmap renders NIST family boxes
    const families = this.page.locator('[class*="heatmap"], [class*="Heatmap"]');
    await expect(families.first()).toBeVisible({ timeout: 15_000 });
  }

  async clickHeatmapFamily(familyName: string) {
    await this.openDiagnostics();
    await this.page.getByText(familyName, { exact: true }).click();
    // Should open control drill-down modal
    await this.page.waitForSelector('[role="dialog"], [class*="modal"], [class*="Modal"]', { state: 'visible' });
  }

  async expectActivityFeed() {
    await this.openDiagnostics();
    // Activity feed section
    const feed = this.page.getByText(/recent|activity/i);
    await expect(feed.first()).toBeVisible();
  }

  // ── Advance Phase ─────────────────────────────────────────────────────────

  async expectPhaseProgress() {
    await this.openDiagnostics();
    const phase = this.page.getByText(/phase|categorize|select|implement|assess|authorize|monitor/i);
    await expect(phase.first()).toBeVisible();
  }
}
