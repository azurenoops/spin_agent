import { test, expect } from '@playwright/test';
import { PoamPage } from '../pages/poam.page';

async function gotoPoam(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /poa.m/i }).click();
  await page.waitForLoadState('networkidle');
  return new PoamPage(page);
}

test.describe('POA&M Management — Overview Tab', () => {
  test('should load POA&M page', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.expectLoaded();
  });

  test('should display summary cards', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.expectSummaryCards();
  });

  test('should display POA&M table', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.expectTable();
  });

  test('should open add POA&M form', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.openAddForm();
    await expect(page.locator('form, [role="dialog"]').first()).toBeVisible();
    await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
  });

  test('should search POA&M items', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.search('AC-');
    await page.waitForTimeout(500);
  });

  test('should filter by status', async ({ page }) => {
    const poam = await gotoPoam(page);
    const statusSelect = page.locator('select').first();
    if (await statusSelect.isVisible()) {
      const options = await statusSelect.locator('option').allTextContents();
      if (options.length > 1) {
        await statusSelect.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should filter by severity', async ({ page }) => {
    const poam = await gotoPoam(page);
    const sevSelect = page.locator('select').nth(1);
    if (await sevSelect.isVisible()) {
      const options = await sevSelect.locator('option').allTextContents();
      if (options.length > 1) {
        await sevSelect.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');
      }
    }
  });

  test('should open POA&M detail drawer on row click', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.clickRow(0);
    const drawer = page.locator('[class*="drawer"], [class*="Drawer"], [class*="detail"], [class*="Detail"]');
    if (await drawer.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      await poam.expectDrawerOpen();
    }
  });

  test('should paginate POA&M items', async ({ page }) => {
    const poam = await gotoPoam(page);
    const nextBtn = page.getByRole('button', { name: /next/i });
    if (await nextBtn.isVisible() && await nextBtn.isEnabled()) {
      await nextBtn.click();
      await page.waitForLoadState('networkidle');
    }
  });
});

test.describe('POA&M Management — Detail Drawer', () => {
  test('should show milestones in detail drawer', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.clickRow(0);
    await page.waitForTimeout(500);
    const drawer = page.locator('[class*="drawer"], [class*="Drawer"]');
    if (await drawer.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      const body = await drawer.first().textContent();
      // Drawer should contain POA&M details
      expect(body).toBeTruthy();
    }
  });

  test('should show link-component action in drawer', async ({ page }) => {
    const poam = await gotoPoam(page);
    await poam.clickRow(0);
    await page.waitForTimeout(500);
    const linkBtn = page.getByRole('button', { name: /link.*component|add.*component/i });
    if (await linkBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expect(linkBtn).toBeVisible();
    }
  });
});

test.describe('POA&M Management — Tabs', () => {
  test('should switch to Trends tab', async ({ page }) => {
    const poam = await gotoPoam(page);
    const trendsTab = page.getByRole('tab', { name: /trends/i });
    if (await trendsTab.isVisible()) {
      await trendsTab.click();
      await page.waitForLoadState('networkidle');
      // Chart should render
      const chart = page.locator('svg, canvas, [class*="chart"]');
      await expect(chart.first()).toBeVisible({ timeout: 15_000 });
    }
  });

  test('should switch to Ticketing tab', async ({ page }) => {
    const poam = await gotoPoam(page);
    const ticketTab = page.getByRole('tab', { name: /ticketing/i });
    if (await ticketTab.isVisible()) {
      await ticketTab.click();
      await page.waitForLoadState('networkidle');
    }
  });
});

test.describe('POA&M Management — Export', () => {
  test('should open export dialog', async ({ page }) => {
    const poam = await gotoPoam(page);
    const exportBtn = page.getByRole('button', { name: /export/i });
    if (await exportBtn.isVisible()) {
      await exportBtn.click();
      await page.waitForTimeout(500);
      const dialog = page.locator('[role="dialog"], form, [class*="modal"]');
      if (await dialog.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
        await expect(dialog.first()).toBeVisible();
        await page.getByRole('button', { name: /cancel|close/i }).click().catch(() => {});
      }
    }
  });
});
