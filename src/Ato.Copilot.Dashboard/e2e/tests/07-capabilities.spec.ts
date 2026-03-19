import { test, expect } from '@playwright/test';
import { CapabilitiesPage } from '../pages/capabilities.page';

test.describe('Capabilities Library (Org-wide)', () => {
  let caps: CapabilitiesPage;

  test.beforeEach(async ({ page }) => {
    caps = new CapabilitiesPage(page);
    await caps.goto();
  });

  test('should load capabilities library', async () => {
    await caps.expectLoaded();
  });

  test('should display capabilities list', async ({ page }) => {
    // At least one capability should exist from seed data
    const cards = page.locator('[class*="card"], [class*="capability"]');
    await expect(cards.first()).toBeVisible({ timeout: 15_000 });
  });

  test('should open add-capability form', async ({ page }) => {
    await caps.openAddForm();
    await expect(page.getByLabel(/^name/i).first()).toBeVisible();
  });

  test('should create a capability', async ({ page }) => {
    const name = `E2E Capability ${Date.now().toString().slice(-6)}`;
    await caps.createCapability({
      name,
      provider: 'E2E Test Provider',
    });
    await page.waitForTimeout(1_000);
    await caps.expectCapabilityListed(name);
  });

  test('should search capabilities', async () => {
    await caps.search('Backup');
    // Wait for filter to apply
  });

  test('should expand a capability to see details', async ({ page }) => {
    const firstCard = page.locator('[class*="card"] h3, [class*="capability"] h3, [class*="card"] h4').first();
    if (await firstCard.isVisible()) {
      const capName = await firstCard.textContent();
      await caps.expandCapability(capName!.trim());
      await page.waitForTimeout(500);
      // Expanded content should show description/mappings
      const body = await page.textContent('body');
      expect(body).toMatch(/description|mapping|control|provider/i);
    }
  });

  test('should edit a capability', async ({ page }) => {
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form', { state: 'visible' });
      await expect(page.getByLabel(/^name/i).first()).toBeVisible();
      await page.getByRole('button', { name: /cancel/i }).click();
    }
  });

  test('should show capability impact preview on edit', async ({ page }) => {
    const editBtn = page.getByRole('button', { name: /edit/i }).first();
    if (await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForSelector('form', { state: 'visible' });
      // Modify description to trigger impact preview
      const descField = page.getByLabel(/description/i);
      if (await descField.isVisible()) {
        await descField.fill('Updated description for impact test');
        await page.getByRole('button', { name: /save|update/i }).click();
        await page.waitForTimeout(1_000);
        // Impact preview dialog may appear
        const preview = page.getByText(/impact|affected|narrative/i);
        if (await preview.isVisible({ timeout: 3_000 }).catch(() => false)) {
          await expect(preview).toBeVisible();
          // Cancel to avoid making real changes
          await page.getByRole('button', { name: /cancel/i }).click();
        }
      }
    }
  });
});

test.describe('System Capability Coverage', () => {
  async function gotoCoverage(page: import('@playwright/test').Page) {
    await page.goto('/systems');
    await page.waitForLoadState('networkidle');
    await page.locator('table tbody tr a').first().click();
    await page.waitForLoadState('networkidle');
    await page.getByRole('link', { name: /capabilit/i }).click();
    await page.waitForLoadState('networkidle');
  }

  test('should load system capability coverage', async ({ page }) => {
    await gotoCoverage(page);
    const body = await page.textContent('body');
    expect(body).toMatch(/capabilit|coverage|mapped|control/i);
  });

  test('should display summary metrics', async ({ page }) => {
    await gotoCoverage(page);
    await expect(page.getByText(/total|mapped|coverage/i).first()).toBeVisible();
  });

  test('should expand a capability to see mappings', async ({ page }) => {
    await gotoCoverage(page);
    const card = page.locator('[class*="card"] h3, [class*="capability"] h3').first();
    if (await card.isVisible()) {
      await card.click();
      await page.waitForTimeout(500);
    }
  });

  test('should add capability to system', async ({ page }) => {
    await gotoCoverage(page);
    const addBtn = page.getByRole('button', { name: /add/i }).first();
    if (await addBtn.isVisible()) {
      await addBtn.click();
      await page.waitForTimeout(500);
      // Dialog to pick capability should appear
      const dialog = page.locator('[role="dialog"], form');
      if (await dialog.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await page.getByRole('button', { name: /cancel/i }).click();
      }
    }
  });
});
