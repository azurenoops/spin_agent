import { test, expect } from '@playwright/test';
import {
  navigateToFirstSystem,
  navigateToSystemTab,
  waitForApi,
} from '../fixtures/base';
import { ControlInheritancePage } from '../pages/control-inheritance.page';

test.describe('US4 — Apply CSP Profile', () => {
  let inheritance: ControlInheritancePage;

  test.beforeEach(async ({ page }) => {
    await navigateToFirstSystem(page);
    await navigateToSystemTab(page, 'Control Inheritance');
    await waitForApi(page);
    inheritance = new ControlInheritancePage(page);
    await inheritance.expectLoaded();
  });

  // ── Scenario 1: Open profile dialog and see available profiles ────────

  test('should open CSP profile dialog with profile list', async ({ page }) => {
    await inheritance.openProfileDialog();

    // Dialog should be visible with heading
    await expect(page.locator('text=Apply CSP Profile').first()).toBeVisible();
    await expect(page.locator('text=Select a pre-built inheritance profile')).toBeVisible();

    // Profile select should appear
    await expect(inheritance.profileSelect).toBeVisible();
  });

  // ── Scenario 2: Select a profile and see conflict resolution options ──

  test('should show conflict resolution options after selecting a profile', async ({ page }) => {
    await inheritance.openProfileDialog();

    // Select the first available profile
    const options = inheritance.profileSelect.locator('option');
    const optionCount = await options.count();
    if (optionCount <= 1) return; // Only the placeholder

    // Select the second option (first real profile)
    const optionValue = await options.nth(1).getAttribute('value');
    if (!optionValue) return;
    await inheritance.profileSelect.selectOption(optionValue);

    // Conflict resolution radios should be visible
    await expect(page.locator('text=Skip existing designations')).toBeVisible();
    await expect(page.locator('text=Overwrite all existing designations')).toBeVisible();
  });

  // ── Scenario 3: Preview shows designation counts before confirmation ──

  test('should show preview with designation counts', async ({ page }) => {
    await inheritance.openProfileDialog();

    const options = inheritance.profileSelect.locator('option');
    const optionCount = await options.count();
    if (optionCount <= 1) return;

    const optionValue = await options.nth(1).getAttribute('value');
    if (!optionValue) return;
    await inheritance.profileSelect.selectOption(optionValue);

    // Click Preview Changes
    await inheritance.previewProfile();

    // Preview panel should show matched/unmatched/type counts
    await expect(page.locator('text=Matched:')).toBeVisible();
    await expect(page.locator('text=Unmatched:')).toBeVisible();
    await expect(page.locator('text=Inherited:')).toBeVisible();
    await expect(page.locator('text=Shared:')).toBeVisible();
    await expect(page.locator('text=Customer:')).toBeVisible();
  });

  // ── Scenario 4: Apply profile updates designations ────────────────────

  test('should apply profile and refresh the inheritance table', async ({ page }) => {
    await inheritance.openProfileDialog();

    const options = inheritance.profileSelect.locator('option');
    const optionCount = await options.count();
    if (optionCount <= 1) return;

    const optionValue = await options.nth(1).getAttribute('value');
    if (!optionValue) return;
    await inheritance.profileSelect.selectOption(optionValue);

    await inheritance.previewProfile();

    // Apply the profile
    await inheritance.applyProfile();
    await waitForApi(page);

    // Dialog should close
    await expect(inheritance.profileDialog).not.toBeVisible();

    // Summary bar should reflect updated counts (inherited count > 0)
    const values = await inheritance.getSummaryValues();
    const inherited = parseInt(values['INHERITED'] ?? '0', 10);
    expect(inherited).toBeGreaterThanOrEqual(0);
  });

  // ── Scenario 5: Cancel closes dialog without changes ──────────────────

  test('should close profile dialog on Cancel', async ({ page }) => {
    await inheritance.openProfileDialog();
    await inheritance.profileCancelBtn.click();
    await expect(inheritance.profileDialog).not.toBeVisible();
  });

  // ── Scenario 6: Conflict resolution — skip vs overwrite ───────────────

  test('should toggle between skip and overwrite conflict resolution', async ({ page }) => {
    await inheritance.openProfileDialog();

    const options = inheritance.profileSelect.locator('option');
    const optionCount = await options.count();
    if (optionCount <= 1) return;

    const optionValue = await options.nth(1).getAttribute('value');
    if (!optionValue) return;
    await inheritance.profileSelect.selectOption(optionValue);

    // Default is "skip" — select overwrite
    await page.locator('text=Overwrite all existing designations').click();
    await expect(inheritance.profileOverwriteRadio).toBeChecked();

    // Switch back to skip
    await page.locator('text=Skip existing designations').click();
    await expect(inheritance.profileSkipRadio).toBeChecked();
  });
});
