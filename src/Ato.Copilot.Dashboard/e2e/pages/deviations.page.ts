import { type Page, type Locator, expect } from '@playwright/test';

export class DeviationsPage {
  readonly page: Page;
  readonly addBtn: Locator;
  readonly searchInput: Locator;
  readonly statusFilter: Locator;
  readonly severityFilter: Locator;

  constructor(page: Page) {
    this.page = page;
    this.addBtn = page.getByRole('button', { name: /add deviation/i });
    this.searchInput = page.getByPlaceholder(/search/i);
    this.statusFilter = page.getByLabel(/status/i);
    this.severityFilter = page.getByLabel(/severity/i);
  }

  async expectLoaded() {
    await expect(this.addBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectSummaryCards() {
    await expect(this.page.getByText(/total/i).first()).toBeVisible();
    await expect(this.page.getByText(/approved|pending|denied/i).first()).toBeVisible();
  }

  async selectTypeTab(type: 'All' | 'False Positives' | 'Risk Acceptances' | 'Waivers') {
    await this.page.getByRole('tab', { name: new RegExp(type, 'i') }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async search(term: string) {
    await this.searchInput.fill(term);
    await this.page.waitForTimeout(500);
  }

  async filterByStatus(status: string) {
    await this.statusFilter.selectOption({ label: status });
    await this.page.waitForLoadState('networkidle');
  }

  async filterBySeverity(severity: string) {
    await this.severityFilter.selectOption({ label: severity });
    await this.page.waitForLoadState('networkidle');
  }

  async openAddForm() {
    await this.addBtn.click();
    await this.page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
  }

  async fillDeviationForm(opts: {
    controlId?: string;
    type: 'False Positive' | 'Risk Acceptance' | 'Waiver';
    severity?: string;
    justification: string;
    requestedBy?: string;
  }) {
    if (opts.controlId) {
      await this.page.getByLabel(/control/i).first().fill(opts.controlId);
      await this.page.waitForTimeout(500);
      // Select from autocomplete
      await this.page.locator('[class*="option"], [class*="suggestion"], li').first().click();
    }
    await this.page.getByLabel(opts.type, { exact: false }).check();
    if (opts.severity) {
      await this.page.getByLabel(/severity/i).selectOption({ label: opts.severity });
    }
    await this.page.getByLabel(/justification/i).fill(opts.justification);
    if (opts.requestedBy) {
      await this.page.getByLabel(/requested/i).fill(opts.requestedBy);
    }
  }

  async submitForm() {
    await this.page.getByRole('button', { name: /save|create|submit/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async clickDeviationRow(index = 0) {
    await this.page.locator('table tbody tr, [class*="deviation"] [class*="row"]').nth(index).click();
    await this.page.waitForTimeout(300);
  }

  // ── Drawer actions ─────────────────────────────────────────────────────────

  async expectDrawerOpen() {
    const drawer = this.page.locator('[class*="drawer"], [class*="Drawer"], [class*="detail"]');
    await expect(drawer.first()).toBeVisible();
  }

  async approveDeviation() {
    await this.page.getByRole('button', { name: /approve/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async denyDeviation() {
    await this.page.getByRole('button', { name: /deny/i }).click();
    await this.page.waitForLoadState('networkidle');
  }
}
