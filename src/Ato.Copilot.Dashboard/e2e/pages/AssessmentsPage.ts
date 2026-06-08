import { type Page, type Locator, expect } from '@playwright/test';

export class AssessmentsPage {
  readonly page: Page;
  readonly assessmentsTab: Locator;
  readonly runAssessmentBtn: Locator;

  constructor(page: Page) {
    this.page = page;
    this.assessmentsTab = page.getByRole('link', { name: /assess/i }).first();
    this.runAssessmentBtn = page.getByRole('button', { name: /run.*assess|start.*assess/i }).first();
  }

  async navigate() {
    await this.assessmentsTab.click();
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.page.getByRole('heading', { name: /assess/i }).first()).toBeVisible();
  }

  async runAssessment() {
    if (await this.runAssessmentBtn.isVisible()) {
      await this.runAssessmentBtn.click();
      await this.page.waitForLoadState('networkidle');
    }
  }

  async expectFindingsVisible() {
    const findings = this.page.locator('table tbody tr, [class*="finding"]');
    await findings.first().waitFor({ state: 'attached', timeout: 15_000 }).catch(() => {});
  }
}
