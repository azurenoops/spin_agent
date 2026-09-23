import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const capability = {
  source: 'provider', recordId: 'capability-a', recordType: 'capability', name: 'Security Monitoring & Detection',
  description: 'Detect, investigate and respond to suspicious activity.', category: 'Detection',
  availability: 'Available', isSubscribed: true, systemCount: 1, mutationAuthority: 'provider',
  supportingComponents: [{ id: 'component-a', name: 'Sentinel log analytics', componentType: 'Thing', source: 'provider' }],
  sourceName: 'Provider catalog', controlCount: 2, reviewState: 'NeedsReview', responsibility: null,
};
const detail = {
  capability, supportingComponents: capability.supportingComponents,
  responsibilities: [], narrativeReviews: [], sourceReference: 'Reviewed source SSP', providerName: 'Synthetic Provider',
  controlCoverage: [{ controlId: 'AU-6', designation: 'NotDesignated', remainingDuty: null },
    { controlId: 'SI-4', designation: 'NotDesignated', remainingDuty: null }],
};

async function capture(page: Page, testInfo: TestInfo, name: string) {
  await expect(page.locator('html')).not.toHaveClass(/dark/);
  await expect(page.locator('main')).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await expect(page.locator('main')).toHaveCount(1);
  const dimensions = await page.evaluate(() => ({
    width: window.innerWidth, documentWidth: document.documentElement.scrollWidth,
    overflowing: Array.from(document.querySelectorAll('body *')).map(element => ({
      tag: element.tagName, classes: element.className, right: element.getBoundingClientRect().right,
      overflow: getComputedStyle(element).overflowX, position: getComputedStyle(element).position,
    })).filter(element => element.right > window.innerWidth).slice(0, 20),
  }));
  expect(dimensions.documentWidth, JSON.stringify(dimensions)).toBeLessThanOrEqual(dimensions.width);
  const dialog = page.getByRole('dialog');
  if (await dialog.count()) {
    const geometry = await dialog.evaluate(element => ({
      modal: element.matches(':modal'), left: element.getBoundingClientRect().left,
      right: element.getBoundingClientRect().right, top: element.getBoundingClientRect().top,
      bottom: element.getBoundingClientRect().bottom, height: window.innerHeight,
      clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
    }));
    expect(geometry.modal).toBe(true);
    expect(geometry.left).toBeGreaterThanOrEqual(0);
    expect(geometry.right).toBeLessThanOrEqual(dimensions.width);
    expect(geometry.top).toBeGreaterThanOrEqual(0);
    expect(geometry.bottom).toBeLessThanOrEqual(geometry.height);
    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth);
  }
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true });
}

for (const width of [1440, 390]) {
  test(`organization table and relationship detail retain the light layout at ${width}px`, async ({ page, context, baseURL }, testInfo) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await context.route('**/api/csp/organizations?*', route => route.fulfill({ json: { data: {
      items: [{ id: 'org-a', displayName: 'Organization A', lifecycle: 'Active', onboarding: 'Active',
        reviewState: 'NeedsReview', systemCount: 1, distinctAdoptionCount: 2 }],
      page: 1, pageSize: 25, total: 1, aggregateState: 'Available',
    } } }));
    await context.route('**/api/csp/organizations/org-a', route => route.fulfill({ json: { data: {
      id: 'org-a', displayName: 'Organization A', lifecycle: 'Active', onboarding: 'Active',
      systems: [{ id: 'system-a', name: 'Mission system', rmfPhase: 'Implement', isActive: true }],
      subscriptions: [{ id: 'subscription-a', systemId: 'system-a', capabilityId: 'capability-a', sourceRevision: '1', isActive: true }],
      activity: [{ action: 'Capability subscribed', occurredAt: '2026-09-21T00:00:00Z', outcome: 'Completed' }],
    } } }));
    await context.route('**/api/csp/organizations/org-a/provisioning/current', route => route.fulfill({
      status: 404, json: { status: 'error', error: { code: 'PROVISIONING_NOT_FOUND', message: 'No operation exists' } },
    }));

    // Act
    await page.goto('/workspaces/csp/organizations');
    await expect(page.getByRole('columnheader', { name: 'Organization', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Organization A', exact: true })).toBeVisible();
    await capture(page, testInfo, 'organizations');
    await page.getByRole('link', { name: 'Organization A', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'Overview & systems' })).toBeVisible();
    await expect(page.getByText('Mission system', { exact: true })).toBeVisible();
    await capture(page, testInfo, 'organization-detail');

    // Assert
    await page.getByRole('tab', { name: 'Provider subscriptions' }).click();
    await expect(page.getByText('capability-a', { exact: true })).toBeVisible();
    await page.getByRole('tab', { name: 'Provider activity' }).click();
    await expect(page.getByText('Capability subscribed', { exact: true })).toBeVisible();
    await expect(page.locator('html')).not.toHaveClass(/dark/);
  });

  test(`capability library, detail and guided setup match the light hierarchy at ${width}px`, async ({ page, context, baseURL }, testInfo) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installWorkspaceFixture(context, baseURL!);
    await context.route('**/api/workspaces/organizations/org-a/capabilities?*', route =>
      route.fulfill({ json: { data: { items: [capability], page: 1, pageSize: 25, total: 1, aggregateState: 'Available' } } }));
    await context.route('**/api/workspaces/organizations/org-a/capabilities/provider/capability-a?*', route =>
      route.fulfill({ json: { data: detail } }));
    await context.route('**/api/workspaces/organizations/org-a/catalog-access', route =>
      route.fulfill({ json: { data: { canManageCatalog: true } } }));
    const systemRequests: string[] = [];
    page.on('request', request => {
      if (/\/api\/.*(?:systems|capability-setups)/.test(request.url())) systemRequests.push(request.url());
    });
    let completionCount = 0;
    await context.route('**/api/workspaces/organizations/org-a/catalog-additions', async route => {
      completionCount++;
      expect(route.request().postDataJSON()).toMatchObject({
        source: 'provider', recordId: 'capability-a', recordType: 'capability',
        components: [], newComponents: [], organizationContribution: 'Enterprise incident triage and procedures',
        owner: 'Organization SOC',
      });
      expect(route.request().postDataJSON()).not.toHaveProperty('systemId');
      return route.fulfill({ status: 201, json: { data: {
        source: 'provider', recordId: 'capability-a', recordType: 'capability', name: capability.name, existing: false,
      } } });
    });

    // Act
    const root = '/workspaces/organizations/org-a';
    await page.goto(`${root}/security-capabilities`);
    await expect(page.getByRole('button', { name: 'By capability', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByRole('columnheader', { name: 'Capability / supporting components' })).toBeVisible();
    await expect(page.getByRole('link', { name: capability.name, exact: true })).toBeVisible();
    await capture(page, testInfo, 'capability-library');
    await page.getByRole('link', { name: capability.name, exact: true }).click();
    await expect(page.getByRole('heading', { name: 'What delivers this capability' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Responsibility', exact: true })).toBeVisible();
    await capture(page, testInfo, 'capability-detail');
    const detailUrl = page.url();
    const add = page.getByRole('button', { name: 'Add capability', exact: true });
    await add.click();
    const dialog = page.getByRole('dialog', { name: 'Add a security capability' });
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(add).toBeFocused();
    await expect(page).toHaveURL(detailUrl);
    await add.click();
    await page.keyboard.press('Shift+Tab');
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await expect(dialog.getByRole('combobox', { name: 'Apply to', exact: true })).toHaveCount(0);
    await expect(dialog.getByRole('radio', { name: 'Inherit from CSP' })).toBeChecked();
    await expect(dialog.getByText('Provider-managed · Read-only')).toBeVisible();
    await expect(dialog.getByText('Organization: Organization A', { exact: true })).toBeVisible();
    await expect(page.getByRole('radio', { name: /Security Monitoring/ })).toBeChecked();
    await capture(page, testInfo, 'setup-capability');
    await page.getByRole('button', { name: 'Continue' }).click();
    await expect(page.getByRole('heading', { name: 'Define the organization contribution' })).toBeVisible();
    await expect(dialog.getByRole('checkbox', { name: /Subscribe/ })).toHaveCount(0);
    await dialog.getByRole('textbox', { name: 'Organization contribution', exact: true }).fill('Enterprise incident triage and procedures');
    await dialog.getByRole('textbox', { name: 'Organization owner', exact: true }).fill('Organization SOC');
    await capture(page, testInfo, 'setup-components');
    await page.getByRole('button', { name: 'Review changes' }).click();
    await expect(page.getByRole('heading', { name: 'Review organization changes' })).toBeVisible();

    // Assert
    expect(completionCount).toBe(0);
    await expect(page.locator('pre')).toHaveCount(0);
    await capture(page, testInfo, 'setup-review');
    await page.getByRole('button', { name: 'Save to organization' }).click();
    await expect(page.getByRole('heading', { name: 'Added to organization' })).toBeVisible();
    expect(completionCount).toBe(1);
    expect(systemRequests).toEqual([]);
    await expect(page.getByRole('link', { name: 'View capability' })).toHaveAttribute('href',
      `${root}/security-capabilities/provider/capability-a?recordType=capability`);
    await capture(page, testInfo, 'setup-complete');
    await page.getByRole('button', { name: 'Done', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page).toHaveURL(detailUrl);
  });
}

for (const width of [1440, 390]) {
  for (const createNew of [false, true]) {
    test(`${createNew ? 'new' : 'existing'} local capability completes in a dialog at ${width}px`, async ({ page, context, baseURL }, testInfo) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installWorkspaceFixture(context, baseURL!);
      const local = { ...capability, source: 'local', recordId: 'local-a', name: 'Local monitoring',
        sourceName: 'Organization catalog', mutationAuthority: 'organization', supportingComponents: [], isSubscribed: false };
      await context.route('**/api/workspaces/organizations/org-a/capabilities?*', route =>
        route.fulfill({ json: { data: { items: [local], page: 1, pageSize: 25, total: 1, aggregateState: 'Available' } } }));
      await context.route('**/api/workspaces/organizations/org-a/capabilities/local/*', route =>
        route.fulfill({ json: { data: { ...detail, capability: local, supportingComponents: [] } } }));
      await context.route('**/api/dashboard/systems/system-a/workspace-access', route => route.fulfill({ json: {
        status: 'success', data: { systemId: 'system-a', roles: ['SystemOwner'],
          permissions: { canRead: true, canManageSystem: true, canReviewNarratives: false } },
      } }));
      await context.route('**/api/dashboard/systems/system-a/components?*', route => route.fulfill({ json: {
        items: [{ id: 'local-component', name: 'SOC analysts', componentType: 'Person', description: 'Review incidents' }],
        nextCursor: null, totalCount: 1,
      } }));
      let intent: Record<string, unknown> = {};
      let completions = 0;
      await context.route('**/api/workspaces/organizations/org-a/capability-setups/prepare', async route => {
        const request = route.request().postDataJSON();
        expect(request).toMatchObject({ source: 'local', systemId: 'system-a', subscribe: false,
          componentIds: createNew ? [] : ['local-component'] });
        intent = { ...request, operationId: 'setup-local', recordId: createNew ? 'created-local' : 'local-a',
          subscribeRequested: false, recordState: 'Pending', componentLinksState: 'Pending',
          subscriptionState: 'NotRequested', outcomes: [], lastError: null };
        await route.fulfill({ json: { data: intent } });
      });
      await context.route('**/api/workspaces/organizations/org-a/capability-setups/setup-local', route =>
        route.fulfill({ json: { data: intent } }));
      await context.route('**/api/workspaces/organizations/org-a/capability-setups', async route => {
        completions++;
        expect(route.request().postDataJSON()).toMatchObject({ source: 'local', systemId: 'system-a',
          recordId: createNew ? 'created-local' : 'local-a', preparedOperationId: 'setup-local', subscribe: false });
        intent = { ...intent, recordState: 'Completed', componentLinksState: 'Completed',
          outcomes: [{ writeKind: 'system-link', writeId: 'system-a', state: 'Completed', error: null }] };
        await route.fulfill({ json: { data: intent } });
      });

      // Act
      const library = '/workspaces/organizations/org-a/systems/system-a/security-capabilities';
      await page.goto(library);
      await page.getByRole('button', { name: 'Add capability', exact: true }).click();
      const dialog = page.getByRole('dialog');
      if (createNew) {
        await dialog.getByRole('checkbox', { name: 'Create a new local capability' }).check();
        for (const [label, value] of [['Capability name', 'Mission monitoring'], ['Provider', 'Organization'],
          ['Category', 'AU'], ['Description', 'Monitor mission events'], ['Owner', 'SOC team']]) {
          await dialog.getByLabel(label!, { exact: true }).fill(value!);
        }
        await expect(dialog.getByRole('combobox', { name: 'Implementation status', exact: true })).toHaveValue('Planned');
      } else {
        await dialog.getByRole('radio', { name: /Local monitoring/ }).check();
      }
      await capture(page, testInfo, 'local-setup-capability');
      await dialog.getByRole('button', { name: 'Continue →' }).click();
      if (!createNew) await dialog.getByRole('checkbox', { name: /SOC analysts/ }).check();
      await dialog.getByRole('button', { name: 'Continue →' }).click();
      await dialog.getByRole('checkbox', { name: /I reviewed the source/ }).check();
      await dialog.getByRole('button', { name: 'Apply setup' }).click();

      // Assert
      await expect(dialog.getByRole('heading', { name: 'Capability added' })).toBeVisible();
      expect(completions).toBe(1);
      await expect(dialog.getByRole('link', { name: 'View capability' })).toHaveAttribute('href',
        `${library}/local/${createNew ? 'created-local' : 'local-a'}?recordType=capability`);
      await capture(page, testInfo, 'local-setup-complete');
      await dialog.getByRole('button', { name: 'Done' }).click();
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page).toHaveURL(library);
    });
  }
}
