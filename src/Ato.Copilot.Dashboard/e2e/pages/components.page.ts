import { type Page, type Locator, expect } from '@playwright/test';

export class ComponentsPage {
  readonly page: Page;
  readonly addBtn: Locator;
  readonly searchInput: Locator;
  readonly discoverAzureBtn: Locator;

  constructor(page: Page) {
    this.page = page;
    this.addBtn = page.getByRole('button', { name: /add component/i });
    this.searchInput = page.getByPlaceholder(/search/i);
    this.discoverAzureBtn = page.getByRole('button', { name: /discover.*azure/i });
  }

  async expectLoaded() {
    await expect(this.addBtn).toBeVisible({ timeout: 15_000 });
  }

  async expectSummaryCards() {
    await expect(this.page.getByText(/people/i).first()).toBeVisible();
    await expect(this.page.getByText(/places/i).first()).toBeVisible();
    await expect(this.page.getByText(/things/i).first()).toBeVisible();
    await expect(this.page.getByText(/total/i).first()).toBeVisible();
  }

  async expectComponentListed(name: string) {
    await expect(this.page.getByText(name).first()).toBeVisible();
  }

  async search(term: string) {
    await this.searchInput.fill(term);
    await this.page.waitForTimeout(500);
  }

  // ── Create ─────────────────────────────────────────────────────────────────

  async openAddForm() {
    await this.addBtn.click();
    await this.page.waitForSelector('form', { state: 'visible' });
  }

  async fillComponentForm(opts: {
    name: string;
    type: 'Person' | 'Place' | 'Thing';
    subType?: string;
    description?: string;
    owner?: string;
    personName?: string;
    email?: string;
    rmfRole?: string;
    status?: string;
    boundary?: string;
  }) {
    // Type radio
    await this.page.getByLabel(opts.type, { exact: true }).check();

    // Name
    await this.page.getByLabel(/^name/i).first().fill(opts.name);

    if (opts.subType) {
      await this.page.getByLabel(/sub.?type/i).fill(opts.subType);
    }
    if (opts.description) {
      await this.page.getByLabel(/description/i).fill(opts.description);
    }
    if (opts.owner) {
      await this.page.getByLabel(/owner/i).fill(opts.owner);
    }
    if (opts.personName) {
      await this.page.getByLabel(/person name/i).fill(opts.personName);
    }
    if (opts.email) {
      await this.page.getByLabel(/email/i).fill(opts.email);
    }
    if (opts.rmfRole) {
      await this.page.getByLabel(/rmf role/i).selectOption({ label: opts.rmfRole });
    }
    if (opts.status) {
      await this.page.getByLabel(/status/i).selectOption({ label: opts.status });
    }
    if (opts.boundary) {
      await this.page.getByLabel(/boundary/i).selectOption({ label: opts.boundary });
    }
  }

  async submitForm() {
    await this.page.getByRole('button', { name: /save|create|submit/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async createComponent(opts: Parameters<ComponentsPage['fillComponentForm']>[0]) {
    await this.openAddForm();
    await this.fillComponentForm(opts);
    await this.submitForm();
  }

  // ── Edit ───────────────────────────────────────────────────────────────────

  async clickEdit(componentName: string) {
    const row = this.page.locator(`text=${componentName}`).first().locator('..');
    // Walk up to find the edit button in the same row/card
    await row.locator('..').locator('..').getByText(/edit/i).first().click();
    await this.page.waitForSelector('form', { state: 'visible' });
  }

  async expectFormValue(label: RegExp, value: string) {
    const input = this.page.getByLabel(label);
    await expect(input).toHaveValue(value);
  }

  async expectSelectValue(label: RegExp, value: string) {
    const select = this.page.getByLabel(label);
    await expect(select).toHaveValue(value);
  }

  // ── Delete ─────────────────────────────────────────────────────────────────

  async clickDelete(componentName: string) {
    const row = this.page.locator(`text=${componentName}`).first().locator('..');
    await row.locator('..').locator('..').getByText(/delete/i).first().click();
  }

  async confirmDelete() {
    await this.page.getByRole('button', { name: /confirm|yes|delete/i }).click();
    await this.page.waitForLoadState('networkidle');
  }
}
