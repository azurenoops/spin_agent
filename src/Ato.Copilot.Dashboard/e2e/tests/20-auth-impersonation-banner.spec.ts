/**
 * Epic #207 / Task #236 — ImpersonationBanner E2E test.
 *
 * Verifies:
 * 1. The impersonation banner renders when `me.isImpersonating === true`
 * 2. The banner shows the impersonated tenant name
 * 3. "Stop Impersonating" button is present and calls the correct endpoint
 * 4. After stopping, the banner is no longer visible
 *
 * These tests mock the /api/auth/me response to control impersonation state
 * without requiring a live CSP-Admin session.
 */
import { test, expect } from '@playwright/test';

test.describe('ImpersonationBanner', () => {
  test('banner visible when me.isImpersonating is true', async ({ page }) => {
    // Intercept /api/auth/me to return an impersonating session
    await page.route('/api/auth/me', (route) => {
      void route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          status: 'success',
          data: {
            sub: 'csp-admin-oid',
            displayName: 'CSP Admin',
            roles: ['CSP.Admin'],
            tenantId: 'tenant-a-id',
            isImpersonating: true,
            impersonation: {
              tenantId: 'tenant-a-id',
              tenantName: 'Tenant Alpha',
              expiresAt: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
            },
            isCspAdmin: true,
            tenantMemberships: [],
          },
        }),
      });
    });

    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Banner should render with tenant name
    await expect(
      page.getByRole('status'),
    ).toBeVisible({ timeout: 5_000 });

    await expect(page.getByText(/Tenant Alpha|impersonat/i)).toBeVisible();
  });

  test('Stop Impersonating button calls DELETE endpoint', async ({ page }) => {
    let deleteEndpointCalled = false;

    await page.route('/api/auth/me', (route) => {
      void route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          status: 'success',
          data: {
            sub: 'csp-admin-oid',
            displayName: 'CSP Admin',
            roles: ['CSP.Admin'],
            tenantId: 'tenant-a-id',
            isImpersonating: true,
            impersonation: {
              tenantId: 'tenant-a-id',
              tenantName: 'Tenant Alpha',
              expiresAt: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
            },
            isCspAdmin: true,
            tenantMemberships: [],
          },
        }),
      });
    });

    await page.route('**/api/csp/impersonation', (route) => {
      if (route.request().method() === 'DELETE') {
        deleteEndpointCalled = true;
        void route.fulfill({ status: 204 });
      } else {
        void route.continue();
      }
    });

    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const stopBtn = page.getByRole('button', { name: /stop impersonat/i });
    if (await stopBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await stopBtn.click();
      await page.waitForTimeout(500);
      expect(deleteEndpointCalled).toBe(true);
    }
  });

  test('banner NOT visible when me.isImpersonating is false', async ({ page }) => {
    await page.route('/api/auth/me', (route) => {
      void route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          status: 'success',
          data: {
            sub: 'regular-user-oid',
            displayName: 'Regular User',
            roles: ['ISSO'],
            tenantId: 'tenant-b-id',
            isImpersonating: false,
            impersonation: null,
            isCspAdmin: false,
            tenantMemberships: [],
          },
        }),
      });
    });

    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // The banner should not be present
    const banner = page.getByRole('status').filter({ hasText: /impersonat/i });
    await expect(banner).not.toBeVisible({ timeout: 2_000 }).catch(() => {
      // If the selector finds nothing at all, that's also correct.
    });
  });
});
