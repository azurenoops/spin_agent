import { expect, test } from '@playwright/test';
import { installWorkspaceFixture as fixture, switchWorkspace } from '../fixtures/workspace-shell';

for (const width of [1440, 390]) {
  test(`ordinary MissionOwner deep link, full roles, refresh and unsaved switch confirmation at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    // Act
    await page.goto('/systems/system-a/mission-purpose?review=1#mission');
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-a\/systems\/system-a\/profile\/MissionAndPurpose\?review=1#mission$/);
    await expect(page.getByPlaceholder("Describe the system's mission...")).toHaveValue('Authorized synthetic mission');
    const header = page.getByRole('region', { name: 'Active workspace' });
    await expect(header).toContainText('Active organization: Organization A');
    await expect(header).toContainText('Selected system: Synthetic Mission System');
    await expect(header).toContainText('Effective roles: MissionOwner, Reader');
    await expect(header.getByRole('link', { name: 'Manage memberships' })).toHaveCount(0);
    await page.getByPlaceholder("Describe the system's mission...").fill('Unsaved mission');
    await header.getByRole('button', { name: 'Switch workspace' }).click();
    await page.getByRole('button', { name: 'Stay here' }).click();
    await expect(page.getByPlaceholder("Describe the system's mission...")).toHaveValue('Unsaved mission');
    await page.reload();
    await expect(page.getByPlaceholder("Describe the system's mission...")).toHaveValue('Authorized synthetic mission');
    expect(requests.filter(request => /impersonat|select-tenant/.test(request.path))).toEqual([]);
    const accessIndex = requests.findIndex(request => request.path.endsWith('/workspace-access'));
    const profileIndex = requests.findIndex(request => request.path.endsWith('/profile/MissionAndPurpose'));
    expect(accessIndex).toBeGreaterThan(-1);
    expect(profileIndex).toBeGreaterThan(accessIndex);
    expect(requests[profileIndex]).toMatchObject({ kind: 'organization', tenant: 'org-a', mode: 'ordinary' });
    expect(errors).toEqual([]);
  });

  test(`provider landing uses validated CSP context at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!, { providerOnly: true });
    // Act
    await page.goto('/');
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/csp$/);
    await expect(page.getByTestId('csp-dashboard-page')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Active workspace' })).toContainText('Provider workspace · Synthetic Provider');
    await page.goto('/csp-dashboard?tab=organizations#overview');
    await expect(page).toHaveURL(/\/workspaces\/csp\?tab=organizations#overview$/);
    expect(requests.filter(request => /impersonat|select-tenant/.test(request.path))).toEqual([]);
  });

  test(`explicit choice, switch, browser history and independent tabs at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!, { multiple: true });
    // Act
    await page.goto('/?review=1#portfolio');
    await expect(page).toHaveURL(/\/login\/select-tenant$/);
    await page.getByRole('button', { name: /Organization A/ }).click();
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-a\?review=1#portfolio$/);
    const other = await context.newPage();
    await other.goto('/workspaces/organizations/org-b');
    await expect(other.getByRole('region', { name: 'Active workspace' })).toContainText('Organization B');
    await switchWorkspace(page, 'Organization B');
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-b$/);
    await page.goBack();
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-a\?review=1#portfolio$/);
    await expect(page.getByRole('region', { name: 'Active workspace' })).toContainText('Organization A');
    await other.reload();
    await expect(other.getByRole('region', { name: 'Active workspace' })).toContainText('Organization B');
    expect(requests.filter(request => /impersonat|select-tenant/.test(request.path))).toEqual([]);
    await other.close();
  });

  test(`mismatched context blocks all private children and permits recovery at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!, { multiple: true, mismatch: true });
    // Act
    await page.goto('/workspaces/organizations/org-b/systems/system-a/profile/MissionAndPurpose');
    // Assert
    await expect(page.getByRole('alert')).toContainText('did not authorize');
    await expect(page.getByRole('region', { name: 'Active workspace' })).toHaveCount(0);
    expect(requests.filter(request => request.path !== '/api/auth/login-config' && request.path !== '/api/auth/me')).toEqual([]);
    await page.getByRole('link', { name: 'Choose a workspace' }).click();
    await expect(page.getByRole('button', { name: /Organization A/ })).toBeVisible();
  });

  test(`confirmed audited support and cookie-only banner do not select another tab at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!, { providerOnly: true, support: true });
    await page.goto('/workspaces/csp');
    // Act
    await page.getByRole('button', { name: 'Audited support', exact: true }).click();
    expect(requests.some(request => request.method === 'POST')).toBe(false);
    await page.getByRole('button', { name: 'Start audited support' }).click();
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/support\/organizations\/org-a$/);
    await expect(page.getByRole('region', { name: 'Active workspace' })).toContainText('Audited support workspace');
    await expect(page.getByTestId('impersonation-banner-051')).toContainText('Impersonating Organization A');
    const ordinary = await context.newPage();
    await ordinary.goto('/workspaces/organizations/org-a');
    await expect(ordinary.getByRole('region', { name: 'Active workspace' })).toContainText('Organization workspace');
    await expect(ordinary.getByTestId('impersonation-banner-051')).toHaveCount(0);
    expect(requests.filter(request => request.method === 'DELETE')).toHaveLength(0);
    await page.getByTestId('impersonation-banner-051').getByRole('button', { name: 'Exit', exact: true }).click();
    await page.getByTestId('impersonation-exit-confirm-btn').click();
    await expect(page).toHaveURL(/\/workspaces\/csp$/);
    await expect(page.getByTestId('csp-dashboard-page')).toBeVisible();
    await expect(ordinary.getByRole('region', { name: 'Active workspace' })).toContainText('Organization workspace');
    await ordinary.close();
  });
}
