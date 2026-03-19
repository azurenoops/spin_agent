import { type Page, type Locator, expect } from '@playwright/test';

export class RemediationPage {
  readonly page: Page;
  readonly searchInput: Locator;
  readonly viewToggle: Locator;

  constructor(page: Page) {
    this.page = page;
    this.searchInput = page.getByPlaceholder(/search/i);
    this.viewToggle = page.getByRole('button', { name: /kanban|table|view/i });
  }

  async expectLoaded() {
    const text = await this.page.textContent('body');
    expect(text).toMatch(/open|in.?progress|completed|overdue/i);
  }

  async expectSummaryCards() {
    await expect(this.page.getByText(/open/i).first()).toBeVisible();
  }

  async search(term: string) {
    await this.searchInput.fill(term);
    await this.page.waitForTimeout(500);
  }

  // ── View toggle ────────────────────────────────────────────────────────────

  async switchToKanban() {
    await this.page.getByRole('button', { name: /kanban/i }).click();
    await this.page.waitForTimeout(300);
  }

  async switchToTable() {
    await this.page.getByRole('button', { name: /table/i }).click();
    await this.page.waitForTimeout(300);
  }

  async expectKanbanColumns() {
    const columns = this.page.locator('[class*="column"], [class*="kanban"]');
    await expect(columns.first()).toBeVisible();
  }

  async expectTableView() {
    const table = this.page.locator('table');
    await expect(table.first()).toBeVisible();
  }

  // ── Task interactions ──────────────────────────────────────────────────────

  async clickTask(index = 0) {
    await this.page.locator('table tbody tr, [class*="card"]').nth(index).click();
    await this.page.waitForTimeout(300);
  }

  async expectDrawerOpen() {
    const drawer = this.page.locator('[class*="drawer"], [class*="Drawer"], [class*="detail"]');
    await expect(drawer.first()).toBeVisible();
  }
}
