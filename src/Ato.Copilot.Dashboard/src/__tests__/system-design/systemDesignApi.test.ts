import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { saveComponentScope, type ComponentScopeUse, type SaveComponentScopeRequest } from '../../api/systemDesign';
import { componentDesignFixture } from '../fixtures/componentReview';

vi.mock('../../api/client', () => ({
  default: { get: vi.fn(), put: vi.fn() },
}));

const body: SaveComponentScopeRequest = {
  expectedRevision: 2, source: 'provider', componentId: 'backup', sourceRevision: 'source-r1',
  decision: 'Included', boundaryId: 'area', usage: 'Human-reviewed use',
  wordingDraftId: 'draft-a', wordingDraftRevision: 7,
};

function savedGraph(scope: ComponentScopeUse) {
  return { ...componentDesignFixture('org-a', 'system/a'), revision: 3, componentScopes: [scope] };
}

beforeEach(() => vi.clearAllMocks());

describe('component scope API', () => {
  it('sends the revision-bound scope and confirms the persisted response', async () => {
    // Arrange
    const scope: ComponentScopeUse = {
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'source-r1',
      decision: 'Included', boundaryId: 'area', boundaryName: 'Mission API', usage: 'Human-reviewed use',
      wordingBasis: {
        draftId: 'draft-a', revision: 7, sourceHash: 'hash-a', origin: 'AI proposed',
        originalWording: 'Proposal wording', userEdited: true, sourceIds: ['release-r1'],
      },
    };
    const result = savedGraph(scope);
    vi.mocked(apiClient.put).mockResolvedValue({ data: result } as never);
    const signal = new AbortController().signal;

    // Act
    const saved = await saveComponentScope('system/a', body, signal);

    // Assert
    expect(saved).toEqual(result);
    expect(apiClient.put).toHaveBeenCalledWith('/systems/system%2Fa/design/component-scope', body, { signal });
  });

  it('rejects a response that does not confirm the requested system revision and use', async () => {
    // Arrange
    const scope: ComponentScopeUse = {
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'source-r1',
      decision: 'Excluded', boundaryId: null, boundaryName: null, usage: 'Different saved text',
    };
    vi.mocked(apiClient.put).mockResolvedValue({ data: savedGraph(scope) } as never);

    // Act / Assert
    await expect(saveComponentScope('system/a', body)).rejects.toThrow(/did not confirm the requested scope draft/);
  });
});
