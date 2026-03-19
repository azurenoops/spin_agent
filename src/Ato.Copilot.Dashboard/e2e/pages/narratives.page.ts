import { type Page, type Locator, expect } from '@playwright/test';

export class NarrativesPage {
  readonly page: Page;
  readonly addBtn: Locator;
  readonly familyFilter: Locator;
  readonly statusFilter: Locator;

  constructor(page: Page) {
    this.page = page;
    this.addBtn = page.getByRole('button', { name: /add narrative/i });
    this.familyFilter = page.getByLabel(/family/i);
    this.statusFilter = page.getByLabel(/status/i);
  }

  async expectLoaded() {
    await expect(this.addBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectTable() {
    const rows = this.page.locator('table tbody tr');
    await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  }

  async filterByFamily(family: string) {
    await this.familyFilter.selectOption({ label: family });
    await this.page.waitForLoadState('networkidle');
  }

  async filterByStatus(status: string) {
    await this.statusFilter.selectOption({ label: status });
    await this.page.waitForLoadState('networkidle');
  }

  async openAddForm() {
    await this.addBtn.click();
    await this.page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
  }

  async clickEditNarrative(index = 0) {
    await this.page.getByRole('button', { name: /edit/i }).nth(index).click();
    await this.page.waitForTimeout(300);
  }

  async clickRegenerate(index = 0) {
    await this.page.getByRole('button', { name: /regenerate/i }).nth(index).click();
  }
}
