import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Page Object Model for the Control Inheritance page.
 * Route: /systems/:id/inheritance
 */
export class ControlInheritancePage {
  readonly page: Page;

  // ─── Header buttons ────────────────────────────────────────────────────────
  readonly heading: Locator;
  readonly applyProfileBtn: Locator;
  readonly importCrmBtn: Locator;
  readonly generateCrmBtn: Locator;

  // ─── Summary bar ───────────────────────────────────────────────────────────
  readonly summaryBar: Locator;

  // ─── Filter bar ────────────────────────────────────────────────────────────
  readonly searchInput: Locator;
  readonly familyFilter: Locator;
  readonly typeFilter: Locator;

  // ─── Table ─────────────────────────────────────────────────────────────────
  readonly table: Locator;
  readonly selectAllCheckbox: Locator;
  readonly tableRows: Locator;

  // ─── Bulk update toolbar ───────────────────────────────────────────────────
  readonly bulkTypeSelect: Locator;
  readonly bulkProviderInput: Locator;
  readonly bulkRespInput: Locator;
  readonly bulkApplyBtn: Locator;
  readonly bulkClearBtn: Locator;

  // ─── CRM view ─────────────────────────────────────────────────────────────
  readonly crmPanel: Locator;
  readonly crmExportFormatSelect: Locator;
  readonly crmExportLayoutSelect: Locator;
  readonly crmExportBtn: Locator;
  readonly crmCloseBtn: Locator;

  // ─── CSP profile dialog ────────────────────────────────────────────────────
  readonly profileDialog: Locator;
  readonly profileSelect: Locator;
  readonly profileSkipRadio: Locator;
  readonly profileOverwriteRadio: Locator;
  readonly profilePreviewBtn: Locator;
  readonly profileApplyBtn: Locator;
  readonly profileCancelBtn: Locator;

  // ─── CRM import dialog ────────────────────────────────────────────────────
  readonly importDialog: Locator;
  readonly importFileInput: Locator;
  readonly importBrowseBtn: Locator;

  // ─── Audit panel ──────────────────────────────────────────────────────────
  readonly auditPanel: Locator;

  constructor(page: Page) {
    this.page = page;

    // Header
    this.heading = page.getByRole('heading', { name: /Control Inheritance/i });
    this.applyProfileBtn = page.getByRole('button', { name: /Apply CSP Profile/i });
    this.importCrmBtn = page.getByRole('button', { name: /Import CRM/i });
    this.generateCrmBtn = page.getByRole('button', { name: /Generate CRM/i });

    // Summary bar — the 6-card grid
    this.summaryBar = page.locator('.grid').first();

    // Filter bar
    this.searchInput = page.getByPlaceholder(/Search control ID or provider/i);
    this.familyFilter = page.locator('select').filter({ hasText: /All Families/i });
    this.typeFilter = page.locator('select').filter({ hasText: /All Types/i });

    // Table
    this.table = page.locator('table').first();
    this.selectAllCheckbox = this.table.locator('thead input[type="checkbox"]');
    this.tableRows = this.table.locator('tbody tr');

    // Bulk toolbar (only visible when controls selected)
    this.bulkTypeSelect = page.locator('select[name="bulkType"]');
    this.bulkProviderInput = page.locator('input[name="bulkProvider"]');
    this.bulkRespInput = page.locator('input[name="bulkResp"]');
    this.bulkApplyBtn = page.locator('form').getByRole('button', { name: /^Apply$/i });
    this.bulkClearBtn = page.getByRole('button', { name: /Clear/i });

    // CRM view panel
    this.crmPanel = page.locator('text=Customer Responsibility Matrix').locator('..');
    this.crmExportFormatSelect = page.locator('select').filter({ hasText: /CSV|Excel/i }).last();
    this.crmExportLayoutSelect = page.locator('select').filter({ hasText: /Custom|FedRAMP|eMASS/i }).last();
    this.crmExportBtn = page.getByRole('button', { name: /^Export$/i });
    this.crmCloseBtn = page.getByRole('button', { name: /^Close$/i });

    // CSP profile dialog
    this.profileDialog = page.locator('.fixed').filter({ hasText: /Apply CSP Profile/i });
    this.profileSelect = this.profileDialog.locator('select').first();
    this.profileSkipRadio = this.profileDialog.locator('input[type="radio"][value="skip"]');
    this.profileOverwriteRadio = this.profileDialog.locator('input[type="radio"][value="overwrite"]');
    this.profilePreviewBtn = this.profileDialog.getByRole('button', { name: /Preview Changes/i });
    this.profileApplyBtn = this.profileDialog.getByRole('button', { name: /Apply Profile/i });
    this.profileCancelBtn = this.profileDialog.getByRole('button', { name: /Cancel/i });

    // CRM import dialog
    this.importDialog = page.locator('.fixed').filter({ hasText: /Import CRM/i });
    this.importFileInput = this.importDialog.locator('input[type="file"]');
    this.importBrowseBtn = this.importDialog.getByRole('button', { name: /Browse Files/i });

    // Audit panel
    this.auditPanel = page.locator('text=Audit History:').locator('..');
  }

  /** Navigate directly to the Control Inheritance page for a system */
  async goto(systemId: string) {
    await this.page.goto(`/systems/${systemId}/inheritance`);
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.heading).toBeVisible();
  }

  /** Wait for the inheritance table to have at least one row */
  async expectTablePopulated() {
    await expect(this.tableRows.first()).toBeVisible();
  }

  /** Get the text content of summary cards */
  async getSummaryValues(): Promise<Record<string, string>> {
    const cards = this.summaryBar.locator('div.rounded-xl');
    const count = await cards.count();
    const values: Record<string, string> = {};
    for (let i = 0; i < count; i++) {
      const label = await cards.nth(i).locator('p').first().textContent();
      const value = await cards.nth(i).locator('p').nth(1).textContent();
      if (label && value) values[label.trim()] = value.trim();
    }
    return values;
  }

  /** Click a control ID link to open audit panel */
  async clickControlId(controlId: string) {
    await this.table.getByRole('button', { name: controlId }).click();
    await this.page.waitForLoadState('networkidle');
  }

  /** Start inline editing on a control row */
  async editRow(controlId: string) {
    const row = this.table.locator('tr').filter({ hasText: controlId });
    await row.getByRole('button', { name: /Edit/i }).click();
  }

  /** Set inline edit fields and save */
  async saveInlineEdit(opts: {
    inheritanceType?: string;
    provider?: string;
    customerResponsibility?: string;
  }) {
    if (opts.inheritanceType) {
      await this.table.locator('tbody select').selectOption(opts.inheritanceType);
    }
    if (opts.provider !== undefined) {
      const input = this.table.locator('tbody input[list="known-providers"]');
      await input.clear();
      await input.fill(opts.provider);
    }
    if (opts.customerResponsibility !== undefined) {
      const input = this.table.locator('tbody input[placeholder="Customer responsibility"]');
      await input.clear();
      await input.fill(opts.customerResponsibility);
    }
    await this.table.getByRole('button', { name: /Save/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  /** Select a row by its checkbox */
  async selectRow(controlId: string) {
    const row = this.table.locator('tr').filter({ hasText: controlId });
    await row.locator('input[type="checkbox"]').check();
  }

  /** Select all rows via the header checkbox */
  async selectAll() {
    await this.selectAllCheckbox.check();
  }

  /** Apply bulk update */
  async applyBulkUpdate(opts: {
    inheritanceType: string;
    provider?: string;
    customerResponsibility?: string;
  }) {
    await this.bulkTypeSelect.selectOption(opts.inheritanceType);
    if (opts.provider) {
      await this.bulkProviderInput.fill(opts.provider);
    }
    if (opts.customerResponsibility) {
      await this.bulkRespInput.fill(opts.customerResponsibility);
    }
    await this.bulkApplyBtn.click();
    await this.page.waitForLoadState('networkidle');
  }

  /** Filter by family */
  async filterByFamily(family: string) {
    await this.familyFilter.selectOption(family);
    await this.page.waitForLoadState('networkidle');
  }

  /** Filter by inheritance type */
  async filterByType(type: string) {
    await this.typeFilter.selectOption(type);
    await this.page.waitForLoadState('networkidle');
  }

  /** Get the count of visible table rows */
  async getRowCount(): Promise<number> {
    return this.tableRows.count();
  }

  /** Get the text of the Showing X-Y of Z pagination */
  async getPaginationText(): Promise<string> {
    return (await this.page.locator('text=/Showing \\d/').textContent()) ?? '';
  }

  // ─── CRM helpers ──────────────────────────────────────────────────────────

  async openCrmView() {
    await this.generateCrmBtn.click();
    await this.page.waitForLoadState('networkidle');
  }

  async closeCrmView() {
    await this.crmCloseBtn.click();
  }

  // ─── CSP profile helpers ───────────────────────────────────────────────────

  async openProfileDialog() {
    await this.applyProfileBtn.click();
    await this.page.waitForLoadState('networkidle');
  }

  async selectProfile(profileLabel: string) {
    await this.profileSelect.selectOption({ label: profileLabel });
  }

  async previewProfile() {
    await this.profilePreviewBtn.click();
    await this.page.waitForLoadState('networkidle');
  }

  async applyProfile() {
    await this.profileApplyBtn.click();
    await this.page.waitForLoadState('networkidle');
  }

  // ─── Import helpers ────────────────────────────────────────────────────────

  async openImportDialog() {
    await this.importCrmBtn.click();
  }

  /** Upload a file in the import dialog (bypasses the browse button) */
  async uploadImportFile(filePath: string) {
    await this.importFileInput.setInputFiles(filePath);
    await this.page.waitForLoadState('networkidle');
  }

  /** Set a column mapping in the import dialog */
  async setImportMapping(field: string, column: string) {
    const row = this.importDialog.locator('label', { hasText: field }).locator('..');
    await row.locator('select').selectOption(column);
  }

  async applyImport() {
    await this.importDialog.getByRole('button', { name: /Apply Import/i }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async closeImportDialog() {
    await this.importDialog.locator('button', { hasText: '×' }).click();
  }
}
