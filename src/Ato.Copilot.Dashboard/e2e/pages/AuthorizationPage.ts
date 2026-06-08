import { type Page, type Locator, expect } from '@playwright/test';

export class AuthorizationPage {
  readonly page: Page;
  readonly authTab: Locator;
  readonly recordDecisionBtn: Locator;

  constructor(page: Page) {
    this.page = page;
    this.authTab = page.getByRole('link', { name: /author|decision|ato/i }).first();
    this.recordDecisionBtn = page.getByRole('button', { name: /record|create.*decision|authorize/i }).first();
  }

  async navigate() {
    await this.authTab.click();
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.page.getByRole('heading', { name: /author|decision|ato/i }).first()).toBeVisible();
  }

  async recordAtoDecision(opts: { type: string; validDays: number }) {
    if (await this.recordDecisionBtn.isVisible()) {
      await this.recordDecisionBtn.click();
      await this.page.waitForSelector('[role="dialog"]', { state: 'visible' });
      const typeSelect = this.page.getByLabel(/decision type|type/i).first();
      if (await typeSelect.isVisible()) await typeSelect.selectOption({ label: opts.type });
      const daysInput = this.page.getByLabel(/valid|days|duration/i).first();
      if (await daysInput.isVisible()) await daysInput.fill(String(opts.validDays));
      await this.page.getByRole('button', { name: /save|submit|confirm/i }).first().click();
      await this.page.waitForLoadState('networkidle');
    }
  }
}
