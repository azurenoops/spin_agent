import { type Page, type Locator, expect } from '@playwright/test';

export class BoundariesPage {
  readonly page: Page;
  readonly createBtn: Locator;
  readonly searchInput: Locator;
  readonly typeFilter: Locator;

  constructor(page: Page) {
    this.page = page;
    this.createBtn = page.getByRole('button', { name: /create boundary|add boundary/i });
    this.searchInput = page.getByPlaceholder(/search/i);
    this.typeFilter = page.locator('select').first();
  }

  async expectLoaded() {
    await expect(this.createBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectBoundaryListed(name: string) {
    await expect(this.page.getByText(name).first()).toBeVisible();
  }

  async openCreateForm() {
    await this.createBtn.click();
    await this.page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
  }

  async fillBoundaryForm(opts: {
    name: string;
    type?: 'Physical' | 'Logical' | 'Hybrid';
    description?: string;
  }) {
    await this.page.getByLabel(/^name/i).first().fill(opts.name);
    if (opts.type) {
      await this.page.getByLabel(opts.type, { exact: true }).check();
    }
    if (opts.description) {
      await this.page.getByLabel(/description/i).fill(opts.description);
    }
  }

  async submitForm() {
    await this.page.getByRole('button', { name: /save|create|submit/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async createBoundary(opts: Parameters<BoundariesPage['fillBoundaryForm']>[0]) {
    await this.openCreateForm();
    await this.fillBoundaryForm(opts);
    await this.submitForm();
  }

  async expandBoundary(name: string) {
    await this.page.getByText(name).first().click();
    await this.page.waitForTimeout(300);
  }

  async clickEdit(name: string) {
    const section = this.page.locator(`text=${name}`).first().locator('..').locator('..');
    await section.getByText(/edit/i).first().click();
    await this.page.waitForSelector('form, [role="dialog"]', { state: 'visible' });
  }

  async clickDelete(name: string) {
    const section = this.page.locator(`text=${name}`).first().locator('..').locator('..');
    await section.getByText(/delete/i).first().click();
  }

  async confirmDelete() {
    await this.page.getByRole('button', { name: /confirm|yes|delete/i }).click();
    await this.page.waitForLoadState('networkidle');
  }
}
