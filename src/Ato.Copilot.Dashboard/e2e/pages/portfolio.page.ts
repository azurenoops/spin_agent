import { type Page, type Locator, expect } from '@playwright/test';

export class PortfolioPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly systemsLink: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { level: 1 }).first();
    this.systemsLink = page.getByRole('link', { name: /systems/i }).first();
  }

  async goto() {
    await this.page.goto('/');
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.page).toHaveURL('/');
  }

  /** Check that KPI metric cards are rendered */
  async expectMetricCards() {
    // Look for the numeric KPI cards on the portfolio risk profile
    const cards = this.page.locator('[class*="metric"], [class*="card"], [class*="Card"]');
    await expect(cards.first()).toBeVisible();
  }

  /** Click a system name to navigate to its detail page */
  async clickSystem(name: string) {
    await this.page.getByText(name).first().click();
    await this.page.waitForLoadState('networkidle');
  }

  async navigateToSystems() {
    await this.systemsLink.click();
    await this.page.waitForLoadState('networkidle');
  }
}
