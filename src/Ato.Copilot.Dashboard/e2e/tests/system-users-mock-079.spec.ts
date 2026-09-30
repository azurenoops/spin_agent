import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { UserCategoryItem } from '../../src/types/dashboard';

const root = '/workspaces/organizations/org-a/systems/system-a';
const path = '/api/dashboard/systems/system-a/profile/UsersAndAccess';
const samples: UserCategoryItem[] = [
  { id: 'staff', categoryName: 'Mission staff', description: 'Internal users', approximateCount: 120, accessMethod: 'Web Portal (SSO)', dataSensitivityLevel: 'CUI', sortOrder: 0 },
  { id: 'operators', categoryName: 'Support operators', description: 'Privileged users', approximateCount: 8, accessMethod: 'CAC/PIV', dataSensitivityLevel: 'CUI', sortOrder: 1 },
  { id: 'partners', categoryName: 'External partners', description: 'Guest users', approximateCount: null, accessMethod: 'VPN + MFA', dataSensitivityLevel: 'Public', sortOrder: 2 },
];

for (const width of [1440, 390]) {
  for (const readOnly of [false, true]) {
    test(`Users mock layout and ${readOnly ? 'inspection' : 'draft CRUD'} at ${width}px`, async ({ page, context, baseURL }, info) => {
      // Arrange: synthetic populations test real components, not demo persistence.
      await page.setViewportSize({ width, height: 1050 });
      await installWorkspaceFixture(context, baseURL!);
      let rows: UserCategoryItem[] = structuredClone(samples).map(row => ({ ...row, revision: 1, governanceStatus: 'Draft',
        canSubmit: !readOnly, canReview: false, canWithdraw: false }));
      let rejectSave = false;
      const writes: { method: string; pathname: string }[] = [];
      page.on('request', request => {
        const pathname = new URL(request.url()).pathname;
        if (pathname.startsWith('/api/') && request.method() !== 'GET') writes.push({ method: request.method(), pathname });
      });
      await context.route(`**${path}`, async route => {
        if (route.request().method() === 'PUT') {
          if (readOnly) return route.fulfill({ status: 403, json: { error: 'Read-only access.' } });
          if (rejectSave) return route.fulfill({ status: 409, json: { error: 'Review permission changed. Retry after checking access.' } });
          const request = route.request().postDataJSON();
          expect(JSON.parse(request.content)).toEqual({ accessOverview: 'Recorded access model.', authenticationMethod: '["CAC/PIV"]', retainedSource: 'source-a' });
          rows = request.childItems.map((row: UserCategoryItem, index: number) => ({ ...row, id: row.id ?? `saved-${index}`,
            revision: (row.revision ?? 0) + 1, governanceStatus: 'Draft', canSubmit: true, canReview: false, canWithdraw: false }));
        }
        return route.fulfill({ json: {
          id: 'profile-users', sectionType: 'UsersAndAccess', canEditProfile: !readOnly, governanceStatus: 'Draft',
          draftContent: '{"accessOverview":"Recorded access model.","authenticationMethod":"[\\"CAC/PIV\\"]","retainedSource":"source-a"}',
          userCategories: rows, dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [], approvedContent: null,
          lastEditedAt: '2026-09-27T12:00:00Z',
        } });
      });
      await context.route(`**${path}/user-categories/*/review`, async route => {
        const id = new URL(route.request().url()).pathname.split('/').at(-2);
        const request = route.request().postDataJSON();
        const target = rows.find(row => row.id === id)!;
        expect(request.expectedRevision).toBe(target.revision);
        expect(['submit', 'approve']).toContain(request.action);
        rows = rows.map(row => row.id === id ? { ...row, revision: row.revision! + 1,
          governanceStatus: request.action === 'submit' ? 'UnderReview' : 'Approved',
          canSubmit: false, canWithdraw: request.action === 'submit', canReview: request.action === 'submit' } : row);
        return route.fulfill({ json: { id: 'profile-users', sectionType: 'UsersAndAccess', canEditProfile: true, governanceStatus: 'Draft',
          reviewResult: { categoryId: id, action: request.action, revision: target.revision! + 1,
            governanceStatus: request.action === 'submit' ? 'UnderReview' : 'Approved', pendingDeletion: false },
          draftContent: '{"accessOverview":"Recorded access model.","authenticationMethod":"[\\"CAC/PIV\\"]","retainedSource":"source-a"}',
          userCategories: rows, dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [], approvedContent: null } });
      });

      // Act / Assert: exact page-primary placement, six tabs and compact, honest columns.
      await page.goto(`${root}/profile/UsersAndAccess`);
      const heading = page.getByRole('heading', { name: 'Users & access', exact: true });
      await expect(heading).toBeVisible();
      const header = page.locator('main header').first();
      const nav = page.getByRole('navigation', { name: 'System task views' });
      await expect(nav.getByRole('link')).toHaveCount(7);
      await expect(nav.locator('[aria-current="page"]')).toHaveText('Users');
      expect((await heading.boundingBox())!.y).toBeLessThan((await nav.boundingBox())!.y);
      const table = page.getByRole('table', { name: 'User categories' });
      await expect(table.getByRole('columnheader')).toHaveText(['Category', 'Context', 'Count', 'Access method', 'Actions']);
      await expect(table.getByRole('row')).toHaveCount(4);
      await expect(table.getByRole('row', { name: /External partners/ })).toContainText('Not recorded');
      await expect(table.getByRole('button')).toHaveCount(3);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      if (width === 1440) expect(await table.evaluate(node => node.scrollWidth <= node.parentElement!.clientWidth)).toBe(true);
      await page.screenshot({ path: info.outputPath(`users-${readOnly ? 'reader' : 'editor'}-${width}.png`), fullPage: true });

      // Act / Assert: Open is read-only inspection and retains all recorded fields.
      const open = page.getByRole('button', { name: 'Open user category Mission staff' });
      await open.click();
      await expect(page.getByRole('dialog')).toContainText('CUI');
      await expect(page.getByRole('dialog')).toContainText('Internal users');
      await expect(page.getByRole('dialog')).toContainText('does not grant accounts');
      expect(writes).toEqual([]);
      if (readOnly) {
        await expect(page.getByRole('button', { name: 'Add user category', exact: true })).toHaveCount(0);
        await expect(page.getByRole('dialog').getByRole('button', { name: 'Edit category', exact: true })).toHaveCount(0);
        await expect(page.getByRole('dialog').getByRole('button', { name: 'Remove category', exact: true })).toHaveCount(0);
        await page.keyboard.press('Escape');
        await expect(open).toBeFocused();
        return;
      }
      await page.keyboard.press('Escape');
      await expect(open).toBeFocused();

      // Act / Assert: named additions remain a draft until explicit Save Draft.
      const add = header.getByRole('button', { name: 'Add user category', exact: true });
      await expect(page.getByRole('button', { name: 'Add user category', exact: true })).toHaveCount(1);
      await expect(header.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
      await add.click();
      await page.getByRole('dialog').getByLabel('Category', { exact: true }).fill('Mission coordinators');
      await page.getByRole('dialog').getByLabel('Count', { exact: true }).fill('1.5');
      await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await expect(page.getByRole('dialog').getByRole('alert')).toContainText('whole number');
      await page.getByRole('dialog').getByLabel('Count', { exact: true }).fill('12');
      await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await expect(add).toBeFocused();
      await expect(table.getByRole('row')).toHaveCount(5);
      await expect(page.getByRole('button', { name: 'Submit for Review', exact: true })).toHaveCount(0);
      await expect(page.getByText('Save draft changes before reviewing an individual user category.', { exact: true })).toBeVisible();
      expect(writes).toEqual([]);
      rejectSave = true;
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Review permission changed. Retry after checking access.', { exact: true })).toBeVisible();
      await expect(table).toContainText('Mission coordinators');
      rejectSave = false;
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Users working data saved. This does not approve any category.', { exact: true })).toBeVisible();
      await page.reload();
      await expect(table.getByRole('row')).toHaveCount(5);

      // Act / Assert: edit/reorder and removal affect the selected population only.
      await open.click();
      await page.getByRole('dialog').getByRole('button', { name: 'Move down', exact: true }).click();
      await page.getByRole('dialog').getByRole('button', { name: 'Edit category', exact: true }).click();
      await page.getByRole('dialog').getByLabel('Description', { exact: true }).fill('Reviewed staff description');
      await page.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Users working data saved. This does not approve any category.', { exact: true })).toBeVisible();
      expect(rows[1]).toMatchObject({ id: 'staff', sortOrder: 1, description: 'Reviewed staff description', approximateCount: 120 });
      await page.getByRole('button', { name: 'Open user category Mission coordinators' }).click();
      await page.getByRole('dialog').getByRole('button', { name: 'Remove category', exact: true }).click();
      await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
      await expect(page.getByRole('dialog')).toContainText('Mission coordinators');
      await page.getByRole('dialog').getByRole('button', { name: 'Remove category', exact: true }).click();
      await page.getByRole('button', { name: 'Remove from draft', exact: true }).click();
      await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
      await expect(page.getByText('Users working data saved. This does not approve any category.', { exact: true })).toBeVisible();
      await expect(table.getByRole('row')).toHaveCount(4);
      expect(rows.map(row => row.id)).toEqual(['operators', 'staff', 'partners']);
      expect(writes).toEqual(Array.from({ length: 4 }, () => ({ method: 'PUT', pathname: path })));

      // Act / Assert: review targets one persisted row and leaves sibling drafts alone.
      await open.click();
      await page.getByRole('button', { name: 'Submit category for review', exact: true }).click();
      await page.getByRole('button', { name: 'Confirm submission', exact: true }).click();
      await expect(page.getByText('User category submitted for review.', { exact: true })).toBeVisible();
      await open.click();
      await expect(page.getByRole('button', { name: 'Edit category', exact: true })).toBeDisabled();
      await page.getByRole('button', { name: 'Approve category', exact: true }).click();
      await page.getByRole('button', { name: 'Confirm approval', exact: true }).click();
      await expect(page.getByText('User category approved.', { exact: true })).toBeVisible();
      expect(rows.find(row => row.id === 'staff')?.governanceStatus).toBe('Approved');
      expect(rows.filter(row => row.id !== 'staff').every(row => row.governanceStatus === 'Draft')).toBe(true);
      expect(writes.slice(4)).toEqual(Array.from({ length: 2 }, () => ({ method: 'POST', pathname: `${path}/user-categories/staff/review` })));
    });
  }
}
