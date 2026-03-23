import { test, expect } from '@playwright/test';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';
import {
  navigateToFirstSystem,
  navigateToSystemTab,
  waitForApi,
} from '../fixtures/base';
import { ControlInheritancePage } from '../pages/control-inheritance.page';

// ─── Test fixtures: generate a temporary CSV for import tests ────────────

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const FIXTURE_DIR = path.join(__dirname, '..', 'fixtures', 'data');
const CSV_FILE = path.join(FIXTURE_DIR, 'test-import.csv');

test.beforeAll(() => {
  if (!fs.existsSync(FIXTURE_DIR)) {
    fs.mkdirSync(FIXTURE_DIR, { recursive: true });
  }
  // Create a small CSV with known control IDs and one unrecognisable ID
  const rows = [
    'Control ID,Responsibility Type,Provider,Customer Responsibility',
    'AC-1,Inherited,Azure Government FedRAMP High,',
    'AC-2,Shared,Azure Government FedRAMP High,Customer manages user accounts',
    'AC-3,Customer,,Customer fully responsible',
    'FAKE-99,Inherited,Test Provider,',
  ];
  fs.writeFileSync(CSV_FILE, rows.join('\n'), 'utf-8');
});

test.afterAll(() => {
  if (fs.existsSync(CSV_FILE)) fs.unlinkSync(CSV_FILE);
  // Clean up fixture dir if empty
  if (fs.existsSync(FIXTURE_DIR) && fs.readdirSync(FIXTURE_DIR).length === 0) {
    fs.rmdirSync(FIXTURE_DIR);
  }
});

test.describe('US5 — Import CRM Spreadsheet', () => {
  let inheritance: ControlInheritancePage;

  test.beforeEach(async ({ page }) => {
    await navigateToFirstSystem(page);
    await navigateToSystemTab(page, 'Control Inheritance');
    await waitForApi(page);
    inheritance = new ControlInheritancePage(page);
    await inheritance.expectLoaded();
  });

  // ── Scenario 1: Open import dialog and upload CSV ─────────────────────

  test('should open import dialog with upload zone', async ({ page }) => {
    await inheritance.openImportDialog();

    // Should see the import heading and drag-drop area
    await expect(page.locator('text=Import CRM')).toBeVisible();
    await expect(page.locator('text=Drag & drop a CSV or Excel file')).toBeVisible();
    await expect(inheritance.importBrowseBtn).toBeVisible();
  });

  // ── Scenario 2: Upload shows column mapping dialog ────────────────────

  test('should show column mapping after uploading a CSV', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);

    // After upload, should move to mapping step
    await expect(page.locator('text=Column Mapping')).toBeVisible();
    await expect(page.locator('text=test-import.csv')).toBeVisible();

    // Should show detected columns as select options
    await expect(page.locator('text=Control ID *')).toBeVisible();
    await expect(page.locator('text=Inheritance Type *')).toBeVisible();
  });

  // ── Scenario 3: Suggested column mapping is auto-applied ──────────────

  test('should auto-suggest column mappings from CSV headers', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);

    // The selects should have auto-mapped values based on CSV headers
    const controlIdSelect = inheritance.importDialog.locator('select').nth(0);
    const selectedValue = await controlIdSelect.inputValue();
    // Should be mapped to something (non-empty if suggestion matched)
    expect(selectedValue).toBeTruthy();
  });

  // ── Scenario 4: Sample data preview ───────────────────────────────────

  test('should show sample data rows in the mapping step', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);

    // Sample data table should be present
    await expect(page.locator('text=/Sample Data/i')).toBeVisible();
    // Should have at least one row of sample data
    const sampleTable = inheritance.importDialog.locator('table');
    const sampleRows = sampleTable.locator('tbody tr');
    expect(await sampleRows.count()).toBeGreaterThan(0);
  });

  // ── Scenario 5: Conflict resolution options ───────────────────────────

  test('should show conflict resolution options in mapping step', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);

    await expect(page.locator('text=Conflict Resolution')).toBeVisible();
    await expect(page.locator('text=Overwrite existing designations')).toBeVisible();
    await expect(page.locator('text=Skip controls with existing designations')).toBeVisible();
  });

  // ── Scenario 6: Apply import shows results ────────────────────────────

  test('should apply import and show result summary', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);

    // Apply the import (mappings should be auto-suggested)
    await inheritance.applyImport();

    // Result step should show import complete message
    await expect(page.locator('text=Import complete!')).toBeVisible();

    // Should show result counters
    await expect(page.locator('text=Controls Imported')).toBeVisible();
    await expect(page.locator('text=Controls Skipped')).toBeVisible();
    await expect(page.locator('text=Not Found')).toBeVisible();
  });

  // ── Scenario 7: Not found controls are flagged ────────────────────────

  test('should flag unrecognizable control IDs in results', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);
    await inheritance.applyImport();

    // FAKE-99 should be listed as not found
    await expect(page.locator('text=Controls not found in baseline')).toBeVisible();
    await expect(page.locator('text=FAKE-99')).toBeVisible();
  });

  // ── Scenario 8: Close import dialog via Done ──────────────────────────

  test('should close import dialog with Done button after import', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.uploadImportFile(CSV_FILE);
    await inheritance.applyImport();

    await page.getByRole('button', { name: /Done/i }).click();
    await expect(inheritance.importDialog).not.toBeVisible();
  });

  // ── Scenario 9: Close import dialog via X ─────────────────────────────

  test('should close import dialog with X button', async ({ page }) => {
    await inheritance.openImportDialog();
    await inheritance.closeImportDialog();
    await expect(inheritance.importDialog).not.toBeVisible();
  });
});
