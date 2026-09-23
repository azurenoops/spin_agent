import { test, expect, type Page, type TestInfo, type BrowserContext } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { InitialAdministrator, ProvisioningResult } from '../../src/features/workspace-operations/types';

const directory = '11111111-1111-1111-1111-111111111111';
const object = '22222222-2222-2222-2222-222222222222';
const person = '33333333-3333-3333-3333-333333333333';
const administrator: InitialAdministrator = { directoryTenantId: directory, objectId: object,
  newPerson: { displayName: 'Jordan Lee', email: 'administrator@example.mil' } };
const organization = { id: 'org-new', displayName: 'Mission Operations', lifecycle: 'Active', onboarding: 'Pending',
  legalEntityName: 'Mission Operations Directorate', primaryPocName: 'Primary Contact', primaryPocEmail: 'contact@example.mil',
  memberCount: 0, setupState: 'Pending', systems: [], subscriptions: [], activity: [] };
const initial: ProvisioningResult = { tenantId: organization.id, operationId: 'operation-new', idempotencyKey: 'known-key',
  tenantState: 'Completed', administratorState: 'Pending', membershipState: 'Pending',
  personState: 'Pending', initialAdministrator: administrator, canEditAdministrator: true, lastError: null };

async function capture(page: Page, info: TestInfo, name: string) {
  await expect(page.locator('html')).not.toHaveClass(/dark/);
  await expect(page.locator('main')).toHaveCount(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath(`${name}.png`), fullPage: true });
}

async function fixture(context: BrowserContext, baseURL: string, failFirst = false) {
  await installWorkspaceFixture(context, baseURL, { providerOnly: true });
  let saved: ProvisioningResult = { ...initial };
  let creates = 0;
  let resumes = 0;
  await context.route('**/api/csp/organizations/org-new', route => route.fulfill({ json: { data: {
    ...organization, setupState: saved.administratorState === 'Completed' ? 'Completed' : 'Pending',
    memberCount: saved.membershipState === 'Completed' ? 1 : 0,
  } } }));
  await context.route('**/api/csp/organizations?*', route => route.fulfill({ json: { data: {
    items: [{ ...organization, reviewState: 'NotRequired', systemCount: 0, distinctAdoptionCount: 0,
      setupState: saved.administratorState === 'Completed' ? 'Completed' : 'Pending' }], total: 1, page: 1, pageSize: 25,
  } } }));
  await context.route('**/api/csp/organization-creations/*', route => route.fulfill({ json: { data: {
    tenantId: organization.id, operationId: saved.operationId, displayName: organization.displayName,
    status: 'Active', onboardingState: 'Pending', existing: true,
  } } }));
  await context.route('**/api/csp/dashboard/tenants', route => {
    creates++;
    const body = route.request().postDataJSON();
    saved = { ...initial, idempotencyKey: route.request().headers()['idempotency-key'],
      initialAdministrator: body.initialAdministrator ?? null, personState: body.initialAdministrator ? 'Pending' : 'NotRequested' };
    return route.fulfill({ status: 201, json: { data: { tenantId: organization.id, operationId: saved.operationId,
      displayName: organization.displayName, status: 'Active', onboardingState: 'Pending', existing: false } } });
  });
  await context.route('**/api/csp/organizations/org-new/provisioning**', route => {
    if (route.request().method() !== 'PATCH') return route.fulfill({ json: { data: saved } });
    resumes++;
    if (failFirst && resumes === 1) {
      saved = { ...saved, personState: 'Completed', membershipState: 'Completed', canEditAdministrator: false,
        initialAdministrator: { directoryTenantId: directory, objectId: object, personId: person },
        lastError: 'Administrator assignment could not be saved.' };
      return route.fulfill({ status: 503, json: { error: { code: 'ENROLLMENT_FAILED', message: saved.lastError } } });
    }
    saved = { ...saved, personState: 'Completed', membershipState: 'Completed', administratorState: 'Completed',
      canEditAdministrator: false, lastError: null };
    return route.fulfill({ json: { data: saved } });
  });
  return { counts: () => ({ creates, resumes }) };
}

async function fillDetails(page: Page) {
  await page.getByLabel('Organization name', { exact: true }).fill(organization.displayName);
  await page.getByLabel('Legal entity name (optional)').fill(organization.legalEntityName);
  await page.getByLabel('Primary contact name (optional)').fill(organization.primaryPocName);
  await page.getByLabel('Primary contact email (optional)').fill(organization.primaryPocEmail);
}

for (const width of [1440, 390]) {
  test(`details, administrator, review and completion match the Light organization mocks at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const state = await fixture(context, baseURL!);
    await page.goto('/workspaces/csp/organizations/new');
    // Act
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await expect(page.getByLabel('Organization name', { exact: true })).toBeFocused();
    await fillDetails(page);
    await expect(page.getByText('Enter an organization name.')).toHaveCount(0);
    await capture(page, info, 'organization-details');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByLabel('Directory tenant ID', { exact: true }).fill(directory);
    await page.getByLabel('User object ID', { exact: true }).fill(object);
    await page.getByLabel('Administrator name', { exact: true }).fill('Jordan Lee');
    await page.getByLabel('Administrator email', { exact: true }).fill('administrator@example.mil');
    await capture(page, info, 'initial-administrator');
    await page.getByRole('button', { name: 'Review setup' }).click();
    await expect(page.getByText('Organization-local Person record', { exact: true })).toBeVisible();
    expect(state.counts()).toEqual({ creates: 0, resumes: 0 });
    await capture(page, info, 'review');
    await page.getByRole('button', { name: 'Create organization', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Organization setup complete' })).toBeVisible();
    await capture(page, info, 'completion');
    await page.getByRole('link', { name: 'View organization', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Manage members', exact: true })).toBeVisible();
    await capture(page, info, 'handoff');
    // Assert
    expect(state.counts()).toEqual({ creates: 1, resumes: 1 });
    await expect(page.getByText('Setup complete', { exact: true })).toBeVisible();
    await expect(page.getByText('Identity confirmed', { exact: true })).toHaveCount(0);
  });

  test(`deferred enrollment stays pending and resumes from the organization list at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const state = await fixture(context, baseURL!);
    await page.goto('/workspaces/csp/organizations/new');
    // Act
    await fillDetails(page);
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByLabel('Enroll administrator now').focus();
    await page.keyboard.press('ArrowDown');
    await expect(page.getByLabel('Complete enrollment later')).toBeChecked();
    await page.getByRole('button', { name: 'Review setup' }).click();
    await page.getByRole('button', { name: 'Create organization', exact: true }).click();
    await expect(page.getByText('Organization created · Enrollment pending', { exact: true })).toBeVisible();
    await capture(page, info, 'deferred-enrollment');
    await page.getByRole('link', { name: 'Finish later' }).click();
    await page.getByRole('link', { name: 'Resume setup' }).click();
    await page.reload();
    // Assert
    await expect(page.getByText('Organization: Completed', { exact: true })).toBeVisible();
    await expect(page.getByText('Organization setup complete', { exact: true })).toHaveCount(0);
    expect(state.counts()).toEqual({ creates: 1, resumes: 0 });
  });

  test(`partial failure preserves saved Person and membership across refresh at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const state = await fixture(context, baseURL!, true);
    await page.goto('/workspaces/csp/organizations/org-new/provisioning?key=known-key');
    // Act
    await page.getByRole('button', { name: 'Continue enrollment' }).click();
    await expect(page.getByText('Membership: Completed', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Correct administrator details' })).toHaveCount(0);
    await capture(page, info, 'recovery');
    await page.reload();
    await page.getByRole('button', { name: 'Retry administrator enrollment' }).click();
    // Assert
    await expect(page.getByText('Organization setup complete', { exact: true })).toBeVisible();
    expect(state.counts()).toEqual({ creates: 0, resumes: 2 });
  });
}
