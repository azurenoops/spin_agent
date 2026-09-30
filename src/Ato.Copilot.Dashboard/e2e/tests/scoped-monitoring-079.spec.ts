import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';

const root = '/workspaces/organizations/org-a/systems/system-a/conmon';

for (const width of [1440, 390]) {
  test(`scoped collection health and explicit rule creation at ${width}px`, async ({ context, page, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    const saved: unknown[] = [];
    const rules: { id: string; name: string; boundaryDefinitionId: string; baselineReference: string; ownerId: string;
      signal: string; triggerCondition: string; cadenceMinutes: number; severityOverride: string; isEnabled: boolean; version: number; lastEvaluatedAt: null }[] = [];
    await context.route('**/api/dashboard/systems/system-a/conmon/workspace', route => route.fulfill({ json: {
      canManageRules: true, canReviewImpacts: true, boundaries: [{ id: 'boundary-a', name: 'Reviewed workload boundary' }],
      rules, coverage: [{ assignmentId: 'assignment-a', boundaryId: 'boundary-a', resourceId: '/subscriptions/demo/resourceGroups/mission/providers/demo/resources/one',
        providerComponentId: null, health: 'Missing', lastSuccessAt: null, error: 'No successful collection recorded' }],
      changes: [], evaluations: [], impacts: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/conmon/rules', async route => {
      const body = route.request().postDataJSON();
      saved.push(body);
      rules.push({ id: 'rule-a', name: body.name, boundaryDefinitionId: body.boundaryDefinitionId,
        baselineReference: body.baselineReference, ownerId: body.ownerId, signal: body.signal,
        triggerCondition: JSON.stringify(body.condition), cadenceMinutes: body.cadenceMinutes,
        severityOverride: body.severity, isEnabled: body.isEnabled, version: 1, lastEvaluatedAt: null });
      await route.fulfill({ json: rules[0] });
    });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    // Act
    await page.goto(root);
    // Assert
    await expect(page.getByRole('heading', { name: 'Monitoring scope & health' })).toBeVisible();
    await expect(page.getByText('No successful collection recorded')).toBeVisible();
    await expect(page.getByText('0 / 1')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Review monitored scope →' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/boundaries');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await expect(page.locator('main')).toHaveCount(1);
    // Act
    await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Rules', exact: true }).click();
    await page.getByRole('button', { name: 'Create rule →' }).click();
    await expect(page.getByRole('dialog', { name: 'Create monitoring rule' })).toBeVisible();
    await page.keyboard.press('Shift+Tab');
    expect(await page.evaluate(() => !!document.activeElement?.closest('dialog'))).toBe(true);
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Review drift');
    await page.getByRole('textbox', { name: 'Reviewed baseline reference' }).fill('DEMO baseline v1');
    await page.getByRole('textbox', { name: 'Owner', exact: true }).fill('DEMO ISSM');
    await page.getByRole('button', { name: 'Save reviewed rule' }).click();
    // Assert
    await expect(page.getByRole('dialog', { name: 'Monitoring rule' })).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(page.getByRole('cell', { name: 'Review drift Version 1 · DEMO ISSM', exact: true })).toBeVisible();
    expect(saved).toEqual([expect.objectContaining({ name: 'Review drift', boundaryDefinitionId: 'boundary-a',
      baselineReference: 'DEMO baseline v1', ownerId: 'DEMO ISSM', condition: { field: 'Type', operator: 'Equals', value: 'Drift' } })]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`monitoring-rules-${width}.png`), fullPage: true });
    expect(errors).toEqual([]);
  });
}
