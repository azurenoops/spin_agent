import { test, expect } from '@playwright/test';
import {
  navigateToFirstSystem,
  navigateToSystemTab,
  waitForApi,
} from '../fixtures/base';
import { ControlInheritancePage } from '../pages/control-inheritance.page';

test.describe('US2 — Bulk-Update Inheritance', () => {
  let inheritance: ControlInheritancePage;

  test.beforeEach(async ({ page }) => {
    await navigateToFirstSystem(page);
    await navigateToSystemTab(page, 'Control Inheritance');
    await waitForApi(page);
    inheritance = new ControlInheritancePage(page);
    await inheritance.expectTablePopulated();
  });

  // ── Scenario 1: Select multiple controls shows bulk toolbar ─────────────

  test('should show bulk toolbar when controls are selected', async () => {
    // Select the first row checkbox
    const firstId = await inheritance.tableRows.first().locator('button.text-indigo-600').textContent();
    if (!firstId) return;
    await inheritance.selectRow(firstId.trim());

    // Bulk toolbar should appear with "1 control selected"
    await expect(inheritance.bulkTypeSelect).toBeVisible();
    await expect(inheritance.page.locator('text=/1 control.* selected/i')).toBeVisible();
  });

  test('should show correct count when multiple controls selected', async () => {
    // Select first two rows
    const rows = inheritance.tableRows;
    const count = await rows.count();
    if (count < 2) return;

    const id1 = await rows.nth(0).locator('button.text-indigo-600').textContent();
    const id2 = await rows.nth(1).locator('button.text-indigo-600').textContent();
    if (!id1 || !id2) return;

    await inheritance.selectRow(id1.trim());
    await inheritance.selectRow(id2.trim());

    await expect(inheritance.page.locator('text=/2 controls selected/i')).toBeVisible();
  });

  // ── Scenario 2: Select all via header checkbox ─────────────────────────

  test('should select all visible controls via header checkbox', async () => {
    await inheritance.selectAll();
    await expect(inheritance.bulkTypeSelect).toBeVisible();
    // The selected count should match visible rows
    const rowCount = await inheritance.getRowCount();
    const selectedText = await inheritance.page.locator('text=/\\d+ controls? selected/i').textContent();
    expect(selectedText).toContain(String(rowCount));
  });

  // ── Scenario 3: Apply bulk update sets designations ────────────────────

  test('should apply bulk update to selected controls', async ({ page }) => {
    // Select first two rows
    const rows = inheritance.tableRows;
    const count = await rows.count();
    if (count < 2) return;

    const id1 = (await rows.nth(0).locator('button.text-indigo-600').textContent())?.trim();
    const id2 = (await rows.nth(1).locator('button.text-indigo-600').textContent())?.trim();
    if (!id1 || !id2) return;

    await inheritance.selectRow(id1);
    await inheritance.selectRow(id2);

    // Apply bulk update: set type to "Shared" with a provider
    await inheritance.applyBulkUpdate({
      inheritanceType: 'Shared',
      provider: 'AWS GovCloud FedRAMP High',
      customerResponsibility: 'Customer manages IAM policies',
    });

    await waitForApi(page);

    // After bulk update, toolbar should be hidden (selection cleared)
    await expect(inheritance.bulkTypeSelect).not.toBeVisible();

    // The rows should reflect the updated type
    const row1 = inheritance.table.locator('tr').filter({ hasText: id1 });
    await expect(row1.locator('span.rounded-full')).toContainText('Shared');
  });

  // ── Scenario 4: Clear selection ────────────────────────────────────────

  test('should clear selection when Clear button is clicked', async () => {
    const firstId = await inheritance.tableRows.first().locator('button.text-indigo-600').textContent();
    if (!firstId) return;

    await inheritance.selectRow(firstId.trim());
    await expect(inheritance.bulkClearBtn).toBeVisible();

    await inheritance.bulkClearBtn.click();
    await expect(inheritance.bulkTypeSelect).not.toBeVisible();
  });
});
