import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import {
  confirmCapabilityResponsibilities, dispatchCapabilityResponsibilityImpacts,
  getCapabilityResponsibilities, reconcileCapabilityResponsibilities,
} from '../../api/capabilityResponsibilities';
import { responsibilityItem, responsibilitySnapshotJson } from '../helpers/capabilityResponsibilityFixture';

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

  it('retains the opaque source pin instead of hashing the redacted display snapshot', async () => {
    // Arrange
    const sourceRevision = 'OPAQUE.server-revision.not-the-redacted-JSON-hash';
    const data = { ...preview, items: [{ ...responsibilityItem(), sourceRevision }] };
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    vi.mocked(apiClient.put).mockResolvedValue({ data });
    // Act
    const read = await getCapabilityResponsibilities('system/a');
    const body = { baselineId: read.baselineId!, sourceRevision: read.items[0]!.sourceRevision,
      reviewRevision: read.items[0]!.reviewRevision, allocations: [{
        controlId: 'AC-1', inheritanceType: 'Customer' as const, provider: null, customerResponsibility: 'Customer responsibility.',
      }] };
    await confirmCapabilityResponsibilities('system/a', 'capability-a', body);
    // Assert
    expect(read.items[0]!.sourceSnapshotJson).toContain('[redacted]');
    expect(vi.mocked(apiClient.put).mock.calls[0]?.[1]).toMatchObject({ sourceRevision });
  });

  it('accepts an unavailable source with withheld snapshot content', async () => {
    // Arrange
    const data = { ...preview, items: [{ ...responsibilityItem('PendingReview'), sourceAvailable: false, sourceSnapshotJson: null }] };
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).resolves.toEqual(data);
  });

  it.each([
    { sourceAvailable: undefined },
    { sourceSnapshotJson: null },
    { sourceSnapshotJson: '{bad-json' },
    { sourceSnapshotJson: responsibilitySnapshotJson({ Controls: undefined }) },
    { sourceSnapshotJson: responsibilitySnapshotJson({ Controls: 'AC-1' }) },
    { sourceSnapshotJson: responsibilitySnapshotJson({ Controls: [1] }) },
    { sourceSnapshotJson: responsibilitySnapshotJson({ Id: 'another-capability' }) },
    { sourceAvailable: false, sourceSnapshotJson: responsibilitySnapshotJson() },
    { sourceSnapshotJson: responsibilitySnapshotJson({ Component: {
      CspInheritedComponentId: 'component-a', CspProfileId: 'provider-a', Name: 'Component', Description: '', Status: 1,
      SourceArtifactReference: 'https://example.test/source?sig=synthetic-secret',
    } }) },
  ])('rejects incomplete, malformed, mismatched or unredacted required snapshots', async fields => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { ...preview, items: [{ ...responsibilityItem(), ...fields }] } });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).rejects.toThrow(/snapshot|incomplete/i);
  });

  it('retains the persisted public review snapshot after the provider metadata is unavailable', async () => {
    // Arrange
    const reviewedSourceSnapshotJson = responsibilitySnapshotJson({ Name: 'Previously reviewed public capability' });
    const data = { ...preview, items: [{
      ...responsibilityItem('PendingReview'), componentId: null, cspProfileId: null,
      sourceAvailable: false, sourceSnapshotJson: null, reviewedSourceSnapshotJson, reviewedSourceRevision: 'OLD-OPAQUE-PIN',
    }] };
    vi.mocked(apiClient.get).mockResolvedValue({ data });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).resolves.toEqual(data);
  });

  it('does not accept malformed persisted history as a current-snapshot fallback', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { ...preview, items: [{
      ...responsibilityItem(), reviewedSourceSnapshotJson: '{}',
    }] } });
    // Act / Assert
    await expect(getCapabilityResponsibilities('system/a')).rejects.toThrow(/snapshot.*malformed/i);
  });
});
