import { expect, test, type Page, type TestInfo } from '@playwright/test';
import axe from 'axe-core';
import {
  installSystemCapabilityFixture, providerCapabilityId, providerComponentId, systemCapabilityRoot,
} from '../fixtures/system-capabilities';

async function checkLayout(page: Page, testInfo: TestInfo, name: string) {
  await expect(page.locator('main')).toHaveCount(1);
  const dimensions = await page.evaluate(() => ({
    viewport: innerWidth, document: document.documentElement.scrollWidth,
    overflowing: Array.from(document.querySelectorAll('body *')).filter(element => element.getBoundingClientRect().right > innerWidth)
      .slice(0, 16).map(element => ({ tag: element.tagName, classes: element.className,
        right: element.getBoundingClientRect().right, width: element.getBoundingClientRect().width,
        overflow: getComputedStyle(element).overflowX, minWidth: getComputedStyle(element).minWidth })),
  }));
  expect(dimensions.document, JSON.stringify(dimensions)).toBeLessThanOrEqual(dimensions.viewport);
  const dialog = page.getByRole('dialog');
  if (await dialog.count()) {
    const bounds = await dialog.evaluate(element => ({
      modal: element.matches(':modal'), left: element.getBoundingClientRect().left,
      right: element.getBoundingClientRect().right, top: element.getBoundingClientRect().top,
      bottom: element.getBoundingClientRect().bottom, height: innerHeight, width: innerWidth,
    }));
    expect(bounds.modal).toBe(true);
    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.top).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(bounds.width);
    expect(bounds.bottom).toBeLessThanOrEqual(bounds.height);
  }
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const root = document.querySelector('dialog[open]') ?? document.querySelector('main');
    if (!root) throw new Error('No system content available for accessibility validation.');
    const browserAxe = (window as Window & { axe: typeof import('axe-core') }).axe;
    const result = await browserAxe.run(root, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } });
    return result.violations.map(violation => ({
      id: violation.id, description: violation.description, nodes: violation.nodes.map(node => ({ target: node.target, summary: node.failureSummary })),
    }));
  });
  expect(violations).toEqual([]);
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true });
}

for (const width of [1440, 390]) {
  for (const theme of ['light', 'dark']) {
    test(`applied views and provider component drawer: ${width}px ${theme}`, async ({ page, context, baseURL }, testInfo) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installSystemCapabilityFixture(context, baseURL!);
      await context.addInitScript(value => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ theme: value, role: 'ISSM' })), theme);
      const errors: string[] = [];
      page.on('pageerror', error => errors.push(error.message));
      // Act
      await page.goto(systemCapabilityRoot);
      // Assert
      await expect(page.getByRole('heading', { name: 'Security Capabilities', exact: true })).toBeVisible();
      await expect(page.getByRole('region', { name: 'Active workspace' })).toContainText('Selected system: Synthetic Mission System');
      await expect(page.getByRole('link', { name: 'Security monitoring', exact: true })).toBeVisible();
      await expect(page.locator('html')).toHaveClass(theme === 'dark' ? /dark/ : /^(?!.*dark)/);
      await checkLayout(page, testInfo, '01-by-capability');
      // Act
      await page.getByRole('tab', { name: 'By component' }).click();
      // Assert
      await expect(page.getByRole('button', { name: 'Incident response policy', exact: true })).toBeVisible();
      await expect(page.getByText('Direct system assignment', { exact: true })).toBeVisible();
      await checkLayout(page, testInfo, '03-by-component');
      // Act
      await page.getByRole('button', { name: 'Microsoft Sentinel', exact: true }).click();
      const drawer = page.getByRole('dialog', { name: 'Component details' });
      // Assert
      await expect(drawer.getByRole('heading', { name: 'Microsoft Sentinel' })).toBeVisible();
      await expect(drawer).toContainText('source is managed by the provider');
      await expect(drawer).toContainText('Azure workload');
      await expect(drawer).toContainText('Organization operations (Excluded)');
      await checkLayout(page, testInfo, '03-component-drawer');
      // Act
      await page.keyboard.press('Escape');
      // Assert
      await expect(drawer).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Microsoft Sentinel', exact: true })).toBeFocused();
      expect(errors).toEqual([]);
    });

    test(`implementation, duties and independent narrative freshness: ${width}px ${theme}`, async ({ page, context, baseURL }, testInfo) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      const state = await installSystemCapabilityFixture(context, baseURL!);
      await context.addInitScript(value => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ theme: value, role: 'ISSM' })), theme);
      // Act
      await page.goto(`${systemCapabilityRoot}/provider/${providerCapabilityId}`);
      // Assert
      await expect(page.getByRole('heading', { name: 'Contributing components' })).toBeVisible();
      await expect(page.getByText('SOC analyst team', { exact: true })).toBeVisible();
      await expect(page.getByText('Microsoft Sentinel', { exact: true })).toBeVisible();
      await checkLayout(page, testInfo, '02-implementation');
      // Act
      await page.getByRole('tab', { name: 'Coverage & duties' }).click();
      // Assert
      await expect(page.getByRole('heading', { name: 'Review AU-2', exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm AU-2 responsibility' })).toBeDisabled();
      await checkLayout(page, testInfo, '05-coverage-duties');
      // Act
      await page.getByRole('combobox', { name: 'Responsibility allocation', exact: true }).selectOption('Shared');
      await page.getByLabel('Provider responsibility', { exact: true }).fill('Provider logging');
      await page.getByLabel('Customer responsibility', { exact: true }).fill('Mission log review');
      await page.getByLabel('Provider coverage verified').check();
      await page.getByLabel('Customer duties reviewed').check();
      // Assert
      await expect(page.getByRole('button', { name: 'Confirm AU-2 responsibility' })).toBeDisabled();
      // Act
      await page.getByLabel('Review notes (required)').fill('Reviewed source coverage and mission duties.');
      await page.getByRole('button', { name: 'Confirm AU-2 responsibility' }).click();
      // Assert
      await expect.poll(() => state.writes.length).toBe(1);
      expect(state.writes[0]?.body).toMatchObject({
        baselineId: 'baseline-a', sourceRevision: 'a'.repeat(64), reviewRevision: 'review-a',
        allocations: [{ controlId: 'AU-2', inheritanceType: 'Shared', provider: 'Provider logging', customerResponsibility: 'Mission log review' }],
        providerCoverageVerified: true, customerDutiesReviewed: true, reviewNotes: 'Reviewed source coverage and mission duties.',
      });
      await expect(page.getByRole('region', { name: 'Persisted responsibility review evidence' })).toContainText('Reviewed source coverage and mission duties.');
      // Act
      await page.getByRole('tab', { name: 'Evidence & narratives' }).click();
      // Assert
      await expect(page.getByText('Approved monitoring procedure.pdf')).toBeVisible();
      await expect(page.getByText('Approved policy content retained.', { exact: true })).toBeVisible();
      await expect(page.getByText('Approved technical content retained.', { exact: true })).toBeVisible();
      await expect(page.getByText(/Freshness: Current/)).toBeVisible();
      await expect(page.getByText(/Freshness: SourceChanged/)).toBeVisible();
      await expect(page.getByRole('button', { name: 'Generate Technical proposal for AU-2', exact: true })).toBeDisabled();
      await checkLayout(page, testInfo, '06-evidence-narratives');
    });
  }
}

test('organization-only systems, explicit recovery and denied writes remain usable', async ({ page, context, baseURL }) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!, { organizationOnly: true, unavailable: true, denied: true });
  // Act
  await page.goto(systemCapabilityRoot);
  // Assert
  await expect(page.getByRole('alert')).toContainText('System capability service unavailable');
  await expect(page.getByText('No security capabilities applied to this system yet.')).toHaveCount(0);
  // Act
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  // Assert
  await expect(page.getByRole('link', { name: 'Incident response', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Security monitoring', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add from library' })).toBeDisabled();
  expect(state.writes).toEqual([]);
});

test('library selection persists across pages and partial setup recovers after refresh', async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!, { partial: true, paginatedLibrary: true });
  await page.goto(`${systemCapabilityRoot}/add`);
  // Act / Assert
  await expect(page.getByRole('heading', { name: 'Add security capabilities to Synthetic Mission System' })).toBeVisible();
  await expect(page.getByLabel('Select Security monitoring', { exact: true })).toBeDisabled();
  await page.getByLabel('Select Boundary logging', { exact: true }).check();
  await page.getByLabel('Select Business continuity', { exact: true }).check();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await page.getByLabel('Select Recovery planning 26', { exact: true }).check();
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByLabel('Select Boundary logging', { exact: true })).toBeChecked();
  await expect(page.getByLabel('Select Business continuity', { exact: true })).toBeChecked();
  await checkLayout(page, testInfo, '07-library-selection');
  await page.getByRole('button', { name: 'Continue to applicability' }).click();
  await expect(page.getByRole('heading', { name: 'Review system applicability' })).toBeVisible();
  await expect(page.getByText(/The target system is locked to this route/)).toBeVisible();
  await page.getByLabel('Place Microsoft Sentinel in Azure workload', { exact: true }).check();
  await checkLayout(page, testInfo, '08-applicability');
  await page.getByRole('button', { name: 'Continue to review' }).click();
  await expect(page.getByRole('heading', { name: 'Review changes before adding' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add to system', exact: true })).toBeDisabled();
  await page.getByLabel('I reviewed the exact saved plan for this system.').check();
  await page.getByRole('button', { name: 'Add to system', exact: true }).click();
  await expect(page.getByText('One write remains pending.')).toBeVisible();
  await page.reload();
  await expect(page.getByText('One write remains pending.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry unfinished changes' })).toBeDisabled();
  await page.getByLabel('I reviewed the exact saved plan for this system.').check();
  await page.getByRole('button', { name: 'Retry unfinished changes' }).click();
  await expect(page.getByRole('heading', { name: 'Capabilities added to Synthetic Mission System' })).toBeVisible();
  await checkLayout(page, testInfo, 'setup-saved');
  const prepare = state.writes.filter(call => call.path.endsWith('/setups/prepare'));
  expect(prepare).toHaveLength(1);
  expect(prepare[0].body).toMatchObject({ selections: [
    { source: 'provider', recordId: '55555555-5555-5555-5555-555555555555',
      placements: [{ source: 'provider', componentId: providerComponentId, boundaryId: 'boundary-a' }] },
    { source: 'local', recordId: 'local-continuity' },
    { source: 'local', recordId: 'local-extra-25' },
  ] });
  expect(state.writes.filter(call => call.path.endsWith('/complete')).map(call => call.body)).toEqual([
    { expectedRevision: 1 }, { expectedRevision: 2 },
  ]);
});

test('organization-only capabilities apply without selecting a provider', async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!, { organizationOnly: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${systemCapabilityRoot}/add`);
  // Act
  await page.getByLabel('Select Business continuity', { exact: true }).check();
  await page.getByRole('button', { name: 'Continue to applicability' }).click();
  await expect(page.getByRole('button', { name: 'Choose supporting organization capabilities' })).toHaveCount(0);
  await page.getByLabel('Place SOC analyst team in System-wide', { exact: true }).check();
  await checkLayout(page, testInfo, 'organization-applicability');
  await page.getByRole('button', { name: 'Continue to review' }).click();
  await page.getByLabel('I reviewed the exact saved plan for this system.').check();
  await page.getByRole('button', { name: 'Add to system', exact: true }).click();
  // Assert
  await expect(page.getByRole('heading', { name: 'Capabilities added to Synthetic Mission System' })).toBeVisible();
  await checkLayout(page, testInfo, 'organization-saved');
  const prepare = state.writes.find(call => call.path.endsWith('/setups/prepare'));
  expect(prepare?.body).toMatchObject({ selections: [{ source: 'local', recordId: 'local-continuity',
    placements: [{ source: 'local', componentId: 'local-team', boundaryId: null }] }] });
});

test('removal presents retained records and refuses a stale preview before refreshing', async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!, { staleRemoval: true });
  await page.goto(`${systemCapabilityRoot}/provider/${providerCapabilityId}`);
  // Act
  await page.getByRole('button', { name: 'Remove from this system', exact: true }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Remove from this system' });
  await expect(dialog.getByText('Shared library records, unrelated capability links, other systems and approved historical narratives are retained.')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Unsubscribe from this system' })).toBeDisabled();
  await dialog.getByLabel('I reviewed this exact removal and the retained records.').check();
  await dialog.getByRole('button', { name: 'Unsubscribe from this system' }).click();
  // Assert
  await expect(dialog.getByRole('alert')).toContainText('relationships changed');
  await expect(dialog.getByRole('button', { name: 'Unsubscribe from this system' })).toHaveCount(0);
  expect(state.writes.filter(call => call.path.endsWith('/complete'))).toHaveLength(1);
  await dialog.getByRole('button', { name: 'Refresh capability' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Security monitoring', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Remove from this system', exact: true }).first().click();
  await page.getByRole('dialog').getByLabel('I reviewed this exact removal and the retained records.').check();
  await page.getByRole('dialog').getByRole('button', { name: 'Unsubscribe from this system' }).click();
  await expect(page.getByRole('dialog').getByText('Removed Security monitoring from Synthetic Mission System', { exact: true })).toBeVisible();
  await checkLayout(page, testInfo, 'removal-saved');
  expect(state.writes.filter(call => call.path.endsWith('/complete'))).toHaveLength(2);
});

test('protected evidence and proposal review preserve approved content until explicit acceptance', async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!, { proposals: true });
  await page.goto(`${systemCapabilityRoot}/provider/${providerCapabilityId}`);
  await page.getByRole('tab', { name: 'Evidence & narratives' }).click();
  // Act
  const pendingDownload = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Open reference', exact: true }).click();
  const download = await pendingDownload;
  // Assert
  expect(download.suggestedFilename()).toBe('Approved monitoring procedure.pdf');
  await expect(page.getByText('Approved policy content retained.', { exact: true })).toBeVisible();
  // Act
  await page.getByRole('button', { name: 'View Policy proposal for AU-2' }).click();
  const dialog = page.getByRole('dialog', { name: 'Review AU-2 Policy proposal' });
  // Assert
  await expect(dialog.getByText('Reviewed policy proposal.', { exact: true })).toBeVisible();
  await expect(dialog.getByText('Approved policy content retained.', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Accept proposal' })).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Return for revision' })).toBeDisabled();
  await checkLayout(page, testInfo, 'proposal-review');
  // Act
  await dialog.getByLabel('I reviewed the source, proposed content and dependency findings.').check();
  await expect(dialog.getByRole('button', { name: 'Return for revision' })).toBeDisabled();
  await dialog.getByRole('textbox', { name: 'Review note', exact: true }).fill('Reviewed source and retained previous approved version.');
  await dialog.getByRole('button', { name: 'Accept proposal' }).click();
  // Assert
  await expect(dialog).toHaveCount(0);
  await expect(page.getByText('Reviewed policy proposal.', { exact: true })).toBeVisible();
  await expect(page.getByText('Approved technical content retained.', { exact: true })).toBeVisible();
  expect(state.writes).toEqual([{ path: `/api/workspaces/organizations/org-a/systems/system-a/security-capabilities/provider/capability/${providerCapabilityId}/narrative-proposals/proposal-a/review`,
    body: { expectedRevision: 7, decision: 'Approve', note: 'Reviewed source and retained previous approved version.' } }]);
});

test('responsibility authority does not grant narrative acceptance', async ({ page, context, baseURL }) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!, { proposals: true, narrativeReviewDenied: true });
  await page.goto(`${systemCapabilityRoot}/provider/${providerCapabilityId}`);
  // Act
  await page.getByRole('tab', { name: 'Evidence & narratives' }).click();
  await page.getByRole('button', { name: 'View Policy proposal for AU-2' }).click();
  const dialog = page.getByRole('dialog', { name: 'Review AU-2 Policy proposal' });
  // Assert
  await expect(dialog.getByText(/Current reviewer permission/)).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Accept proposal' })).toBeDisabled();
  await expect(dialog.getByRole('checkbox')).toBeDisabled();
  expect(state.writes).toEqual([]);
});

test('provider drawer changes only the reviewed system boundary placement', async ({ page, context, baseURL }, testInfo) => {
  // Arrange
  const state = await installSystemCapabilityFixture(context, baseURL!);
  await page.setViewportSize({ width: 390, height: 1000 });
  await page.goto(`${systemCapabilityRoot}?view=component`);
  // Act
  await page.getByRole('button', { name: 'Microsoft Sentinel', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Component details' });
  await dialog.getByRole('button', { name: 'Manage system placement' }).click();
  await dialog.getByRole('combobox', { name: 'Boundary for this component' }).selectOption('boundary-c');
  await dialog.getByRole('button', { name: 'Assign to boundary', exact: true }).click();
  // Assert
  await expect(dialog.getByRole('status')).toHaveText('Assigned to Mission workload.');
  await expect(dialog.getByRole('button', { name: 'Remove from Mission workload', exact: true })).toBeVisible();
  await expect(dialog.getByText(/source is managed by the provider and is read-only/)).toBeVisible();
  await checkLayout(page, testInfo, 'component-placement-assigned');
  // Act
  await dialog.getByRole('button', { name: 'Remove from Mission workload', exact: true }).click();
  await expect(dialog.getByRole('button', { name: 'Confirm placement removal' })).toBeDisabled();
  await dialog.getByRole('checkbox', { name: /only this boundary placement/ }).check();
  await dialog.getByRole('button', { name: 'Confirm placement removal' }).click();
  // Assert
  await expect(dialog.getByRole('status')).toHaveText('Removed from Mission workload. Other placements are unchanged.');
  await expect(dialog.getByRole('button', { name: 'Remove from Azure workload', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Remove from Organization operations', exact: true })).toBeVisible();
  expect(state.writes.map(write => write.path)).toEqual([
    `/api/workspaces/organizations/org-a/systems/system-a/security-capabilities/provider/component/${providerComponentId}/placements/assign`,
    `/api/workspaces/organizations/org-a/systems/system-a/security-capabilities/provider/component/${providerComponentId}/placements/placement-new/unassign`,
  ]);
});
