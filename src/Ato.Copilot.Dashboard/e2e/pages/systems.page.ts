import { type Page, type Locator, expect } from '@playwright/test';

export class SystemsPage {
  readonly page: Page;
  readonly addSystemBtn: Locator;
  readonly systemTable: Locator;
  readonly impactFilter: Locator;
  readonly phaseFilter: Locator;

  constructor(page: Page) {
    this.page = page;
    this.addSystemBtn = page.getByRole('button', { name: /add system|register.*system|new system/i });
    this.systemTable = page.locator('table').first();
    this.impactFilter = page.locator('select').first();
    this.phaseFilter = page.locator('select').nth(1);
  }

  async goto() {
    await this.page.goto('/systems');
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.page).toHaveURL('/systems');
  }

  async expectSystemsListed() {
    const rows = this.systemTable.locator('tbody tr');
    await expect(rows.first()).toBeVisible();
  }

  async clickSystem(name: string) {
    await this.page.getByText(name).first().click();
    await this.page.waitForLoadState('networkidle');
  }

  /**  Open the "Add System" dialog */
  async openAddSystemForm() {
    await this.addSystemBtn.click();
    await this.page.waitForSelector('[role="dialog"], form', { state: 'visible' });
  }

  /** Fill and submit the register system form */
  async registerSystem(opts: {
    name: string;
    acronym?: string;
    description?: string;
  }) {
    await this.openAddSystemForm();
    await this.page.getByLabel(/name/i).first().fill(opts.name);
    if (opts.acronym) {
      await this.page.getByLabel(/acronym/i).fill(opts.acronym);
    }
    if (opts.description) {
      await this.page.getByLabel(/description/i).fill(opts.description);
    }
    await this.page.getByRole('button', { name: /save|create|submit/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  /** Sort by a column header */
  async sortByColumn(columnName: string) {
    await this.systemTable.getByText(columnName, { exact: false }).first().click();
    await this.page.waitForLoadState('networkidle');
  }

  /** Filter by impact level */
  async filterByImpact(value: string) {
    await this.impactFilter.selectOption({ label: value });
    await this.page.waitForLoadState('networkidle');
  }
}
