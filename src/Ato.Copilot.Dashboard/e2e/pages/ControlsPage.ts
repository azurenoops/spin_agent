import { type Page, type Locator, expect } from '@playwright/test';

export class ControlsPage {
  readonly page: Page;
  readonly controlsTab: Locator;
  readonly controlTable: Locator;
  readonly searchInput: Locator;

  constructor(page: Page) {
    this.page = page;
    this.controlsTab = page.getByRole('link', { name: /controls/i }).first();
    this.controlTable = page.locator('table').first();
    this.searchInput = page.getByPlaceholder(/search.*control/i).first();
  }

  async navigate() {
    await this.controlsTab.click();
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.page.getByRole('heading', { name: /controls/i }).first()).toBeVisible();
  }

  async selectControlBySearch(controlId: string) {
    if (await this.searchInput.isVisible()) {
      await this.searchInput.fill(controlId);
      await this.page.waitForTimeout(400);
    }
    const row = this.page.locator(`table tbody tr`).filter({ hasText: controlId }).first();
    if (await row.isVisible()) {
      const checkbox = row.locator('input[type="checkbox"]');
      if (await checkbox.isVisible()) await checkbox.check();
    }
  }

  async saveSelections() {
    const saveBtn = this.page.getByRole('button', { name: /save|apply|confirm/i }).first();
    if (await saveBtn.isVisible()) {
      await saveBtn.click();
      await this.page.waitForLoadState('networkidle');
    }
  }
}
