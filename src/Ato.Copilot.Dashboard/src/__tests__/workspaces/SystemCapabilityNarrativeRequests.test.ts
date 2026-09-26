import { beforeEach, describe, expect, it, vi } from 'vitest';
import { generateScopedSystemCapabilityProposal } from '../../features/workspace-operations/system-capabilities/systemCapabilityNarrativeRequests';
const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('../../features/workspace-operations/workspaceRequest', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/workspace-operations/workspaceRequest')>(), workspaceRequest: request,
}));

describe('Source-bound system capability narrative generation', () => {
  beforeEach(() => vi.clearAllMocks());
  const body = { controlId: 'AC-1', narrativeType: 'Policy', expectedVersion: 2, sourceRevision: 'source-1' };

  it('posts the exact source and revision to the documented origin-binding adapter', async () => {
    // Arrange
    request.mockResolvedValue({ id: 'proposal-a', revision: 1, controlId: 'AC-1', narrativeType: 'Policy' });
    // Act
    const result = await generateScopedSystemCapabilityProposal('tenant/a', 'system/a', 'provider', 'capability/a', body);
    // Assert
    expect(request).toHaveBeenCalledWith({
      method: 'POST', url: '/api/workspaces/organizations/tenant%2Fa/systems/system%2Fa/security-capabilities/provider/capability/capability%2Fa/narrative-proposals',
      data: body,
    });
    expect(result.id).toBe('proposal-a');
  });

  it('rejects returned proposal identity mismatches before navigation', async () => {
    // Arrange
    request.mockResolvedValue({ id: 'proposal-a', revision: 1, controlId: 'AC-2', narrativeType: 'Policy' });
    // Act
    const result = generateScopedSystemCapabilityProposal('tenant-a', 'system-a', 'local', 'capability-a', body);
    // Assert
    await expect(result).rejects.toMatchObject({ status: 502, code: 'INVALID_SYSTEM_CAPABILITY_PROPOSAL' });
  });
});
