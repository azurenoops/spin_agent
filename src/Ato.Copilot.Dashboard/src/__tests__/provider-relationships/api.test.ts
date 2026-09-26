import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import {
  associateProviderRelationship,
  listApplicableProviderCapabilities,
  listProviderRelationships,
  listSystemHostingAllocations,
  listAllSystemHostingAllocations,
  previewProviderRelationship,
  proposeProviderCapabilityAdoption,
  reviewProviderRelationship,
} from '../../features/provider-relationships/api';
import { allocation, allocationResponse, capability, adoption as adoptionResult } from './fixtures';

vi.mock('../../api/client', () => ({ default: { request: vi.fn() } }));

beforeEach(() => vi.resetAllMocks());

describe('mission provider relationship transport', () => {
  it('reads every hosting page without dropping later associated scopes', async () => {
    // Arrange
    vi.mocked(apiClient.request)
      .mockResolvedValueOnce({ data: { status: 'success', data: { items: [allocationResponse], page: 1, pageSize: 25, total: 2 } } })
      .mockResolvedValueOnce({ data: { status: 'success', data: {
        items: [{ ...allocationResponse, assignmentId: 'assignment-b', relationshipId: 'relationship-b' }], page: 2, pageSize: 25, total: 2,
      } } });
    // Act
    const items = await listAllSystemHostingAllocations('system-a');
    // Assert
    expect(items.map(item => item.assignmentId)).toEqual(['assignment-a', 'assignment-b']);
    expect(items[1]?.relationshipId).toBe('relationship-b');
  });

  it.each([
    { items: [allocationResponse], page: 2, pageSize: 25, total: 2 },
    { items: [{ ...allocationResponse, assignmentId: 'assignment-b', systemId: 'system-b' }], page: 2, pageSize: 25, total: 2 },
    { items: [], page: 2, pageSize: 25, total: 2 },
    { items: [{ ...allocationResponse, assignmentId: 'assignment-b' }], page: 2, pageSize: 25, total: 3 },
  ])('rejects duplicate, cross-system, incomplete or changed hosting pages', async next => {
    // Arrange
    vi.mocked(apiClient.request)
      .mockResolvedValueOnce({ data: { status: 'success', data: { items: [allocationResponse], page: 1, pageSize: 25, total: 2 } } })
      .mockResolvedValueOnce({ data: { status: 'success', data: next } });
    // Act / Assert
    await expect(listAllSystemHostingAllocations('system-a')).rejects.toThrow(/Hosting allocations/);
    expect(apiClient.request).toHaveBeenCalledTimes(2);
  });

  it('uses the existing dashboard client, encoded system path and bounded paging', async () => {
    // Arrange
    const result = { items: [], page: 2, pageSize: 25, total: 0 };
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: result } });
    const signal = new AbortController().signal;

    // Act
    const actual = await listProviderRelationships('system/a', 2, signal);

    // Assert
    expect(actual).toEqual(result);
    expect(apiClient.request).toHaveBeenCalledWith({
      method: 'GET', url: '/systems/system%2Fa/provider-relationships',
      params: { page: 2, pageSize: 25 }, signal,
    });
  });

  it('associates only the exact technical allocation with a stable operation key', async () => {
    // Arrange
    const result = { relationshipId: 'relationship-1', revision: 1, assignmentId: 'assignment-1', state: 'Undetermined' };
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: result } });

    // Act
    await associateProviderRelationship('system-1', { assignmentId: 'assignment-1', expectedAssignmentRevision: 4 }, 'associate-key');

    // Assert
    expect(apiClient.request).toHaveBeenCalledWith({
      method: 'POST', url: '/systems/system-1/provider-relationships',
      data: { assignmentId: 'assignment-1', expectedAssignmentRevision: 4 },
      headers: { 'Idempotency-Key': 'associate-key' },
    });
  });

  it('keeps review bound to the exact server preview instead of resubmitting mutable scope', async () => {
    // Arrange
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: {} } });
    const proposal = {
      expectedRevision: 3, expectedAssignmentRevision: 4, relationshipState: 'SeparateBoundaryConsumer' as const,
      evidence: [], rationale: 'Mission maintains a separate boundary.',
    };
    const decision = {
      expectedRevision: 3, previewId: 'preview-1', previewHash: 'immutable-preview-hash',
      rationale: 'Mission maintains a separate boundary.',
    };

    // Act
    await previewProviderRelationship('system-1', 'relationship/1', proposal);
    await reviewProviderRelationship('system-1', 'relationship/1', decision);

    // Assert
    expect(apiClient.request).toHaveBeenNthCalledWith(1, {
      method: 'POST', url: '/systems/system-1/provider-relationships/relationship%2F1/previews', data: proposal,
    });
    expect(apiClient.request).toHaveBeenNthCalledWith(2, {
      method: 'POST', url: '/systems/system-1/provider-relationships/relationship%2F1/review', data: decision,
    });
  });

  it('requests server applicability and sends exact adoption context only after explicit action', async () => {
    // Arrange
    vi.mocked(apiClient.request)
      .mockResolvedValueOnce({ data: { status: 'success', data: { items: [capability], page: 1, pageSize: 25, total: 1 } } })
      .mockResolvedValueOnce({ data: { status: 'success', data: { ...adoptionResult, releaseId: 'release-1' } } });
    const query = { page: 1, assignmentId: 'assignment-1', environment: 'AzureUSGovernment' as const };
    const adoption = {
      assignmentId: 'assignment-1', expectedAssignmentRevision: 4, capabilityId: 'capability-1',
      releaseId: 'release-1', contextSnapshotHash: 'context-hash', applicabilityPreviewHash: 'applicability-hash',
    };

    // Act
    await listApplicableProviderCapabilities('system-1', query);
    await proposeProviderCapabilityAdoption('system-1', adoption, 'adoption-key');

    // Assert
    expect(apiClient.request).toHaveBeenNthCalledWith(1, {
      method: 'GET', url: '/systems/system-1/applicable-provider-capabilities',
      params: { ...query, pageSize: 25 }, signal: undefined,
    });
    expect(apiClient.request).toHaveBeenNthCalledWith(2, {
      method: 'POST', url: '/systems/system-1/provider-capability-adoptions',
      data: adoption, headers: { 'Idempotency-Key': 'adoption-key' },
    });
  });

  it('reads existing allocations for the exact system without provider-side scope grants', async () => {
      // Arrange
      const data = { items: [allocationResponse], page: 2, pageSize: 25, total: 26 };
      vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data } });
      // Act
      const actual = await listSystemHostingAllocations('system/a', 2);
      // Assert
      expect(actual).toEqual({ ...data, items: [allocation] });
      expect(apiClient.request).toHaveBeenCalledWith({
        method: 'GET', url: '/systems/system%2Fa/provider-relationships',
        params: { page: 2, pageSize: 25 }, signal: undefined,
      });
    });

  it('fails closed on a successful envelope with incomplete capability responsibilities', async () => {
      // Arrange
      vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: {
        items: [{ ...capability, customerDuties: undefined }], page: 1, pageSize: 25, total: 1,
      } } });
      // Act
      const result = listApplicableProviderCapabilities('system-a', { page: 1, assignmentId: 'assignment-a' });
      // Assert
      await expect(result).rejects.toThrow('incomplete');
    });

  it('retains the existing relationship identity when association permission is false', async () => {
    // Arrange
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: {
      items: [{ ...allocationResponse, relationshipId: 'existing-relationship', canAssociate: false }],
      page: 1, pageSize: 25, total: 1,
    } } });
    // Act
    const actual = await listSystemHostingAllocations('system-a');
    // Assert
    expect(actual.items[0]).toMatchObject({ relationshipId: 'existing-relationship', canAssociate: false });
  });

  it('fails closed on missing allocation permissions instead of inferring them from roles', async () => {
      // Arrange
      vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: {
        items: [{ ...allocationResponse, canAssociate: undefined }], page: 1, pageSize: 25, total: 1,
      } } });
      // Act
      const result = listSystemHostingAllocations('system-a');
      // Assert
      await expect(result).rejects.toThrow('incomplete');
    });

  it('retains flattened dashboard error messages, codes and corrective guidance', async () => {
    // Arrange
    vi.mocked(apiClient.request).mockRejectedValue({
      status: 'error', error: {
        message: 'The assignment changed.', errorCode: 'AUTHORIZATION_CONTEXT_STALE',
        suggestion: 'Refresh the relationship and create a new preview.',
      },
    });

    // Act
    const request = listProviderRelationships('system-1');

    // Assert
    await expect(request).rejects.toMatchObject({
      code: 'AUTHORIZATION_CONTEXT_STALE',
      message: 'The assignment changed. Refresh the relationship and create a new preview.',
    });
  });

  it('does not convert an invalid success envelope into an empty authorized list', async () => {
    // Arrange
    vi.mocked(apiClient.request).mockResolvedValue({ data: { items: [] } });

    // Act
    const request = listProviderRelationships('system-1');

    // Assert
    await expect(request).rejects.toThrow('The server did not return provider relationship data.');
  });

  it('does not report a malformed association write response as saved', async () => {
    // Arrange
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: {} } });
    // Act
    const result = associateProviderRelationship('system-a', { assignmentId: 'assignment-a', expectedAssignmentRevision: 3 }, 'retry-key');
    // Assert
    await expect(result).rejects.toThrow('not confirmed');
  });

  it('does not report the wrong release adoption response as saved', async () => {
    // Arrange
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data: { ...adoptionResult, releaseId: 'other-release' } } });
    // Act
    const result = proposeProviderCapabilityAdoption('system-a', {
      assignmentId: 'assignment-a', expectedAssignmentRevision: 3, capabilityId: 'capability-a',
      releaseId: 'release-a', contextSnapshotHash: 'context-hash', applicabilityPreviewHash: 'preview-hash',
    }, 'retry-key');
    // Assert
    await expect(result).rejects.toThrow('not confirmed');
  });

  it('accepts the exact backend applicability DTO without inventing an extra context hash field', async () => {
    // Arrange
    const data = { items: [capability], page: 1, pageSize: 25, total: 1 };
    vi.mocked(apiClient.request).mockResolvedValue({ data: { status: 'success', data } });
    // Act
    const result = await listApplicableProviderCapabilities('system-a', { page: 1, assignmentId: 'assignment-a' });
    // Assert
    expect(result.items[0]?.applicability.snapshotHash).toBe('context-hash');
    expect(result.items[0]).not.toHaveProperty('contextSnapshotHash');
  });
});
