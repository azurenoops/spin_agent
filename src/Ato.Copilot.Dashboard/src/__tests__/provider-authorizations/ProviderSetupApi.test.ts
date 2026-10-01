import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../features/csp-onboarding/providerSetupApi';
import { getProviderSetup } from '../../features/csp-onboarding/setupApi';
import { packageRequest } from '../../features/package-imports/request';
import { setupState } from './testData';
import type { UploadIntent } from '../../features/csp-onboarding/providerSetupApi';

vi.mock('../../features/package-imports/request', () => ({ packageRequest: vi.fn() }));
const key = '44444444-4444-4444-8444-444444444444';
const intent: UploadIntent = {
  intentId: key, intentHash: 'A'.repeat(64), revision: 1, savedAt: '2026-09-30T12:00:00Z', receipt: null,
  input: { intentId: key, schemaVersion: 1, packageName: 'Retained exact name', entryPoint: 'Onboarding',
    associationMode: 'Unassociated', offeringHintId: null, context: null,
    files: [{ ordinal: 0, fileName: 'synthetic.txt', mediaType: 'text/plain', byteLength: 1, sha256: 'B'.repeat(64) }],
    handlingPolicyVersion: 'test-policy', declaredContent: { classification: 'Unclassified', markings: [], containsOnlySyntheticData: true } },
  reconciliation: { outcome: 'NotObserved', observedAt: '2026-09-30T12:00:00Z', nextAction: 'ReselectSameFiles' },
};
beforeEach(() => { vi.clearAllMocks(); vi.mocked(packageRequest).mockResolvedValue({}); });
describe('canonical provider setup transport', () => {
  it('shares the canonical read and cancellation signal with setup home', async () => {
    // Arrange
    const controller = new AbortController();
    // Act
    await getProviderSetup(controller.signal);
    // Assert
    expect(getProviderSetup).toBe(api.getSetup);
    expect(packageRequest).toHaveBeenCalledWith({ url: '/api/csp/onboarding/setup', signal: controller.signal });
  });

  it('binds exact revisions and stable keys for save, section commit and completion', async () => {
    // Arrange
    const draft = setupState().draft!.fields;
    // Act
    await api.saveDraft(3, draft, key);
    await api.commitSetup(4, 2, 'FirstOffering', key, 9);
    await api.completeSetup(5, 2, [key], key);
    // Assert
    expect(packageRequest).toHaveBeenNthCalledWith(1, { method: 'PUT', url: '/api/csp/onboarding/setup/draft',
      data: { expectedRevision: 3, draft }, headers: { 'Idempotency-Key': key } });
    expect(packageRequest).toHaveBeenNthCalledWith(2, { method: 'POST', url: '/api/csp/onboarding/setup/commits',
      data: { expectedRevision: 4, expectedProfileRevision: 2, section: 'FirstOffering', expectedOfferingRevision: 9 }, headers: { 'Idempotency-Key': key } });
    expect(packageRequest).toHaveBeenNthCalledWith(3, { method: 'POST', url: '/api/csp/onboarding/setup/completion',
      data: { expectedRevision: 5, expectedProfileRevision: 2, confirmed: true, acknowledgedUnresolvedIntentIds: [key] }, headers: { 'Idempotency-Key': key } });
  });

  it('prepares metadata and reconciles the stored key/hash without transmitting source bytes', async () => {
    // Arrange
    const input = intent.input;
    // Act
    await api.prepareUpload(3, input);
    await api.getUploadIntent(key);
    await api.reconcileReceipt(intent);
    // Assert
    expect(packageRequest).toHaveBeenNthCalledWith(1, { method: 'POST', url: '/api/csp/package-imports/upload-intents',
      data: { expectedSetupRevision: 3, intent: input }, headers: { 'Idempotency-Key': key } });
    expect(packageRequest).toHaveBeenNthCalledWith(2, { url: `/api/csp/package-imports/upload-intents/${key}` });
    expect(packageRequest).toHaveBeenNthCalledWith(3, { method: 'POST', url: '/api/csp/package-imports/receipt-reconciliation',
      data: { requestKey: key, intentHash: intent.intentHash } });
  });

  it.each([false, true])('sends original retained identity and files to the canonical ingress (active=%s)', async active => {
    // Arrange
    const file = new File(['x'], 'synthetic.txt', { type: 'text/plain' });
    // Act
    await api.uploadSource(intent, [file], active);
    // Assert
    const request = vi.mocked(packageRequest).mock.calls[0]![0];
    expect(request.url).toBe(active ? '/api/csp/inherited-components/import' : '/api/csp/onboarding/atos/upload');
    expect(request.headers).toEqual({ 'Idempotency-Key': key, 'X-Provider-Upload-Intent': key, Prefer: 'respond-async' });
    expect(request.data).toBeInstanceOf(FormData);
    const form = request.data as FormData;
    expect(form.get('name')).toBe('Retained exact name');
    expect(form.getAll('files')).toEqual([file]);
  });
});
