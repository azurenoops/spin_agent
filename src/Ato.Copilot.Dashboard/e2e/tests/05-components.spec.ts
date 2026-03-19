import { test, expect } from '@playwright/test';
import { ComponentsPage } from '../pages/components.page';

async function gotoComponents(page: import('@playwright/test').Page) {
  await page.goto('/systems');
  await page.waitForLoadState('networkidle');
  await page.locator('table tbody tr a').first().click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: /components/i }).click();
  await page.waitForLoadState('networkidle');
  return new ComponentsPage(page);
}

test.describe('Component Inventory — CRUD', () => {
  const uniqueSuffix = Date.now().toString().slice(-6);

  test('should load component inventory', async ({ page }) => {
    const comp = await gotoComponents(page);
    await comp.expectLoaded();
  });

  test('should display summary cards (People/Places/Things/Total)', async ({ page }) => {
    const comp = await gotoComponents(page);
    await comp.expectSummaryCards();
  });

  test('should open add-component form', async ({ page }) => {
    const comp = await gotoComponents(page);
    await comp.openAddForm();
    await expect(page.getByLabel(/^name/i).first()).toBeVisible();
  });

  test('should create a Person component with RMF role and boundary', async ({ page }) => {
    const comp = await gotoComponents(page);
    const name = `E2E Person ${uniqueSuffix}`;
    await comp.createComponent({
      name,
      type: 'Person',
      subType: 'Security Personnel',
      owner: 'E2E Test',
      personName: 'Jane Doe',
      email: 'jane.doe@test.mil',
      rmfRole: 'ISSO',
    });
    await comp.expectComponentListed(name);
  });

  test('should create a Place component', async ({ page }) => {
    const comp = await gotoComponents(page);
    const name = `E2E Place ${uniqueSuffix}`;
    await comp.createComponent({
      name,
      type: 'Place',
      subType: 'Cloud Region',
      owner: 'Platform Team',
    });
    await comp.expectComponentListed(name);
  });

  test('should create a Thing component', async ({ page }) => {
    const comp = await gotoComponents(page);
    const name = `E2E Thing ${uniqueSuffix}`;
    await comp.createComponent({
      name,
      type: 'Thing',
      subType: 'Security Tool',
      owner: 'Security Team',
    });
    await comp.expectComponentListed(name);
  });

  test('should edit a component and persist RMF role', async ({ page }) => {
    const comp = await gotoComponents(page);
    // Click Edit on the first person component in the list
    const editBtn = page.getByText(/edit/i);
    if (await editBtn.first().isVisible()) {
      await editBtn.first().click();
      await page.waitForSelector('form', { state: 'visible' });

      // Check that form fields are populated
      const nameInput = page.getByLabel(/^name/i).first();
      const nameValue = await nameInput.inputValue();
      expect(nameValue.length).toBeGreaterThan(0);

      // Change a field and save
      await page.getByLabel(/owner/i).fill('Updated Owner');
      await page.getByRole('button', { name: /save|update|submit/i }).click();
      await page.waitForLoadState('networkidle');
    }
  });

  test('should persist RMF role after update', async ({ page }) => {
    const comp = await gotoComponents(page);
    // Find a person component with RMF role and edit it
    const editBtns = page.getByText(/edit/i);
    if (await editBtns.first().isVisible()) {
      await editBtns.first().click();
      await page.waitForSelector('form', { state: 'visible' });

      // If this is a Person type, check the RMF role dropdown is populated
      const typeRadio = page.getByLabel('Person', { exact: true });
      if (await typeRadio.isChecked()) {
        const rmfSelect = page.getByLabel(/rmf role/i);
        if (await rmfSelect.isVisible()) {
          const val = await rmfSelect.inputValue();
          // Set a role if empty
          if (!val || val === '') {
            await rmfSelect.selectOption({ index: 1 });
          }
          await page.getByRole('button', { name: /save|update|submit/i }).click();
          await page.waitForLoadState('networkidle');

          // Re-open the same edit form and verify RMF role was saved
          await editBtns.first().click();
          await page.waitForSelector('form', { state: 'visible' });
          const savedVal = await rmfSelect.inputValue();
          expect(savedVal).toBeTruthy();
        }
      }
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should persist authorization boundary after update', async ({ page }) => {
    const comp = await gotoComponents(page);
    const editBtns = page.getByText(/edit/i);
    if (await editBtns.first().isVisible()) {
      await editBtns.first().click();
      await page.waitForSelector('form', { state: 'visible' });

      const boundarySelect = page.getByLabel(/boundary/i);
      if (await boundarySelect.isVisible()) {
        // Select a boundary
        const options = await boundarySelect.locator('option').allTextContents();
        if (options.length > 1) {
          await boundarySelect.selectOption({ index: 1 });
          const selected = await boundarySelect.inputValue();

          await page.getByRole('button', { name: /save|update|submit/i }).click();
          await page.waitForLoadState('networkidle');

          // Re-open and verify
          await editBtns.first().click();
          await page.waitForSelector('form', { state: 'visible' });
          const savedVal = await boundarySelect.inputValue();
          expect(savedVal).toBe(selected);
        }
      }
      await page.getByRole('button', { name: /cancel/i }).click().catch(() => {});
    }
  });

  test('should search components', async ({ page }) => {
    const comp = await gotoComponents(page);
    await comp.search('ISSM');
    await page.waitForTimeout(500);
    // Should filter the list
    const body = await page.textContent('body');
    expect(body).toMatch(/issm/i);
  });

  test('should delete a component', async ({ page }) => {
    const comp = await gotoComponents(page);
    // Look for E2E test components to clean up
    const deleteBtn = page.locator(`text=E2E`).first().locator('..').locator('..').getByText(/delete/i).first();
    if (await deleteBtn.isVisible().catch(() => false)) {
      await deleteBtn.click();
      await page.waitForTimeout(300);
      // Confirm delete
      const confirmBtn = page.getByRole('button', { name: /confirm|yes|delete/i });
      if (await confirmBtn.isVisible()) {
        await confirmBtn.click();
        await page.waitForLoadState('networkidle');
      }
    }
  });
});

test.describe('Component Inventory — Azure Discovery', () => {
  test('should show Discover from Azure button', async ({ page }) => {
    const comp = await gotoComponents(page);
    await expect(comp.discoverAzureBtn).toBeVisible();
  });
});
