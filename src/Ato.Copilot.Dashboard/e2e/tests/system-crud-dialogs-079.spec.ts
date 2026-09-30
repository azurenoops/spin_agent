import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`decision and exchange dialogs preserve page context at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic API contracts, never live authorization or exchange writes.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!);
    await context.addInitScript(() => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'AO' })));
    await context.route('**/api/dashboard/systems/system-a/workspace-access', route => route.fulfill({ json: {
      status: 'success', data: { systemId: 'system-a', roles: ['AuthorizingOfficial'], permissions: {
        canRead: true, canDecideAuthorization: true,
      } },
    } }));
    const writes: string[] = [];
    await context.route('**/api/dashboard/systems/system-a/authorization', route => {
      if (route.request().method() === 'POST') {
        writes.push('decision');
        return route.fulfill({ status: 409, json: { error: 'Decision context changed; review again.' } });
      }
      return route.fulfill({ json: { id: 'decision-a', decisionType: 'ATOwC', expirationDate: '2027-01-01',
        residualRiskLevel: 'Medium', issuedBy: 'synthetic-ao', issuedByName: 'Synthetic AO',
        decisionDate: '2026-09-01', override: null } });
    });
    await context.route('**/api/dashboard/systems/system-a/risk-acceptances', route => route.fulfill({ json: [] }));
    await context.route('**/api/dashboard/systems/system-a/authorization/record-context?*', route => route.fulfill({ json: {
      systemId: 'system-a', canRecord: true, activeDecisionId: 'decision-a', page: 1, pageSize: 50,
      sourceTotal: 0, packageTotal: 0, recordTotal: 0, sourceEvidence: [], completedPackages: [], records: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/emass/exchanges', route => {
      if (route.request().method() === 'POST') writes.push('exchange');
      return route.fulfill({ json: { data: { version: 0, canRecord: true, items: [] } } });
    });
    await context.route('**/api/dashboard/systems/system-a/emass/exchange-exports', route => route.fulfill({ json: { data: [{
      packageId: 'package-a', packageHash: 'a'.repeat(64), exportGeneratedAt: '2026-09-26T12:00:00Z', purpose: 'InitialSubmission',
    }] } }));

    // Act
    await page.goto(`${root}/authorize`);
    await page.getByRole('button', { name: 'Issue Authorization', exact: true }).click();
    const decision = page.getByRole('dialog', { name: 'Issue Authorization Decision' });
    await decision.getByLabel('Terms and Conditions').fill('Retain these exercise conditions.');
    await decision.getByRole('button', { name: 'Confirm authorization decision', exact: true }).click();

    // Assert
    await expect(decision.getByRole('alert')).toHaveText('Decision context changed; review again.');
    await expect(decision.getByLabel('Terms and Conditions')).toHaveValue('Retain these exercise conditions.');
    await page.screenshot({ path: info.outputPath(`decision-dialog-${width}.png`), fullPage: true });
    await decision.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Issue Authorization', exact: true })).toBeFocused();
    await page.getByRole('button', { name: 'Apply Temporary Override', exact: true }).click();
    const override = page.getByRole('dialog', { name: 'Apply Temporary Override' });
    await expect(override.getByLabel('Override Status')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(override).toHaveCount(0);
    expect(writes).toEqual(['decision']);

    // Act
    await page.goto(`${root}/emass/status`);
    const openExchange = page.getByRole('button', { name: 'Record external observation', exact: true });
    await expect(openExchange).toBeVisible();
    await expect(page.getByLabel('Retained export', { exact: true })).toHaveCount(0);
    await openExchange.click();
    const exchange = page.getByRole('dialog', { name: 'Record external observation' });
    await exchange.getByLabel('Retained export', { exact: true }).selectOption('package-a');
    await exchange.getByLabel('External reference').fill('EXERCISE-REFERENCE');
    await page.keyboard.press('Shift+Tab');

    // Assert
    expect(await page.evaluate(() => !!document.activeElement?.closest('dialog'))).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`exchange-dialog-${width}.png`), fullPage: true });
    await page.keyboard.press('Escape');
    await expect(exchange).toHaveCount(0);
    await expect(openExchange).toBeFocused();
    expect(writes).toEqual(['decision']);
  });
}
