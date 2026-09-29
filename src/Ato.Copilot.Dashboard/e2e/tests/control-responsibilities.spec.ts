import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const systemRoot = '/workspaces/organizations/org-a/systems/system-a';
const snapshot = {
  ReleaseRevision: 3,
  Capability: {
    Id: 'capability-a',
    Name: 'Audit collection',
    Description: 'Collects audit records for the mission system.',
    Status: 1,
    Controls: ['AU-2'],
    Component: {
      CspInheritedComponentId: 'component-a',
      CspProfileId: 'provider-a',
      Name: 'Provider audit service',
      Description: 'Operates the shared audit platform.',
      Status: 1,
      SourceArtifactReference: '[redacted]',
    },
  },
};
const responsibility = {
  subscriptionId: 'subscription-a',
  capabilityId: 'capability-a',
  componentId: 'component-a',
  cspProfileId: 'provider-a',
  providerName: 'Flankspeed',
  controlId: 'AU-2',
  sourceRevision: 'source-1',
  reviewRevision: 'review-1',
  state: 'PendingReview',
  reviewedSourceRevision: null,
  confirmedBy: null,
  confirmedAt: null,
  allocation: null,
  effectiveInheritanceType: null,
  designationSource: null,
  sourceAvailable: true,
  sourceSnapshotJson: JSON.stringify(snapshot),
  reviewedSourceSnapshotJson: null,
};

for (const width of [1440, 390]) {
  test(`control responsibilities use a review drawer at ${width}px`, async ({ context, page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { canManageSystem: true });
    let current: Record<string, unknown> = responsibility;
    let reconciliationRequested = false;
    await page.route('**/api/dashboard/systems/system-a/capability-subscriptions/responsibilities', route =>
      route.fulfill({
        json: {
          systemId: 'system-a',
          baselineId: 'baseline-a',
          baselineName: 'Moderate baseline · CNSSI 1253 IL4',
          canConfirm: true,
          items: [current],
          pendingImpacts: [],
        },
      }));
    await page.route('**/api/dashboard/systems/system-a/capability-subscriptions/capability-a/responsibilities', async route => {
      const body = route.request().postDataJSON();
      expect(body).toEqual({
        baselineId: 'baseline-a',
        sourceRevision: 'source-1',
        reviewRevision: 'review-1',
        allocations: [{
          controlId: 'AU-2',
          inheritanceType: 'Shared',
          provider: 'Flankspeed',
          customerResponsibility: 'Review alerts and retain evidence.',
        }],
      });
      current = {
        ...responsibility,
        state: 'Applied',
        reviewedSourceRevision: 'source-1',
        confirmedBy: 'Synthetic Owner',
        confirmedAt: '2026-09-28T16:00:00Z',
        allocation: {
          controlId: 'AU-2',
          inheritanceType: 'Shared',
          provider: 'Flankspeed',
          customerResponsibility: 'Review alerts and retain evidence.',
        },
        effectiveInheritanceType: 'Shared',
        designationSource: 'CapabilitySubscription',
      };
      await route.fulfill({
        json: {
          systemId: 'system-a',
          baselineId: 'baseline-a',
          baselineName: 'Moderate baseline · CNSSI 1253 IL4',
          canConfirm: true,
          items: [current],
          pendingImpacts: [],
        },
      });
    });
    await page.route('**/api/dashboard/systems/system-a/capability-subscriptions/reconcile', async route => {
      reconciliationRequested = true;
      await route.fulfill({
        json: {
          systemId: 'system-a',
          baselineId: 'baseline-a',
          baselineName: 'Moderate baseline · CNSSI 1253 IL4',
          canConfirm: true,
          items: [current],
          pendingImpacts: [],
        },
      });
    });

    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await expect(page.getByText('Baseline: Moderate baseline · CNSSI 1253 IL4')).toBeVisible();
    await expect(page.getByText('Baseline: baseline-a')).toHaveCount(0);

    // Assert
    await expect(page.getByRole('heading', { name: 'Control responsibilities' })).toBeVisible();
    await expect(page.getByText(/snapshot is missing or malformed/i)).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Preview contribution →' })).toHaveAttribute(
      'href',
      `${systemRoot}/documents#ssp-sections`,
    );
    await expect(page.getByRole('link', { name: 'View package readiness →' })).toHaveAttribute('href', systemRoot);

    await page.getByRole('button', { name: 'Open AU-2 responsibility' }).click();
    const drawer = page.getByRole('dialog', { name: 'Review AU-2 responsibility' });
    await expect(drawer).toBeVisible();
    const box = await drawer.boundingBox();
    expect(box?.width).toBeGreaterThanOrEqual(width >= 768 ? 700 : 380);
    await expect(drawer.getByRole('definition').filter({ hasText: /^Audit collection$/ })).toBeVisible();
    await drawer.getByRole('combobox', { name: 'Allocation for AU-2' }).selectOption('Shared');
    await expect(drawer.getByRole('textbox', { name: 'Provider for AU-2' })).toHaveValue('Flankspeed');
    await drawer.getByRole('textbox', { name: 'Customer responsibility for AU-2' }).fill('Review alerts and retain evidence.');
    await drawer.getByRole('checkbox', { name: 'I reviewed the provider revision and the selected allocations.' }).check();
    await drawer.getByRole('button', { name: 'Confirm selected allocations' }).click();

    await expect(drawer).not.toBeVisible();
    await expect(page.getByRole('table').getByText('Shared')).toBeVisible();
    await expect(page.getByRole('table').getByText('Source reviewed')).toBeVisible();
    await page.getByRole('button', { name: 'Reconcile current baseline' }).click();
    await expect.poll(() => reconciliationRequested).toBe(true);
    await expect(page.getByText(/Current baseline reconciled/)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  });
}
