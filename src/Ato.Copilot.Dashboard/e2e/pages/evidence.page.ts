import { type Page, type Locator, expect } from '@playwright/test';

export class EvidencePage {
  readonly page: Page;
  readonly uploadBtn: Locator;
  readonly searchInput: Locator;
  readonly categoryFilter: Locator;
  readonly sourceFilter: Locator;

  constructor(page: Page) {
    this.page = page;
    this.uploadBtn = page.getByRole('button', { name: /upload/i });
    this.searchInput = page.getByPlaceholder(/search/i);
    this.categoryFilter = page.getByLabel(/category/i);
    this.sourceFilter = page.getByLabel(/source/i);
  }

  async expectLoaded() {
    await expect(this.uploadBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectSummaryMetrics() {
    await expect(this.page.getByText(/total/i).first()).toBeVisible();
  }

  async search(term: string) {
    await this.searchInput.fill(term);
    await this.page.waitForTimeout(500);
  }

  async filterByCategory(category: string) {
    await this.categoryFilter.selectOption({ label: category });
    await this.page.waitForLoadState('networkidle');
  }

  async openUploadDialog() {
    await this.uploadBtn.click();
    await this.page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
  }

  async sortByColumn(column: string) {
    await this.page.locator('th').getByText(column, { exact: false }).first().click();
    await this.page.waitForTimeout(300);
  }

  async clickDownload(index = 0) {
    await this.page.getByRole('button', { name: /download/i }).nth(index).click();
  }

  async clickDelete(index = 0) {
    await this.page.getByRole('button', { name: /delete/i }).nth(index).click();
  }
}
