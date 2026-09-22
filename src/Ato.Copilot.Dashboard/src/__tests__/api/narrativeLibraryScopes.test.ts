import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { getOrganizationLibraryAccess, getProviderLibraryAccess, getScopedReferences,
  importScopedReference, updateScopedReference, publishScopedReference, getImpactReceipts } from '../../api/narrativeLibrary';

vi.mock('../../api/client', () => ({ default: {
  defaults: { baseURL: '/api/dashboard' }, get: vi.fn(), post: vi.fn(), patch: vi.fn(),
} }));
const reference = { id: 'reference-a', referenceKey: 'key-a', title: 'Reference', scope: 'Organization', scopeId: 'org-a',
  sourceName: 'reference.txt', sourceSha256: 'hash', version: 1, revision: 1, isPublished: false,
  createdAt: '2026-09-22T00:00:00Z', createdBy: 'author', publishedAt: null, publishedBy: null, passages: [] };
beforeEach(() => { vi.clearAllMocks(); });

describe('distinct narrative library contracts', () => {
  it('keeps organization and provider access DTOs distinct without any system path', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: { tenantId: 'org-a', canPublishShared: true, capabilities: [] } })
      .mockResolvedValueOnce({ data: { cspProfileId: 'provider-a', displayName: 'Provider A', canPublish: true, capabilities: [] } });
    // Act
    const org = await getOrganizationLibraryAccess();
    const provider = await getProviderLibraryAccess();
    // Assert
    expect(org.tenantId).toBe('org-a');
    expect(provider.cspProfileId).toBe('provider-a');
    expect(vi.mocked(apiClient.get).mock.calls.map(call => call[0])).toEqual(['/narrative-library/access', '/csp/narrative-library/access']);
  });
  it.each([{}, { tenantId: 'org-a', capabilities: [] }, { tenantId: 'org-a', canPublishShared: 'true', capabilities: [] }])('rejects unknown organization permissions', async data => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getOrganizationLibraryAccess()).rejects.toThrow(/access/i);
  });
  it('does not interpret organization access as provider access', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { tenantId: 'org-a', canPublishShared: true, capabilities: [] } });
    // Act / Assert
    await expect(getProviderLibraryAccess()).rejects.toThrow(/access/i);
  });
  it('uses provider-owned reference paths and preserves draft/publication revisions', async () => {
    // Arrange
    const providerReference = { ...reference, scope: 'Provider', scopeId: 'provider-a' };
    vi.mocked(apiClient.get).mockResolvedValue({ data: [providerReference] });
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: providerReference })
      .mockResolvedValueOnce({ data: { ...providerReference, revision: 3, isPublished: true } });
    vi.mocked(apiClient.patch).mockResolvedValue({ data: { ...providerReference, revision: 2 } });
    const target = { kind: 'provider' as const };
    const form = new FormData();
    const patch = { expectedRevision: 1, scope: 'Provider', scopeId: 'provider-a', passages: [] };
    // Act
    await getScopedReferences(target);
    await importScopedReference(target, form);
    await updateScopedReference(target, 'reference-a', patch);
    await publishScopedReference(target, 'reference-a', 2, []);
    // Assert
    expect(apiClient.patch).toHaveBeenCalledWith('/csp/narrative-library/reference-a', patch, expect.objectContaining({ baseURL: '/api' }));
    expect(apiClient.post).toHaveBeenLastCalledWith('/csp/narrative-library/reference-a/publish',
      { expectedRevision: 2, reviewed: true, passages: [] }, expect.objectContaining({ baseURL: '/api' }));
    expect(vi.mocked(apiClient.get).mock.calls[0]?.[0]).not.toContain('/systems/');
  });
  it('rejects provider records returned through an organization library', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: [{ ...reference, scope: 'Provider', scopeId: 'provider-a' }] });
    // Act / Assert
    await expect(getScopedReferences({ kind: 'organization' })).rejects.toThrow(/scope|reference/i);
  });
  it('reads paged immutable receipts using real system/proposal IDs', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { items: [], totalCount: 0, page: 2, pageSize: 50 } });
    // Act
    await getImpactReceipts('system/a', 'proposal/a', 2);
    // Assert
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system%2Fa/narrative-library/proposals/proposal%2Fa/impact-receipts',
      expect.objectContaining({ baseURL: '/api', params: { page: 2, pageSize: 50 } }));
  });
});
