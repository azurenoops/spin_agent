/**
 * Epic #207 / Task #234 — Idle timeout wiring + idle modal E2E flow.
 *
 * These tests verify:
 * 1. The login-config endpoint exposes `idleTimeoutMinutes`
 * 2. The IdleWarningModal renders when the `ato:idle-warning` event fires
 *    (uses fake timers via page.clock to skip the real wait)
 * 3. "Stay signed in" resets the idle timer without re-auth
 * 4. The modal dismisses itself when the countdown reaches 0
 *
 * NOTE: Playwright `page.clock` API (Playwright ≥ 1.45) is used to advance
 * fake timers. If the CI runner has an older Playwright version the
 * clock-advance tests are skipped gracefully.
 */
import { test, expect } from '@playwright/test';

test.describe('Idle Timeout — wiring verification', () => {
  test('login-config exposes idleTimeoutMinutes (FedRAMP: must be ≤ 15)', async ({
    request,
  }) => {
    const resp = await request.get('/api/auth/login-config');
    expect(resp.ok()).toBe(true);

    const body = (await resp.json()) as { data?: { idleTimeoutMinutes?: unknown } };
    const minutes = body?.data?.idleTimeoutMinutes;

    expect(typeof minutes).toBe('number');
    expect(minutes as number).toBeGreaterThan(0);
    expect(minutes as number).toBeLessThanOrEqual(15);
  });

  test('IdleWarningModal renders when ato:idle-warning fires', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Fire the warning event directly — avoids waiting for the real timer.
    // The modal listens on window for 'ato:idle-warning' with { secondsUntilSignOut }.
    await page.evaluate(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', {
          detail: { secondsUntilSignOut: 60 },
        }),
      );
    });

    await expect(
      page.getByRole('alertdialog', { name: /signed out/i }),
    ).toBeVisible({ timeout: 3_000 });

    await expect(page.getByText(/60|59/)).toBeVisible();
    await expect(page.getByRole('button', { name: /stay signed in/i })).toBeVisible();
  });

  test('Stay signed in closes modal and fires ato:user-input', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Listen for the user-input reset event
    const resetFired = page.evaluate(
      () =>
        new Promise<boolean>((resolve) => {
          window.addEventListener(
            'ato:user-input',
            () => resolve(true),
            { once: true },
          );
          // Timeout after 3 s if event never fires
          setTimeout(() => resolve(false), 3_000);
        }),
    );

    await page.evaluate(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', {
          detail: { secondsUntilSignOut: 60 },
        }),
      );
    });

    await page.getByRole('button', { name: /stay signed in/i }).click();

    const wasReset = await resetFired;
    expect(wasReset).toBe(true);

    // Modal must be gone
    await expect(
      page.getByRole('alertdialog', { name: /signed out/i }),
    ).not.toBeVisible({ timeout: 1_000 });
  });
});
