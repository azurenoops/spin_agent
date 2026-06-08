import { type Page, type Locator, expect } from '@playwright/test';

export class DashboardPage {
  readonly page: Page;
  readonly systemsLink: Locator;
  readonly portfolioHeading: Locator;

  constructor(page: Page) {
    this.page = page;
    this.systemsLink = page.getByRole('link', { name: /systems/i }).first();
    this.portfolioHeading = page.getByRole('heading', { name: /portfolio|dashboard/i }).first();
  }

  async goto() {
    await this.page.goto('/');
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.portfolioHeading.or(this.page.locator('nav'))).toBeVisible();
  }

  async navigateToSystems() {
    await this.systemsLink.click();
    await this.page.waitForLoadState('networkidle');
  }
}
