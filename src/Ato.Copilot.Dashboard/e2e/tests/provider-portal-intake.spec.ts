import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { packageStatus } from '../../src/__tests__/package-imports/fixtures';
import { setupState } from '../../src/__tests__/provider-authorizations/testData';
import type { UploadIntent, UploadIntentInput } from '../../src/features/csp-onboarding/providerSetupApi';

for (const width of [1440, 390]) {
  test(`active provider portal retains registered receipt after 25 historical sources at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await installWorkspaceFixture(context, baseURL!, { providerOnly: true });
    await page.setViewportSize({ width, height: 1000 });
    const policy = setupState().handling;
    const history = Array.from({ length: 25 }, (_, index) => `historical-receipt-${index}`);
    const requests: string[] = [];
    let intent: UploadIntent | null = null;
    let received = false;
    let uploadCount = 0;
    const receipt = { ...packageStatus({ processingState: 'Received',
      packageId: '11111111-1111-4111-8111-111111111111', operationId: '11111111-1111-4111-8111-111111111111' }), association: null };
    await context.route('**/api/csp/onboarding/handling-policy', route => route.fulfill({ json: { status: 'success', data: policy } }));
    await context.route(/\/api\/csp\/(?:package-imports(?:\/.*)?|inherited-components\/import)(?:\?.*)?$/, async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      requests.push(`${request.method()} ${path}`);
      const success = (data: unknown, status = 200) => route.fulfill({ status, json: { status: 'success', data } });
      if (path.endsWith('/upload-intents') && request.method() === 'GET')
        return success({ items: intent && !received ? [intent] : [], page: 1, pageSize: 25, total: intent && !received ? 1 : 0 });
      if (path.endsWith('/upload-intents') && request.method() === 'POST') {
        const body = request.postDataJSON() as { expectedSetupRevision: number; intent: UploadIntentInput };
        expect(body.expectedSetupRevision).toBe(0);
        expect(body.intent.entryPoint).toBe('ActivePortal');
        expect(body.intent.associationMode).toBe('Unassociated');
        expect(body.intent.context).toBeNull();
        intent = { input: body.intent, intentId: body.intent.intentId, intentHash: 'A'.repeat(64), revision: 1,
          savedAt: '2026-09-30T12:00:00Z', receipt: null,
          reconciliation: { outcome: 'NotObserved', nextAction: 'ReselectSameFiles', observedAt: '2026-09-30T12:00:00Z' } };
        return success(intent, 201);
      }
      if (path.includes('/upload-intents/')) {
        if (!intent) return route.fulfill({ status: 404 });
        if (received) intent.receipt = receipt;
        return success(intent);
      }
      if (path.endsWith('/inherited-components/import')) {
        expect(request.headers()['x-provider-upload-intent']).toBe(intent!.intentId);
        expect(request.headers()['idempotency-key']).toBe(intent!.intentId);
        expect(request.headers()['idempotency-key'].length).toBeLessThanOrEqual(100);
        expect(request.headers()['x-workspace-kind']).toBe('csp');
        received = true; uploadCount++;
        history.push(receipt.packageId);
        return route.abort('connectionfailed');
      }
      if (path.endsWith(receipt.packageId)) return success(receipt);
      return route.fulfill({ status: 404, json: { status: 'error', error: { message: `Unconfigured portal fixture ${path}` } } });
    });
    await page.goto('/workspaces/csp/authorizations/import');
    await page.getByLabel('Declared source classification').selectOption('Unclassified');
    await page.getByLabel('These files contain only synthetic data.').check();
    await page.getByLabel('Select source files', { exact: true }).setInputFiles({
      name: 'twenty-sixth-synthetic.txt', mimeType: 'text/plain', buffer: Buffer.from('Synthetic portal source; no real authorization.'),
    });
    // Act
    await page.getByRole('button', { name: 'Upload package', exact: true }).click();
    await expect(page.getByRole('alert').first()).toBeVisible();
    await page.reload();
    // The interrupted request is recoverable by its server-held identity, without browser File objects.
    await page.getByRole('button', { name: 'Check retained receipt' }).click();
    // Assert
    await expect(page).toHaveURL(new RegExp(`packageId=${receipt.packageId}`));
    await expect(page.getByText('Receipt confirmed · Revision 4')).toBeVisible();
    expect(history).toHaveLength(26);
    expect(uploadCount).toBe(1);
    expect(requests.filter(value => value.includes('/onboarding/setup'))).toEqual([]);
    expect(requests.filter(value => /association|boundary-revisions|approve|publish/.test(value))).toEqual([]);
    await page.screenshot({ path: info.outputPath(`active-portal-recovered-${width}.png`), fullPage: true });
  });
}
