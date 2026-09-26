import axios from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  approvePackage, editPackageCandidate, excludePackageEntry, getPackageCandidates, getPackageEntries,
  getPackageStatus, getPackageReviewState, listPackages, packageArtifactUrl, previewPackage, publishPackage, receivePackage, retryPackage,
} from '../../features/package-imports/api';

vi.mock('axios', async importOriginal => {
  const actual = await importOriginal<typeof import('axios')>();
  return { ...actual, default: { ...actual.default, request: vi.fn() } };
});
beforeEach(() => vi.clearAllMocks());

describe('package import HTTP contract', () => {
  it('uses private package-scoped reads and protected artifact paths', async () => {
    // Arrange
    const signal = new AbortController().signal;
    vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: {} } });
    // Act
    await listPackages(3, signal);
    await getPackageStatus('package/id', signal);
    await getPackageEntries('package/id', 2, signal);
    await getPackageReviewState('package/id', signal);
    // Assert
    expect(axios.request).toHaveBeenNthCalledWith(1, { method: 'GET', url: '/api/csp/package-imports', params: { page: 3, pageSize: 25 }, signal });
    expect(axios.request).toHaveBeenNthCalledWith(2, { method: 'GET', url: '/api/csp/package-imports/package%2Fid', signal });
    expect(axios.request).toHaveBeenNthCalledWith(3, { method: 'GET', url: '/api/csp/package-imports/package%2Fid/entries', params: { page: 2, pageSize: 25 }, signal });
    expect(axios.request).toHaveBeenNthCalledWith(4, { method: 'GET', url: '/api/csp/package-imports/package%2Fid/review-state', signal });
    expect(packageArtifactUrl('package/id', 'source/id')).toBe('/api/csp/package-imports/package%2Fid/artifacts/source%2Fid/content');
  });

  it('sends concurrency tokens for edits/exclusions and stable keys for retry', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: {} } });
    const change = {
      expectedRevision: 3, name: 'Source component', description: '', componentType: 'Service',
      classification: '', serviceCategory: '', controlDuties: {}, contributorIds: [],
      reviewAction: 'NeedsReview' as const, rationale: null, duplicateResolution: null,
    };
    // Act
    await editPackageCandidate('p', 'c', change);
    await excludePackageEntry('p', 'e', 5, 'Excluded with explicit scope rationale.');
    await retryPackage('p', 'stable-retry');
    // Assert
    expect(axios.request).toHaveBeenNthCalledWith(1, { method: 'PATCH', url: '/api/csp/package-imports/p/candidates/c', data: change });
    expect(axios.request).toHaveBeenNthCalledWith(2, { method: 'PATCH', url: '/api/csp/package-imports/p/entries/e', data: { expectedRevision: 5, rationale: 'Excluded with explicit scope rationale.' } });
    expect(axios.request).toHaveBeenNthCalledWith(3, { method: 'POST', url: '/api/csp/package-imports/p/retry', headers: { 'Idempotency-Key': 'stable-retry' } });
  });
  it.each([true, false])('explicitly negotiates durable asynchronous receipt (onboarding=%s)', async onboarding => {
    // Arrange
    const file = new File(['source'], 'package.json');
    vi.mocked(axios.request).mockResolvedValue({ status: 202, data: { status: 'success', data: { packageId: 'package', operationId: 'operation' } } });
    // Act
    await receivePackage([file], 'stable-upload-key', onboarding);
    // Assert
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      method: 'POST', url: onboarding ? '/api/csp/onboarding/atos/upload' : '/api/csp/inherited-components/import',
      headers: { 'Content-Type': 'multipart/form-data', Prefer: 'respond-async', 'Idempotency-Key': 'stable-upload-key' },
    }));
    const payload: unknown = vi.mocked(axios.request).mock.calls[0]?.[0]?.data;
    if (!(payload instanceof FormData)) throw new Error('Expected a multipart package upload.');
    expect(payload.getAll('files')).toEqual([file]);
  });

  it('does not accept a legacy tally in place of a receipt', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data: { documentsAccepted: 2 } } });
    // Act
    const receipt = receivePackage([new File(['source'], 'package.json')], 'same-key', true);
    // Assert
    await expect(receipt).rejects.toThrow(/durable package receipt/i);
  });

  it('sends pagination and both candidate filters to the server', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: { items: [], page: 2, pageSize: 25, total: 0 } } });
    const query = { page: 2, pageSize: 25, type: 'Capability', reviewState: 'NeedsReview' };
    // Act
    await getPackageCandidates('a/b', query);
    // Assert
    expect(axios.request).toHaveBeenCalledWith({ method: 'GET', url: '/api/csp/package-imports/a%2Fb/candidates', params: query, signal: undefined });
  });

  it('keeps exact preview, approval and idempotent publication as separate requests', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: {} } });
    const selection = { expectedRevision: 5, candidates: [{ candidateId: 'c', revision: 3 }] };
    const decision = { previewId: 'p', previewHash: 'hash', revision: 5 };
    // Act
    await previewPackage('package', selection);
    await approvePackage('package', decision);
    await publishPackage('package', decision, 'publication-key');
    // Assert
    expect(axios.request).toHaveBeenNthCalledWith(1, { method: 'POST', url: '/api/csp/package-imports/package/approval-previews', data: selection });
    expect(axios.request).toHaveBeenNthCalledWith(2, { method: 'POST', url: '/api/csp/package-imports/package/approve', data: decision });
    expect(axios.request).toHaveBeenNthCalledWith(3, { method: 'POST', url: '/api/csp/package-imports/package/publish', data: decision, headers: { 'Idempotency-Key': 'publication-key' } });
  });
});
