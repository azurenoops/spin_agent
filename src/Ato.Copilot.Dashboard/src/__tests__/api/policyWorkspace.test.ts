import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { getPolicyWorkspace, getPolicyLibrary, getPolicySource, getPolicyReference, addPolicyReference,
  editPolicyReference, unlinkPolicyReference, policyError, type PolicyQuery } from '../../api/policyWorkspace';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } }));
const query: PolicyQuery = { search: '', status: '', sourceChanged: '', page: 1, pageSize: 25 };
const response = { systemId: 'a', systemName: 'System A', items: [], totalCount: 0, unfilteredTotal: 0, page: 1, pageSize: 25,
  permissions: { canAssign: false, assignReason: 'Denied', canCreateLibrary: false, createReason: 'Denied' } };
const reference = { id: 'ref-a', policyId: 'source-a', name: 'Policy A', rationale: 'Applies to A.',
  retainedVersionLabel: 'Captured Sep 28, 2026', sourceStatus: 'Active', sourceChanged: false, retention: 'Retained',
  revision: 2, canEdit: true, canRemove: true, actionReason: null };
beforeEach(() => vi.clearAllMocks());
describe('Policy workspace transport', () => {
  it('omits empty optional filters and passes cancellation', async () => {
    // Arrange
    const controller = new AbortController();
    vi.mocked(apiClient.get).mockResolvedValue({ data: response });
    // Act
    await getPolicyWorkspace('a', query, controller.signal);
    // Assert
    expect(apiClient.get).toHaveBeenCalledWith('/systems/a/policy-workspace', { params: { page: 1, pageSize: 25 }, signal: controller.signal });
  });
  it.each([{ ...response, systemId: 'b' }, { ...response, permissions: {} }, { ...response, totalCount: null }])('rejects mismatched or incomplete scope', async body => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: body });
    // Act / Assert
    await expect(getPolicyWorkspace('a', query)).rejects.toThrow('Unexpected or mismatched');
  });
  it('rejects another system library and mismatched source/detail identities', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { systemId: 'b', items: [], totalCount: 0, page: 1, pageSize: 25 } });
    // Act / Assert
    await expect(getPolicyLibrary('a', { search: '', page: 1, pageSize: 25 })).rejects.toThrow('mismatched');
    await expect(getPolicySource('a', 'source-a')).rejects.toThrow('mismatched');
    await expect(getPolicyReference('a', 'ref-a')).rejects.toThrow('mismatched');
  });
  it('pins the exact selected source and uses assignment revision for edit/unlink', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: reference });
    vi.mocked(apiClient.patch).mockResolvedValue({ data: reference });
    vi.mocked(apiClient.delete).mockResolvedValue({});
    // Act
    await addPolicyReference('a', { policyId: 'source-a', expectedSourceRevision: 'retained-revision', rationale: 'Applies to A.' });
    await editPolicyReference('a', 'ref-a', { expectedRevision: 2, rationale: 'Edited rationale.' });
    await unlinkPolicyReference('a', 'ref-a', 2);
    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/a/policy-workspace/references',
      { policyId: 'source-a', expectedSourceRevision: 'retained-revision', rationale: 'Applies to A.' });
    expect(apiClient.patch).toHaveBeenCalledWith('/systems/a/policy-workspace/references/ref-a',
      { expectedRevision: 2, rationale: 'Edited rationale.' });
    expect(apiClient.delete).toHaveBeenCalledWith('/systems/a/policy-workspace/references/ref-a', { params: { expectedRevision: 2 } });
  });
  it.each([
    [403, 'does not authorize'], [404, 'unavailable or not accessible'], [409, 'Refresh before retrying'],
  ])('explains an empty HTTP %s failure without treating it as empty data', (status, expected) => {
    // Arrange
    const failure = { response: { status, data: '' } };
    // Act
    const message = policyError(failure);
    // Assert
    expect(message).toContain(expected);
  });
});
