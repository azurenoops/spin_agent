import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

for (const width of [1440, 390]) {
  test(`empty organization offers clean mission intake at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic HTTP fixtures validate the current UI, not backend authorization or persistence.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!);
    const mutations: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/') && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) {
        mutations.push(`${request.method()} ${new URL(request.url()).pathname}`);
      }
    });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));

    // Act / Assert
    await page.goto('/workspaces/organizations/org-a/portfolio');
    await expect(page.getByText('No systems available in this workspace.')).toBeVisible();
    const create = page.getByRole('link', { name: 'Create mission system' });
    await expect(create).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/new');
    await page.screenshot({ path: info.outputPath(`organization-landing-${width}.png`), fullPage: true });
    await create.click();
    const wizard = page.getByRole('dialog', { name: 'Register New System' });
    await expect(wizard).toBeVisible();
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-a\/systems$/);
    await expect(wizard.getByRole('heading', { name: 'Step 1: System Registration' })).toBeVisible();
    await expect(wizard.getByPlaceholder('e.g. ACME Portal')).toHaveValue('');
    await expect(wizard.getByPlaceholder('e.g. AP', { exact: true })).toHaveValue('');
    expect(mutations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`clean-mission-intake-${width}.png`), fullPage: true });
    await wizard.getByRole('button', { name: 'Close wizard' }).click();
    await expect(wizard).not.toBeVisible();
    expect(mutations).toEqual([]);
    expect(errors).toEqual([]);
  });
}
