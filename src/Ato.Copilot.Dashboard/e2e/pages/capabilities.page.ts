import { type Page, type Locator, expect } from '@playwright/test';

export class CapabilitiesPage {
  readonly page: Page;
  readonly addBtn: Locator;
  readonly searchInput: Locator;
  readonly categoryFilter: Locator;
  readonly statusFilter: Locator;

  constructor(page: Page) {
    this.page = page;
    this.addBtn = page.getByRole('button', { name: /new capability|add capability/i });
    this.searchInput = page.getByPlaceholder(/search/i);
    this.categoryFilter = page.getByLabel(/category/i);
    this.statusFilter = page.getByLabel(/status/i);
  }

  async goto() {
    await this.page.goto('/capabilities');
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.addBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectCapabilityListed(name: string) {
    await expect(this.page.getByText(name).first()).toBeVisible();
  }

  async search(term: string) {
    await this.searchInput.fill(term);
    await this.page.waitForTimeout(500);
  }

  async filterByCategory(category: string) {
    await this.categoryFilter.selectOption({ label: category });
    await this.page.waitForLoadState('networkidle');
  }

  async openAddForm() {
    await this.addBtn.click();
    await this.page.waitForSelector('form', { state: 'visible' });
  }

  async fillCapabilityForm(opts: {
    name: string;
    provider: string;
    category?: string;
    description?: string;
    status?: string;
    owner?: string;
  }) {
    await this.page.getByLabel(/^name/i).first().fill(opts.name);
    await this.page.getByLabel(/provider/i).fill(opts.provider);
    if (opts.category) {
      await this.page.getByLabel(/category/i).selectOption({ label: opts.category });
    }
    if (opts.description) {
      await this.page.getByLabel(/description/i).fill(opts.description);
    }
    if (opts.status) {
      await this.page.getByLabel(/status/i).selectOption({ label: opts.status });
    }
    if (opts.owner) {
      await this.page.getByLabel(/owner/i).fill(opts.owner);
    }
  }

  async submitForm() {
    await this.page.getByRole('button', { name: /save|create|submit/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async createCapability(opts: Parameters<CapabilitiesPage['fillCapabilityForm']>[0]) {
    await this.openAddForm();
    await this.fillCapabilityForm(opts);
    await this.submitForm();
  }

  async expandCapability(name: string) {
    await this.page.getByText(name).first().click();
    await this.page.waitForTimeout(300);
  }

  async clickEdit(name: string) {
    await this.expandCapability(name);
    await this.page.getByRole('button', { name: /edit/i }).first().click();
    await this.page.waitForSelector('form', { state: 'visible' });
  }

  async clickDelete(name: string) {
    await this.expandCapability(name);
    await this.page.getByRole('button', { name: /delete/i }).first().click();
  }
}
