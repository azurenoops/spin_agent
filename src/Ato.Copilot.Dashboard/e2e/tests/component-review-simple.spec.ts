import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { installSystemCapabilityFixture, systemCapabilityRoot } from '../fixtures/system-capabilities';

for (const width of [1440, 390]) {
  test(`component service-use draft, keyboard and evidence at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const state = await installSystemCapabilityFixture(context, baseURL!, { component: {
      name: 'Azure Backup', sourceName: 'Flankspeed',
      description: 'Synthetic demonstration source. Recovery Services vault backs up provider-managed workloads using recovery points.',
    } });
    await page.goto(`${systemCapabilityRoot}?view=component`);
    // Act
    await page.getByRole('button', { name: 'Azure Backup', exact: true }).focus();
    await page.keyboard.press('Enter');
    const panel = page.getByRole('dialog', { name: 'Component details' });
    // Assert
    await expect(panel.getByText('System scope not recorded')).toBeVisible();
    await expect(panel.getByText('Recovery Services vault · Service', { exact: true })).toBeVisible();
    await expect(panel.getByText('Managed by Flankspeed')).toBeVisible();
    await expect(panel.getByText('Synthetic demonstration source', { exact: true })).toBeVisible();
    await expect(panel.getByText('24 hours')).toHaveCount(0);
    // Act
    await panel.getByRole('tab', { name: 'Overview', exact: true }).focus();
    await page.keyboard.press('ArrowRight');
    // Assert
    await expect(panel.getByRole('tab', { name: 'System scope', exact: true })).toBeFocused();
    // Act
    await panel.getByLabel('Used by this system', { exact: true }).check();
    await panel.getByLabel('System area supported').selectOption('boundary-c');
    await panel.getByLabel('How it is used', { exact: true }).fill('Proposed use by the mission workload. Protected workloads and restore results need evidence.');
    await panel.getByRole('button', { name: 'Save scope draft', exact: true }).click();
    // Assert
    await expect(panel.getByRole('status')).toContainText('Scope draft saved');
    await expect(panel.getByText('Scope draft ready for review', { exact: true })).toBeVisible();
    expect(state.writes).toHaveLength(1);
    expect(state.writes[0]?.body).toMatchObject({ decision: 'Included', boundaryId: 'boundary-c' });
    await expect(panel.getByRole('link', { name: 'Review scope draft' })).toHaveAttribute('href', /profile\/SystemDesign$/);
    // Act
    await panel.getByRole('tab', { name: 'Evidence', exact: true }).click();
    // Assert
    await expect(panel.getByText('Protected workloads', { exact: true })).toBeVisible();
    await expect(panel.getByText('Configuration and retention records', { exact: true })).toBeVisible();
    await expect(panel.getByText('Restore results', { exact: true })).toBeVisible();
    await expect(panel.getByText('Evidence unavailable for this component.', { exact: true })).toBeVisible();
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const result = await (window as Window & { axe: typeof import('axe-core') }).axe.run(document.querySelector('dialog[open]')!, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
      });
      return result.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) }));
    });
    expect(violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`component-evidence-${width}.png`), fullPage: true });
    // Act
    await page.keyboard.press('Escape');
    // Assert
    await expect(panel).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Azure Backup', exact: true })).toBeFocused();
  });
}

test('component scope rejection retains corrections and never reports success', async ({ page, context, baseURL }) => {
  // Arrange
  await installSystemCapabilityFixture(context, baseURL!, { scopeSaveFailure: true });
  await page.goto(`${systemCapabilityRoot}?view=component`);
  await page.getByRole('button', { name: 'Microsoft Sentinel', exact: true }).click();
  const panel = page.getByRole('dialog', { name: 'Component details' });
  await panel.getByRole('button', { name: 'Review system scope', exact: true }).click();
  // Act
  await panel.getByLabel('Not used by this system', { exact: true }).check();
  await panel.getByLabel('How it is used', { exact: true }).fill('Retain this user correction');
  await panel.getByRole('button', { name: 'Save scope draft', exact: true }).click();
  // Assert
  await expect(panel.getByRole('alert')).toContainText('Scope save rejected');
  await expect(panel.getByRole('textbox', { name: 'How it is used', exact: true })).toHaveValue('Retain this user correction');
  await expect(panel.getByText(/Scope draft saved/)).toHaveCount(0);
});
