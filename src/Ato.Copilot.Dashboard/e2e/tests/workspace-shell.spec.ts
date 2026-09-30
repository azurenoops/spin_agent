import { expect, test } from '@playwright/test';
import { installWorkspaceFixture as fixture, openWorkspaceContext, switchWorkspace } from '../fixtures/workspace-shell';

for (const width of [1440, 390]) {
  test(`ordinary MissionOwner deep link, full roles, refresh and unsaved switch confirmation at ${width}px`, async ({ page, context, baseURL }, info) => {
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
    const library = page.getByRole('link', { name: 'Organization Narrative Library', exact: true });
    await expect(library).toHaveAttribute('href', '/workspaces/organizations/org-a/narrative-library');
    expect(await page.getByRole('button', { name: /^Chat \(/ }).evaluate(button => button.nextElementSibling?.getAttribute('aria-label')))
      .toBe('Organization Narrative Library');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath('condensed-workspace-header.png') });
    await expect(page.getByRole('region', { name: 'Active workspace' })).toHaveCount(0);
    const header = await openWorkspaceContext(page);
    await expect(header).toContainText('Active organization: Organization A');
    await expect(header).toContainText('Selected system: Synthetic Mission System');
    await expect(header).toContainText('Effective roles: MissionOwner, Reader');
    await expect(header.getByRole('link', { name: 'Manage memberships' })).toHaveCount(0);
    await page.getByPlaceholder("Describe the system's mission...").fill('Unsaved mission');
    await openWorkspaceContext(page);
    await page.getByRole('menuitem', { name: 'Switch workspace' }).click();
    await expect(page).toHaveURL(/\/profile\/MissionAndPurpose\?review=1#mission$/);
    await page.getByRole('dialog', { name: 'Switch workspace', exact: true }).getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByRole('button', { name: 'Account menu', exact: true })).toBeFocused();
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

  test(`Escape cancels a selected workspace without discarding the current draft at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!, { multiple: true });
    const current = '/workspaces/organizations/org-a/systems/system-a/profile/MissionAndPurpose?draft=1#mission';
    await page.goto(current);
    await page.getByPlaceholder("Describe the system's mission...").fill('Keep this unsaved mission');
    // Act
    await openWorkspaceContext(page);
    await page.getByRole('menuitem', { name: 'Switch workspace', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Switch workspace', exact: true });
    await dialog.getByRole('button', { name: /Organization B/ }).click();
    await expect(dialog.getByRole('button', { name: 'Switch to Organization B', exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    // Assert
    await expect(dialog).toHaveCount(0);
    await expect(page).toHaveURL(new RegExp(`${current.replace(/[?]/g, '\\?')}$`));
    await expect(page.getByPlaceholder("Describe the system's mission...")).toHaveValue('Keep this unsaved mission');
    await expect(page.getByRole('button', { name: 'Account menu', exact: true })).toBeFocused();
    expect(requests.filter(request => request.method !== 'GET')).toEqual([]);
  });

  test(`provider landing uses validated CSP context at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await fixture(context, baseURL!, { providerOnly: true });
    // Act
    await page.goto('/');
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/csp$/);
    await expect(page.getByRole('heading', { name: 'Your provider workspace', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Provider Narrative Library', exact: true })).toHaveAttribute('href', '/workspaces/csp/narrative-library');
    await expect(await openWorkspaceContext(page)).toContainText('Provider workspace · Synthetic Provider');
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
    await expect(await openWorkspaceContext(other)).toContainText('Organization B');
    await switchWorkspace(page, 'Organization B');
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-b$/);
    await page.goBack();
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/organizations\/org-a\?review=1#portfolio$/);
    await expect(await openWorkspaceContext(page)).toContainText('Organization A');
    await other.reload();
    await expect(await openWorkspaceContext(other)).toContainText('Organization B');
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
    await page.goto('/workspaces/csp/organizations/org-a');
    // Act
    await page.getByRole('button', { name: 'Audited support', exact: true }).first().click();
    expect(requests.some(request => request.method === 'POST')).toBe(false);
    await page.getByRole('button', { name: 'Start audited support' }).click();
    // Assert
    await expect(page).toHaveURL(/\/workspaces\/support\/organizations\/org-a$/);
    await expect(await openWorkspaceContext(page)).toContainText('Audited support workspace');
    await expect(page.getByTestId('impersonation-banner-051')).toContainText('Impersonating Organization A');
    const ordinary = await context.newPage();
    await ordinary.goto('/workspaces/organizations/org-a');
    await expect(await openWorkspaceContext(ordinary)).toContainText('Organization workspace');
    await expect(ordinary.getByTestId('impersonation-banner-051')).toHaveCount(0);
    expect(requests.filter(request => request.method === 'DELETE')).toHaveLength(0);
    await page.getByTestId('impersonation-banner-051').getByRole('button', { name: 'Exit', exact: true }).click();
    await page.getByTestId('impersonation-exit-confirm-btn').click();
    await expect(page).toHaveURL(/\/workspaces\/csp$/);
    await expect(page.getByRole('heading', { name: 'Your provider workspace', exact: true })).toBeVisible();
    await expect(await openWorkspaceContext(ordinary)).toContainText('Organization workspace');
    await ordinary.close();
  });
}
