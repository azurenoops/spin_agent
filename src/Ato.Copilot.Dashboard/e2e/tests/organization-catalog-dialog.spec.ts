import { expect, test, type Locator } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

async function expectDialogFits(dialog: Locator) {
  const geometry = await dialog.evaluate(element => ({
    left: element.getBoundingClientRect().left, right: element.getBoundingClientRect().right,
    width: innerWidth, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
  }));
  expect(geometry.left).toBeGreaterThanOrEqual(0);
  expect(geometry.right).toBeLessThanOrEqual(geometry.width);
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth);
}

for (const width of [1440, 390]) {
  for (const kind of ['capability', 'component'] as const) {
    test(`organization ${kind} creation stages writes without any system at ${width}px`, async ({ page, context, baseURL }, testInfo) => {
      // Arrange
      await page.setViewportSize({ width, height: 950 });
      const requests = await installWorkspaceFixture(context, baseURL!);
      await context.route('**/api/workspaces/organizations/org-a/catalog-access', route =>
        route.fulfill({ json: { data: { canManageCatalog: true } } }));
      await context.route('**/api/workspaces/organizations/org-a/capabilities?*', route => route.fulfill({
        json: { data: { items: [], total: 0, page: 1, pageSize: 25, aggregateState: 'Available' } },
      }));
      let saves = 0;
      await context.route('**/api/workspaces/organizations/org-a/catalog-additions', async route => {
        saves++;
        const body = route.request().postDataJSON();
        expect(body).toMatchObject({ source: 'local', recordType: kind, owner: 'Organization security team',
          organizationContribution: 'We operate the enterprise service and maintain its procedures.' });
        expect(body).not.toHaveProperty('systemId');
        expect(body).not.toHaveProperty('subscribe');
        if (kind === 'capability') {
          expect(body.capability).toMatchObject({ name: 'Enterprise protection', category: 'AU' });
          expect(body.newComponents).toEqual([expect.objectContaining({ name: 'Enterprise policy', componentType: 'Policy' })]);
        } else {
          expect(body.component).toMatchObject({ name: 'Enterprise protection', componentType: 'Person' });
          expect(body).not.toHaveProperty('capability');
        }
        return route.fulfill({ status: 201, json: { data: {
          source: 'local', recordType: kind, recordId: 'created-record', name: 'Enterprise protection', existing: false,
        } } });
      });

      // Act
      await page.goto('/workspaces/organizations/org-a/security-capabilities');
      await page.getByRole('button', { name: 'Add capability', exact: true }).click();
      const dialog = page.getByRole('dialog', { name: 'Add a security capability' });
      await dialog.getByRole('radio', { name: kind === 'capability' ? 'Capability' : 'Component', exact: true }).check();
      await expect(dialog.getByRole('radio', { name: 'Create in organization' })).toBeChecked();
      await expect(dialog.getByText('Organization: Organization A', { exact: true })).toBeVisible();
      if (width === 1440) expect((await dialog.boundingBox())!.width).toBeLessThanOrEqual(600);
      await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Enterprise protection');
      await dialog.getByRole('textbox', { name: 'Description', exact: true }).fill('Reusable organization protection service');
      if (kind === 'capability') await dialog.getByRole('combobox', { name: 'Control family', exact: true }).selectOption('AU');
      else await dialog.getByRole('combobox', { name: 'Component type', exact: true }).selectOption('Person');
      await dialog.getByRole('textbox', { name: 'Organization owner', exact: true }).fill('Organization security team');
      if (kind === 'capability') {
        await dialog.getByRole('button', { name: 'Add or link component', exact: true }).click();
        await dialog.getByRole('button', { name: 'Add new component', exact: true }).click();
        await dialog.getByRole('textbox', { name: 'Component name', exact: true }).fill('Enterprise policy');
        await dialog.getByRole('textbox', { name: 'Component description', exact: true }).fill('Organization-wide operating standards');
        await dialog.getByRole('combobox', { name: 'Component type', exact: true }).selectOption('Policy');
        await dialog.getByRole('button', { name: 'Stage component', exact: true }).click();
        await dialog.getByRole('button', { name: 'Done selecting components', exact: true }).click();
        await expect(dialog.getByRole('button', { name: 'Remove Enterprise policy' })).toBeVisible();
      }
      await expectDialogFits(dialog);
      await page.screenshot({ path: testInfo.outputPath(`organization-${kind}-source.png`), fullPage: true });
      await dialog.getByRole('button', { name: 'Continue', exact: true }).click();
      await expect(dialog.getByRole('textbox', { name: 'Organization owner', exact: true })).toHaveValue('Organization security team');
      await dialog.getByRole('textbox', { name: 'Organization contribution', exact: true }).fill('We operate the enterprise service and maintain its procedures.');
      await expectDialogFits(dialog);
      await page.screenshot({ path: testInfo.outputPath(`organization-${kind}-contribution.png`), fullPage: true });
      expect(saves).toBe(0);
      await dialog.getByRole('button', { name: 'Review changes', exact: true }).click();
      await expect(dialog.getByRole('definition').filter({ hasText: 'Enterprise protection' })).toBeVisible();
      const summary = dialog.getByRole('complementary', { name: 'Review changes' });
      await expect(summary).toBeVisible();
      await expect(dialog.getByLabel('Organization addition progress').getByText('Review', { exact: true })).toBeVisible();
      if (width === 1440) {
        const details = await dialog.locator('dl').boundingBox();
        expect((await summary.boundingBox())!.x).toBeGreaterThanOrEqual(details!.x + details!.width);
      }
      await expectDialogFits(dialog);
      await page.screenshot({ path: testInfo.outputPath(`organization-${kind}-review.png`), fullPage: true });
      await dialog.getByRole('button', { name: 'Save to organization', exact: true }).click();

      // Assert
      await expect(dialog.getByRole('heading', { name: 'Added to organization', exact: true })).toBeVisible();
      await expect(dialog.getByRole('link', { name: `View ${kind}`, exact: true })).toHaveAttribute('href',
        `/workspaces/organizations/org-a/security-capabilities/local/created-record?recordType=${kind}`);
      expect(saves).toBe(1);
      expect(requests.filter(request => /\/systems|capability-setups/.test(request.path))).toEqual([]);
      await expect(page.locator('html')).not.toHaveClass(/dark/);
      expect(await dialog.evaluate(element => element.matches(':modal'))).toBe(true);
      await expectDialogFits(dialog);
      await dialog.getByRole('button', { name: 'Done', exact: true }).click();
      await expect(dialog).toHaveCount(0);
    });
  }
}
