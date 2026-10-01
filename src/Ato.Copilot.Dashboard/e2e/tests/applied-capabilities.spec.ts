import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const systemRoot = '/workspaces/organizations/org-a/systems/system-a';
const access = {
  canRead: true,
  canManage: true,
  canReviewResponsibilities: true,
  canManageEvidence: true,
  canAuthorNarratives: true,
  canReviewNarratives: true,
};
const placement = {
  id: 'placement-a',
  boundaryId: 'boundary-a',
  boundaryName: 'Operations boundary',
  state: 'InScope',
  revision: 'placement-1',
};
const component = {
  source: 'provider',
  recordType: 'component',
  recordId: 'component-a',
  name: 'Provider collector',
  description: 'Collects and forwards audit records.',
  componentType: 'Thing',
  subType: 'Service',
  sourceName: 'Example provider',
  mutationAuthority: 'provider',
  sourceRevision: 'component-1',
  placements: [placement],
  capabilities: [{ source: 'provider', recordType: 'capability', recordId: 'capability-a', name: 'Audit logging' }],
};
const capability = {
  source: 'provider',
  recordType: 'capability',
  recordId: 'capability-a',
  name: 'Audit logging',
  description: 'Collects and reviews system audit records.',
  sourceName: 'Example provider',
  mutationAuthority: 'provider',
  sourceRevision: 'source-1',
  isApplied: true,
  isAvailable: true,
  status: 'Applied',
  componentType: null,
  subType: null,
  components: [component],
  capabilities: [],
  placements: [],
  controlIds: ['AU-2'],
  reviewRequiredCount: 1,
};
const availableComponent = {
  ...component,
  source: 'local',
  recordId: 'local-component-b',
  name: 'Mission identity service',
  sourceName: 'SPIN Demo Organization',
  mutationAuthority: 'organization',
  sourceRevision: 'local-component-1',
  placements: [],
  capabilities: [{ source: 'local', recordType: 'capability', recordId: 'local-capability-b', name: 'Identity management' }],
};
const availableCapability = {
  ...capability,
  source: 'local',
  recordId: 'local-capability-b',
  name: 'Identity management',
  sourceName: 'SPIN Demo Organization',
  mutationAuthority: 'organization',
  sourceRevision: 'local-source-1',
  components: [availableComponent],
  isApplied: false,
  reviewRequiredCount: 0,
};

for (const width of [1440, 390, 320]) {
  test(`applied capability review drawer works at ${width}px`, async ({ context, page, baseURL }) => {
    // Arrange
    let setupCompleted = false;
    let preparedOperation: Record<string, unknown> | null = null;
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { canManageSystem: true });
    await page.route(url => url.pathname === '/api/workspaces/organizations/org-a/systems/system-a/security-capabilities', route => {
      const available = new URL(route.request().url()).searchParams.get('scope') === 'available';
      return route.fulfill({ json: {
        status: 'success',
        data: {
          items: available ? [availableCapability]
            : setupCompleted ? [capability, { ...availableCapability, isApplied: true, status: 'Applied' }] : [capability],
          page: 1,
          pageSize: 25,
          total: available || !setupCompleted ? 1 : 2,
          scope: available ? 'available' : 'applied',
          grouping: 'capability',
          permissions: access,
          boundaries: [{ id: 'boundary-a', name: 'Operations boundary' }],
        },
      } });
    });
    await page.route('**/security-capabilities/provider/capability/capability-a', route => route.fulfill({
      json: {
        status: 'success',
        data: {
          item: capability,
          permissions: access,
          baselineId: 'baseline-a',
          controls: [{
            controlId: 'AU-2',
            providerCoverage: 'Collect audit records',
            organizationDuty: 'Review audit records',
            allocation: 'Shared',
            reviewState: 'PendingReview',
            confirmedSourceRevision: null,
            availableSourceRevision: 'source-1',
            reviewRevision: 'review-1',
            sourceSnapshot: null,
            confirmedSourceSnapshot: null,
          }],
          evidence: [],
          narratives: [],
          relationshipRevision: 'relationship-1',
          responsibilityReviewUrl: `${systemRoot}/inheritance/subscriptions`,
        },
      },
    }));
    await page.route('**/security-capabilities/provider/component/component-a', route => route.fulfill({
      json: {
        status: 'success',
        data: {
          item: {
            ...capability,
            ...component,
            components: [],
            controlIds: [],
            reviewRequiredCount: 0,
          },
          permissions: access,
          baselineId: 'baseline-a',
          controls: [],
          evidence: [],
          narratives: [],
          relationshipRevision: 'relationship-1',
          responsibilityReviewUrl: `${systemRoot}/inheritance/subscriptions`,
        },
      },
    }));
    await page.route('**/responsibilities/AU-2/draft', route => route.fulfill({ json: { status: 'success', data: {
      systemId: 'system-a', source: 'provider', capabilityId: 'capability-a', controlId: 'AU-2',
      canSave: true, saved: null, isStale: false,
      prepared: { controlId: 'AU-2', contextRevision: 'd'.repeat(64), allocation: null, allocationOrigin: 'From system records',
        providerResponsibility: '', providerOrigin: 'From system records', customerResponsibility: 'Recorded fixture duty',
        customerOrigin: 'From system records', aiSummary: null, flags: ['Verify applicability'], references: [] },
    } } }));
    await page.route('**/security-capabilities/setups/prepare', async route => {
      const request = route.request();
      const body = request.postDataJSON() as { idempotencyKey: string; selections: unknown[] };
      preparedOperation = {
        operationId: 'operation-a',
        idempotencyKey: body.idempotencyKey,
        tenantId: 'org-a',
        systemId: 'system-a',
        kind: 'Setup',
        state: 'Prepared',
        revision: 0,
        selections: body.selections,
        plannedWrites: [{
          writeKind: 'system-link',
          writeId: 'system-link-a',
          source: 'local',
          recordId: 'local-capability-b',
          componentId: null,
          boundaryId: null,
          alreadyExists: false,
        }],
        outcomes: [],
        lastError: null,
        createdAt: '2026-09-28T00:00:00Z',
        updatedAt: '2026-09-28T00:00:00Z',
      };
      await route.fulfill({
        json: {
          status: 'success',
          data: {
            existing: false,
            operation: preparedOperation,
          },
        },
      });
    });
    await page.route('**/security-capabilities/setups/operation-a', route => route.fulfill({
      json: { status: 'success', data: preparedOperation },
    }));
    await page.route('**/security-capabilities/setups/operation-a/complete', async route => {
      expect(route.request().postDataJSON()).toEqual({ expectedRevision: 0 });
      setupCompleted = true;
      await route.fulfill({
        json: {
          status: 'success',
          data: {
            ...preparedOperation,
            state: 'Completed',
            revision: 2,
            outcomes: [{
              writeKind: 'system-link',
              writeId: 'system-link-a',
              state: 'Completed',
              error: null,
              updatedAt: '2026-09-28T00:01:00Z',
            }],
            updatedAt: '2026-09-28T00:01:00Z',
          },
        },
      });
    });

    // Act
    await page.goto(`${systemRoot}/security-capabilities`);

    // Assert
    await expect(page.getByRole('heading', { name: 'Applied security capabilities' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Add CSP hosting & capabilities' })).toHaveAttribute(
      'href',
      `${systemRoot}/provider-relationships/setup`,
    );
    await expect(page.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute(
      'href',
      `${systemRoot}/documents#ssp-sections`,
    );
    await page.getByRole('button', { name: 'Open Audit logging' }).click();
    const reviewDrawer = page.getByRole('dialog', { name: 'Review applied capability' });
    await expect(reviewDrawer).toBeVisible();
    const overview = reviewDrawer.getByRole('tab', { name: 'Overview' });
    await overview.focus();
    await page.keyboard.press('End');
    await expect(reviewDrawer.getByRole('tab', { name: 'Responsibilities' })).toBeFocused();
    await expect(reviewDrawer.getByRole('heading', { name: 'Prepared responsibility · AU-2' })).toBeVisible();
    await reviewDrawer.getByRole('textbox', { name: 'Customer responsibility draft' }).fill('Reviewer correction');
    await expect(reviewDrawer.getByText('Collect audit records')).toBeVisible();
    expect(await reviewDrawer.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const dialog = document.querySelector('dialog[open]');
      if (!dialog) throw new Error('The capability review dialog is unavailable.');
      const browserAxe = (window as Window & { axe: typeof import('axe-core') }).axe;
      return (await browserAxe.run(dialog, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } })).violations;
    });
    expect(violations).toEqual([]);
    await reviewDrawer.getByRole('tab', { name: 'Overview' }).click();
    await reviewDrawer.getByRole('tab', { name: 'Responsibilities' }).click();
    await expect(reviewDrawer.getByRole('textbox', { name: 'Customer responsibility draft' })).toHaveValue('Reviewer correction');
    await reviewDrawer.getByRole('tab', { name: 'Where it applies' }).click();
    await reviewDrawer.getByRole('button', { name: 'Change location for Provider collector' }).click();
    const componentDrawer = page.getByRole('dialog', { name: 'Component details' });
    await expect(componentDrawer).toBeVisible();
    await expect(componentDrawer.getByRole('button', { name: 'Manage system placement' })).toBeEnabled();
    await componentDrawer.getByRole('button', { name: 'Close dialog' }).click();

    await page.getByRole('button', { name: 'Add organization capability' }).click();
    const setupDrawer = page.getByRole('dialog', { name: 'Add organization capability' });
    await expect(setupDrawer).toBeVisible();
    const drawerBox = await setupDrawer.boundingBox();
    expect(drawerBox?.width).toBeGreaterThanOrEqual(width >= 768 ? 700 : width - 10);
    const optionBox = await setupDrawer.getByTestId('capability-option').first().boundingBox();
    expect(optionBox?.width).toBeGreaterThanOrEqual(width >= 768 ? 600 : width - 100);
    await setupDrawer.getByRole('checkbox', { name: 'Select Identity management' }).check();
    await setupDrawer.getByRole('button', { name: 'Continue to applicability' }).click();
    await setupDrawer.getByRole('button', { name: 'Continue to review' }).click();
    await expect(setupDrawer.getByRole('heading', { name: 'Review changes before adding' })).toBeVisible();
    await expect(page).toHaveURL(`${systemRoot}/security-capabilities?step=3&operationId=operation-a`);
    await setupDrawer.getByRole('checkbox', { name: /reviewed.*exact.*plan/i }).check();
    await setupDrawer.getByRole('button', { name: 'Add to system' }).click();
    await expect(setupDrawer.getByRole('heading', { name: 'Capabilities added to Synthetic Mission System' })).toBeVisible();
    await expect(page.getByRole('table').getByRole('link', { name: 'Identity management' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  });
}
