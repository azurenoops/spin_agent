import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import {
  confirmCapabilityResponsibilities, dispatchCapabilityResponsibilityImpacts,
  getCapabilityResponsibilities, reconcileCapabilityResponsibilities,
} from '../../api/capabilityResponsibilities';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }));
const preview = { systemId: 'system/a', baselineId: 'baseline-1', canConfirm: true, items: [], pendingImpacts: [] };
beforeEach(() => { vi.clearAllMocks(); });

describe('capability responsibility API contract', () => {
  it('reads the raw preview using the scoped shared client and an encoded system ID', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: preview });
    // Act
    const result = await getCapabilityResponsibilities('system/a');
    // Assert
    expect(result).toEqual(preview);
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system%2Fa/capability-subscriptions/responsibilities', expect.anything());
  });
  it('sends exact baseline/source/review pins and only explicit allocations', async () => {
    // Arrange
    const body = { baselineId: 'baseline-1', sourceRevision: 'source-2', reviewRevision: 'review-3',
      allocations: [{ controlId: 'AC-1', inheritanceType: 'Shared' as const, provider: 'Reviewed CSP', customerResponsibility: 'Customer reviews accounts.' }] };
    vi.mocked(apiClient.put).mockResolvedValue({ data: preview });
    // Act
    await confirmCapabilityResponsibilities('system/a', 'capability/a', body);
    // Assert
    expect(apiClient.put).toHaveBeenCalledWith('/systems/system%2Fa/capability-subscriptions/capability%2Fa/responsibilities', body, expect.anything());
  });
  it('keeps reconciliation and mark-only dispatch as separate explicit requests', async () => {
    // Arrange
    const delivered = { delivered: 2, pending: 1, proposalIds: ['proposal-a'],
      deferred: [{ impactId: 'impact-a', controlId: 'AC-1', reason: 'MissingNarrative' }] };
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: preview }).mockResolvedValueOnce({ data: delivered });
    // Act
    await reconcileCapabilityResponsibilities('system/a');
    const result = await dispatchCapabilityResponsibilityImpacts('system/a');
    // Assert
    expect(vi.mocked(apiClient.post).mock.calls.map(call => call[0])).toEqual([
      '/systems/system%2Fa/capability-subscriptions/reconcile',
      '/systems/system%2Fa/capability-subscriptions/review-impacts/dispatch',
    ]);
    expect(result).toEqual(delivered);
  });
  it('retains ProblemDetails HTTP 409 after the shared client unwraps the error body', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockRejectedValue({ status: 409, title: 'Source changed', errorCode: 'RESPONSIBILITY_REVIEW_CONFLICT' });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).rejects.toMatchObject({ status: 409, message: 'Source changed' });
  });
  it.each([
    { ...preview, canConfirm: undefined },
    { ...preview, systemId: 'other-system' },
    { ...preview, pendingImpacts: null },
    { ...preview, items: [{ controlId: 'AC-1' }] },
  ])('does not turn malformed or foreign previews into success', async body => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: body });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).rejects.toThrow(/invalid|incomplete|match/i);
  });
  it('rejects missing delivery details instead of hiding deferred work', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: { delivered: 1, pending: 1 } });
    // Act / Assert
    await expect(dispatchCapabilityResponsibilityImpacts('system/a')).rejects.toThrow(/incomplete/);
  });
  it('preserves nested HTTP denial status and message', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockRejectedValue({ response: { status: 403, data: { title: 'Assigned ISSO required.' } } });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).rejects.toMatchObject({ status: 403, message: 'Assigned ISSO required.' });
  });
  it('surfaces non-structured transport failure explicitly', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockRejectedValue('offline');
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).rejects.toThrow('The responsibility request failed.');
  });
});
