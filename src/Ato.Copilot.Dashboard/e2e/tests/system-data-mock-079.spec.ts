import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { DataTypeItem } from '../../src/types/dashboard';

const root = '/workspaces/organizations/org-a/systems/system-a';
const endpoint = '/api/dashboard/systems/system-a/profile/DataTypes';
for (const width of [1440, 390]) {
  for (const readOnly of [false, true]) {
    test(`Data mock and ${readOnly ? 'read-only inspection' : 'draft actions'} at ${width}px`, async ({ page, context, baseURL }, info) => {
      // Arrange: realistic synthetic records are never written to the demo database.
      await page.setViewportSize({ width, height: 1050 });
      await installWorkspaceFixture(context, baseURL!);
      let rows: DataTypeItem[] = [
        { id: 'data-a', dataTypeName: 'Mission support records', description: 'Operational information', sensitivityClassification: 'CUI',
          source: 'Internal System', destination: 'Internal Database', applicableRegulations: 'FISMA', sortOrder: 0 },
        { id: 'data-b', dataTypeName: 'Contact directory', description: 'Personal information', sensitivityClassification: 'PII',
          source: 'User Input', destination: 'User Display', applicableRegulations: 'Privacy Act', sortOrder: 1 },
      ];
      let content = '{"dataOverview":"Mission information handling.","highestSensitivityLevel":"CUI","legacySource":"preserved"}';
      let fail = true;
      let status = 'Draft';
      const writes: { path: string; method: string; body: unknown }[] = [];
      page.on('request', request => {
        const path = new URL(request.url()).pathname;
        if (path.startsWith('/api/') && request.method() !== 'GET') writes.push({ path, method: request.method(), body: request.postDataJSON() });
      });
      await context.route(`**${endpoint}`, route => {
        if (route.request().method() === 'PUT') {
          if (readOnly) return route.fulfill({ status: 403, json: { error: 'Read-only data profile.' } });
          if (fail) { fail = false; return route.fulfill({ status: 409, json: { error: 'Data profile changed. Review before retrying.' } }); }
          const request = route.request().postDataJSON();
          content = request.content;
          rows = request.childItems.map((row: DataTypeItem, index: number) => ({ ...row, id: row.id ?? `new-data-${index}`, sortOrder: index }));
        }
        return route.fulfill({ json: { id: 'profile-data', sectionType: 'DataTypes', governanceStatus: status,
          canEditProfile: !readOnly, draftContent: content, dataTypeEntries: rows,
          userCategories: [], ppsEntries: [], leveragedAuthorizations: [], approvedContent: null,
        } });
      });
      await context.route('**/api/dashboard/systems/system-a/profile/submit', route => {
        expect(route.request().postDataJSON()).toEqual({ action: 'submit', sectionTypes: ['DataTypes'] });
        status = 'UnderReview';
        return route.fulfill({ json: { submittedSections: ['DataTypes'], skippedSections: [] } });
      });
      // Act / Assert: exact Data composition, one Add entry, compact rows and real link destinations.
      await page.goto(`${root}/profile/DataTypes`);
      const heading = page.getByRole('heading', { name: 'Data types & sensitivity', exact: true });
      await expect(heading).toBeVisible();
      const header = page.locator('main header').first();
      const tabs = page.getByRole('navigation', { name: 'System task views' });
      await expect(tabs.getByRole('link')).toHaveCount(6);
      await expect(tabs.locator('[aria-current="page"]')).toHaveText('Data');
      expect((await heading.boundingBox())!.y).toBeLessThan((await tabs.boundingBox())!.y);
      const table = page.getByRole('table', { name: 'Information types', exact: true });
      await expect(table).toContainText('Mission support records');
      await expect(table).toContainText('Contact directory');
      await expect(table.getByRole('button')).toHaveCount(2);
      await expect(table.getByRole('combobox')).toHaveCount(0);
      await expect(page.locator('summary').filter({ hasText: /^Information handling context$/ })).toHaveCount(0);
      await expect(page.getByLabel('Data Overview', { exact: true })).toHaveCount(0);
      await expect(table).not.toContainText('Moderate');
      await expect(page.getByRole('link', { name: 'Preview contribution', exact: true })).toHaveAttribute('href', `${root}/documents/preview?contribution=DataTypes`);
      await expect(page.getByRole('link', { name: 'Categorization & baseline', exact: true })).toHaveAttribute('href', `${root}/baseline`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      if (width === 1440) expect(await table.evaluate(node => node.scrollWidth <= node.parentElement!.clientWidth)).toBe(true);
      await page.screenshot({ path: info.outputPath(`data-${readOnly ? 'reader' : 'editor'}-${width}.png`) });
      const open = table.getByRole('button', { name: 'Open data type Mission support records', exact: true });
      await open.click();
      await expect(page.getByRole('dialog')).toContainText('Internal Database');
      await expect(page.getByRole('dialog')).toContainText('FISMA');
      expect(writes).toEqual([]);
      if (readOnly) {
        await expect(page.getByRole('button', { name: 'Add data type', exact: true })).toHaveCount(0);
        await expect(page.getByRole('dialog').getByRole('button', { name: 'Edit data type', exact: true })).toHaveCount(0);
        await page.keyboard.press('Escape');
        await expect(open).toBeFocused();
        await page.getByRole('button', { name: 'Manage information handling context', exact: true }).click();
        const contextDialog = page.getByRole('dialog', { name: 'System-wide information handling context', exact: true });
        await expect(contextDialog.getByLabel('Data Overview', { exact: true })).toBeDisabled();
        await expect(contextDialog.getByRole('button', { name: 'Save information context', exact: true })).toHaveCount(0);
        await page.keyboard.press('Escape');
        return;
      }
      await page.keyboard.press('Escape');
      await expect(open).toBeFocused();
      const add = header.getByRole('button', { name: 'Add data type', exact: true });
      await expect(page.getByRole('button', { name: 'Add data type', exact: true })).toHaveCount(1);
      await expect(header.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
      // Act / Assert: new names and all canonical fields persist only on explicit Save.
      await add.click();
      let dialog = page.getByRole('dialog', { name: 'Add data type', exact: true });
      await dialog.getByLabel('Data Type', { exact: true }).fill('Telemetry archive');
      await dialog.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await expect(dialog.getByRole('alert')).toContainText('Classification is required');
      await dialog.getByLabel('Classification', { exact: true }).selectOption('CUI');
      await dialog.getByLabel('Description', { exact: true }).fill('Archived operational telemetry.');
      await dialog.getByLabel('Source', { exact: true }).selectOption('Sensor / IoT');
      await dialog.getByLabel('Destination', { exact: true }).selectOption('Archive');
      await dialog.getByLabel('Regulations', { exact: true }).selectOption('FISMA');
      await dialog.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await expect(add).toBeFocused();
      await expect(page.getByRole('button', { name: 'Submit for Review', exact: true })).toBeDisabled();
      expect(writes).toEqual([]);
      const contextAction = page.getByRole('button', { name: 'Manage information handling context', exact: true });
      await contextAction.click();
      const contextDialog = page.getByRole('dialog', { name: 'System-wide information handling context', exact: true });
      await contextDialog.getByLabel('Data Overview', { exact: true }).fill('Discard this context only.');
      await contextDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
      await expect(contextAction).toBeFocused();
      await expect(table).toContainText('Telemetry archive');
      expect(writes).toEqual([]);
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Data profile changed. Review before retrying.', { exact: true })).toBeVisible();
      await expect(table).toContainText('Telemetry archive');
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
      expect(JSON.parse(content)).toEqual({ dataOverview: 'Mission information handling.', highestSensitivityLevel: 'CUI', legacySource: 'preserved' });
      expect(rows[2]).toMatchObject({ dataTypeName: 'Telemetry archive', sensitivityClassification: 'CUI', source: 'Sensor / IoT', destination: 'Archive', applicableRegulations: 'FISMA' });
      await page.reload();
      await expect(table).toContainText('Telemetry archive');
      await table.getByRole('button', { name: 'Open data type Telemetry archive', exact: true }).click();
      await page.getByRole('dialog').getByRole('button', { name: 'Edit data type', exact: true }).click();
      await page.getByRole('dialog').getByLabel('Description', { exact: true }).fill('Reviewed telemetry handling.');
      await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
      expect(rows[2]!.description).toBe('Reviewed telemetry handling.');
      await table.getByRole('button', { name: 'Open data type Telemetry archive', exact: true }).click();
      await page.getByRole('dialog').getByRole('button', { name: 'Remove data type', exact: true }).click();
      await page.getByRole('button', { name: 'Remove from draft', exact: true }).click();
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
      expect(rows.map(row => row.id)).toEqual(['data-a', 'data-b']);
      await contextAction.click();
      await page.getByRole('dialog').getByLabel('Data Overview', { exact: true }).fill('Updated system-wide handling context.');
      await page.getByRole('button', { name: 'Save information context', exact: true }).click();
      await expect(page.getByRole('dialog')).toHaveCount(0);
      expect(JSON.parse(content)).toEqual({ dataOverview: 'Updated system-wide handling context.', highestSensitivityLevel: 'CUI', legacySource: 'preserved' });
      expect(rows.map(row => row.id)).toEqual(['data-a', 'data-b']);
      // Act / Assert: review is a saved DataTypes section operation, not per-row fake approval.
      await page.getByRole('button', { name: 'Submit for Review', exact: true }).click();
      await expect(page.getByText('Section submitted for review.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Add data type', exact: true })).toHaveCount(0);
      await expect(table).toContainText('UnderReview');
      expect(writes.filter(write => write.method === 'POST').map(write => write.path)).toEqual(['/api/dashboard/systems/system-a/profile/submit']);
      expect(writes.every(write => write.path === endpoint || write.path.endsWith('/profile/submit'))).toBe(true);
    });
  }
}
