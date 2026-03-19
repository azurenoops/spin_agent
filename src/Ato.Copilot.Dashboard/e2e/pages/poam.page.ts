import { type Page, type Locator, expect } from '@playwright/test';

export class PoamPage {
  readonly page: Page;
  readonly addBtn: Locator;
  readonly searchInput: Locator;
  readonly statusFilter: Locator;
  readonly severityFilter: Locator;
  readonly exportBtn: Locator;

  constructor(page: Page) {
    this.page = page;
    this.addBtn = page.getByRole('button', { name: /add.*poa.m|create.*poa.m/i });
    this.searchInput = page.getByPlaceholder(/search/i);
    this.statusFilter = page.getByLabel(/status/i);
    this.severityFilter = page.getByLabel(/severity/i);
    this.exportBtn = page.getByRole('button', { name: /export/i });
  }

  async expectLoaded() {
    await expect(this.addBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectSummaryCards() {
    await expect(this.page.getByText(/open|total/i).first()).toBeVisible();
  }

  async expectTable() {
    const rows = this.page.locator('table tbody tr');
    await expect(rows.first()).toBeVisible({ timeout: 15_000 });
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

  async fillPoamForm(opts: {
    controlId?: string;
    weakness: string;
    severity: string;
    dueDate: string;
    pocName?: string;
    pocEmail?: string;
    resourceRequirements?: string;
  }) {
    if (opts.controlId) {
      await this.page.getByLabel(/control/i).first().fill(opts.controlId);
      await this.page.waitForTimeout(500);
      await this.page.locator('[class*="option"], li').first().click();
    }
    await this.page.getByLabel(/weakness/i).fill(opts.weakness);
    await this.page.getByLabel(/severity/i).selectOption({ label: opts.severity });
    await this.page.getByLabel(/date|completion/i).first().fill(opts.dueDate);
    if (opts.pocName) {
      await this.page.getByLabel(/poc.*name|point.*contact/i).fill(opts.pocName);
    }
    if (opts.pocEmail) {
      await this.page.getByLabel(/poc.*email/i).fill(opts.pocEmail);
    }
    if (opts.resourceRequirements) {
      await this.page.getByLabel(/resource/i).fill(opts.resourceRequirements);
    }
  }

  async submitForm() {
    await this.page.getByRole('button', { name: /save|create|submit/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async clickRow(index = 0) {
    await this.page.locator('table tbody tr').nth(index).click();
    await this.page.waitForTimeout(300);
  }

  async expectDrawerOpen() {
    const drawer = this.page.locator('[class*="drawer"], [class*="Drawer"], [class*="detail"]');
    await expect(drawer.first()).toBeVisible();
  }

  // ── Drawer actions ─────────────────────────────────────────────────────────

  async markComplete() {
    await this.page.getByRole('button', { name: /complete/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async linkComponents() {
    await this.page.getByRole('button', { name: /link.*component/i }).click();
    await this.page.waitForSelector('[role="dialog"], form', { state: 'visible' });
  }

  async linkTask() {
    await this.page.getByRole('button', { name: /link.*task/i }).click();
    await this.page.waitForSelector('[role="dialog"], form', { state: 'visible' });
  }

  // ── Tabs ───────────────────────────────────────────────────────────────────

  async goToTrendsTab() {
    await this.page.getByRole('tab', { name: /trends/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async goToTicketingTab() {
    await this.page.getByRole('tab', { name: /ticketing/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  // ── Export ─────────────────────────────────────────────────────────────────

  async openExportDialog() {
    await this.exportBtn.click();
    await this.page.waitForSelector('[role="dialog"], form', { state: 'visible' });
  }

  // ── Pagination ─────────────────────────────────────────────────────────────

  async goToNextPage() {
    await this.page.getByRole('button', { name: /next/i }).click();
    await this.page.waitForLoadState('networkidle');
  }
}
