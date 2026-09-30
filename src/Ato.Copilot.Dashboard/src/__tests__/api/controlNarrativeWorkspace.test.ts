import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { getControlNarrativeDetail, getControlNarrativeWorkspace } from '../../api/controlNarrativeWorkspace';

vi.mock('../../api/client', () => ({ default: { get: vi.fn() } }));

const statement = {
  state: 'Missing',
  hasContent: false,
  hasApprovedContent: false,
  proposalId: null,
  proposalStatus: null,
  isStale: false,
};
const item = {
  id: 'implementation-1',
  controlId: 'AC-2',
  controlTitle: 'Account Management',
  family: 'AC',
  implementationStatus: 'Planned',
  currentVersion: 3,
  approvalStatus: 'Draft',
  policy: statement,
  technical: { ...statement, state: 'Draft', hasContent: true },
  nextAction: 'AuthorPolicy',
  nextActionLabel: 'Add policy statement',
  nextActionReason: null,
};
const workspace = {
  systemId: 'system/a',
  counts: { needsAttention: 1, allControls: 1, approvedStatements: 0, proposedUpdates: 0 },
  items: [item],
  permissions: { canAuthor: true, canReview: false, canManageEvidence: true },
};

beforeEach(() => vi.clearAllMocks());

describe('control narrative workspace API', () => {
  it('reads the scoped workspace and preserves server-calculated counts', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: workspace });

    // Act
    const result = await getControlNarrativeWorkspace('system/a', { view: 'needs-attention', page: 2, pageSize: 25 });

    // Assert
    expect(result).toEqual(workspace);
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system%2Fa/narrative-workspace', {
      params: { view: 'needs-attention', page: 2, pageSize: 25 },
      signal: undefined,
    });
  });

  it.each([
    { ...workspace, systemId: 'foreign-system' },
    { ...workspace, counts: { needsAttention: 0, allControls: 0, approvedStatements: 0 } },
    { ...workspace, items: [{ ...item, controlTitle: '' }] },
    { ...workspace, permissions: { canAuthor: true } },
  ])('rejects malformed or foreign success-shaped responses', async response => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: response });

    // Act / Assert
    await expect(getControlNarrativeWorkspace('system/a')).rejects.toThrow(/workspace|response|match/i);
  });

  it('validates direct-linked detail identity', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: {
      systemId: 'system/a',
      id: item.id, controlId: item.controlId, controlTitle: item.controlTitle, family: item.family,
      implementationStatus: item.implementationStatus, approvalStatus: item.approvalStatus, currentVersion: item.currentVersion,
      statements: {
        policy: { currentContent: '', approvedContent: '', state: 'Missing' },
        technical: { currentContent: 'Accounts are federated.', approvedContent: '', state: 'Draft' },
      },
      proposals: [],
      responsibilities: [],
      history: [],
      permissions: { canAuthor: true, authorReason: null, canReview: false,
        reviewReason: 'Narrative review permission is required.', canManageEvidence: true, evidenceReason: null },
    } });

    // Act
    const result = await getControlNarrativeDetail('system/a', 'AC-2');

    // Assert
    expect(result.controlId).toBe('AC-2');
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system%2Fa/narrative-workspace/AC-2', expect.anything());
  });
});
