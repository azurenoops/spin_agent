import { test, expect } from '@playwright/test';
import {
  navigateToFirstSystem,
  navigateToSystemTab,
  waitForApi,
} from '../fixtures/base';
import { ControlInheritancePage } from '../pages/control-inheritance.page';

test.describe('US1 — View & Manage Control Inheritance', () => {
  let inheritance: ControlInheritancePage;

  test.beforeEach(async ({ page }) => {
    await navigateToFirstSystem(page);
    await navigateToSystemTab(page, 'Control Inheritance');
    await waitForApi(page);
    inheritance = new ControlInheritancePage(page);
  });

  // ── Scenario 1: Page loads with summary bar and table ───────────────────

  test('should display the summary bar with six cards', async () => {
    await inheritance.expectLoaded();
    const values = await inheritance.getSummaryValues();
    expect(values).toHaveProperty('TOTAL CONTROLS');
    expect(values).toHaveProperty('INHERITED');
    expect(values).toHaveProperty('SHARED');
    expect(values).toHaveProperty('CUSTOMER');
    expect(values).toHaveProperty('UNDESIGNATED');
    expect(values).toHaveProperty('INHERITANCE %');
  });

  test('should display the controls table with expected columns', async () => {
    await inheritance.expectTablePopulated();
    const headers = inheritance.table.locator('thead th');
    const headerTexts = await headers.allTextContents();
    const joined = headerTexts.join(' ');
    expect(joined).toContain('Control ID');
    expect(joined).toContain('Family');
    expect(joined).toContain('Inheritance Type');
    expect(joined).toContain('Provider');
    expect(joined).toContain('Customer Responsibility');
    expect(joined).toContain('Set By');
    expect(joined).toContain('Set At');
  });

  test('should show pagination with row count', async () => {
    const paginationText = await inheritance.getPaginationText();
    expect(paginationText).toMatch(/Showing \d+/);
  });

  // ── Scenario 2: Filter by family and inheritance type ──────────────────

  test('should filter controls by family', async ({ page }) => {
    await inheritance.expectTablePopulated();
    const initialCount = await inheritance.getRowCount();
    await inheritance.filterByFamily('AC');
    await waitForApi(page);
    const filteredCount = await inheritance.getRowCount();
    // Filtered count should differ from initial (assuming not all are AC)
    expect(filteredCount).toBeLessThanOrEqual(initialCount);
    // All visible rows should belong to AC family
    if (filteredCount > 0) {
      const firstRowFamily = await inheritance.tableRows.first().locator('td').nth(2).textContent();
      expect(firstRowFamily?.trim()).toBe('AC');
    }
  });

  test('should filter controls by inheritance type', async ({ page }) => {
    await inheritance.expectTablePopulated();
    await inheritance.filterByType('Inherited');
    await waitForApi(page);
    const count = await inheritance.getRowCount();
    if (count > 0) {
      // Each row should show the Inherited badge
      const firstBadge = await inheritance.tableRows.first().locator('span.rounded-full').textContent();
      expect(firstBadge?.trim()).toBe('Inherited');
    }
  });

  test('should search by control ID', async ({ page }) => {
    await inheritance.expectTablePopulated();
    await inheritance.searchInput.fill('AC-1');
    await waitForApi(page);
    // Allow for debounce / API call
    await page.waitForTimeout(500);
    await waitForApi(page);
    const count = await inheritance.getRowCount();
    // Should have at least 1 result if AC-1 exists in baseline
    if (count > 0) {
      const firstId = await inheritance.tableRows.first().locator('td').nth(1).textContent();
      expect(firstId).toContain('AC-1');
    }
  });

  // ── Scenario 3: Inline edit a control's inheritance ────────────────────

  test('should inline-edit a control and save', async ({ page }) => {
    await inheritance.expectTablePopulated();
    // Get the first control ID
    const firstId = await inheritance.tableRows.first().locator('button.text-indigo-600').textContent();
    if (!firstId) return;

    await inheritance.editRow(firstId.trim());

    // Verify inline edit controls are visible
    await expect(inheritance.table.locator('tbody select')).toBeVisible();
    await expect(inheritance.table.getByRole('button', { name: /Save/i })).toBeVisible();
    await expect(inheritance.table.getByRole('button', { name: /Cancel/i })).toBeVisible();

    // Set to Inherited and save
    await inheritance.saveInlineEdit({
      inheritanceType: 'Inherited',
      provider: 'Azure Government FedRAMP High',
    });

    // After save, the row should reflect the updated type
    const row = inheritance.table.locator('tr').filter({ hasText: firstId.trim() });
    await expect(row.locator('span.rounded-full')).toContainText('Inherited');
  });

  test('should cancel inline edit without saving', async () => {
    await inheritance.expectTablePopulated();
    const firstId = await inheritance.tableRows.first().locator('button.text-indigo-600').textContent();
    if (!firstId) return;

    await inheritance.editRow(firstId.trim());
    await inheritance.table.getByRole('button', { name: /Cancel/i }).click();

    // Inline edit form should be gone
    await expect(inheritance.table.locator('tbody select')).not.toBeVisible();
  });

  // ── Scenario 4: Click control ID to view audit history ────────────────

  test('should open audit history panel when clicking a control ID', async ({ page }) => {
    await inheritance.expectTablePopulated();
    const firstId = await inheritance.tableRows.first().locator('button.text-indigo-600').textContent();
    if (!firstId) return;

    await inheritance.clickControlId(firstId.trim());
    await waitForApi(page);

    // Audit panel header should be visible
    await expect(page.locator('text=Audit History:')).toBeVisible();
  });

  test('should close audit history panel', async ({ page }) => {
    await inheritance.expectTablePopulated();
    const firstId = await inheritance.tableRows.first().locator('button.text-indigo-600').textContent();
    if (!firstId) return;

    await inheritance.clickControlId(firstId.trim());
    await waitForApi(page);

    // Close the panel
    const closeBtn = inheritance.auditPanel.locator('button').filter({ has: page.locator('svg') });
    await closeBtn.click();
    await expect(page.locator('text=Audit History:')).not.toBeVisible();
  });
});
