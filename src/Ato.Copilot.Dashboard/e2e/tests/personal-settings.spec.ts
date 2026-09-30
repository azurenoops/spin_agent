import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture, systemCapabilityRoot } from '../fixtures/system-capabilities';

for (const width of [1440, 390]) {
  test(`personal settings preserve authority and report account-save failure at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await installSystemCapabilityFixture(context, baseURL!);
    await page.setViewportSize({ width, height: 1000 });
    await context.addInitScript(() => localStorage.setItem('ato-dashboard-settings', JSON.stringify({
      theme: 'light', activeFramework: 'FedRAMP Rev. 5', defaultExportFormat: 'xlsx',
    })));
    let writes = 0;
    let releaseFailure!: () => void;
    const failedResponse = new Promise<void>(resolve => { releaseFailure = resolve; });
    await context.route('**/api/dashboard/notifications/preferences', async route => {
      if (route.request().method() === 'GET') return route.fulfill({ json: {
        poamOverdueAlerts: true, atoExpirationAlerts: true, complianceDriftAlerts: true, alertDaysBefore: 30,
      } });
      writes++;
      if (writes === 1) {
        await failedResponse;
        return route.fulfill({ status: 503, json: { message: 'Synthetic account save unavailable' } });
      }
      return route.fulfill({ json: route.request().postDataJSON() });
    });
    await page.goto(systemCapabilityRoot);
    // Act
    await page.getByRole('button', { name: 'Settings', exact: true }).click();
    const drawer = page.getByRole('dialog', { name: 'Settings', exact: true });
    // Assert
    await expect(drawer.getByRole('button', { name: 'Preferences', exact: true })).toHaveAttribute('aria-expanded', 'true');
    await expect(drawer.getByRole('navigation')).toHaveCount(0);
    for (const label of ['Organization Framework', 'Session Timeout', 'Default Export Format', 'Chat Verbosity']) {
      await expect(drawer.getByLabel(label, { exact: true })).toHaveCount(0);
    }
    await drawer.getByRole('combobox', { name: 'Theme', exact: true }).selectOption('dark');
    await expect(page.locator('html')).toHaveClass(/dark/);
    await drawer.getByRole('button', { name: 'Notifications', exact: true }).click();
    await drawer.getByLabel('Warning days before expiration', { exact: true }).fill('7');
    await drawer.getByRole('button', { name: 'Save preferences', exact: true }).click();
    await expect(drawer.getByRole('status')).toHaveText('Saving');
    await expect(drawer.getByRole('button', { name: 'Close dialog', exact: true })).toBeDisabled();
    releaseFailure();
    await expect(drawer.getByRole('alert')).toContainText('Couldn’t save');
    await expect(drawer.getByText('Saved', { exact: true })).toHaveCount(0);
    await drawer.getByRole('button', { name: 'Retry', exact: true }).click();
    await expect(drawer.getByRole('status')).toHaveText('Saved');
    await drawer.getByRole('button', { name: 'Reset personal preferences', exact: true }).click();
    await drawer.getByRole('button', { name: 'Confirm reset', exact: true }).click();
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    expect(writes).toBe(2);
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('ato-dashboard-settings') ?? '{}').activeFramework)).toBe('FedRAMP Rev. 5');
    await expect(drawer.getByLabel('Warning days before expiration', { exact: true })).toHaveValue('7');
    await expect(drawer).toHaveJSProperty('scrollWidth', await drawer.evaluate(element => element.clientWidth));
    await page.screenshot({ path: info.outputPath('personal-settings.png') });
    await page.keyboard.press('Escape');
    await expect(drawer).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Settings', exact: true })).toBeFocused();
  });
}
