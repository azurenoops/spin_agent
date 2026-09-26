import { describe, expect, it, vi } from 'vitest';
import { confirmSystemCapabilityResponsibilities } from '../../features/workspace-operations/system-capabilities/systemCapabilityResponsibilityRequests';
import { responsibilityItem } from '../helpers/capabilityResponsibilityFixture';
const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('../../features/workspace-operations/workspaceRequest', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/workspace-operations/workspaceRequest')>(), workspaceRequest: request,
}));

describe('Strict source-bound responsibility confirmation', () => {
  const body = {
    baselineId: 'baseline-a', sourceRevision: 'source-1', reviewRevision: 'review-1',
    allocations: [{ controlId: 'AC-1', inheritanceType: 'Shared' as const, provider: 'Collect logs', customerResponsibility: 'Review logs' }],
    providerCoverageVerified: true as const, customerDutiesReviewed: true as const, reviewNotes: 'Saved notes.',
  };

  it('uses the strict adapter and validates the returned persisted review evidence', async () => {
    // Arrange
    const item = { ...responsibilityItem('Applied', 'AC-1'), ...{
      providerCoverageVerified: true, customerDutiesReviewed: true, reviewNotes: 'Saved notes.',
    } };
    request.mockResolvedValue({ systemId: 'system/a', baselineId: 'baseline-a', canConfirm: true, items: [item], pendingImpacts: [] });
    const signal = new AbortController().signal;
    // Act
    await confirmSystemCapabilityResponsibilities('tenant/a', 'system/a', 'capability-a', body, signal);
    // Assert
    expect(request).toHaveBeenCalledWith({
      method: 'POST',
      url: '/api/workspaces/organizations/tenant%2Fa/systems/system%2Fa/security-capabilities/provider/capability/capability-a/responsibilities/confirm',
      data: body, signal,
    });
  });

  it('fails explicitly if the response does not include the persisted notes and checks', async () => {
    // Arrange
    request.mockResolvedValue({ systemId: 'system-a', baselineId: 'baseline-a', canConfirm: true,
      items: [responsibilityItem('Applied', 'AC-1')], pendingImpacts: [] });
    // Act
    const result = confirmSystemCapabilityResponsibilities('tenant-a', 'system-a', 'capability-a', body);
    // Assert
    await expect(result).rejects.toMatchObject({ code: 'INVALID_RESPONSIBILITY_CONFIRMATION' });
  });
});
