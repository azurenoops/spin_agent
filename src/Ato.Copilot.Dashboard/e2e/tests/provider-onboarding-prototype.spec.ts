import { expect, test, type Page } from '@playwright/test';

const prototypeUrl = '/provider-onboarding-prototype.html';

async function chooseScenario(page: Page, name: string) {
  await page.getByRole('button', { name }).click();
}

async function startNewSetup(page: Page) {
  await chooseScenario(page, 'New authorized provider administrator');
  await page.getByRole('button', { name: 'Start provider setup' }).click();
}

async function completeIdentity(page: Page) {
  await page.getByRole('button', { name: 'Save & continue' }).click();
}

async function deferContacts(page: Page) {
  await page.getByLabel('Defer incomplete contact details and create follow-up work').check();
  await page.getByRole('button', { name: 'Save & continue' }).click();
}

test.beforeEach(async ({ page }) => {
  await page.goto(prototypeUrl);
  await page.getByRole('button', { name: 'Reset prototype' }).click();
});

test('existing ATO journey retains one receipt and ends with unresolved onboarding work', async ({ page }) => {
  // Arrange
  await startNewSetup(page);
  await completeIdentity(page);
  await deferContacts(page);
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByText('We have an existing authorization', { exact: true }).click();
  await page.getByLabel('Choose synthetic eMASS package').setInputFiles({
    name: 'flank-speed-emass.zip',
    mimeType: 'application/zip',
    buffer: Buffer.from('Synthetic eMASS package'),
  });
  await page.getByRole('button', { name: 'Analyze synthetic eMASS package' }).click();
  await expect(page.getByRole('heading', { name: 'Package understanding' })).toBeVisible();
  await expect(page.getByText('Exact authorization boundary mapping remains unresolved')).toBeVisible();
  await page.getByRole('button', { name: 'Use proposed facts' }).click();
  await expect(page.getByLabel('Decision reference')).toHaveValue('eMASS-ATO-FS-2025-017');
  await page.getByLabel('Issuing AO').fill('Synthetic AO');
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByLabel('Upload supporting material').check();
  await page.getByLabel('Choose synthetic source file').setInputFiles({
    name: 'flank-speed-ato.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('Synthetic prototype source'),
  });

  // Act
  await page.getByRole('button', { name: 'Simulate uncertain upload response' }).click();
  await expect(page.getByRole('status')).toContainText('Receipt uncertain');
  await page.getByRole('button', { name: 'Reconcile original receipt' }).click();
  await page.getByRole('button', { name: 'Simulate processing' }).click();
  await page.getByRole('button', { name: 'Complete extraction' }).click();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByRole('button', { name: 'Finish provider setup' }).click();

  // Assert
  await expect(page.getByRole('heading', { name: 'Provider setup complete' })).toBeVisible();
  await expect(page.getByText('Onboarding is complete; offering workspaces are outside this prototype.')).toBeVisible();
  await expect(page.getByText('Map existing authorization coverage to exact scope')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open provider workspace' }))
    .toHaveAttribute('href', '/workspaces/csp/authorizations');
  await expect(page.getByLabel('Prototype persona')).toHaveCount(0);
});

test('initial authorization ends with package-preparation follow-up without a decision', async ({ page }) => {
  // Arrange
  await startNewSetup(page);
  await completeIdentity(page);
  await deferContacts(page);
  await page.getByRole('button', { name: 'Save & continue' }).click();

  // Act
  await page.getByText('We are preparing for initial authorization', { exact: true }).click();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByText('Add records later', { exact: true }).click();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByRole('button', { name: 'Finish provider setup' }).click();

  // Assert
  await expect(page.getByText('Package-preparation tasks open')).toBeVisible();
  await expect(page.getByText('Prepare initial authorization package')).toBeVisible();
  await expect(page.getByText('No recorded authorization decision')).toBeVisible();
});

test('deferred setup ends with accountable onboarding follow-up', async ({ page }) => {
  // Arrange
  await startNewSetup(page);
  await completeIdentity(page);
  await deferContacts(page);

  // Act
  await page.getByLabel('Add an offering later').check();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByText('We need to determine the authorization scope', { exact: true }).click();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByText('Add records later', { exact: true }).click();
  await page.getByRole('button', { name: 'Save & continue' }).click();
  await page.getByRole('button', { name: 'Finish provider setup' }).click();

  // Assert
  await expect(page.getByRole('heading', { name: 'Provider setup complete' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Add the first offering' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Determine authorization scope and boundary' })).toBeVisible();
  await expect(page.getByText('Onboarding is complete; offering workspaces are outside this prototype.')).toBeVisible();
});

test('invited ISSO confirms membership without provider registration or workspace handoff', async ({ page }) => {
  // Act
  await chooseScenario(page, 'Invited user joining an existing provider');
  await page.getByRole('button', { name: 'Confirm membership and continue' }).click();

  // Assert
  await expect(page.getByRole('heading', { name: 'Membership confirmed' })).toBeVisible();
  await expect(page.getByText('Provider registration was not repeated.')).toBeVisible();
  await expect(page.getByText('This onboarding prototype stops before opening the assigned workspace.')).toBeVisible();
  await expect(page.getByLabel('Prototype persona')).toHaveCount(0);
});

test('save, reload, and resume preserve the original synthetic draft', async ({ page }) => {
  // Arrange
  await startNewSetup(page);
  await page.getByLabel('Service contact email').fill('resume@example.invalid');

  // Act
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  await page.reload();
  await chooseScenario(page, 'Administrator resuming unfinished setup');
  await page.getByRole('button', { name: 'Resume saved setup' }).click();

  // Assert
  await expect(page.getByLabel('Service contact email')).toHaveValue('resume@example.invalid');
  await expect(page.getByText('Saved provider setup restored from this browser.')).toBeVisible();
});

test('mobile setup remains keyboard reachable without horizontal overflow', async ({ page }) => {
  // Arrange
  await page.setViewportSize({ width: 390, height: 844 });
  await startNewSetup(page);

  // Act
  await page.keyboard.press('Tab');
  const dimensions = await page.locator('html').evaluate(element => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth,
  }));

  // Assert
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  await expect(page.getByRole('button', { name: 'Save & finish later' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save & continue' })).toBeVisible();
  await expect(page.getByLabel('Operating organization')).toBeVisible();
});
