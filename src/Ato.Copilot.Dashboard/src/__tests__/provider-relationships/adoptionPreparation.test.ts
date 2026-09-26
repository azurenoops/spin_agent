import { beforeEach, describe, expect, it, vi } from 'vitest';
import { canPlanAdoption, prepareAdoption, MissionReviewRequiredError } from '../../features/provider-relationships/adoptionPreparation';
import { listApplicableProviderCapabilities } from '../../features/provider-relationships/api';
import { capability } from './fixtures';

vi.mock('../../features/provider-relationships/api', () => ({ listApplicableProviderCapabilities: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
const planned = {
  ...capability, canProposeAdoption: false, canConfirmResponsibilities: true,
  outstandingDecisions: [...capability.outstandingDecisions, 'MissionAssociationRequired'],
};
const afterAssociation = {
  ...capability, canConfirmResponsibilities: true, applicabilityPreviewHash: 'after-association',
};

describe('post-confirmation adoption preparation', () => {
  it('permits planning only when canonical permission and a missing association explain the prerequisite', () => {
    // Arrange
    const noPermission = { ...planned, canConfirmResponsibilities: false };
    const stale = { ...planned, applicabilityState: 'ReviewRequired', reasonCodes: ['HOSTING_CONTEXT_STALE'] };
    // Act
    const results = [planned, noPermission, stale].map(canPlanAdoption);
    // Assert
    expect(results).toEqual([true, false, false]);
  });

  it('refreshes the exact release preview after the expected association-only change', async () => {
    // Arrange
    vi.mocked(listApplicableProviderCapabilities).mockResolvedValue({
      items: [afterAssociation], page: 1, pageSize: 25, total: 1,
    });
    // Act
    const result = await prepareAdoption('system-a', planned);
    // Assert
    expect(listApplicableProviderCapabilities).toHaveBeenCalledWith('system-a', {
      page: 1, assignmentId: planned.assignmentId, capabilityId: planned.capabilityId, releaseId: planned.releaseId,
    });
    expect(result.capability).toEqual(afterAssociation);
    expect(result.body).toEqual({
      assignmentId: planned.assignmentId, expectedAssignmentRevision: planned.assignmentRevision,
      capabilityId: planned.capabilityId, releaseId: planned.releaseId,
      contextSnapshotHash: planned.applicability.snapshotHash, applicabilityPreviewHash: 'after-association',
    });
  });

  it.each([
    { customerDuties: ['New customer duties'] },
    { releaseSnapshotHash: 'different-release-hash' },
    { assignmentRevision: 44 },
    { authorizationRelationship: 'ExplicitlyCoveredByRecordedScope' as const },
    { sourceReferences: [] },
    { canProposeAdoption: false },
  ])('never silently accepts a changed reviewed source or missing authority: %j', async change => {
    // Arrange
    vi.mocked(listApplicableProviderCapabilities).mockResolvedValue({
      items: [{ ...afterAssociation, ...change }], page: 1, pageSize: 25, total: 1,
    });
    // Act
    const result = prepareAdoption('system-a', planned);
    // Assert
    await expect(result).rejects.toBeInstanceOf(MissionReviewRequiredError);
  });

  it.each([
    { items: [], total: 0 },
    { items: [afterAssociation, afterAssociation], total: 2 },
    { items: [{ ...afterAssociation, outstandingDecisions: ['MissionAssociationRequired'] }], total: 1 },
  ])('blocks an absent, ambiguous or still unassociated exact release: %j', async data => {
    // Arrange
    vi.mocked(listApplicableProviderCapabilities).mockResolvedValue({ ...data, page: 1, pageSize: 25 });
    // Act
    const result = prepareAdoption('system-a', planned);
    // Assert
    await expect(result).rejects.toBeInstanceOf(MissionReviewRequiredError);
  });

  it('surfaces read outages rather than substituting the old request', async () => {
    // Arrange
    vi.mocked(listApplicableProviderCapabilities).mockRejectedValue(new Error('Exact release unavailable'));
    // Act
    const result = prepareAdoption('system-a', planned);
    // Assert
    await expect(result).rejects.toThrow('Exact release unavailable');
  });
});
