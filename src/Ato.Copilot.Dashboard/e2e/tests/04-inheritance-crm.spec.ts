import { test, expect } from '@playwright/test';
import {
  navigateToFirstSystem,
  navigateToSystemTab,
  waitForApi,
} from '../fixtures/base';
import { ControlInheritancePage } from '../pages/control-inheritance.page';

test.describe('US3 — Generate and Export CRM', () => {
  let inheritance: ControlInheritancePage;

  test.beforeEach(async ({ page }) => {
    await navigateToFirstSystem(page);
    await navigateToSystemTab(page, 'Control Inheritance');
    await waitForApi(page);
    inheritance = new ControlInheritancePage(page);
    await inheritance.expectLoaded();
  });

  // ── Scenario 1: Generate CRM shows family-grouped view ────────────────

  test('should open CRM view with family-grouped controls', async ({ page }) => {
    await inheritance.openCrmView();

    // CRM panel heading should be visible
    await expect(page.locator('text=Customer Responsibility Matrix')).toBeVisible();

    // Should have summary stats (Inherited, Shared, Customer, Undesignated, Designated %)
    await expect(page.locator('text=Inherited')).toBeVisible();
    await expect(page.locator('text=Shared')).toBeVisible();
    await expect(page.locator('text=Customer')).toBeVisible();
    await expect(page.locator('text=Undesignated')).toBeVisible();
    await expect(page.locator('text=Designated')).toBeVisible();
  });

  // ── Scenario 2: CRM view shows system name and baseline ───────────────

  test('should display system name and baseline level in CRM header', async ({ page }) => {
    await inheritance.openCrmView();
    // The CRM panel has a subtitle with system name, baseline, and control count
    const subtitle = page.locator('text=/Baseline/i');
    await expect(subtitle).toBeVisible();
  });

  // ── Scenario 3: Family groups are rendered ─────────────────────────────

  test('should render control family group headers', async ({ page }) => {
    await inheritance.openCrmView();
    // Look for at least one family header (e.g., "AC — Access Control")
    const familyHeaders = page.locator('text=/^[A-Z]{2} — /');
    const count = await familyHeaders.count();
    expect(count).toBeGreaterThan(0);
  });

  // ── Scenario 4: Export format and layout selectors ─────────────────────

  test('should have format and layout selectors for export', async ({ page }) => {
    await inheritance.openCrmView();

    // Format selector should have CSV and Excel
    const formatSelect = page.locator('select').filter({ hasText: /CSV/ });
    await expect(formatSelect).toBeVisible();

    // Layout selector should have Custom, FedRAMP, eMASS
    const layoutSelect = page.locator('select').filter({ hasText: /Custom/ });
    await expect(layoutSelect).toBeVisible();
  });

  // ── Scenario 5: Export triggers download ───────────────────────────────

  test('should trigger CSV download when Export is clicked', async ({ page }) => {
    await inheritance.openCrmView();

    // Listen for download event
    const downloadPromise = page.waitForEvent('download');
    await inheritance.crmExportBtn.click();
    const download = await downloadPromise;

    // Verify the filename contains "crm" and ends with ".csv"
    expect(download.suggestedFilename()).toMatch(/crm.*\.csv$/);
  });

  test('should trigger Excel download with xlsx extension', async ({ page }) => {
    await inheritance.openCrmView();

    // Switch format to Excel
    const formatSelect = page.locator('select').filter({ hasText: /CSV/ });
    await formatSelect.selectOption('excel');

    const downloadPromise = page.waitForEvent('download');
    await inheritance.crmExportBtn.click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toMatch(/crm.*\.xlsx$/);
  });

  // ── Scenario 6: Close CRM view ────────────────────────────────────────

  test('should close CRM view when Close button is clicked', async ({ page }) => {
    await inheritance.openCrmView();
    await expect(page.locator('text=Customer Responsibility Matrix')).toBeVisible();

    await inheritance.closeCrmView();
    await expect(page.locator('text=Customer Responsibility Matrix')).not.toBeVisible();
  });
});
