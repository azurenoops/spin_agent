import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { offering } from '../../src/__tests__/provider-authorizations/testData';

const root = `/workspaces/csp/authorizations/offerings/${offering.offeringId}`;
for (const width of [1440, 390]) {
  for (const state of ['associated', 'adopted', 'unknown', 'empty', 'partial', 'failed', 'stale'] as const) {
    test(`mission handoff mock parity preserves ${state} records at ${width}`, async ({ page, context, baseURL }, info) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
      let failed = state === 'failed';
      const mission = {
        assignmentId: 'exact-assignment', systemId: 'exact-system',
        systemName: 'A readable mission system name with long words to verify responsive wrapping'.repeat(2),
        targetTenantName: 'Recorded customer organization', associated: state !== 'unknown',
        relationshipState: state === 'unknown' ? 'LegacyUnrecognized' : 'SeparateBoundaryConsumer',
        adoptedCapabilityCount: state === 'adopted' ? 2 : 0,
        assignedScopes: [{ kind: 'Service', serviceId: 'exact-service', serviceName: 'Recorded shared service',
          environment: 'ManualService', tenantReference: null }],
        adoptedReleases: state === 'adopted' ? [
          { capabilityId: 'cap-1', capabilityName: 'Recorded protection', releaseId: 'selected-release', revision: 2,
            currentReleaseRevision: 5, updateAvailable: true },
        ] : [],
      };
      const writes: string[] = [];
      const errors: string[] = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('request', request => {
        if (request.url().includes('/api/') && request.method() !== 'GET') writes.push(request.method());
      });
      await context.route('**/api/csp/offerings/**', route => {
        const url = new URL(route.request().url());
        if (!url.pathname.endsWith('/boundary-overview')) return route.fulfill({ json: { status: 'success', data: offering } });
        if (failed) return route.fulfill({ status: 403, json: { status: 'error', error: { message: 'Mission records are restricted', errorCode: 'FORBIDDEN' } } });
        const next = Number(url.searchParams.get('missionPage') || 1);
        return route.fulfill({ json: { status: 'success', data: {
          offeringId: offering.offeringId, offeringRevision: offering.revision + (state === 'stale' ? 1 : 0),
          capabilities: { items: [], total: 0, page: 1, pageSize: 10, published: 0, awaitingReview: 0 },
          missionSystems: { total: state === 'empty' ? 0 : state === 'adopted' ? 2 : 1, page: next, pageSize: 1,
            items: state === 'empty' || state === 'partial' ? [] : [{ ...mission, assignmentId: next === 1 ? 'exact-assignment' : 'second-assignment' }] },
        } } });
      });
      // Act
      await page.goto(`${root}/mission-use`);
      const panel = page.getByRole('region', { name: 'Mission use', exact: true });
      await expect(panel).toBeVisible();
      if (failed) {
        await expect(page.getByRole('alert')).toContainText('Mission records are restricted');
        await expect(page.getByText(/No mission hosting allocations recorded/)).toHaveCount(0);
        failed = false;
        await page.getByRole('button', { name: 'Retry', exact: true }).click();
      }
      // Assert
      await expect(page.getByRole('region', { name: 'What the Mission Owner still needs to review', exact: true })).toBeVisible();
      if (state === 'empty' || state === 'partial') {
        if (state === 'empty') await expect(page.getByText(/No mission hosting allocations recorded/)).toBeVisible();
        else await expect(page.getByRole('alert')).toContainText('Mission allocation records are missing');
        await expect(page.getByRole('button', { name: /^View handoff/ })).toHaveCount(0);
      } else {
        const trigger = page.getByRole('button', { name: `View handoff for ${mission.systemName}`, exact: true });
        await expect(trigger).toBeVisible();
        await expect(panel).toContainText(state === 'unknown' ? 'Unrecognized recorded relationship' : 'Separate mission boundary consuming provider services');
        if (state === 'stale') await expect(page.getByRole('alert')).toContainText('Offering context changed');
        const facts = panel.locator('.offering-mission-facts .provider-fact');
        const first = (await facts.nth(0).boundingBox())!;
        const second = (await facts.nth(1).boundingBox())!;
        expect(width === 1440 ? Math.abs(first.y - second.y) < 2 : second.y > first.y + first.height).toBe(true);
        expect(await facts.first().locator('dd').evaluate(element => getComputedStyle(element).textAlign)).toBe('left');
        await trigger.focus();
        await page.keyboard.press('Enter');
        const dialog = page.getByRole('dialog', { name: `Mission handoff · ${mission.systemName}`, exact: true });
        await expect(dialog).toBeVisible();
        await expect(dialog.getByRole('link', { name: 'Inspect service relationship' })).toHaveAttribute('href', `${root}/missions/exact-assignment`);
        await dialog.getByText('Allocated scope & provenance', { exact: true }).click();
        await expect(dialog).toContainText('exact-system');
        await expect(dialog).toContainText('exact-assignment');
        await expect(dialog).toContainText('Recorded shared service');
        await expect(dialog).toContainText(mission.relationshipState);
        if (state === 'adopted') {
          await expect(dialog).toContainText('Selected revision 2 · Provider revision 5 available for explicit review');
          await expect(dialog).toContainText('selected-release');
        } else await expect(dialog).toContainText('No provider capabilities adopted');
        expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
        await dialog.getByRole('button', { name: 'Close handoff', exact: true }).focus();
        await page.keyboard.press('Tab');
        await expect(dialog.getByRole('button', { name: 'Close dialog', exact: true })).toBeFocused();
        await page.keyboard.press('Shift+Tab');
        await expect(dialog.getByRole('button', { name: 'Close handoff', exact: true })).toBeFocused();
        await page.keyboard.press('Escape');
        await expect(dialog).toHaveCount(0);
        await expect(trigger).toBeFocused();
        if (state === 'adopted') {
          await page.getByRole('button', { name: 'Next', exact: true }).click();
          await trigger.click();
          await expect(dialog.getByRole('link', { name: 'Inspect service relationship' })).toHaveAttribute('href', `${root}/missions/second-assignment`);
          await page.keyboard.press('Escape');
        }
        if (state === 'associated') {
          const mock = await context.newPage();
          await mock.setViewportSize({ width, height: 1000 });
          await mock.goto(pathToFileURL(resolve('../../docs/design/workspace-ui-mocks/provider-offering-workspace-focused.html')).href);
          await mock.getByRole('tab', { name: 'Mission use', exact: true }).click();
          const style = (element: Element) => {
            const value = getComputedStyle(element);
            return { padding: value.paddingTop, radius: value.borderRadius };
          };
          expect(await panel.evaluate(style)).toEqual(await mock.locator('#missions>.panel').first().evaluate(style));
          await mock.locator('#missions').screenshot({ path: info.outputPath(`mission-mock-${width}.png`) });
          await mock.close();
          await page.addScriptTag({ content: axe.source });
          const accessibility = await page.evaluate(async () => (window as Window & { axe: typeof axe }).axe.run('.offering-mission-workspace',
            { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } }));
          expect(accessibility.violations).toEqual([]);
          await page.locator('.offering-mission-workspace').screenshot({ path: info.outputPath(`mission-production-${width}.png`) });
        }
      }
      await page.getByText('Allocation management', { exact: true }).click();
      await expect(page.getByRole('link', { name: 'Manage existing allocation workflow' })).toHaveAttribute('href', `${root}/inherited-coverage?task=missions`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect(writes).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
}
